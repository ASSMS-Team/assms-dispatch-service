using DispatchService.Models;
using MySqlConnector;

namespace DispatchService.Repositories;

/// <summary>ADO.NET persistence for candidate evaluation; it owns no Job data.</summary>
public class CandidateEvaluationRepository : ICandidateEvaluationRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CandidateEvaluationRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<CandidateEvaluationPersistenceResult> EvaluateAsync(CandidateEvaluation evaluation)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            long evaluationId;
            try
            {
                await using var reserve = connection.CreateCommand();
                reserve.Transaction = transaction;
                reserve.CommandText = @"
INSERT INTO job_candidate_evaluations
    (event_id, job_id, job_reference, required_skill, normalized_region, evaluation_status, candidate_count, occurred_at)
VALUES
    (@eventId, @jobId, @jobReference, @requiredSkill, @region, 'PROCESSING', 0, @occurredAt);";
                reserve.Parameters.AddWithValue("@eventId", evaluation.EventId);
                reserve.Parameters.AddWithValue("@jobId", evaluation.JobId);
                reserve.Parameters.AddWithValue("@jobReference", evaluation.JobReference);
                reserve.Parameters.AddWithValue("@requiredSkill", evaluation.RequiredSkill);
                reserve.Parameters.AddWithValue("@region", evaluation.NormalizedRegion);
                reserve.Parameters.AddWithValue("@occurredAt", evaluation.OccurredAt);
                await reserve.ExecuteNonQueryAsync();
                evaluationId = reserve.LastInsertedId;
            }
            catch (MySqlException exception) when (exception.Number == 1062)
            {
                await transaction.RollbackAsync();
                return new CandidateEvaluationPersistenceResult(true, 0);
            }

            var candidates = new List<string>();
            await using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                // Both values are parameters. This is the authoritative matching
                // query and is deliberately evaluated afresh for every new event,
                // so technician edits and deactivation affect later evaluations.
                select.CommandText = @"
SELECT t.id
FROM technicians t
WHERE t.status = 'ACTIVE'
  AND UPPER(TRIM(t.region)) = @region
  AND EXISTS (
      SELECT 1
      FROM technician_skills ts
      WHERE ts.technician_id = t.id
        AND UPPER(TRIM(ts.skill)) = @requiredSkill)
ORDER BY t.technician_reference;";
                select.Parameters.AddWithValue("@region", evaluation.NormalizedRegion);
                select.Parameters.AddWithValue("@requiredSkill", evaluation.RequiredSkill.ToUpperInvariant());

                await using var reader = await select.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    candidates.Add(reader.GetValue(0).ToString()!);
            }

            foreach (var technicianId in candidates)
            {
                await using var candidate = connection.CreateCommand();
                candidate.Transaction = transaction;
                candidate.CommandText = @"
INSERT INTO job_candidate_evaluation_candidates (evaluation_id, technician_id)
VALUES (@evaluationId, @technicianId);";
                candidate.Parameters.AddWithValue("@evaluationId", evaluationId);
                candidate.Parameters.AddWithValue("@technicianId", technicianId);
                await candidate.ExecuteNonQueryAsync();
            }

            await using (var complete = connection.CreateCommand())
            {
                complete.Transaction = transaction;
                complete.CommandText = @"
UPDATE job_candidate_evaluations
SET evaluation_status = @status,
    candidate_count = @candidateCount,
    requires_dispatcher_attention = @requiresAttention
WHERE id = @id;";
                complete.Parameters.AddWithValue("@status", candidates.Count == 0 ? "NO_CANDIDATE" : "CANDIDATES_FOUND");
                complete.Parameters.AddWithValue("@candidateCount", candidates.Count);
                complete.Parameters.AddWithValue("@requiresAttention", candidates.Count == 0);
                complete.Parameters.AddWithValue("@id", evaluationId);
                await complete.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return new CandidateEvaluationPersistenceResult(false, candidates.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
