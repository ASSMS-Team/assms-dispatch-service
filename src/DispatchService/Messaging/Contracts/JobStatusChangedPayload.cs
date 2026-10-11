namespace DispatchService.Messaging.Contracts;

/// <summary>The Job Service lifecycle payload Dispatch uses to keep assignment status current.</summary>
public class JobStatusChangedPayload
{
    public const string Topic = "job-status-changed";
    public const string ConsumerGroup = "assms-dispatch-job-status-changed";
    public const string EventType = "JobStatusChanged";
    public const int EventVersion = 1;

    public string JobId { get; set; } = string.Empty;
    public string JobReference { get; set; } = string.Empty;
    public string? AssignmentId { get; set; }
    public string? TechnicianId { get; set; }
    public string? TechnicianReference { get; set; }
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}
