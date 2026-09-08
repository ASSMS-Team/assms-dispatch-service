using DispatchService.Models;
using DispatchService.Repositories;

namespace DispatchService.Tests.Fakes;

public class FakeTechnicianRepository : ITechnicianRepository
{
    public bool ReferenceExists;
    public string? CheckedReference;
    public Technician? CreatedTechnician;
    public Technician? TechnicianToReturn;
    public int CreateCallCount;

    public Task<bool> ReferenceExistsAsync(string reference)
    {
        CheckedReference = reference;
        return Task.FromResult(ReferenceExists);
    }

    public Task CreateAsync(Technician technician)
    {
        CreatedTechnician = technician;
        CreateCallCount++;
        return Task.CompletedTask;
    }

    public Task<Technician?> GetByIdAsync(string id) => Task.FromResult(TechnicianToReturn);
}
