using DispatchService.Models;
using DispatchService.Services;

namespace DispatchService.Tests.Fakes;

public class FakeAutomaticAssignmentService : IAutomaticAssignmentService
{
    public int CallCount { get; private set; }
    public AutomaticAssignmentResult Result { get; set; } = new(AutomaticAssignmentOutcome.NoEligibleCandidate, null);

    public Task<AutomaticAssignmentResult> AssignAsync(string eventId, string jobId, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(Result);
    }
}
