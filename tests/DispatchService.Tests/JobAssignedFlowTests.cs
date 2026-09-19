using System.Text.Json;

using Confluent.Kafka;

using DispatchService.Messaging.Consumers;
using DispatchService.Messaging.Contracts;
using DispatchService.Messaging.Publishers;
using DispatchService.Models;
using DispatchService.Repositories;
using DispatchService.Services;
using DispatchService.Tests.Fakes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DispatchService.Tests;

// The US-06B half of the JobCreated loop: what the consumer does with each
// assignment outcome, how it behaves when the assignment transaction fails, and
// what shape the JobAssigned event leaves this service in.
//
// JobCreatedConsumerTests covers the US-06A evaluation half - subscription,
// malformed messages, and quarantined evaluations. This file picks up where that
// one stops, at the assignment step.
public class JobAssignedFlowTests
{
    private const string EventId = "3d2a1b90-6c77-4f18-b0a1-5c9e7d4a2f31";
    private const string JobId = "9f1c7a24-8f4e-4c3a-9a52-2b6d0f5e1a77";
    private const string AssignmentId = "e2d7b415-9a63-4c08-b1f5-7d4e2a9c6301";
    private const string TechnicianId = "5c8a3f71-4b29-4e6d-8a03-9f1b7c2e4d58";

    private static string ValidMessage(string jobId = JobId) => $$"""
        { "eventId": "{{EventId}}", "eventType": "JobCreated", "eventVersion": 1,
          "occurredAt": "2026-09-14T07:30:00Z", "producer": "job-service",
          "payload": { "jobId": "{{jobId}}", "jobReference": "JOB-123", "customerId": "c", "assetId": "a",
            "serviceCategory": "REPAIR", "problemDescription": "Cooling failure", "priority": "HIGH",
            "region": "WESTERN", "status": "CREATED", "createdAt": "2026-09-14T07:30:00Z" } }
        """;

    private static AssignmentRecord Assignment() => new(
        AssignmentId, JobId, "JOB-123", TechnicianId, "TECH-0001",
        new DateTime(2026, 9, 14, 9, 15, 2, 446, DateTimeKind.Utc));

