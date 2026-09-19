namespace DispatchService.DTOs;

/// <summary>A single live assignment returned to the authenticated Technician.</summary>
public class MyAssignmentResponse
{
    /// <summary>Dispatch-owned assignment id.</summary>
    public string AssignmentId { get; set; } = string.Empty;

    /// <summary>Job Service job id; use it to navigate to the job detail.</summary>
    public string JobId { get; set; } = string.Empty;

    /// <summary>Human-readable job reference (JOB- prefix), for display in the list.</summary>
    public string JobReference { get; set; } = string.Empty;

    /// <summary>Current job status as tracked by Dispatch (e.g. ASSIGNED).</summary>
    public string JobStatus { get; set; } = string.Empty;

    /// <summary>When Dispatch assigned this job to the technician (UTC).</summary>
    public DateTime AssignedAt { get; set; }
}
