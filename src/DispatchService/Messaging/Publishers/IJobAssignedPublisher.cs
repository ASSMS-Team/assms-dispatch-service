using DispatchService.Models;

namespace DispatchService.Messaging.Publishers;

public interface IJobAssignedPublisher : IDisposable
{
    Task PublishAsync(PendingJobAssignedEvent pendingEvent, CancellationToken cancellationToken);
}
