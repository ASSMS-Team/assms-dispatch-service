using DispatchService.Models;
using DispatchService.Repositories;

namespace DispatchService.Services;

public enum CandidateEvaluationError { None, InvalidEvent, UnmappedServiceCategory }

public record CandidateEvaluationResult(CandidateEvaluationPersistenceResult? Value, CandidateEvaluationError Error);

public interface ICandidateEvaluationService
{
    Task<CandidateEvaluationResult> EvaluateAsync(
        string eventId,
        string eventType,
        int eventVersion,
        string producer,
        string jobId,
        string jobReference,
        string serviceCategory,
        string region,
        DateTime occurredAt);
}

public class CandidateEvaluationService : ICandidateEvaluationService
{
    private readonly ICandidateEvaluationRepository _repository;
    private readonly RequiredSkillResolver _requiredSkillResolver;

    public CandidateEvaluationService(
        ICandidateEvaluationRepository repository,
        RequiredSkillResolver requiredSkillResolver)
    {
        _repository = repository;
        _requiredSkillResolver = requiredSkillResolver;
    }

    public async Task<CandidateEvaluationResult> EvaluateAsync(
        string eventId,
        string eventType,
        int eventVersion,
        string producer,
        string jobId,
        string jobReference,
        string serviceCategory,
        string region,
        DateTime occurredAt)
    {
        if (!Guid.TryParse(eventId, out _) || !Guid.TryParse(jobId, out _) ||
            !string.Equals(eventType, "JobCreated", StringComparison.Ordinal) ||
            eventVersion != 1 || !string.Equals(producer, "job-service", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(jobReference) || string.IsNullOrWhiteSpace(region) ||
            occurredAt == default)
            return new(null, CandidateEvaluationError.InvalidEvent);

        if (!_requiredSkillResolver.TryResolve(serviceCategory, out var requiredSkill))
            return new(null, CandidateEvaluationError.UnmappedServiceCategory);

        var result = await _repository.EvaluateAsync(new CandidateEvaluation
        {
            EventId = eventId,
            JobId = jobId,
            JobReference = jobReference.Trim(),
            RequiredSkill = requiredSkill,
            NormalizedRegion = region.Trim().ToUpperInvariant(),
            OccurredAt = occurredAt.ToUniversalTime(),
        });

        return new(result, CandidateEvaluationError.None);
    }
}
