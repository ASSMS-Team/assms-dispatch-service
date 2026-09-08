using DispatchService.Models;

namespace DispatchService.Repositories;

public interface ITechnicianRepository
{
    Task<bool> ReferenceExistsAsync(string reference);
    Task CreateAsync(Technician technician);
    Task<Technician?> GetByIdAsync(string id);
}
