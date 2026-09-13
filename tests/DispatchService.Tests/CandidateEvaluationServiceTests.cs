using DispatchService.Services;
using DispatchService.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace DispatchService.Tests;

public class CandidateEvaluationServiceTests
{
    private const string EventId = "3d2a1b90-6c77-4f18-b0a1-5c9e7d4a2f31";
    private const string JobId = "9f1c7a24-8f4e-4c3a-9a52-2b6d0f5e1a77";

    [Fact]
    public async Task EvaluateAsync_WithMappedJobCreated_NormalizesRegionAndWritesTheCandidateEvaluation()
    {
        var repository = new FakeCandidateEvaluationRepository { Result = new(false, 2) };
        var service = CreateService(repository);

        var result = await service.EvaluateAsync(
            EventId, "JobCreated", 1, "job-service", JobId, "JOB-123", "REPAIR", " western ",
            new DateTime(2026, 9, 10, 7, 30, 0, DateTimeKind.Utc));

        Assert.Equal(CandidateEvaluationError.None, result.Error);
        Assert.Equal(2, result.Value!.CandidateCount);
        var written = Assert.IsType<DispatchService.Models.CandidateEvaluation>(repository.ReceivedEvaluation);
        Assert.Equal(EventId, written.EventId);
        Assert.Equal(JobId, written.JobId);
        Assert.Equal("AC", written.RequiredSkill);
        Assert.Equal("WESTERN", written.NormalizedRegion);
        Assert.Equal(DateTimeKind.Utc, written.OccurredAt.Kind);
    }

    [Fact]
    public async Task EvaluateAsync_WithNoEligibleTechnician_PersistsTheNoMatchOutcomeForDispatcherAttention()
    {
        var repository = new FakeCandidateEvaluationRepository { Result = new(false, 0) };
        var service = CreateService(repository);

        var result = await service.EvaluateAsync(
            EventId, "JobCreated", 1, "job-service", JobId, "JOB-123", "REPAIR", "WESTERN", DateTime.UtcNow);

        Assert.Equal(CandidateEvaluationError.None, result.Error);
        Assert.Equal(0, result.Value!.CandidateCount);
        Assert.False(result.Value.IsDuplicate);
        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_WithDuplicateEvent_ReturnsTheRepositoryIdempotencyResult()
    {
        var repository = new FakeCandidateEvaluationRepository { Result = new(true, 0) };
        var service = CreateService(repository);

        var result = await service.EvaluateAsync(
            EventId, "JobCreated", 1, "job-service", JobId, "JOB-123", "REPAIR", "WESTERN", DateTime.UtcNow);

        Assert.Equal(CandidateEvaluationError.None, result.Error);
        Assert.True(result.Value!.IsDuplicate);
        Assert.Equal(1, repository.CallCount);
    }

    [Fact]
    public async Task EvaluateAsync_WithoutAMappedRequiredSkill_QuarantinesWithoutPersisting()
    {
        var repository = new FakeCandidateEvaluationRepository();
        var service = CreateService(repository);

        var result = await service.EvaluateAsync(
            EventId, "JobCreated", 1, "job-service", JobId, "JOB-123", "WARRANTY_CLAIM", "WESTERN", DateTime.UtcNow);

        Assert.Equal(CandidateEvaluationError.UnmappedServiceCategory, result.Error);
        Assert.Equal(0, repository.CallCount);
    }

    [Theory]
    [InlineData("not-a-guid", "JobCreated", 1, "job-service")]
    [InlineData(EventId, "JobAssigned", 1, "job-service")]
    [InlineData(EventId, "JobCreated", 2, "job-service")]
    [InlineData(EventId, "JobCreated", 1, "another-service")]
    public async Task EvaluateAsync_WithInvalidEnvelope_QuarantinesWithoutPersisting(
        string eventId, string eventType, int eventVersion, string producer)
    {
        var repository = new FakeCandidateEvaluationRepository();
        var service = CreateService(repository);

        var result = await service.EvaluateAsync(
            eventId, eventType, eventVersion, producer, JobId, "JOB-123", "REPAIR", "WESTERN", DateTime.UtcNow);

        Assert.Equal(CandidateEvaluationError.InvalidEvent, result.Error);
        Assert.Equal(0, repository.CallCount);
    }

    private static CandidateEvaluationService CreateService(FakeCandidateEvaluationRepository repository)
    {
        var options = Options.Create(new CandidateMatchingOptions
        {
            RequiredSkillByServiceCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["REPAIR"] = "AC",
            },
        });
        return new CandidateEvaluationService(repository, new RequiredSkillResolver(options));
    }
}
