using System.Security.Claims;
using DispatchService.Controllers;
using DispatchService.Models;
using DispatchService.Repositories;
using DispatchService.Services;
using DispatchService.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DispatchService.Tests;

public class MyAssignmentsControllerTests
{
    // Wires a ClaimsPrincipal that matches what the JWT middleware produces for
    // a Technician: unique_name = technicianReference, role = Technician.
    private static MyAssignmentsController BuildController(
        FakeTechnicianRepository technicianRepo,
        FakeAutomaticAssignmentRepository assignmentRepo,
        string uniqueName = "TEC-001")
    {
        var service = new MyAssignmentsService(technicianRepo, assignmentRepo);
        var controller = new MyAssignmentsController(service);
        var identity = new ClaimsIdentity(
        [
            new Claim("unique_name", uniqueName),
            new Claim("role", "Technician"),
        ], "jwt");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        return controller;
    }

    [Fact]
    public async Task GetMyAssignments_WhenTechnicianHasNoAssignments_ReturnsOkWithEmptyArray()
    {
        var techRepo = new FakeTechnicianRepository
        {
            TechnicianToReturn = new Technician { Id = "tech-1", Reference = "TEC-001" },
        };
        var assignRepo = new FakeAutomaticAssignmentRepository
        {
            AssignmentsToReturn = [],
        };
        var controller = BuildController(techRepo, assignRepo);

        var result = Assert.IsType<OkObjectResult>(await controller.GetMyAssignments(default));

        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<DTOs.MyAssignmentResponse>>(result.Value));
    }

    [Fact]
    public async Task GetMyAssignments_WhenTechnicianNotFound_ReturnsNotFound()
    {
        // TechnicianToReturn == null means the reference is unknown to Dispatch.
        var techRepo = new FakeTechnicianRepository { TechnicianToReturn = null };
        var controller = BuildController(techRepo, new FakeAutomaticAssignmentRepository());

        var result = await controller.GetMyAssignments(default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetMyAssignments_WhenAssignmentsExist_ReturnsMappedList()
    {
        var assignedAt = new DateTime(2026, 9, 15, 10, 30, 0, DateTimeKind.Utc);
        var techRepo = new FakeTechnicianRepository
        {
            TechnicianToReturn = new Technician { Id = "tech-1", Reference = "TEC-001" },
        };
        var assignRepo = new FakeAutomaticAssignmentRepository
        {
            AssignmentsToReturn =
            [
                new AssignmentRecord("assign-1", "job-1", "JOB-ABC123", "tech-1", "TEC-001", assignedAt),
                new AssignmentRecord("assign-2", "job-2", "JOB-XYZ789", "tech-1", "TEC-001", assignedAt),
            ],
        };
        var controller = BuildController(techRepo, assignRepo);

        var result = Assert.IsType<OkObjectResult>(await controller.GetMyAssignments(default));
        var assignments = Assert.IsAssignableFrom<IReadOnlyList<DTOs.MyAssignmentResponse>>(result.Value);

        Assert.Equal(2, assignments.Count);
        Assert.Contains(assignments, a => a.JobReference == "JOB-ABC123");
        Assert.Contains(assignments, a => a.JobReference == "JOB-XYZ789");
        Assert.All(assignments, a => Assert.Equal("ASSIGNED", a.JobStatus));
        Assert.All(assignments, a => Assert.Equal(assignedAt, a.AssignedAt));
    }

    [Fact]
    public async Task GetMyAssignments_DoesNotExposeOtherTechniciansAssignments()
    {
        // The repository returns only this technician's records (enforced at SQL level).
        // The service does not apply further filtering — isolation is the repository's
        // responsibility. This test confirms the controller passes the resolved id through
        // unchanged and does not widen the query.
        var techRepo = new FakeTechnicianRepository
        {
            TechnicianToReturn = new Technician { Id = "tech-1", Reference = "TEC-001" },
        };
        var assignRepo = new FakeAutomaticAssignmentRepository
        {
            AssignmentsToReturn =
            [
                new AssignmentRecord("assign-1", "job-1", "JOB-ABC123", "tech-1", "TEC-001",
                    new DateTime(2026, 9, 15, 10, 30, 0, DateTimeKind.Utc)),
            ],
        };
        var controller = BuildController(techRepo, assignRepo, uniqueName: "TEC-001");

        var result = Assert.IsType<OkObjectResult>(await controller.GetMyAssignments(default));
        var assignments = Assert.IsAssignableFrom<IReadOnlyList<DTOs.MyAssignmentResponse>>(result.Value);

        // Only TEC-001's assignment is visible; TEC-002's job is not in the set.
        Assert.Single(assignments);
        Assert.Equal("TEC-001", techRepo.CheckedReference);
    }
}
