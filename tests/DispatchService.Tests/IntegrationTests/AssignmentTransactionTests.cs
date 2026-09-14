using System.Text.Json;

using DispatchService.Messaging.Contracts;
using DispatchService.Models;
using DispatchService.Repositories;
using DispatchService.Services;

using MySqlConnector;

namespace DispatchService.Tests.IntegrationTests;

// The US-06B assignment transaction, against a real MySQL server.
//
// Everything here is behaviour a fake cannot reproduce, which is the whole reason
// these tests exist separately from the unit suite:
//
//   - the assignment row and its outbox row committing or rolling back together;
//   - uq_technician_assignments_job refusing a second assignment, including when
//     two deliveries race;
//   - the candidate SQL computing open-job counts and last-assigned timestamps,
//     which is the half of the tie rule AutomaticAssignmentSelectorTests cannot
//     reach - those tests hand the selector a list, and this one proves the list
//     is built correctly;
//   - the NO_CANDIDATE outcome committing its evaluation rather than rolling back.
//
// Skipped unless ASSMS_DISPATCH_TEST_CONNECTION is set. See MySqlTestEnvironment.
public sealed class AssignmentTransactionTests : IAsyncLifetime
{
    private const string Region = "WESTERN";
    private const string RequiredSkill = "AC";
    private const string JobReference = "JOB-7K2M9X";

    private readonly string _jobId = Guid.NewGuid().ToString();
    private readonly string _eventId = Guid.NewGuid().ToString();

    private IDbConnectionFactory _connectionFactory = null!;
    private AutomaticAssignmentRepository _repository = null!;

    public async Task InitializeAsync()
    {
        if (!MySqlTestEnvironment.IsConfigured) return;

        // Checked before a connection is even opened. Every test below empties
        // all six Dispatch tables, so the one unrecoverable mistake here is
        // aiming them at a database that holds real data.
        MySqlTestEnvironment.EnsureSafeTargetDatabase();

        _connectionFactory = new MySqlConnectionFactory(MySqlTestEnvironment.ConnectionString!);
        _repository = new AutomaticAssignmentRepository(_connectionFactory);

        // The service's own runner, so the tests run against exactly the schema a
        // deployment produces rather than one written out by hand here. It records
        // what it applied, so this is cheap after the first test.
        await new DispatchMigrationRunner(_connectionFactory).ApplyAsync();

        await ResetAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Child tables first: assignment_outbox has a foreign key to
    // technician_assignments, the candidate join table to both evaluations and
    // technicians, and technician_skills to technicians.
    private async Task ResetAsync()
    {
        await ExecuteAsync("DELETE FROM assignment_outbox;");
        await ExecuteAsync("DELETE FROM technician_assignments;");
        await ExecuteAsync("DELETE FROM job_candidate_evaluation_candidates;");
        await ExecuteAsync("DELETE FROM job_candidate_evaluations;");
        await ExecuteAsync("DELETE FROM technician_skills;");
        await ExecuteAsync("DELETE FROM technicians;");
    }

    // ---- the atomic commit -------------------------------------------------

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_CommitsTheAssignmentAndItsOutboxRowTogether()
    {
        // Arrange
        var technicianId = await AddTechnicianAsync("TECH-0001");
        await AddEvaluationAsync(technicianId);

        // Act
        var result = await _repository.AssignFromCandidateEvaluationAsync(
            _eventId, _jobId, DateTime.UtcNow);

        // Assert - the outcome, and then both rows, and then that they point at
        // each other. An assignment with no outbox row would mean nobody
        // downstream is ever told; an outbox row with no assignment would announce
        // work that was never given to anyone.
        Assert.Equal(AutomaticAssignmentOutcome.Assigned, result.Outcome);
        Assert.Equal(technicianId, result.Assignment!.TechnicianId);

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM technician_assignments WHERE job_id = @jobId;"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM assignment_outbox o JOIN technician_assignments a ON a.id = o.assignment_id WHERE a.job_id = @jobId;"));

