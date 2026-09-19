using System.Data;
using System.Text.Json;
using DispatchService.Messaging.Contracts;
using DispatchService.Models;
using DispatchService.Services;
using MySqlConnector;

namespace DispatchService.Repositories;

/// <summary>ADO.NET transaction boundary for US-06B assignments and their outbox records.</summary>
public class AutomaticAssignmentRepository : IAutomaticAssignmentRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IDbConnectionFactory _connectionFactory;

    public AutomaticAssignmentRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AutomaticAssignmentResult> AssignFromCandidateEvaluationAsync(
        string eventId, string jobId, DateTime assignedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var existing = await GetAssignmentForJobAsync(connection, transaction, jobId, cancellationToken);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new(AutomaticAssignmentOutcome.AlreadyAssigned, existing);
            }

            var candidates = await GetCandidatesAsync(connection, transaction, eventId, jobId, cancellationToken);
            var selected = AutomaticAssignmentSelector.Select(candidates);
            if (selected is null)
            {
                await MarkNoCandidateAsync(connection, transaction, eventId, jobId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new(AutomaticAssignmentOutcome.NoEligibleCandidate, null);
            }

            var assignment = new AssignmentRecord(
                Guid.NewGuid().ToString(), jobId, await GetJobReferenceAsync(connection, transaction, eventId, jobId, cancellationToken),
                selected.TechnicianId, selected.TechnicianReference, assignedAt.ToUniversalTime());
            var eventIdForOutbox = Guid.NewGuid().ToString();
            var payload = JsonSerializer.Serialize(new EventEnvelope<JobAssignedPayload>
            {
                EventId = eventIdForOutbox,
                EventType = JobAssignedPayload.EventType,
                EventVersion = JobAssignedPayload.EventVersion,
                OccurredAt = assignment.AssignedAt,
                Producer = "dispatch-service",
                Payload = new JobAssignedPayload
                {
                    AssignmentId = assignment.Id,
                    JobId = assignment.JobId,
                    JobReference = assignment.JobReference,
                    TechnicianId = assignment.TechnicianId,
                    TechnicianReference = assignment.TechnicianReference,
                    AssignedAt = assignment.AssignedAt,
                },
            }, JsonOptions);

            await InsertAssignmentAsync(connection, transaction, assignment, cancellationToken);
            await InsertOutboxAsync(connection, transaction, eventIdForOutbox, assignment, payload, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(AutomaticAssignmentOutcome.Assigned, assignment);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            // The unique job constraint is the final guard if two consumers race.
            return new(AutomaticAssignmentOutcome.AlreadyAssigned, null);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<PendingJobAssignedEvent>> GetPendingJobAssignedEventsAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT o.id, a.job_id, o.topic, o.payload, o.publish_attempts
FROM assignment_outbox o
JOIN technician_assignments a ON a.id = o.assignment_id
WHERE o.published_at IS NULL
ORDER BY o.created_at, o.id
LIMIT @limit;";
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 100));

        var result = new List<PendingJobAssignedEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(
                Convert.ToString(reader.GetValue(0))!,
                Convert.ToString(reader.GetValue(1))!,
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        return result;
    }

    public async Task MarkJobAssignedPublishedAsync(string eventId, DateTime publishedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE assignment_outbox
SET published_at = @publishedAt, publish_attempts = publish_attempts + 1, last_error = NULL
WHERE id = @id AND published_at IS NULL;";
        command.Parameters.AddWithValue("@id", eventId);
        command.Parameters.AddWithValue("@publishedAt", publishedAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkJobAssignedFailedAsync(string eventId, string reason, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE assignment_outbox
SET publish_attempts = publish_attempts + 1, last_error = @reason
WHERE id = @id AND published_at IS NULL;";
        command.Parameters.AddWithValue("@id", eventId);
        command.Parameters.AddWithValue("@reason", reason.Length > 1000 ? reason[..1000] : reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<AssignmentRecord?> GetAssignmentForJobAsync(MySqlConnection connection, MySqlTransaction transaction, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
SELECT a.id, a.job_id, a.technician_id, t.technician_reference, a.assigned_at
FROM technician_assignments a
JOIN technicians t ON t.id = a.technician_id
WHERE a.job_id = @jobId
LIMIT 1
FOR UPDATE;";
        command.Parameters.AddWithValue("@jobId", jobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(
            Convert.ToString(reader.GetValue(0))!,
            Convert.ToString(reader.GetValue(1))!,
            string.Empty,
            Convert.ToString(reader.GetValue(2))!,
            reader.GetString(3),
            reader.GetDateTime(4));
    }

    private static async Task<IReadOnlyList<AssignmentCandidate>> GetCandidatesAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
SELECT t.id,
       t.technician_reference,
       SUM(CASE WHEN a.released_at IS NULL AND UPPER(a.job_status) NOT IN ('COMPLETED', 'CANCELLED') THEN 1 ELSE 0 END) AS open_job_count,
       MAX(a.assigned_at) AS last_assigned_at
FROM job_candidate_evaluations e
JOIN job_candidate_evaluation_candidates c ON c.evaluation_id = e.id
JOIN technicians t ON t.id = c.technician_id AND t.status = 'ACTIVE'
LEFT JOIN technician_assignments a ON a.technician_id = t.id
WHERE e.event_id = @eventId AND e.job_id = @jobId
GROUP BY t.id, t.technician_reference;";
        command.Parameters.AddWithValue("@eventId", eventId);
        command.Parameters.AddWithValue("@jobId", jobId);

        var candidates = new List<AssignmentCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new(
                Convert.ToString(reader.GetValue(0))!, reader.GetString(1), Convert.ToInt32(reader.GetValue(2)),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        }
        return candidates;
    }

    private static async Task<string> GetJobReferenceAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT job_reference FROM job_candidate_evaluations WHERE event_id = @eventId AND job_id = @jobId LIMIT 1;";
        command.Parameters.AddWithValue("@eventId", eventId);
        command.Parameters.AddWithValue("@jobId", jobId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    private static async Task MarkNoCandidateAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId, string jobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
UPDATE job_candidate_evaluations
SET evaluation_status = 'NO_CANDIDATE', candidate_count = 0, requires_dispatcher_attention = TRUE
WHERE event_id = @eventId AND job_id = @jobId;";
        command.Parameters.AddWithValue("@eventId", eventId);
        command.Parameters.AddWithValue("@jobId", jobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAssignmentAsync(MySqlConnection connection, MySqlTransaction transaction, AssignmentRecord assignment, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO technician_assignments (id, technician_id, job_id, job_status, assigned_at)
VALUES (@id, @technicianId, @jobId, 'ASSIGNED', @assignedAt);";
        command.Parameters.AddWithValue("@id", assignment.Id);
        command.Parameters.AddWithValue("@technicianId", assignment.TechnicianId);
        command.Parameters.AddWithValue("@jobId", assignment.JobId);
        command.Parameters.AddWithValue("@assignedAt", assignment.AssignedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOutboxAsync(MySqlConnection connection, MySqlTransaction transaction, string eventId, AssignmentRecord assignment, string payload, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO assignment_outbox (id, assignment_id, event_type, topic, payload)
VALUES (@id, @assignmentId, @eventType, @topic, CAST(@payload AS JSON));";
        command.Parameters.AddWithValue("@id", eventId);
        command.Parameters.AddWithValue("@assignmentId", assignment.Id);
        command.Parameters.AddWithValue("@eventType", JobAssignedPayload.EventType);
        command.Parameters.AddWithValue("@topic", JobAssignedPayload.Topic);
        command.Parameters.AddWithValue("@payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
