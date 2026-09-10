namespace DispatchService.Messaging.Contracts;

/// <summary>The Job Service payload Dispatch uses to begin candidate evaluation.</summary>
public class JobCreatedPayload
{
    public const string Topic = "job-created";
    public const string ConsumerGroup = "assms-dispatch-job-created";
    public const string EventType = "JobCreated";
    public const int EventVersion = 1;

    public string JobId { get; set; } = string.Empty;
    public string JobReference { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string ServiceCategory { get; set; } = string.Empty;
    public string ProblemDescription { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
