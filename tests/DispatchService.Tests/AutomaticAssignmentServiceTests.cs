using DispatchService.Models;
using DispatchService.Services;
using DispatchService.Tests.Fakes;

namespace DispatchService.Tests;

public class AutomaticAssignmentServiceTests
{
    private const string EventId = "3d2a1b90-6c77-4f18-b0a1-5c9e7d4a2f31";
    private const string JobId = "9f1c7a24-8f4e-4c3a-9a52-2b6d0f5e1a77";

    [Fact]
    public async Task AssignAsync_DelegatesAValidEventToTheTransactionalRepository()
    {
        var repository = new FakeAutomaticAssignmentRepository
        {
            AssignmentResult = new(AutomaticAssignmentOutcome.AlreadyAssigned, null),
        };
        var service = new AutomaticAssignmentService(repository);

        var result = await service.AssignAsync(EventId, JobId);

        Assert.True(result.IsDuplicate);
        Assert.Equal(1, repository.AssignCallCount);
        Assert.Equal(EventId, repository.ReceivedEventId);
        Assert.Equal(JobId, repository.ReceivedJobId);
    }

    [Theory]
    [InlineData("invalid", JobId)]
    [InlineData(EventId, "invalid")]
    public async Task AssignAsync_WithInvalidIdentifiers_DoesNotCallTheRepository(string eventId, string jobId)
    {
        var repository = new FakeAutomaticAssignmentRepository();
        var service = new AutomaticAssignmentService(repository);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync(eventId, jobId));

        Assert.Equal(0, repository.AssignCallCount);
    }
}
