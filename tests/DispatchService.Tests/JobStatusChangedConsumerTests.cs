using Confluent.Kafka;
using DispatchService.Messaging.Consumers;
using DispatchService.Messaging.Contracts;
using DispatchService.Repositories;
using DispatchService.Services;
using DispatchService.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DispatchService.Tests;

public class JobStatusChangedConsumerTests
{
    private const string EventId = "504d8866-022d-4a7c-bc50-86deb1764148";
    private const string JobId = "0ded1501-9e25-41bc-a6e3-cf8208bbd650";

    [Fact]
    public async Task ExecuteAsync_WithACompletedEvent_ClosesTheAssignmentAndCommitsUsingTheDispatchGroup()
    {
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var repository = new FakeAutomaticAssignmentRepository();
        var message = kafka.Enqueue(ValidMessage("COMPLETED"), JobId);

        ConsumerConfig? config = null;
        await RunAsync(cts, kafka, repository, captured => config = captured);

        Assert.Equal((JobId, "COMPLETED", 3), Assert.Single(repository.StatusUpdates));
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
        Assert.Equal(JobStatusChangedPayload.Topic, Assert.Single(kafka.SubscribedTopics));
        Assert.Equal(JobStatusChangedPayload.ConsumerGroup, config!.GroupId);
        Assert.False(config.EnableAutoCommit);
        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.Equal(1, kafka.CloseCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithMalformedMessage_QuarantinesAndCommitsWithoutWriting()
    {
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var repository = new FakeAutomaticAssignmentRepository();
        var message = kafka.Enqueue("not valid json", JobId);

        await RunAsync(cts, kafka, repository);

        Assert.Empty(repository.StatusUpdates);
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
    }

    [Fact]
    public async Task ExecuteAsync_WithAnInvalidEvent_QuarantinesAndCommitsWithoutWriting()
    {
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var repository = new FakeAutomaticAssignmentRepository();
        var message = kafka.Enqueue(ValidMessage("COMPLETED"), "a-different-key");

        await RunAsync(cts, kafka, repository);

        Assert.Empty(repository.StatusUpdates);
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheDatabaseWriteFails_SeeksBackToRetryInsteadOfCommitting()
    {
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var repository = new FakeAutomaticAssignmentRepository
        {
            UpdateJobStatusException = new InvalidOperationException("The database was unreachable."),
        };
        var message = kafka.Enqueue(ValidMessage("COMPLETED"), JobId);

        // Stop the host during the first failure so the loop unwinds instead of
        // sleeping out its retry delay.
        await RunAsync(cts, kafka, repository,
            beforeStart: consumer => repository.OnUpdateJobStatus = () => _ = consumer.StopAsync(CancellationToken.None));

        Assert.Empty(kafka.CommittedOffsets);
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.SeekedOffsets));
    }

    [Fact]
    public void Deserialize_ReadsJobServicePayloadTimestampsWithoutAUtcDesignator()
    {
        var envelope = JobStatusChangedConsumer.Deserialize(ValidMessage("IN_PROGRESS"));

        Assert.NotNull(envelope);
        Assert.Equal(JobId, envelope!.Payload.JobId);
        Assert.Equal("IN_PROGRESS", envelope.Payload.NewStatus);
        Assert.Equal(new DateTime(2026, 10, 11, 15, 58, 5, 999).AddTicks(110), envelope.Payload.OccurredAt);
    }

    private static async Task RunAsync(
        CancellationTokenSource cts, FakeKafkaConsumer kafka, FakeAutomaticAssignmentRepository repository,
        Action<ConsumerConfig>? configured = null, Action<JobStatusChangedConsumer>? beforeStart = null)
    {
        kafka.OnMessagesExhausted = cts.Cancel;
        await using var provider = new ServiceCollection()
            .AddSingleton<IAutomaticAssignmentRepository>(repository)
            .AddScoped<IAssignmentStatusService, AssignmentStatusService>()
            .BuildServiceProvider();
        var consumer = new JobStatusChangedConsumer(
            "localhost:9092", provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobStatusChangedConsumer>.Instance,
            config => { configured?.Invoke(config); return kafka; });
        beforeStart?.Invoke(consumer);
        await consumer.StartAsync(cts.Token);
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
    }

    // Shaped like a real event read from the job-status-changed topic: the
    // envelope time is UTC but the payload time has no designator.
    private static string ValidMessage(string newStatus) => $$"""
        { "eventId": "{{EventId}}", "eventType": "JobStatusChanged", "eventVersion": 1,
          "occurredAt": "2026-10-11T15:58:06.0191841Z", "producer": "job-service",
          "payload": { "jobId": "{{JobId}}", "jobReference": "JOB-3KDJYV",
            "assignmentId": "82109fab-28bb-4326-8f21-fc25d488309f",
            "technicianId": "baf773c1-b496-497f-b97b-0b5949aa0c8b", "technicianReference": "TECHNICIAN.LOCAL",
            "oldStatus": "IN_PROGRESS", "newStatus": "{{newStatus}}", "occurredAt": "2026-10-11T15:58:05.999011" } }
        """;
}
