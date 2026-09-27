using DispatchService.Repositories;
using Prometheus;

namespace DispatchService.Messaging.Publishers;

/// <summary>Retries pending JobAssigned records until Kafka acknowledges publication.</summary>
public sealed class JobAssignedOutboxPublisher : BackgroundService
{
    private static readonly Gauge OutboxPendingGauge = Metrics.CreateGauge(
        "assms_dispatch_outbox_pending_count", "Number of unsent outbox events waiting in database queue");

    private static readonly Counter OutboxPublishedCounter = Metrics.CreateCounter(
        "assms_dispatch_outbox_published_total", "Total outbox events published to Kafka", "topic");

    private static readonly Counter OutboxFailedCounter = Metrics.CreateCounter(
        "assms_dispatch_outbox_failed_total", "Total outbox publication failures", "topic", "error_type");

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobAssignedPublisher _publisher;
    private readonly ILogger<JobAssignedOutboxPublisher> _logger;

    public JobAssignedOutboxPublisher(
        IServiceScopeFactory scopeFactory,
        IJobAssignedPublisher publisher,
        ILogger<JobAssignedOutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PublishPendingAsync(stoppingToken);
            await Task.Delay(RetryDelay, stoppingToken);
        }
    }

    public async Task PublishPendingAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAutomaticAssignmentRepository>();
        var pendingEvents = await repository.GetPendingJobAssignedEventsAsync(20, cancellationToken);
        OutboxPendingGauge.Set(pendingEvents.Count);

        foreach (var pendingEvent in pendingEvents)
        {
            try
            {
                await _publisher.PublishAsync(pendingEvent, cancellationToken);
                await repository.MarkJobAssignedPublishedAsync(pendingEvent.Id, DateTime.UtcNow, cancellationToken);
                OutboxPublishedCounter.WithLabels("job-assigned").Inc();
                _logger.LogInformation("Published JobAssigned event {EventId} for job {JobId}.", pendingEvent.Id, pendingEvent.JobId);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                await repository.MarkJobAssignedFailedAsync(pendingEvent.Id, exception.Message, cancellationToken);
                OutboxFailedCounter.WithLabels("job-assigned", exception.GetType().Name).Inc();
                _logger.LogWarning(exception, "JobAssigned event {EventId} remains pending for retry.", pendingEvent.Id);
            }
        }
    }
}
