using DispatchService.Models;
using DispatchService.Services;

namespace DispatchService.Tests.Fakes;

public class FakeAutomaticAssignmentService : IAutomaticAssignmentService
{
    public int CallCount { get; private set; }
    public AutomaticAssignmentResult Result { get; set; } = new(AutomaticAssignmentOutcome.NoEligibleCandidate, null);

    // Left null for the happy path. Set it to stand in for the assignment
    // transaction failing on something outside the message - the database being
    // unreachable, a connection dropping - which is the failure the consume loop
    // must seek back on rather than commit past.
    public Exception? ExceptionToThrow { get; set; }

    // How many opening calls throw before one is allowed to succeed. Zero, the
    // default, never fails. Anything higher stands in for a database that was
    // down and came back.
    public int FailuresBeforeSuccess { get; set; }

    // Runs after the call is counted and before the throw. The consumer tests use
    // it to cancel the host token at the moment of failure, so the loop unwinds
    // instead of sleeping out its retry delay.
    public Action? OnCall { get; set; }

    public readonly List<string> ReceivedJobIds = [];

    public Task<AutomaticAssignmentResult> AssignAsync(string eventId, string jobId, CancellationToken cancellationToken = default)
    {
        CallCount++;
        ReceivedJobIds.Add(jobId);

        OnCall?.Invoke();

        if (CallCount <= FailuresBeforeSuccess)
            throw ExceptionToThrow ?? new InvalidOperationException("The database was unreachable.");

        if (ExceptionToThrow is not null) throw ExceptionToThrow;

        return Task.FromResult(Result);
    }
}
