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

    /// <summary>Job priority (LOW, MEDIUM, HIGH or URGENT). Null for assignments made before this was recorded.</summary>
    public string? Priority { get; set; }

    /// <summary>Service category, for example REPAIR. Null for assignments made before this was recorded.</summary>
    public string? ServiceCategory { get; set; }

    /// <summary>Service region (the job location), for example UVA. Null for assignments made before this was recorded.</summary>
    public string? Region { get; set; }

    /// <summary>The customer's problem description. Null for assignments made before this was recorded.</summary>
    public string? ProblemDescription { get; set; }

    /// <summary>Current job status (ASSIGNED or IN_PROGRESS), kept current from Job Service status events.</summary>
    public string JobStatus { get; set; } = string.Empty;

    /// <summary>When Dispatch assigned this job to the technician (UTC).</summary>
    public DateTime AssignedAt { get; set; }
}