    private static async Task RunAsync(
        CancellationTokenSource cts,
        FakeKafkaConsumer kafka,
        FakeAutomaticAssignmentService assignment,
        FakeCandidateEvaluationService? evaluation = null)
    {
        kafka.OnMessagesExhausted = cts.Cancel;

        await using var provider = new ServiceCollection()
            .AddSingleton<ICandidateEvaluationService>(evaluation ?? new FakeCandidateEvaluationService())
            .AddSingleton<IAutomaticAssignmentService>(assignment)
            .BuildServiceProvider();

        var consumer = new JobCreatedConsumer(
            "localhost:9092",
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<JobCreatedConsumer>.Instance,
            _ => kafka);

        await consumer.StartAsync(cts.Token);
        await consumer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));
    }

    // ---- the three assignment outcomes -------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenATechnicianIsAssigned_CommitsTheEvent()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var assignment = new FakeAutomaticAssignmentService
        {
            Result = new(AutomaticAssignmentOutcome.Assigned, Assignment())
        };
        var message = kafka.Enqueue(ValidMessage());

        // Act
        await RunAsync(cts, kafka, assignment);

        // Assert
        Assert.Equal(1, assignment.CallCount);
        Assert.Equal(JobId, Assert.Single(assignment.ReceivedJobIds));
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
        Assert.Empty(kafka.SeekedOffsets);
    }

    [Fact]
    public async Task ExecuteAsync_WithADuplicateJobCreated_AssignsOnceAndCommitsBoth()
    {
        // Arrange - the same JobCreated twice, which is what at-least-once
        // delivery guarantees will happen. The consumer does not deduplicate in
        // memory: it calls the assignment service for both, and the service's
        // transaction - guarded by uq_technician_assignments_job - reports the
        // second as AlreadyAssigned rather than assigning a second technician.
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var assignment = new FakeAutomaticAssignmentService
        {
            Result = new(AutomaticAssignmentOutcome.AlreadyAssigned, Assignment())
        };

        kafka.Enqueue(ValidMessage());
        kafka.Enqueue(ValidMessage());

        // Act
        await RunAsync(cts, kafka, assignment);

        // Assert - both delivered and both committed. A duplicate is the expected
        // outcome of a replay, not an error, so neither is seeked back.
        Assert.Equal(2, assignment.CallCount);
        Assert.Equal(2, kafka.CommittedOffsets.Count);
        Assert.Empty(kafka.SeekedOffsets);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoEligibleTechnician_CommitsRatherThanBlockingTheTopic()
    {
        // Arrange - a well-formed event that simply has nobody to go to. The job
        // is flagged for Dispatcher attention in job_candidate_evaluations; the
        // offset still moves, because holding the topic on a job nobody can take
        // would stop every job behind it as well.
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var assignment = new FakeAutomaticAssignmentService
        {
            Result = new(AutomaticAssignmentOutcome.NoEligibleCandidate, null)
        };
        var message = kafka.Enqueue(ValidMessage());

        // Act
        await RunAsync(cts, kafka, assignment);

        // Assert
        Assert.Equal(1, assignment.CallCount);
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
        Assert.Empty(kafka.SeekedOffsets);
    }

    [Fact]
    public async Task ExecuteAsync_AfterADuplicateEvaluation_StillRunsTheAssignmentStep()
    {
        // Arrange - the behaviour the consumer comments on explicitly: a process
        // can fail after US-06A commits its evaluation but before the US-06B
        // assignment transaction. Skipping assignment whenever the evaluation was
        // a duplicate would strand that job forever, so the assignment step runs
        // regardless and relies on its own idempotency.
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var evaluation = new FakeCandidateEvaluationService
        {
            // A duplicate evaluation is not an error, so Error stays None.
            Result = new(new(true, 1), CandidateEvaluationError.None)
        };
        var assignment = new FakeAutomaticAssignmentService
        {
            Result = new(AutomaticAssignmentOutcome.Assigned, Assignment())
        };

        kafka.Enqueue(ValidMessage());

        // Act
        await RunAsync(cts, kafka, assignment, evaluation);

        // Assert - the assignment step ran even though the evaluation had already
        // been recorded on an earlier delivery.
        Assert.Equal(1, evaluation.CallCount);
        Assert.Equal(1, assignment.CallCount);
        Assert.Single(kafka.CommittedOffsets);
    }

    // ---- transient database failure: seek and retry ------------------------

    [Fact]
    public async Task ExecuteAsync_WhenTheAssignmentTransactionFails_SeeksBackAndDoesNotCommit()
    {
        // Arrange - the assignment transaction failed for a reason outside the
        // message. The same event will succeed once the database is back, so its
        // offset must not move.
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var assignment = new FakeAutomaticAssignmentService
        {
            ExceptionToThrow = new InvalidOperationException("The database was unreachable.")
        };

        // Cancelling at the moment of failure unwinds the loop instead of
        // sleeping out its five-second retry delay.
        assignment.OnCall = cts.Cancel;

        var message = kafka.Enqueue(ValidMessage());

        // Act
        await RunAsync(cts, kafka, assignment);

        // Assert - seeked, not committed. Seeking is what makes the next read
        // return this message again; without it the consumer's in-memory position
        // has already moved past it and the event would only come back after a
        // rebalance.
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.SeekedOffsets));
        Assert.Empty(kafka.CommittedOffsets);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheAssignmentFailsAndThenSucceeds_CommitsTheRedeliveredMessage()
    {
        // Arrange - a database that was down and came back, which is the case the
        // retry exists for. The fake consumer's Seek re-queues the message, as a
        // real broker would redeliver it.
        using var cts = new CancellationTokenSource();
        var kafka = new FakeKafkaConsumer();
        var assignment = new FakeAutomaticAssignmentService
        {
            FailuresBeforeSuccess = 1,
            Result = new(AutomaticAssignmentOutcome.Assigned, Assignment())
        };
        var message = kafka.Enqueue(ValidMessage());

        // Act
        await RunAsync(cts, kafka, assignment);

        // Assert - tried twice, seeked once, committed once.
        Assert.Equal(2, assignment.CallCount);
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.SeekedOffsets));
        Assert.Equal(message.TopicPartitionOffset, Assert.Single(kafka.CommittedOffsets));
    }

    // ---- the published contract --------------------------------------------

    [Fact]
    public void JobAssignedPayload_DeclaresExactlySixFields()
    {
        // The Job and Reporting services each redefine this payload
        // independently, so nothing at compile time catches a field that has
        // drifted - a renamed field simply deserializes as null forever in both
        // consumers. This guards the count and the names at the producing end.
        var declared = typeof(JobAssignedPayload)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "AssignedAt",
                "AssignmentId",
                "JobId",
                "JobReference",
                "TechnicianId",
                "TechnicianReference"
            },
            declared);
    }

    [Fact]
    public void JobAssignedEnvelope_SerializesToTheContractsWireShape()
    {
        // Arrange - built and serialized exactly as AutomaticAssignmentRepository
        // does when it writes the outbox row, including the camelCase policy.
        // What is pinned is the shape that reaches the topic: the five envelope
        // fields around a six-key payload, in camelCase, with UTC timestamps.
        var assignment = Assignment();

        var json = JsonSerializer.Serialize(
            new EventEnvelope<JobAssignedPayload>
            {
                EventId = "7b41e9c6-0d38-4a52-9f17-3c8b6e2d5a04",
                EventType = JobAssignedPayload.EventType,
                EventVersion = JobAssignedPayload.EventVersion,
                OccurredAt = assignment.AssignedAt,
                Producer = "dispatch-service",
                Payload = new JobAssignedPayload
                {
                    AssignmentId = assignment.Id,
                    JobId = assignment.JobId,
                    JobReference = assignment.JobReference,
                    TechnicianId = assignment.TechnicianId,
                    TechnicianReference = assignment.TechnicianReference,
                    AssignedAt = assignment.AssignedAt,
                },
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // Act
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Assert - the envelope's five non-payload fields, by their wire names.
        Assert.Equal(
            new[] { "eventId", "eventType", "eventVersion", "occurredAt", "payload", "producer" },
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal("JobAssigned", root.GetProperty("eventType").GetString());
        Assert.Equal(1, root.GetProperty("eventVersion").GetInt32());
        Assert.Equal("dispatch-service", root.GetProperty("producer").GetString());

        // And the six payload keys, in camelCase.
        Assert.Equal(
            new[]
            {
                "assignedAt", "assignmentId", "jobId", "jobReference", "technicianId", "technicianReference"
            },
            root.GetProperty("payload")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        // Timestamps go out as ISO 8601 UTC with the trailing Z. Without Kind Utc
        // they would serialize as an unmarked local time and every consumer's
        // range filter would shift by the producer's timezone.
        Assert.EndsWith("Z", root.GetProperty("occurredAt").GetString());
        Assert.EndsWith("Z", root.GetProperty("payload").GetProperty("assignedAt").GetString());
    }

    [Fact]
    public void JobAssignedPayload_NamesTheContractsTopicAndEventType()
    {
        Assert.Equal("job-assigned", JobAssignedPayload.Topic);
        Assert.Equal("JobAssigned", JobAssignedPayload.EventType);
        Assert.Equal(1, JobAssignedPayload.EventVersion);
    }

    // ---- the outbox publisher ----------------------------------------------

    [Fact]
    public async Task PublishPendingAsync_PublishesEveryPendingEventInOrder()
    {
        // Arrange - the repository returns them ordered by created_at then id, and
        // the publisher must not reorder: two events about one job have to reach
        // the topic in the order they were written.
        var repository = new FakeAutomaticAssignmentRepository
        {
            PendingEvents =
            [
                new("event-1", "job-1", "job-assigned", "{}", 0),
                new("event-2", "job-1", "job-assigned", "{}", 0),
                new("event-3", "job-2", "job-assigned", "{}", 0),
            ],
        };
        var publisher = new FakeJobAssignedPublisher();
        var worker = CreateWorker(repository, publisher);

        // Act
        await worker.PublishPendingAsync();

        // Assert
        Assert.Equal(
            new[] { "event-1", "event-2", "event-3" },
            publisher.PublishedEvents.Select(published => published.Id));

        Assert.Equal(new[] { "event-1", "event-2", "event-3" }, repository.PublishedEventIds);
        Assert.Empty(repository.FailedEvents);
    }

    [Fact]
    public async Task PublishPendingAsync_SendsTheStoredEnvelopeVerbatimKeyedOnTheJobId()
    {
        // Arrange - the envelope was serialized when the assignment committed, and
        // is sent byte for byte as stored. Rebuilding it here would mint a fresh
        // event id on every retry and defeat the deduplication in both consumers.
        const string storedEnvelope = """{"eventId":"abc","eventType":"JobAssigned","payload":{"jobId":"job-1"}}""";

        var repository = new FakeAutomaticAssignmentRepository
        {
            PendingEvents = [new("event-1", "job-1", "job-assigned", storedEnvelope, 0)],
        };
        var publisher = new FakeJobAssignedPublisher();
        var worker = CreateWorker(repository, publisher);

        // Act
        await worker.PublishPendingAsync();

        // Assert
        var published = Assert.Single(publisher.PublishedEvents);

        Assert.Equal(storedEnvelope, published.Payload);
        Assert.Equal("job-assigned", published.Topic);

        // Keyed on the job id, so every event about one job lands on one
        // partition in the order it was published.
        Assert.Equal("job-1", published.JobId);
    }

    [Fact]
    public async Task PublishPendingAsync_WhenNothingIsPending_PublishesNothing()
    {
        // Arrange - the steady state. An empty outbox must not produce a spurious
        // publish or a spurious mark.
        var repository = new FakeAutomaticAssignmentRepository();
        var publisher = new FakeJobAssignedPublisher();
        var worker = CreateWorker(repository, publisher);

        // Act
        await worker.PublishPendingAsync();

        // Assert
        Assert.Empty(publisher.PublishedEvents);
        Assert.Empty(repository.PublishedEventIds);
        Assert.Empty(repository.FailedEvents);
    }

    [Fact]
    public async Task PublishPendingAsync_WhenKafkaFails_NeverMarksTheEventPublished()
    {
        // Arrange - the one ordering that must never invert. Marking a row
        // published before the broker acknowledged would lose the event entirely
        // if the publish then failed, which is the single failure the outbox
        // exists to prevent.
        var repository = new FakeAutomaticAssignmentRepository
        {
            PendingEvents = [new("event-1", "job-1", "job-assigned", "{}", 3)],
        };
        var publisher = new FakeJobAssignedPublisher
        {
            ExceptionToThrow = new InvalidOperationException("broker unavailable")
        };
        var worker = CreateWorker(repository, publisher);

        // Act
        await worker.PublishPendingAsync();

        // Assert - recorded as failed, left unpublished, and therefore picked up
        // again by the next pass. Nothing gives up on the row.
        Assert.Empty(repository.PublishedEventIds);
        Assert.Equal("event-1", Assert.Single(repository.FailedEvents).EventId);
    }

    private static JobAssignedOutboxPublisher CreateWorker(
        IAutomaticAssignmentRepository repository,
        IJobAssignedPublisher publisher)
    {
        var services = new ServiceCollection()
            .AddScoped<IAutomaticAssignmentRepository>(_ => repository)
            .BuildServiceProvider();

        return new JobAssignedOutboxPublisher(
            services.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            NullLogger<JobAssignedOutboxPublisher>.Instance);
    }
}
