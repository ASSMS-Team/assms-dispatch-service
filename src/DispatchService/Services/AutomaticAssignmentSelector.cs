using DispatchService.Models;

namespace DispatchService.Services;

/// <summary>Implements the documented deterministic US-06B technician tie rule.</summary>
public static class AutomaticAssignmentSelector
{
    public static AssignmentCandidate? Select(IReadOnlyCollection<AssignmentCandidate> candidates) => candidates
        .OrderBy(candidate => candidate.OpenJobCount)
        // A null last-assigned timestamp represents a never-assigned technician,
        // which the product rule defines as older than every real timestamp.
        .ThenBy(candidate => candidate.LastAssignedAt.HasValue ? 1 : 0)
        .ThenBy(candidate => candidate.LastAssignedAt)
        .ThenBy(candidate => candidate.TechnicianReference, StringComparer.Ordinal)
        .FirstOrDefault();
}
