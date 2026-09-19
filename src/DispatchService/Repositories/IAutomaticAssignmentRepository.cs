using DispatchService.Models;

namespace DispatchService.Repositories;

public interface IAutomaticAssignmentRepository
{
    /// <summary>
    /// Selects from the candidate set stored by US-06A, then commits the
    /// assignment and its outbox event in one database transaction.
    /// </summary>
    Task<AutomaticAssignmentResult> AssignFromCandidateEvaluationAsync(
        string eventId, string jobId, DateTime assignedAt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingJobAssignedEvent>> GetPendingJobAssignedEventsAsync(
        int limit, CancellationToken cancellationToken = default);
    Task MarkJobAssignedPublishedAsync(string eventId, DateTime publishedAt, CancellationToken cancellationToken = default);
    Task MarkJobAssignedFailedAsync(string eventId, string reason, CancellationToken cancellationToken = default);
}
