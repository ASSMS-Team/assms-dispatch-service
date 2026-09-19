using System.Text.Json;
using Confluent.Kafka;
using DispatchService.Messaging.Contracts;
using DispatchService.Services;

namespace DispatchService.Messaging.Consumers;

/// <summary>Consumes JobCreated events and records eligible Dispatch candidates.</summary>
public class JobCreatedConsumer : BackgroundService
{
    private static readonly TimeSpan WriteRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ConsumerConfig _consumerConfig;
    private readonly Func<ConsumerConfig, IConsumer<string, string>> _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobCreatedConsumer> _logger;

    public JobCreatedConsumer(string bootstrapServers, IServiceScopeFactory scopeFactory, ILogger<JobCreatedConsumer> logger)
        : this(bootstrapServers, scopeFactory, logger, config => new ConsumerBuilder<string, string>(config).Build()) { }

    internal JobCreatedConsumer(
        string bootstrapServers,
        IServiceScopeFactory scopeFactory,
        ILogger<JobCreatedConsumer> logger,
        Func<ConsumerConfig, IConsumer<string, string>> consumerFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _consumerFactory = consumerFactory;
        _consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = JobCreatedPayload.ConsumerGroup,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var consumer = _consumerFactory(_consumerConfig);
        consumer.Subscribe(JobCreatedPayload.Topic);
        _logger.LogInformation("Subscribed to {Topic} as group {GroupId}.", JobCreatedPayload.Topic, JobCreatedPayload.ConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> message;
                try { message = consumer.Consume(stoppingToken); }
                catch (ConsumeException exception)
                {
                    _logger.LogError(exception, "Consuming from {Topic} failed.", JobCreatedPayload.Topic);
                    continue;
                }

                EventEnvelope<JobCreatedPayload>? envelope;
                try { envelope = Deserialize(message.Message.Value); }
                catch (JsonException exception)
                {
                    _logger.LogError(exception, "Quarantining malformed JobCreated at {Offset}.", message.TopicPartitionOffset);
                    consumer.Commit(message);
                    continue;
                }

                if (envelope is null || envelope.Payload is null)
                {
                    _logger.LogError("Quarantining JobCreated with no envelope or payload at {Offset}.", message.TopicPartitionOffset);
                    consumer.Commit(message);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ICandidateEvaluationService>();
                    var result = await service.EvaluateAsync(
                        envelope.EventId, envelope.EventType, envelope.EventVersion, envelope.Producer,
                        envelope.Payload.JobId, envelope.Payload.JobReference, envelope.Payload.ServiceCategory,
                        envelope.Payload.Region, envelope.OccurredAt);

                    if (result.Error != CandidateEvaluationError.None)
                    {
                        _logger.LogError(
                            "Quarantining JobCreated event {EventId} for job {JobId}: {Reason}.",
                            envelope.EventId, envelope.Payload.JobId, result.Error);
                        consumer.Commit(message);
                        continue;
                    }

                    // US-06B deliberately runs even after a duplicate evaluation. A process
                    // can fail after US-06A commits but before its assignment transaction;
                    // the assignment repository is idempotent by job id and resumes safely.
                    var assignmentService = scope.ServiceProvider.GetRequiredService<IAutomaticAssignmentService>();
                    var assignment = await assignmentService.AssignAsync(envelope.EventId, envelope.Payload.JobId, stoppingToken);
                    if (assignment.Outcome == DispatchService.Models.AutomaticAssignmentOutcome.Assigned)
                        _logger.LogInformation("Assigned technician {TechnicianId} to job {JobId}. Event {EventId}.", assignment.Assignment!.TechnicianId, envelope.Payload.JobId, envelope.EventId);
                    else if (assignment.Outcome == DispatchService.Models.AutomaticAssignmentOutcome.AlreadyAssigned)
                        _logger.LogInformation("Ignoring repeat assignment processing for job {JobId}. Event {EventId}.", envelope.Payload.JobId, envelope.EventId);
                    else
                        _logger.LogWarning("Job {JobId} has no eligible technician; Dispatcher attention is required. Event {EventId}.", envelope.Payload.JobId, envelope.EventId);

                    consumer.Commit(message);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Evaluating JobCreated at {Offset} failed. It will be retried.", message.TopicPartitionOffset);
                    consumer.Seek(message.TopicPartitionOffset);
                    await Task.Delay(WriteRetryDelay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stopping the {Topic} consumer.", JobCreatedPayload.Topic);
        }
        finally { consumer.Close(); }
    }

    internal static EventEnvelope<JobCreatedPayload>? Deserialize(string value) =>
        JsonSerializer.Deserialize<EventEnvelope<JobCreatedPayload>>(value, SerializerOptions);
}
