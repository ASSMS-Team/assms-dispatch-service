namespace DispatchService.Models;

/// <summary>A durable record of a JobCreated eligibility evaluation.</summary>
public class CandidateEvaluation
{
    public string EventId { get; init; } = string.Empty;
    public string JobId { get; init; } = string.Empty;
    public string JobReference { get; init; } = string.Empty;
    // Job summary shown on the Technician's assignment list (US-09 AC2).
    public string? ServiceCategory { get; init; }
    public string? Priority { get; init; }
    public string? ProblemDescription { get; init; }
    public string RequiredSkill { get; init; } = string.Empty;
    public string NormalizedRegion { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
}

public record CandidateEvaluationPersistenceResult(bool IsDuplicate, int CandidateCount);
