using DispatchService.Models;

namespace DispatchService.Repositories;

public interface ICandidateEvaluationRepository
{
    /// <summary>
    /// Reserves an event id, queries the current Dispatch-owned technician state,
    /// and writes the evaluation atomically. A repeated event id returns
    /// <see cref="CandidateEvaluationPersistenceResult.IsDuplicate"/>.
    /// </summary>
    Task<CandidateEvaluationPersistenceResult> EvaluateAsync(CandidateEvaluation evaluation);
}
