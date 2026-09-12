namespace DispatchService.Messaging.Contracts;

/// <summary>The Dispatch event published after an assignment is committed.</summary>
public class JobAssignedPayload
{
    public const string Topic = "job-assigned";
    public const string EventType = "JobAssigned";
    public const int EventVersion = 1;

    public string AssignmentId { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string JobReference { get; set; } = string.Empty;
    public string TechnicianId { get; set; } = string.Empty;
    public string TechnicianReference { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
}
