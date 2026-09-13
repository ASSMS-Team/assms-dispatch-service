using DispatchService.Models;
using DispatchService.Services;

namespace DispatchService.Tests;

public class AutomaticAssignmentSelectorTests
{
    [Fact]
    public void Select_PrefersTheCandidateWithTheLowestOpenJobCount()
    {
        var selected = AutomaticAssignmentSelector.Select([
            Candidate("t1", "TEC-010", 3, DateTime.UtcNow.AddDays(-2)),
            Candidate("t2", "TEC-020", 1, DateTime.UtcNow.AddDays(-1)),
        ]);

        Assert.Equal("t2", selected!.TechnicianId);
    }

    [Fact]
    public void Select_WhenWorkloadTies_PrefersANeverAssignedTechnician()
    {
        var selected = AutomaticAssignmentSelector.Select([
            Candidate("t1", "TEC-010", 1, DateTime.UtcNow.AddDays(-10)),
            Candidate("t2", "TEC-020", 1, null),
        ]);

        Assert.Equal("t2", selected!.TechnicianId);
    }

    [Fact]
    public void Select_WhenWorkloadTies_PrefersTheOldestLastAssignment()
    {
        var selected = AutomaticAssignmentSelector.Select([
            Candidate("t1", "TEC-010", 1, new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc)),
            Candidate("t2", "TEC-020", 1, new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc)),
        ]);

        Assert.Equal("t2", selected!.TechnicianId);
    }

    [Fact]
    public void Select_WhenAllOtherValuesTie_UsesAscendingOrdinalReference()
    {
        var selected = AutomaticAssignmentSelector.Select([
            Candidate("t1", "TEC-020", 1, null),
            Candidate("t2", "TEC-010", 1, null),
        ]);

        Assert.Equal("t2", selected!.TechnicianId);
    }

    [Fact]
    public void Select_WithNoCandidates_ReturnsNull() => Assert.Null(AutomaticAssignmentSelector.Select([]));

    private static AssignmentCandidate Candidate(string id, string reference, int openJobs, DateTime? lastAssignedAt) =>
        new(id, reference, openJobs, lastAssignedAt);
}
