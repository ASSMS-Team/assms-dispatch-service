using DispatchService.Services;

namespace DispatchService.Tests.Fakes;

public class FakeCandidateEvaluationService : ICandidateEvaluationService
{
    public int CallCount { get; private set; }
    public CandidateEvaluationResult Result { get; set; } = new(new(false, 1), CandidateEvaluationError.None);
    public Exception? ExceptionToThrow { get; set; }
    public Action? OnCall { get; set; }

    public Task<CandidateEvaluationResult> EvaluateAsync(
        string eventId, string eventType, int eventVersion, string producer, string jobId, string jobReference,
        string serviceCategory, string region, DateTime occurredAt)
    {
        CallCount++;
        OnCall?.Invoke();
        if (ExceptionToThrow is not null) throw ExceptionToThrow;
        return Task.FromResult(Result);
    }
}
