using DispatchService.Controllers;
using DispatchService.DTOs;
using DispatchService.Models;
using DispatchService.Services;
using DispatchService.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DispatchService.Tests;

public class TechniciansControllerTests
{
    [Fact]
    public async Task GetAll_WhenNoTechnicians_ReturnsOkWithEmptyArray()
    {
        var controller = new TechniciansController(new TechnicianService(new FakeTechnicianRepository()));

        var action = await controller.GetAll();

        var result = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<TechnicianResponse>>(result.Value));
    }

    [Fact]
    public async Task GetAll_ReturnsNameReferenceRegionSkillsAndState()
    {
        var repository = new FakeTechnicianRepository
        {
            TechniciansToReturn =
            [
                new Technician
                {
                    Id = "technician-1", Reference = "TEC-001", FullName = "Amal Perera",
                    Region = "WESTERN", Status = "ACTIVE", Skills = ["Electrical"],
                },
            ],
        };
        var controller = new TechniciansController(new TechnicianService(repository));

        var action = await controller.GetAll();

        var result = Assert.IsType<OkObjectResult>(action.Result);
        var technician = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<TechnicianResponse>>(result.Value));
        Assert.Equal("Amal Perera", technician.FullName);
        Assert.Equal("TEC-001", technician.Reference);
        Assert.Equal("WESTERN", technician.Region);
        Assert.Equal("ACTIVE", technician.Status);
        Assert.Equal(["Electrical"], technician.Skills);
    }

    [Fact]
    public async Task GetById_WhenUnknown_ReturnsNotFound()
    {
        var repository = new FakeTechnicianRepository();
        var controller = new TechniciansController(new TechnicianService(repository));

        var action = await controller.GetById("unknown-id");

        Assert.IsType<NotFoundResult>(action);
        Assert.Equal("unknown-id", repository.RequestedId);
    }

    [Fact]
    public async Task Create_WhenSkillsAreEmpty_ReturnsFieldValidationError()
    {
        var controller = new TechniciansController(new TechnicianService(new FakeTechnicianRepository()));
        var request = new CreateTechnicianRequest
        {
            Reference = "TEC-001", FullName = "Amal Perera", Region = "WESTERN", Skills = [], Status = "ACTIVE",
        };

        var action = await controller.Create(request);

        var result = Assert.IsType<BadRequestObjectResult>(action);
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Contains("skills", problem.Errors.Keys);
    }
}
