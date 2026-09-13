using DispatchService.Models;
using DispatchService.Repositories;

namespace DispatchService.Services;

public interface IAutomaticAssignmentService
{
    Task<AutomaticAssignmentResult> AssignAsync(string eventId, string jobId, CancellationToken cancellationToken = default);
}

/// <summary>Coordinates idempotent selection and persistence for one JobCreated event.</summary>
public class AutomaticAssignmentService : IAutomaticAssignmentService
{
    private readonly IAutomaticAssignmentRepository _repository;

    public AutomaticAssignmentService(IAutomaticAssignmentRepository repository)
    {
        _repository = repository;
    }

    public Task<AutomaticAssignmentResult> AssignAsync(string eventId, string jobId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(eventId, out _)) throw new ArgumentException("A valid JobCreated event id is required.", nameof(eventId));
        if (!Guid.TryParse(jobId, out _)) throw new ArgumentException("A valid Job id is required.", nameof(jobId));

        return _repository.AssignFromCandidateEvaluationAsync(eventId, jobId, DateTime.UtcNow, cancellationToken);
    }
}
