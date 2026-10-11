using DispatchService.Messaging.Contracts;
using DispatchService.Repositories;

namespace DispatchService.Services;

public enum AssignmentStatusOutcome { Updated, Ignored, InvalidEvent }

public interface IAssignmentStatusService
{
    Task<AssignmentStatusOutcome> ApplyAsync(
        EventEnvelope<JobStatusChangedPayload> envelope, string? messageKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies Job Service lifecycle changes to the Dispatch assignment so the
/// Technician's current list, deactivation and open-job counts see the job's
/// real status. A COMPLETED or CANCELLED status closes the assignment.
/// </summary>
public class AssignmentStatusService : IAssignmentStatusService
{
    // Status only moves forward. A replayed or out-of-order event therefore
    // cannot reopen a closed assignment or move IN_PROGRESS back to ASSIGNED.
    private static readonly Dictionary<string, int> StatusRank = new(StringComparer.Ordinal)
    {
        ["ASSIGNED"] = 1,
        ["IN_PROGRESS"] = 2,
        ["COMPLETED"] = 3,
        ["CANCELLED"] = 3,
    };

    private readonly IAutomaticAssignmentRepository _repository;

    public AssignmentStatusService(IAutomaticAssignmentRepository repository)
    {
        _repository = repository;
    }

    public async Task<AssignmentStatusOutcome> ApplyAsync(
        EventEnvelope<JobStatusChangedPayload> envelope, string? messageKey, CancellationToken cancellationToken = default)
    {
        // Timestamps are not validated: Dispatch does not use them, and Job
        // Service publishes payload.occurredAt without a UTC designator.
        var payload = envelope.Payload;
        if (payload is null
            || !Guid.TryParse(envelope.EventId, out _)
            || !string.Equals(envelope.EventType, JobStatusChangedPayload.EventType, StringComparison.Ordinal)
            || envelope.EventVersion != JobStatusChangedPayload.EventVersion
            || !string.Equals(envelope.Producer, "job-service", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(payload.JobId, out var jobId)
            || string.IsNullOrWhiteSpace(payload.NewStatus)
            || (messageKey is not null && !string.Equals(messageKey, payload.JobId, StringComparison.OrdinalIgnoreCase)))
            return AssignmentStatusOutcome.InvalidEvent;

        var newStatus = payload.NewStatus.Trim().ToUpperInvariant();
        // CREATED and any status Dispatch does not track say nothing about an assignment.
        if (!StatusRank.TryGetValue(newStatus, out var rank))
            return AssignmentStatusOutcome.Ignored;

        var updated = await _repository.UpdateJobStatusAsync(jobId.ToString(), newStatus, rank, cancellationToken);
        return updated ? AssignmentStatusOutcome.Updated : AssignmentStatusOutcome.Ignored;
    }
}
