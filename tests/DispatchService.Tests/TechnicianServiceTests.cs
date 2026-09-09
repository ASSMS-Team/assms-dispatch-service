using DispatchService.DTOs;
using DispatchService.Services;
using DispatchService.Tests.Fakes;

namespace DispatchService.Tests;

public class TechnicianServiceTests
{
    private static CreateTechnicianRequest ValidRequest() => new()
    {
        Reference = "tec-032",
        FullName = "Tharindu Jayasena",
        Region = "WESTERN",
        Status = "ACTIVE",
        Skills = ["Electrical", "AC"],
        Phone = " +94 77 123 4567 ",
        Email = "tharindu@assms.lk",
    };

    [Fact]
    public async Task CreateAsync_WithValidData_CreatesADispatchOwnedTechnician()
    {
        var repository = new FakeTechnicianRepository();
        var service = new TechnicianService(repository);

        var result = await service.CreateAsync(ValidRequest());

        Assert.Equal(TechnicianCreateError.None, result.Error);
        var created = Assert.IsType<DispatchService.Models.Technician>(repository.CreatedTechnician);
        Assert.True(Guid.TryParse(created.Id, out _));
        Assert.Equal("TEC-032", created.Reference);
        Assert.Equal("Tharindu Jayasena", created.FullName);
        Assert.Equal("WESTERN", created.Region);
        Assert.Equal("ACTIVE", created.Status);
        Assert.Equal(["Electrical", "AC"], created.Skills);
        Assert.Equal("+94 77 123 4567", created.Phone);
        Assert.Equal(created.Id, result.Value!.Id);
    }

    [Fact]
    public async Task CreateAsync_WhenReferenceExists_ReturnsDuplicateReferenceWithoutWriting()
    {
        var repository = new FakeTechnicianRepository { ReferenceExists = true };
        var service = new TechnicianService(repository);

        var result = await service.CreateAsync(ValidRequest());

        Assert.Equal("TEC-032", repository.CheckedReference);
        Assert.Equal(TechnicianCreateError.DuplicateReference, result.Error);
        Assert.Equal(0, repository.CreateCallCount);
    }

    [Fact]
    public Task CreateAsync_WithoutSkills_RejectsTheRequest() =>
        AssertInvalidSkillsAsync([]);

    [Fact]
    public Task CreateAsync_WithOnlyWhitespaceSkills_RejectsTheRequest() =>
        AssertInvalidSkillsAsync(["  "]);

    private static async Task AssertInvalidSkillsAsync(List<string> skills)
    {
        var request = ValidRequest();
        request.Skills = skills.ToList();
        var repository = new FakeTechnicianRepository();
        var service = new TechnicianService(repository);

        var result = await service.CreateAsync(request);

        Assert.Equal(TechnicianCreateError.InvalidSkills, result.Error);
        Assert.Equal(0, repository.CreateCallCount);
    }

    [Fact]
    public async Task CreateAsync_DeduplicatesSkillsCaseInsensitively()
    {
        var request = ValidRequest();
        request.Skills = ["Electrical", " electrical ", "AC"];
        var repository = new FakeTechnicianRepository();
        var service = new TechnicianService(repository);

        await service.CreateAsync(request);

        Assert.Equal(["Electrical", "AC"], repository.CreatedTechnician!.Skills);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsTechnicianCapabilityData()
    {
        var repository = new FakeTechnicianRepository
        {
            TechniciansToReturn =
            [
                new DispatchService.Models.Technician
                {
                    Id = "technician-1", Reference = "TEC-001", FullName = "Amal Perera",
                    Region = "WESTERN", Status = "ACTIVE", Skills = ["Electrical", "AC"],
                },
            ],
        };
        var service = new TechnicianService(repository);

        var technicians = await service.GetAllAsync();

        var technician = Assert.Single(technicians);
        Assert.Equal("TEC-001", technician.Reference);
        Assert.Equal("WESTERN", technician.Region);
        Assert.Equal("ACTIVE", technician.Status);
        Assert.Equal(["Electrical", "AC"], technician.Skills);
    }

    [Fact]
    public async Task GetByIdAsync_WhenUnknown_ReturnsNull()
    {
        var service = new TechnicianService(new FakeTechnicianRepository());

        var technician = await service.GetByIdAsync("unknown-id");

        Assert.Null(technician);
    }
}
