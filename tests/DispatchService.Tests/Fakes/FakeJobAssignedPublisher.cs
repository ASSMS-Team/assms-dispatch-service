using DispatchService.Messaging.Publishers;
using DispatchService.Models;

namespace DispatchService.Tests.Fakes;

public class FakeJobAssignedPublisher : IJobAssignedPublisher
{
    public List<PendingJobAssignedEvent> PublishedEvents { get; } = [];
    public Exception? ExceptionToThrow { get; set; }

    public Task PublishAsync(PendingJobAssignedEvent pendingEvent, CancellationToken cancellationToken)
    {
        if (ExceptionToThrow is not null) throw ExceptionToThrow;
        PublishedEvents.Add(pendingEvent);
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
