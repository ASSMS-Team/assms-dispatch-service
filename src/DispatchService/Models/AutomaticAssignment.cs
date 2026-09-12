namespace DispatchService.Models;

/// <summary>A Dispatch-owned assignment selected from a durable candidate evaluation.</summary>
public record AssignmentRecord(
    string Id,
    string JobId,
    string JobReference,
    string TechnicianId,
    string TechnicianReference,
    DateTime AssignedAt);

public enum AutomaticAssignmentOutcome
{
    Assigned,
    AlreadyAssigned,
    NoEligibleCandidate,
}

public record AutomaticAssignmentResult(AutomaticAssignmentOutcome Outcome, AssignmentRecord? Assignment)
{
    public bool IsDuplicate => Outcome == AutomaticAssignmentOutcome.AlreadyAssigned;
}

/// <summary>A candidate with the workload facts needed for deterministic selection.</summary>
public record AssignmentCandidate(
    string TechnicianId,
    string TechnicianReference,
    int OpenJobCount,
    DateTime? LastAssignedAt);

/// <summary>A committed event which has not yet been acknowledged by Kafka.</summary>
public record PendingJobAssignedEvent(string Id, string JobId, string Topic, string Payload, int PublishAttempts);
