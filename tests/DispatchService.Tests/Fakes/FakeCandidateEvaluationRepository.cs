using DispatchService.Models;
using DispatchService.Repositories;

namespace DispatchService.Tests.Fakes;

public class FakeCandidateEvaluationRepository : ICandidateEvaluationRepository
{
    public CandidateEvaluation? ReceivedEvaluation { get; private set; }
    public int CallCount { get; private set; }
    public CandidateEvaluationPersistenceResult Result { get; set; } = new(false, 0);
    public Exception? ExceptionToThrow { get; set; }

    public Task<CandidateEvaluationPersistenceResult> EvaluateAsync(CandidateEvaluation evaluation)
    {
        CallCount++;
        ReceivedEvaluation = evaluation;
        if (ExceptionToThrow is not null) throw ExceptionToThrow;
        return Task.FromResult(Result);
    }
}
