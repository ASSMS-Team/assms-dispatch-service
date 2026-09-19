using DispatchService.Models;

namespace DispatchService.Repositories;

public enum TechnicianDeactivationPersistenceResult { Deactivated, AlreadyInactive, HasOpenAssignments, NotFound }

public interface ITechnicianRepository
{
    Task<bool> ReferenceExistsAsync(string reference);
    Task CreateAsync(Technician technician);
    Task<bool> UpdateAsync(Technician technician);
    Task<TechnicianDeactivationPersistenceResult> DeactivateAsync(string id);
    Task<IReadOnlyList<Technician>> GetAllAsync();
    Task<Technician?> GetByIdAsync(string id);
}