        // The outbox row starts unpublished, which is what makes it visible to the
        // publisher's poll the moment this transaction commits.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM assignment_outbox o JOIN technician_assignments a ON a.id = o.assignment_id WHERE a.job_id = @jobId AND o.published_at IS NULL;"));
    }

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_StoresAnEnvelopeMatchingTheSixFieldContract()
    {
        // Arrange
        var technicianId = await AddTechnicianAsync("TECH-0001");
        await AddEvaluationAsync(technicianId);

        // Act
        var result = await _repository.AssignFromCandidateEvaluationAsync(
            _eventId, _jobId, DateTime.UtcNow);

        // Assert - the envelope is serialized inside the transaction and stored as
        // written, so what is in this column is byte for byte what reaches the
        // topic. Reading it back is the closest a test can get to reading the
        // published message.
        var payload = await ScalarStringAsync(
            "SELECT o.payload FROM assignment_outbox o JOIN technician_assignments a ON a.id = o.assignment_id WHERE a.job_id = @jobId;");

        using var document = JsonDocument.Parse(payload!);
        var root = document.RootElement;

        Assert.Equal("JobAssigned", root.GetProperty("eventType").GetString());
        Assert.Equal(1, root.GetProperty("eventVersion").GetInt32());
        Assert.Equal("dispatch-service", root.GetProperty("producer").GetString());

        // Exactly the six fields, in camelCase. A seventh here, or a missing one,
        // means Job Service and Reporting are reading a contract Dispatch is no
        // longer publishing.
        Assert.Equal(
            new[] { "assignedAt", "assignmentId", "jobId", "jobReference", "technicianId", "technicianReference" },
            root.GetProperty("payload")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

        var eventPayload = root.GetProperty("payload");

        Assert.Equal(_jobId, eventPayload.GetProperty("jobId").GetString());
        Assert.Equal(JobReference, eventPayload.GetProperty("jobReference").GetString());
        Assert.Equal(result.Assignment!.Id, eventPayload.GetProperty("assignmentId").GetString());
        Assert.Equal(technicianId, eventPayload.GetProperty("technicianId").GetString());
        Assert.Equal("TECH-0001", eventPayload.GetProperty("technicianReference").GetString());
    }

    // ---- the rollback and the duplicate guards -----------------------------

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_CalledTwice_AssignsOnceAndQueuesOneEvent()
    {
        // Arrange - the redelivery every at-least-once consumer eventually sees.
        var technicianId = await AddTechnicianAsync("TECH-0001");
        await AddEvaluationAsync(technicianId);

        // Act
        var first = await _repository.AssignFromCandidateEvaluationAsync(_eventId, _jobId, DateTime.UtcNow);
        var second = await _repository.AssignFromCandidateEvaluationAsync(_eventId, _jobId, DateTime.UtcNow);

        // Assert - the second call finds the existing assignment and changes
        // nothing. One assignment, and crucially one outbox row: a second would
        // publish a duplicate JobAssigned carrying a different eventId, which no
        // consumer could deduplicate.
        Assert.Equal(AutomaticAssignmentOutcome.Assigned, first.Outcome);
        Assert.Equal(AutomaticAssignmentOutcome.AlreadyAssigned, second.Outcome);

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM technician_assignments WHERE job_id = @jobId;"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM assignment_outbox o JOIN technician_assignments a ON a.id = o.assignment_id WHERE a.job_id = @jobId;"));
    }

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_WhenTheOutboxInsertCannotRun_LeavesNoAssignmentBehind()
    {
        // Arrange - the rollback half of atomicity, forced deterministically.
        //
        // Renaming assignment_outbox makes the final INSERT in the transaction
        // fail on a table that does not exist. Everything before it - the
        // assignment row - has already been written inside that transaction, so
        // if the rollback did not work the assignment would survive on its own:
        // a job assigned to a technician that no other service is ever told about.
        var technicianId = await AddTechnicianAsync("TECH-0001");
        await AddEvaluationAsync(technicianId);

        await ExecuteAsync("ALTER TABLE assignment_outbox RENAME TO assignment_outbox_hidden;");

        try
        {
            // Act - the repository rethrows anything that is not a duplicate key.
            await Assert.ThrowsAsync<MySqlException>(() =>
                _repository.AssignFromCandidateEvaluationAsync(_eventId, _jobId, DateTime.UtcNow));
        }
        finally
        {
            await ExecuteAsync("ALTER TABLE assignment_outbox_hidden RENAME TO assignment_outbox;");
        }

        // Assert - nothing survived. The job is still unassigned, so the consumer
        // seeking back and retrying will assign it cleanly once the fault is gone.
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM technician_assignments WHERE job_id = @jobId;"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM assignment_outbox;"));
    }

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_UnderConcurrentDelivery_StillAssignsExactlyOnce()
    {
        // Arrange - two consumers handed the same event at the same moment, which
        // is what a rebalance or a replay alongside live traffic can produce.
        var technicianId = await AddTechnicianAsync("TECH-0001");
        await AddEvaluationAsync(technicianId);

        // Act - both run against their own connection, as two processes would.
        var first = RunGuardedAsync();
        var second = RunGuardedAsync();

        await Task.WhenAll(first, second);

        // Assert - the invariant, not which call won. One of the two may also fail
        // outright: Serializable isolation takes gap locks on the FOR UPDATE read,
        // so InnoDB can resolve the race as a deadlock (1213) rather than as a
        // duplicate key (1062). That is acceptable and is handled correctly
        // upstream - the repository rethrows, the consumer seeks back, and the
        // retry finds the assignment already made. What must never happen is two
        // assignments or two queued events.
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM technician_assignments WHERE job_id = @jobId;"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM assignment_outbox o JOIN technician_assignments a ON a.id = o.assignment_id WHERE a.job_id = @jobId;"));

        async Task RunGuardedAsync()
        {
            try
            {
                await new AutomaticAssignmentRepository(_connectionFactory)
                    .AssignFromCandidateEvaluationAsync(_eventId, _jobId, DateTime.UtcNow);
            }
            catch (MySqlException)
            {
                // A deadlock or lock-wait timeout is a legitimate outcome of the
                // race. The count assertions above are what actually matter.
            }
        }
    }

    // ---- no eligible candidate ---------------------------------------------

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_WithNoEligibleCandidate_FlagsForDispatcherAndWritesNeitherRow()
    {
        // Arrange - an evaluation with no candidates attached, which is what
        // US-06A records when nobody matched.
        await AddEvaluationAsync();

        // Act
        var result = await _repository.AssignFromCandidateEvaluationAsync(
            _eventId, _jobId, DateTime.UtcNow);

        // Assert
        Assert.Equal(AutomaticAssignmentOutcome.NoEligibleCandidate, result.Outcome);
        Assert.Null(result.Assignment);

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM technician_assignments WHERE job_id = @jobId;"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM assignment_outbox;"));

        // The evaluation is committed, not rolled back: that nobody was eligible
        // is a fact worth keeping, and the flag is how a Dispatcher finds the job
        // afterwards.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM job_candidate_evaluations WHERE job_id = @jobId AND evaluation_status = 'NO_CANDIDATE' AND requires_dispatcher_attention = TRUE;"));
    }

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_IgnoresAnInactiveTechnician()
    {
        // Arrange - a candidate who was eligible when US-06A evaluated but has
        // since been deactivated. The candidate query re-checks status at
        // assignment time rather than trusting the stored list.
        var technicianId = await AddTechnicianAsync("TECH-0001", status: "INACTIVE");
        await AddEvaluationAsync(technicianId);

        // Act
        var result = await _repository.AssignFromCandidateEvaluationAsync(
            _eventId, _jobId, DateTime.UtcNow);

        // Assert
        Assert.Equal(AutomaticAssignmentOutcome.NoEligibleCandidate, result.Outcome);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM technician_assignments WHERE job_id = @jobId;"));
    }

    // ---- the tie rule, against real SQL ------------------------------------

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_PrefersTheTechnicianWithFewerOpenJobs()
    {
        // Arrange - the first level of the US-06B rule, computed by the candidate
        // query rather than handed to the selector. Busy carries one open job;
        // quiet carries none.
        var busy = await AddTechnicianAsync("TECH-0001");
        var quiet = await AddTechnicianAsync("TECH-0002");

        await AddExistingAssignmentAsync(busy, jobStatus: "ASSIGNED", assignedAt: DateTime.UtcNow.AddHours(-5));
        await AddEvaluationAsync(busy, quiet);

        // Act
        var result = await _repository.AssignFromCandidateEvaluationAsync(
            _eventId, _jobId, DateTime.UtcNow);

        // Assert
        Assert.Equal(AutomaticAssignmentOutcome.Assigned, result.Outcome);
        Assert.Equal(quiet, result.Assignment!.TechnicianId);
    }

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_DoesNotCountCompletedOrCancelledJobsAsOpen()
    {
        // Arrange - the SQL counts an assignment as open only while released_at is
        // null and the status is neither COMPLETED nor CANCELLED. A technician
        // whose history is all closed work must score zero, not three.
        var finished = await AddTechnicianAsync("TECH-0001");
        var quiet = await AddTechnicianAsync("TECH-0002");

        await AddExistingAssignmentAsync(finished, jobStatus: "COMPLETED", assignedAt: DateTime.UtcNow.AddHours(-9));
        await AddExistingAssignmentAsync(finished, jobStatus: "CANCELLED", assignedAt: DateTime.UtcNow.AddHours(-8));
        await AddExistingAssignmentAsync(finished, jobStatus: "ASSIGNED", assignedAt: DateTime.UtcNow.AddHours(-7), released: true);

        // quiet has never been assigned at all, so it wins the second level of the
        // rule - never-assigned sorts ahead of finished's older timestamp.
        await AddEvaluationAsync(finished, quiet);

        // Act
        var result = await _repository.AssignFromCandidateEvaluationAsync(
            _eventId, _jobId, DateTime.UtcNow);

        // Assert - both score zero open jobs, so the tie falls to last-assigned,
        // and never-assigned comes first.
        Assert.Equal(quiet, result.Assignment!.TechnicianId);
    }

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_WhenWorkloadTies_PrefersTheOldestLastAssignment()
    {
        // Arrange - both have one closed job, so open counts tie at zero and the
        // rule falls to the second level. The SQL's MAX(assigned_at) is what
        // supplies it.
        var recent = await AddTechnicianAsync("TECH-0001");
        var stale = await AddTechnicianAsync("TECH-0002");

        await AddExistingAssignmentAsync(recent, jobStatus: "COMPLETED", assignedAt: DateTime.UtcNow.AddHours(-1));
        await AddExistingAssignmentAsync(stale, jobStatus: "COMPLETED", assignedAt: DateTime.UtcNow.AddDays(-30));

        await AddEvaluationAsync(recent, stale);

        // Act
        var result = await _repository.AssignFromCandidateEvaluationAsync(
            _eventId, _jobId, DateTime.UtcNow);

        // Assert
        Assert.Equal(stale, result.Assignment!.TechnicianId);
    }

    [RequiresMySqlFact]
    public async Task AssignFromCandidateEvaluation_WhenEverythingTies_TakesTheLowestReferenceOrdinally()
    {
        // Arrange - two technicians identical in every respect the rule looks at.
        // The third level has to settle it, and settle it the same way every time:
        // an assignment nobody can reproduce is one nobody can explain.
        var second = await AddTechnicianAsync("TECH-0002");
        var first = await AddTechnicianAsync("TECH-0001");

        await AddEvaluationAsync(second, first);

        // Act
        var result = await _repository.AssignFromCandidateEvaluationAsync(
            _eventId, _jobId, DateTime.UtcNow);

        // Assert
        Assert.Equal(first, result.Assignment!.TechnicianId);
        Assert.Equal("TECH-0001", result.Assignment.TechnicianReference);
    }

    // ---- seeding helpers ---------------------------------------------------

    private async Task<string> AddTechnicianAsync(string reference, string status = "ACTIVE")
    {
        var id = Guid.NewGuid().ToString();

        await ExecuteAsync(
            "INSERT INTO technicians (id, technician_reference, full_name, region, status) "
            + "VALUES (@id, @reference, @fullName, @region, @status);",
            command =>
            {
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@reference", reference);
                command.Parameters.AddWithValue("@fullName", $"Test Technician {reference}");
                command.Parameters.AddWithValue("@region", Region);
                command.Parameters.AddWithValue("@status", status);
            });

        await ExecuteAsync(
            "INSERT INTO technician_skills (technician_id, skill) VALUES (@id, @skill);",
            command =>
            {
                command.Parameters.AddWithValue("@id", id);
                command.Parameters.AddWithValue("@skill", RequiredSkill);
            });

        return id;
    }

    private async Task AddExistingAssignmentAsync(
        string technicianId,
        string jobStatus,
        DateTime assignedAt,
        bool released = false)
    {
        await ExecuteAsync(
            "INSERT INTO technician_assignments (id, technician_id, job_id, job_status, assigned_at, released_at) "
            + "VALUES (@id, @technicianId, @jobId, @jobStatus, @assignedAt, @releasedAt);",
            command =>
            {
                command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                command.Parameters.AddWithValue("@technicianId", technicianId);
                // A different job from the one under test - this is history, and
                // uq_technician_assignments_job allows only one row per job.
                command.Parameters.AddWithValue("@jobId", Guid.NewGuid().ToString());
                command.Parameters.AddWithValue("@jobStatus", jobStatus);
                command.Parameters.AddWithValue("@assignedAt", assignedAt);
                command.Parameters.AddWithValue(
                    "@releasedAt", released ? assignedAt.AddHours(1) : (object)DBNull.Value);
            });
    }

    // Writes the US-06A evaluation the assignment reads its candidates from.
    // Pass no technicians for the NO_CANDIDATE case.
    private async Task AddEvaluationAsync(params string?[] technicianIds)
    {
        var candidates = technicianIds.Where(id => id is not null).Cast<string>().ToArray();

        await ExecuteAsync(
            "INSERT INTO job_candidate_evaluations "
            + "(event_id, job_id, job_reference, required_skill, normalized_region, evaluation_status, candidate_count, occurred_at) "
            + "VALUES (@eventId, @jobId, @jobReference, @skill, @region, @status, @count, @occurredAt);",
            command =>
            {
                command.Parameters.AddWithValue("@eventId", _eventId);
                command.Parameters.AddWithValue("@jobId", _jobId);
                command.Parameters.AddWithValue("@jobReference", JobReference);
                command.Parameters.AddWithValue("@skill", RequiredSkill);
                command.Parameters.AddWithValue("@region", Region);
                command.Parameters.AddWithValue(
                    "@status", candidates.Length == 0 ? "NO_CANDIDATE" : "CANDIDATES_FOUND");
                command.Parameters.AddWithValue("@count", candidates.Length);
                command.Parameters.AddWithValue("@occurredAt", DateTime.UtcNow);
            });

        if (candidates.Length == 0) return;

        var evaluationId = await CountAsync(
            "SELECT id FROM job_candidate_evaluations WHERE event_id = @eventId AND job_id = @jobId;");

        foreach (var technicianId in candidates)
        {
            await ExecuteAsync(
                "INSERT INTO job_candidate_evaluation_candidates (evaluation_id, technician_id) "
                + "VALUES (@evaluationId, @technicianId);",
                command =>
                {
                    command.Parameters.AddWithValue("@evaluationId", evaluationId);
                    command.Parameters.AddWithValue("@technicianId", technicianId);
                });
        }
    }

    // ---- database helpers --------------------------------------------------

    private async Task ExecuteAsync(string sql, Action<MySqlCommand>? bind = null)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind?.Invoke(command);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@jobId", _jobId);
        command.Parameters.AddWithValue("@eventId", _eventId);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<string?> ScalarStringAsync(string sql)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@jobId", _jobId);

        return Convert.ToString(await command.ExecuteScalarAsync());
    }
}
