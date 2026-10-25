using System.Reflection;
using DispatchService.Controllers;
using DispatchService.DTOs;
using DispatchService.Messaging.Contracts;
using DispatchService.Models;
using DispatchService.Repositories;
using DispatchService.Security;
using DispatchService.Services;
using DispatchService.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;

namespace DispatchService.Tests;

// Default values, technician validation and responses, and service guards
// that the existing tests did not pin.
public class DispatchMutationTests
{
    // ---------- Default values ----------

    private static readonly string[] Namespaces =
        ["DispatchService.DTOs", "DispatchService.Models", "DispatchService.Messaging.Contracts"];

    // Defaults that are deliberately not empty.
    private static readonly Dictionary<string, string> ExpectedDefaults = new()
    {
        ["CreateTechnicianRequest.Status"] = "ACTIVE",
        ["UpdateTechnicianRequest.Status"] = "ACTIVE",
        ["Technician.Status"] = "ACTIVE",
    };

    public static IEnumerable<object[]> Types() =>
        typeof(TechnicianService).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true, IsGenericTypeDefinition: false }
                        && Namespaces.Contains(t.Namespace)
                        && t.GetConstructor(Type.EmptyTypes) is not null
                        && !t.GetCustomAttributes().Any(a => a.GetType().Name == "RequiredMemberAttribute"))
            .Select(t => new object[] { t.FullName! });

    [Theory]
    [MemberData(nameof(Types))]
    public void StringProperties_HaveTheirDocumentedDefaults(string typeName) =>
        AssertStringDefaults(typeof(TechnicianService).Assembly.GetType(typeName)!);

    [Fact]
    public void EventEnvelope_StringPropertiesDefaultToEmpty() =>
        AssertStringDefaults(typeof(EventEnvelope<JobCreatedPayload>));

    [Fact]
    public void JwtOptions_DefaultToTheSharedIssuerAndAudience_AndNoKey()
    {
        var options = new JwtOptions();

        Assert.Equal("assms-customer-asset-service", options.Issuer);
        Assert.Equal("assms-internal", options.Audience);
        Assert.Equal(string.Empty, options.SigningKey);
    }

    [Fact]
    public void SkillCollections_DefaultToEmpty()
    {
        Assert.Empty(new CreateTechnicianRequest().Skills);
        Assert.Empty(new UpdateTechnicianRequest().Skills);
        Assert.Empty(new Technician().Skills);
    }

    private static void AssertStringDefaults(Type type)
    {
        var instance = Activator.CreateInstance(type)!;
        var nullability = new NullabilityInfoContext();
        foreach (var property in type.GetProperties().Where(p => p.PropertyType == typeof(string) && p.CanRead))
        {
            var value = (string?)property.GetValue(instance);
            if (ExpectedDefaults.TryGetValue($"{type.Name}.{property.Name}", out var expected))
                Assert.Equal(expected, value);
            else if (nullability.Create(property).ReadState == NullabilityState.NotNull)
                Assert.True(value == string.Empty, $"{type.Name}.{property.Name} should default to an empty string but was '{value}'.");
            else
                Assert.Null(value);
        }
    }

    // ---------- TechnicianService ----------

    private static CreateTechnicianRequest Create(params string?[] skills) => new()
    {
        Reference = " tec-100 ", FullName = " Nimal Silva ", Region = " uva ", Status = " active ",
        Skills = skills.ToList()!, Phone = "  ", Email = " nimal@example.com "
    };

    private static UpdateTechnicianRequest Update(string region = "uva", params string?[] skills) => new()
    {
        FullName = " Nimal S. ", Region = region, Status = "inactive", Skills = skills.ToList()!, Phone = " 0771112222 ", Email = ""
    };

    [Fact]
    public async Task Create_NormalizesFields_AndKeepsDistinctTrimmedSkills()
    {
        var repository = new FakeTechnicianRepository();

        var result = await new TechnicianService(repository).CreateAsync(Create(" AC ", "ac", null, "  ", "REFRIGERATION"));

        Assert.Equal(TechnicianCreateError.None, result.Error);
        var created = repository.CreatedTechnician!;
        Assert.Equal("TEC-100", created.Reference);
        Assert.Equal("Nimal Silva", created.FullName);
        Assert.Equal("UVA", created.Region);
        Assert.Equal("ACTIVE", created.Status);
        Assert.Equal(new[] { "AC", "REFRIGERATION" }, created.Skills);
        Assert.Null(created.Phone);
        Assert.Equal("nimal@example.com", created.Email);
    }

    [Fact]
    public async Task Create_AcceptsASkillOfExactlyFiftyCharacters_ButRejectsFiftyOne()
    {
        Assert.Equal(TechnicianCreateError.None, (await new TechnicianService(new FakeTechnicianRepository()).CreateAsync(Create(new string('x', 50)))).Error);
        Assert.Equal(TechnicianCreateError.InvalidSkills, (await new TechnicianService(new FakeTechnicianRepository()).CreateAsync(Create("AC", new string('x', 51)))).Error);
    }

    [Fact]
    public async Task Create_RejectsWhenOnlyBlankSkillsAreGiven()
    {
        var repository = new FakeTechnicianRepository();

        Assert.Equal(TechnicianCreateError.InvalidSkills, (await new TechnicianService(repository).CreateAsync(Create(" ", null))).Error);
        Assert.Equal(0, repository.CreateCallCount);
    }

    public static IEnumerable<object[]> Regions() =>
        new[] { "WESTERN", "CENTRAL", "SOUTHERN", "NORTHERN", "EASTERN", "NORTH_WESTERN", "NORTH_CENTRAL", "uva", " Sabaragamuwa " }
            .Select(r => new object[] { r });

    [Theory]
    [MemberData(nameof(Regions))]
    public async Task Update_AcceptsEverySupportedRegion(string region)
    {
        var repository = new FakeTechnicianRepository { TechnicianToReturn = new Technician { Id = "tech-1", Reference = "TEC-100" } };

        var result = await new TechnicianService(repository).UpdateAsync("tech-1", Update(region, "AC"));

        Assert.Equal(TechnicianUpdateError.None, result.Error);
        Assert.Equal(region.Trim().ToUpperInvariant(), result.Value!.Region);
        Assert.Equal("INACTIVE", result.Value.Status);
        Assert.Equal("Nimal S.", result.Value.FullName);
        Assert.Equal("0771112222", result.Value.Phone);
        Assert.Null(result.Value.Email);
    }

    [Fact]
    public async Task Update_ValidatesSkillLengthAndBlankness()
    {
        var repository = new FakeTechnicianRepository { TechnicianToReturn = new Technician { Id = "tech-1" } };
        var service = new TechnicianService(repository);

        Assert.Equal(TechnicianUpdateError.None, (await service.UpdateAsync("tech-1", Update("UVA", new string('x', 50)))).Error);
        Assert.Equal(TechnicianUpdateError.InvalidSkills, (await service.UpdateAsync("tech-1", Update("UVA", "AC", new string('x', 51)))).Error);
        Assert.Equal(TechnicianUpdateError.InvalidSkills, (await service.UpdateAsync("tech-1", Update("UVA", " ", null))).Error);
        Assert.Equal(new[] { "AC" }, (await service.UpdateAsync("tech-1", Update("UVA", " AC ", "ac", null)))!.Value!.Skills);
    }

    [Fact]
    public async Task Update_TreatsMissingSkillsAsInvalid()
    {
        var repository = new FakeTechnicianRepository { TechnicianToReturn = new Technician { Id = "tech-1" } };
        var request = Update("UVA", "AC");
        request.Skills = null!;

        Assert.Equal(TechnicianUpdateError.InvalidSkills, (await new TechnicianService(repository).UpdateAsync("tech-1", request)).Error);
    }

    [Fact]
    public async Task GetById_ReturnsNull_ForAnUnknownTechnician_AndTheResponseOtherwise()
    {
        var repository = new FakeTechnicianRepository();
        var service = new TechnicianService(repository);

        Assert.Null(await service.GetByIdAsync("missing"));
        repository.TechnicianToReturn = new Technician { Id = "tech-1", Reference = "TEC-100" };
        Assert.Equal("TEC-100", (await service.GetByIdAsync("tech-1"))!.Reference);
    }

    // ---------- TechniciansController ----------

    private static void AssertFieldError(IActionResult result, int status, string key, string message)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(message, Assert.Single(Assert.Single(problem.Errors, e => e.Key == key).Value));
    }

    [Fact]
    public async Task Controller_Create_ReportsInvalidSkills_AndDuplicateReferences()
    {
        AssertFieldError(await new TechniciansController(new TechnicianService(new FakeTechnicianRepository())).Create(Create(" ")),
            400, "skills", "Provide at least one non-empty skill of at most 50 characters.");
        AssertFieldError(await new TechniciansController(new TechnicianService(new FakeTechnicianRepository { ReferenceExists = true })).Create(Create("AC")),
            409, "reference", "A technician already uses this reference.");
    }

    [Fact]
    public async Task Controller_Update_ReportsInvalidSkillsAndRegion()
    {
        var repository = new FakeTechnicianRepository { TechnicianToReturn = new Technician { Id = "tech-1" } };
        var controller = new TechniciansController(new TechnicianService(repository));

        AssertFieldError(await controller.Update("tech-1", Update("UVA", " ")), 400, "skills", "Provide at least one non-empty skill of at most 50 characters.");
        AssertFieldError(await controller.Update("tech-1", Update("LONDON", "AC")), 400, "region", "Region must be one of the nine supported provinces.");
    }

    [Fact]
    public async Task Controller_Deactivate_ReportsOpenAssignmentsWithTitleAndDetail()
    {
        var controller = new TechniciansController(new TechnicianService(
            new FakeTechnicianRepository { DeactivationResult = TechnicianDeactivationPersistenceResult.HasOpenAssignments }));

        var conflict = Assert.IsType<ConflictObjectResult>(await controller.Deactivate("tech-1"));

        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
        Assert.Equal("Technician has open assignments.", problem.Title);
        Assert.Equal("Reassign or close the technician's open jobs before deactivation.", problem.Detail);
    }

    [Fact]
    public async Task Controller_GetById_ReturnsOkForAKnownTechnician_AndNotFoundOtherwise()
    {
        var repository = new FakeTechnicianRepository();
        var controller = new TechniciansController(new TechnicianService(repository));

        Assert.IsType<NotFoundResult>(await controller.GetById("missing"));
        repository.TechnicianToReturn = new Technician { Id = "tech-1", Reference = "TEC-100" };
        Assert.Equal("TEC-100", Assert.IsType<TechnicianResponse>(Assert.IsType<OkObjectResult>(await controller.GetById("tech-1")).Value).Reference);
    }

    // ---------- Service guards ----------

    [Fact]
    public async Task AutomaticAssignment_RejectsInvalidIds_WithClearMessages()
    {
        var service = new AutomaticAssignmentService(new FakeAutomaticAssignmentRepository());

        var badEvent = await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync("not-a-guid", Guid.NewGuid().ToString()));
        Assert.StartsWith("A valid JobCreated event id is required.", badEvent.Message);
        var badJob = await Assert.ThrowsAsync<ArgumentException>(() => service.AssignAsync(Guid.NewGuid().ToString(), "not-a-guid"));
        Assert.StartsWith("A valid Job id is required.", badJob.Message);
    }

    [Fact]
    public async Task AssignmentStatus_TracksAssignedAsTheFirstLifecycleStep()
    {
        var repository = new FakeAutomaticAssignmentRepository();
        var jobId = Guid.NewGuid().ToString();
        var envelope = new EventEnvelope<JobStatusChangedPayload>
        {
            EventId = Guid.NewGuid().ToString(), EventType = JobStatusChangedPayload.EventType,
            EventVersion = JobStatusChangedPayload.EventVersion, Producer = "job-service",
            Payload = new JobStatusChangedPayload { JobId = jobId, NewStatus = "ASSIGNED" }
        };

        Assert.Equal(AssignmentStatusOutcome.Updated, await new AssignmentStatusService(repository).ApplyAsync(envelope, jobId));
        Assert.Equal((jobId, "ASSIGNED", 1), Assert.Single(repository.StatusUpdates));
    }

    [Fact]
    public async Task AssignmentStatus_RejectsAnEnvelopeWithoutAPayload()
    {
        var envelope = new EventEnvelope<JobStatusChangedPayload>
        {
            EventId = Guid.NewGuid().ToString(), EventType = JobStatusChangedPayload.EventType,
            EventVersion = JobStatusChangedPayload.EventVersion, Producer = "job-service", Payload = null!
        };

        Assert.Equal(AssignmentStatusOutcome.InvalidEvent, await new AssignmentStatusService(new FakeAutomaticAssignmentRepository()).ApplyAsync(envelope, null));
    }
}
