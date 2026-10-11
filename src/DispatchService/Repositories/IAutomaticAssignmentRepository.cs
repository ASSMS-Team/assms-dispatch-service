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

    /// <summary>Returns live (non-released, non-terminal) assignments for a technician.</summary>
    Task<IReadOnlyList<AssignmentRecord>> GetByTechnicianIdAsync(string technicianId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the job's assignment forward to <paramref name="jobStatus"/>. Returns
    /// false when the job has no assignment or the assignment is already at or
    /// past <paramref name="statusRank"/>.
    /// </summary>
    Task<bool> UpdateJobStatusAsync(string jobId, string jobStatus, int statusRank, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingJobAssignedEvent>> GetPendingJobAssignedEventsAsync(
        int limit, CancellationToken cancellationToken = default);
    Task MarkJobAssignedPublishedAsync(string eventId, DateTime publishedAt, CancellationToken cancellationToken = default);
    Task MarkJobAssignedFailedAsync(string eventId, string reason, CancellationToken cancellationToken = default);
}

