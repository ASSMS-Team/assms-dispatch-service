namespace DispatchService.Messaging.Contracts;

/// <summary>Common envelope published with every ASSMS business event.</summary>
public class EventEnvelope<TPayload>
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int EventVersion { get; set; }
    public DateTime OccurredAt { get; set; }
    public string Producer { get; set; } = string.Empty;
    public TPayload Payload { get; set; } = default!;
}
