using Confluent.Kafka;
using DispatchService.Messaging.Consumers;
using DispatchService.Messaging.Contracts;
using DispatchService.Services;
using DispatchService.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DispatchService.Tests;

public class JobCreatedConsumerTests
{
    private const string EventId = "3d2a1b90-6c77-4f18-b0a1-5c9e7d4a2f31";
    private const string JobId = "9f1c7a24-8f4e-4c3a-9a52-2b6d0f5e1a77";

    [Fact]
    public async Task ExecuteAsync_WithValidMessage_EvaluatesAndCommitsUsingTheDispatchConsumerGroup()
    {
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var evaluation = new FakeCandidateEvaluationService();
        var message = kafka.Enqueue(ValidMessage());

        ConsumerConfig? config = null;
        await RunAsync(cts, kafka, evaluation, captured => config = captured);

        Assert.Equal(1, evaluation.CallCount);
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
        Assert.Equal(JobCreatedPayload.Topic, Assert.Single(kafka.SubscribedTopics));
        Assert.Equal(JobCreatedPayload.ConsumerGroup, config!.GroupId);
        Assert.False(config.EnableAutoCommit);
        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
        Assert.Equal(1, kafka.CloseCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithMalformedMessage_QuarantinesAndCommitsWithoutCallingTheService()
    {
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var evaluation = new FakeCandidateEvaluationService();
        var message = kafka.Enqueue("not valid json");

        await RunAsync(cts, kafka, evaluation);

        Assert.Equal(0, evaluation.CallCount);
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
        Assert.Empty(kafka.SeekedOffsets);
    }

    [Fact]
    public async Task ExecuteAsync_WithAnUnmappedCategory_CommitsTheQuarantinedEventWithoutPersistenceRetry()
    {
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var evaluation = new FakeCandidateEvaluationService
        {
            Result = new(null, CandidateEvaluationError.UnmappedServiceCategory),
        };
        var message = kafka.Enqueue(ValidMessage());

        await RunAsync(cts, kafka, evaluation);

        Assert.Equal(1, evaluation.CallCount);
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
        Assert.Empty(kafka.SeekedOffsets);
    }

    [Fact]
    public void Deserialize_MapsTheSharedJobCreatedEnvelope()
    {
        var envelope = JobCreatedConsumer.Deserialize(ValidMessage());

        Assert.NotNull(envelope);
        Assert.Equal(EventId, envelope!.EventId);
        Assert.Equal(JobCreatedPayload.EventType, envelope.EventType);
        Assert.Equal(JobId, envelope.Payload.JobId);
        Assert.Equal("REPAIR", envelope.Payload.ServiceCategory);
    }

    private static async Task RunAsync(CancellationTokenSource cts, FakeKafkaConsumer kafka, FakeCandidateEvaluationService evaluation, Action<ConsumerConfig>? configured = null)
    {
        kafka.OnMessagesExhausted = cts.Cancel;
        await using var provider = new ServiceCollection().AddSingleton<ICandidateEvaluationService>(evaluation).BuildServiceProvider();
        var consumer = new JobCreatedConsumer(
            "localhost:9092", provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<JobCreatedConsumer>.Instance,
            config => { configured?.Invoke(config); return kafka; });
        await consumer.StartAsync(cts.Token);
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static string ValidMessage() => $$"""
        { "eventId": "{{EventId}}", "eventType": "JobCreated", "eventVersion": 1,
          "occurredAt": "2026-09-10T07:30:00Z", "producer": "job-service",
          "payload": { "jobId": "{{JobId}}", "jobReference": "JOB-123", "customerId": "c", "assetId": "a",
            "serviceCategory": "REPAIR", "problemDescription": "Cooling failure", "priority": "HIGH",
            "region": "WESTERN", "status": "CREATED", "createdAt": "2026-09-10T07:30:00Z" } }
        """;
}
