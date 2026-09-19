using DispatchService.Messaging.Publishers;
using DispatchService.Models;
using DispatchService.Repositories;
using DispatchService.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DispatchService.Tests;

public class JobAssignedOutboxPublisherTests
{
    [Fact]
    public async Task PublishPendingAsync_WhenKafkaAcknowledges_MarksTheOutboxEventPublished()
    {
        var repository = new FakeAutomaticAssignmentRepository
        {
            PendingEvents = [new("event-1", "job-1", "job-assigned", "{}", 0)],
        };
        var publisher = new FakeJobAssignedPublisher();
        var worker = CreateWorker(repository, publisher);

        await worker.PublishPendingAsync();

        Assert.Single(publisher.PublishedEvents);
        Assert.Equal(["event-1"], repository.PublishedEventIds);
        Assert.Empty(repository.FailedEvents);
    }

    [Fact]
    public async Task PublishPendingAsync_WhenKafkaFails_KeepsTheOutboxEventPendingForRetry()
    {
        var repository = new FakeAutomaticAssignmentRepository
        {
            PendingEvents = [new("event-1", "job-1", "job-assigned", "{}", 0)],
        };
        var publisher = new FakeJobAssignedPublisher { ExceptionToThrow = new InvalidOperationException("broker unavailable") };
        var worker = CreateWorker(repository, publisher);

        await worker.PublishPendingAsync();

        Assert.Empty(repository.PublishedEventIds);
        var failed = Assert.Single(repository.FailedEvents);
        Assert.Equal("event-1", failed.EventId);
        Assert.Contains("broker unavailable", failed.Reason);
    }

    private static JobAssignedOutboxPublisher CreateWorker(IAutomaticAssignmentRepository repository, IJobAssignedPublisher publisher)
    {
        var services = new ServiceCollection().AddScoped<IAutomaticAssignmentRepository>(_ => repository).BuildServiceProvider();
        return new JobAssignedOutboxPublisher(services.GetRequiredService<IServiceScopeFactory>(), publisher, NullLogger<JobAssignedOutboxPublisher>.Instance);
    }
}
