using DispatchService.Messaging.Contracts;
using DispatchService.Services;
using DispatchService.Tests.Fakes;

namespace DispatchService.Tests;

public class AssignmentStatusServiceTests
{
    private const string JobId = "9f1c7a24-8f4e-4c3a-9a52-2b6d0f5e1a77";

    private static EventEnvelope<JobStatusChangedPayload> Envelope(string newStatus, string jobId = JobId) => new()
    {
        EventId = Guid.NewGuid().ToString(),
        EventType = JobStatusChangedPayload.EventType,
        EventVersion = JobStatusChangedPayload.EventVersion,
        Producer = "job-service",
        OccurredAt = DateTime.UtcNow,
        Payload = new JobStatusChangedPayload
        {
            JobId = jobId, JobReference = "JOB-123", OldStatus = "ASSIGNED", NewStatus = newStatus,
            // Job Service publishes this without a UTC designator.
            OccurredAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
        },
    };

    [Theory]
    [InlineData("IN_PROGRESS", 2)]
    [InlineData("completed", 3)]
    [InlineData("CANCELLED", 3)]
    public async Task ApplyAsync_MovesTheAssignmentToTheNewStatus(string newStatus, int expectedRank)
    {
        var repository = new FakeAutomaticAssignmentRepository();
        var outcome = await new AssignmentStatusService(repository).ApplyAsync(Envelope(newStatus), JobId);

        Assert.Equal(AssignmentStatusOutcome.Updated, outcome);
        Assert.Equal((JobId, newStatus.ToUpperInvariant(), expectedRank), Assert.Single(repository.StatusUpdates));
    }

    [Fact]
    public async Task ApplyAsync_WhenTheAssignmentIsAlreadyPastTheStatus_IsIgnored()
    {
        var repository = new FakeAutomaticAssignmentRepository { UpdateJobStatusResult = false };
        var outcome = await new AssignmentStatusService(repository).ApplyAsync(Envelope("IN_PROGRESS"), JobId);

        Assert.Equal(AssignmentStatusOutcome.Ignored, outcome);
    }

    [Fact]
    public async Task ApplyAsync_WithAStatusDispatchDoesNotTrack_IsIgnoredWithoutWriting()
    {
        var repository = new FakeAutomaticAssignmentRepository();
        var outcome = await new AssignmentStatusService(repository).ApplyAsync(Envelope("CREATED"), JobId);

        Assert.Equal(AssignmentStatusOutcome.Ignored, outcome);
        Assert.Empty(repository.StatusUpdates);
    }

    [Theory]
    [InlineData("not-a-guid", JobId)]
    [InlineData(JobId, "another-job-key")]
    public async Task ApplyAsync_WithAnInvalidJobIdOrMismatchedKey_IsInvalid(string jobId, string key)
    {
        var repository = new FakeAutomaticAssignmentRepository();
        var outcome = await new AssignmentStatusService(repository).ApplyAsync(Envelope("COMPLETED", jobId), key);

        Assert.Equal(AssignmentStatusOutcome.InvalidEvent, outcome);
        Assert.Empty(repository.StatusUpdates);
    }

    [Fact]
    public async Task ApplyAsync_WithTheWrongEventType_IsInvalid()
    {
        var repository = new FakeAutomaticAssignmentRepository();
        var envelope = Envelope("COMPLETED");
        envelope.EventType = "JobCreated";

        Assert.Equal(AssignmentStatusOutcome.InvalidEvent, await new AssignmentStatusService(repository).ApplyAsync(envelope, JobId));
        Assert.Empty(repository.StatusUpdates);
    }
}
