using DispatchService.Models;
using DispatchService.Repositories;

namespace DispatchService.Tests.Fakes;

public class FakeAutomaticAssignmentRepository : IAutomaticAssignmentRepository
{
    public int AssignCallCount { get; private set; }
    public string? ReceivedEventId { get; private set; }
    public string? ReceivedJobId { get; private set; }
    public AutomaticAssignmentResult AssignmentResult { get; set; } = new(AutomaticAssignmentOutcome.Assigned, null);
    public IReadOnlyList<PendingJobAssignedEvent> PendingEvents { get; set; } = Array.Empty<PendingJobAssignedEvent>();
    public List<string> PublishedEventIds { get; } = [];
    public List<(string EventId, string Reason)> FailedEvents { get; } = [];

    public Task<AutomaticAssignmentResult> AssignFromCandidateEvaluationAsync(string eventId, string jobId, DateTime assignedAt, CancellationToken cancellationToken = default)
    {
        AssignCallCount++;
        ReceivedEventId = eventId;
        ReceivedJobId = jobId;
        return Task.FromResult(AssignmentResult);
    }

    public Task<IReadOnlyList<PendingJobAssignedEvent>> GetPendingJobAssignedEventsAsync(int limit, CancellationToken cancellationToken = default) => Task.FromResult(PendingEvents);
    public Task MarkJobAssignedPublishedAsync(string eventId, DateTime publishedAt, CancellationToken cancellationToken = default)
    {
        PublishedEventIds.Add(eventId);
        return Task.CompletedTask;
    }

    public Task MarkJobAssignedFailedAsync(string eventId, string reason, CancellationToken cancellationToken = default)
    {
        FailedEvents.Add((eventId, reason));
        return Task.CompletedTask;
    }
}
