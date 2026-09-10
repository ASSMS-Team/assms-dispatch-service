namespace DispatchService.Models;

/// <summary>A durable record of a JobCreated eligibility evaluation.</summary>
public class CandidateEvaluation
{
    public string EventId { get; init; } = string.Empty;
    public string JobId { get; init; } = string.Empty;
    public string JobReference { get; init; } = string.Empty;
    public string RequiredSkill { get; init; } = string.Empty;
    public string NormalizedRegion { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
}

public record CandidateEvaluationPersistenceResult(bool IsDuplicate, int CandidateCount);
