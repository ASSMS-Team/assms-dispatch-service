using DispatchService.Models;
using DispatchService.Repositories;

namespace DispatchService.Tests.Fakes;

public class FakeTechnicianRepository : ITechnicianRepository
{
    public bool ReferenceExists;
    public string? CheckedReference;
    public Technician? CreatedTechnician;
    public Technician? TechnicianToReturn;
    public IReadOnlyList<Technician> TechniciansToReturn { get; set; } = Array.Empty<Technician>();
    public string? RequestedId;
    public int CreateCallCount;
    public int UpdateCallCount;
    public Technician? UpdatedTechnician;
    public bool UpdateSucceeds = true;
    public TechnicianDeactivationPersistenceResult DeactivationResult = TechnicianDeactivationPersistenceResult.Deactivated;
    public int DeactivateCallCount;

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

    public Task<bool> UpdateAsync(Technician technician)
    {
        UpdateCallCount++;
        UpdatedTechnician = technician;
        return Task.FromResult(UpdateSucceeds);
    }

    public Task<TechnicianDeactivationPersistenceResult> DeactivateAsync(string id)
    {
        RequestedId = id;
        DeactivateCallCount++;
        if (DeactivationResult == TechnicianDeactivationPersistenceResult.Deactivated && TechnicianToReturn is not null)
            TechnicianToReturn.Status = "INACTIVE";
        return Task.FromResult(DeactivationResult);
    }

    public Task<Technician?> GetByIdAsync(string id)
    {
        RequestedId = id;
        return Task.FromResult(TechnicianToReturn);
    }

    public Task<IReadOnlyList<Technician>> GetAllAsync() => Task.FromResult(TechniciansToReturn);
}
