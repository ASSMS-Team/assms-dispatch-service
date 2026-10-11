using System.Text.Json;
using Confluent.Kafka;
using DispatchService.Messaging.Contracts;
using DispatchService.Services;
using Prometheus;

namespace DispatchService.Messaging.Consumers;

/// <summary>Consumes JobStatusChanged events and keeps Dispatch assignment status current.</summary>
public class JobStatusChangedConsumer : BackgroundService
{
    private static readonly Counter EventsConsumedCounter = Metrics.CreateCounter(
        "assms_kafka_events_consumed_total", "Total Kafka events processed by consumer",
        new CounterConfiguration { LabelNames = new[] { "topic", "consumer_group", "status" } });
    private static readonly TimeSpan WriteRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ConsumerConfig _consumerConfig;
    private readonly Func<ConsumerConfig, IConsumer<string, string>> _consumerFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobStatusChangedConsumer> _logger;

    public JobStatusChangedConsumer(string bootstrapServers, IServiceScopeFactory scopeFactory, ILogger<JobStatusChangedConsumer> logger)
        : this(bootstrapServers, scopeFactory, logger, config => new ConsumerBuilder<string, string>(config).Build()) { }

    internal JobStatusChangedConsumer(
        string bootstrapServers,
        IServiceScopeFactory scopeFactory,
        ILogger<JobStatusChangedConsumer> logger,
        Func<ConsumerConfig, IConsumer<string, string>> consumerFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _consumerFactory = consumerFactory;
        _consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = JobStatusChangedPayload.ConsumerGroup,
            EnableAutoCommit = false,
            // Earliest: a new group first replays the retained history, which
            // closes assignments whose jobs completed before this consumer existed.
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using var consumer = _consumerFactory(_consumerConfig);
        consumer.Subscribe(JobStatusChangedPayload.Topic);
        _logger.LogInformation("Subscribed to {Topic} as group {GroupId}.", JobStatusChangedPayload.Topic, JobStatusChangedPayload.ConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> message;
                try { message = consumer.Consume(stoppingToken); }
                catch (ConsumeException exception)
                {
                    _logger.LogError(exception, "Consuming from {Topic} failed.", JobStatusChangedPayload.Topic);
                    continue;
                }

                EventEnvelope<JobStatusChangedPayload>? envelope;
                try { envelope = Deserialize(message.Message.Value); }
                catch (JsonException exception)
                {
                    _logger.LogError(exception, "Quarantining malformed JobStatusChanged at {Offset}.", message.TopicPartitionOffset);
                    consumer.Commit(message);
                    continue;
                }

                if (envelope is null || envelope.Payload is null)
                {
                    _logger.LogError("Quarantining JobStatusChanged with no envelope or payload at {Offset}.", message.TopicPartitionOffset);
                    consumer.Commit(message);
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IAssignmentStatusService>();
                    var outcome = await service.ApplyAsync(envelope, message.Message.Key, stoppingToken);

                    if (outcome == AssignmentStatusOutcome.InvalidEvent)
                    {
                        _logger.LogError("Quarantining invalid JobStatusChanged event {EventId} at {Offset}.", envelope.EventId, message.TopicPartitionOffset);
                        EventsConsumedCounter.WithLabels(JobStatusChangedPayload.Topic, JobStatusChangedPayload.ConsumerGroup, "discarded_invalid").Inc();
                    }
                    else if (outcome == AssignmentStatusOutcome.Updated)
                    {
                        _logger.LogInformation("Assignment for job {JobId} is now {Status}. Event {EventId}.", envelope.Payload.JobId, envelope.Payload.NewStatus, envelope.EventId);
                        EventsConsumedCounter.WithLabels(JobStatusChangedPayload.Topic, JobStatusChangedPayload.ConsumerGroup, "success").Inc();
                    }
                    else
                    {
                        // No assignment for the job, or a status the assignment has already passed.
                        _logger.LogInformation("Ignoring JobStatusChanged {Status} for job {JobId}. Event {EventId}.", envelope.Payload.NewStatus, envelope.Payload.JobId, envelope.EventId);
                        EventsConsumedCounter.WithLabels(JobStatusChangedPayload.Topic, JobStatusChangedPayload.ConsumerGroup, "ignored").Inc();
                    }

                    consumer.Commit(message);
                }
                catch (Exception exception)
                {
                    EventsConsumedCounter.WithLabels(JobStatusChangedPayload.Topic, JobStatusChangedPayload.ConsumerGroup, "failed").Inc();
                    _logger.LogError(exception, "Applying JobStatusChanged at {Offset} failed. It will be retried.", message.TopicPartitionOffset);
                    consumer.Seek(message.TopicPartitionOffset);
                    await Task.Delay(WriteRetryDelay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Stopping the {Topic} consumer.", JobStatusChangedPayload.Topic);
        }
        finally { consumer.Close(); }
    }

    internal static EventEnvelope<JobStatusChangedPayload>? Deserialize(string value) =>
        JsonSerializer.Deserialize<EventEnvelope<JobStatusChangedPayload>>(value, SerializerOptions);
}
