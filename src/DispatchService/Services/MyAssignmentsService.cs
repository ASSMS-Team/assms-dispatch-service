using DispatchService.DTOs;
using DispatchService.Repositories;

namespace DispatchService.Services;

public enum MyAssignmentsError { None, TechnicianNotFound }

public record MyAssignmentsResult(IReadOnlyList<MyAssignmentResponse>? Assignments, MyAssignmentsError Error);

/// <summary>
/// Resolves the authenticated technician from their reference claim and returns
/// their live assignments. This service is intentionally thin: all security
/// decisions are made by the JWT middleware and controller authorize attribute;
/// the service only handles the business lookup.
/// </summary>
public class MyAssignmentsService
{
    private readonly ITechnicianRepository _technicianRepository;
    private readonly IAutomaticAssignmentRepository _assignmentRepository;

    public MyAssignmentsService(
        ITechnicianRepository technicianRepository,
        IAutomaticAssignmentRepository assignmentRepository)
    {
        _technicianRepository = technicianRepository;
        _assignmentRepository = assignmentRepository;
    }

    /// <summary>
    /// Returns all live assignments for the technician identified by
    /// <paramref name="technicianReference"/> (the JWT unique_name claim).
    /// Returns <see cref="MyAssignmentsError.TechnicianNotFound"/> when no
    /// Dispatch technician record matches the reference.
    /// </summary>
    public async Task<MyAssignmentsResult> GetAssignmentsAsync(string technicianReference, CancellationToken cancellationToken = default)
    {
        var technician = await _technicianRepository.GetByReferenceAsync(technicianReference);
        if (technician is null)
            return new(null, MyAssignmentsError.TechnicianNotFound);

        var assignments = await _assignmentRepository.GetByTechnicianIdAsync(technician.Id, cancellationToken);

        var response = assignments
            .Select(a => new MyAssignmentResponse
            {
                AssignmentId = a.Id,
                JobId = a.JobId,
                JobReference = a.JobReference,
                JobStatus = "ASSIGNED",
                AssignedAt = a.AssignedAt,
            })
            .ToList();

        return new(response, MyAssignmentsError.None);
    }
}
