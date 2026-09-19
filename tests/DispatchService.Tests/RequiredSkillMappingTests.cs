using DispatchService.Services;

using Microsoft.Extensions.Options;

namespace DispatchService.Tests;

// The service-category to required-skill mapping.
//
// This is the one piece of Dispatch's matching behaviour that lives in
// configuration rather than in code, which makes it the one piece that can be
// wrong in an environment without any build or test failing. A category with no
// mapping is quarantined: the event is committed past, no evaluation row is
// written, and the job is never assigned. Nothing raises an error - the job just
// silently never happens.
//
// These tests pin the shape of a correct mapping and the behaviour when one is
// missing. They cannot verify what is actually deployed to staging; see
// docs/deployment/kafka-staging.md for that check.
public class RequiredSkillMappingTests
{
    // The five values JobCreated's serviceCategory can carry, as fixed by the
    // contract document and by chk_jobs_service_category in the Job Service's
    // V01 migration. Every one of them must be mapped.
    private static readonly string[] AllServiceCategories =
    [
        "INSTALLATION",
        "REPAIR",
        "MAINTENANCE",
        "INSPECTION",
        "WARRANTY_CLAIM"
    ];

    // The complete mapping, matching appsettings.Example.json. Kept here as the
    // reference an environment is compared against.
    private static readonly Dictionary<string, string> CompleteMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INSTALLATION"] = "AC_INSTALLATION",
        ["REPAIR"] = "AC",
        ["MAINTENANCE"] = "AC_MAINTENANCE",
        ["INSPECTION"] = "AC_INSPECTION",
        ["WARRANTY_CLAIM"] = "AC_WARRANTY",
    };

    private static RequiredSkillResolver Resolver(Dictionary<string, string> mapping) =>
        new(Options.Create(new CandidateMatchingOptions { RequiredSkillByServiceCategory = mapping }));

    [Fact]
    public void CompleteMapping_CoversEveryServiceCategoryJobCreatedCanCarry()
    {
        // The guard that matters. If a sixth service category is added upstream
        // and nobody adds its skill mapping, every job in that category is
        // quarantined in production with no error anywhere - so the category list
        // and the mapping are asserted against each other here.
        Assert.Equal(
            AllServiceCategories.OrderBy(category => category, StringComparer.Ordinal),
            CompleteMapping.Keys.OrderBy(category => category, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("INSTALLATION", "AC_INSTALLATION")]
    [InlineData("REPAIR", "AC")]
    [InlineData("MAINTENANCE", "AC_MAINTENANCE")]
    [InlineData("INSPECTION", "AC_INSPECTION")]
    [InlineData("WARRANTY_CLAIM", "AC_WARRANTY")]
    public void TryResolve_WithTheCompleteMapping_ResolvesEveryCategory(string category, string expectedSkill)
    {
        // Arrange
        var resolver = Resolver(CompleteMapping);

        // Act
        var resolved = resolver.TryResolve(category, out var skill);

        // Assert
        Assert.True(resolved);
        Assert.Equal(expectedSkill, skill);
    }

    [Fact]
    public void TryResolve_IsCaseInsensitiveOnTheCategory()
    {
        // The dictionary is built with OrdinalIgnoreCase, so a producer that ever
        // publishes a differently cased category still matches. The skill it
        // returns is not normalised - it is returned exactly as configured,
        // because it has to equal a technician_skills.skill value character for
        // character.
        var resolver = Resolver(CompleteMapping);

        Assert.True(resolver.TryResolve("repair", out var lower));
        Assert.True(resolver.TryResolve("RePaIr", out var mixed));

        Assert.Equal("AC", lower);
        Assert.Equal("AC", mixed);
    }

    [Fact]
    public void TryResolve_TrimsSurroundingWhitespaceOnTheCategory()
    {
        var resolver = Resolver(CompleteMapping);

        Assert.True(resolver.TryResolve("  REPAIR  ", out var skill));
        Assert.Equal("AC", skill);
    }

    [Fact]
    public void TryResolve_WithAnUnmappedCategory_RefusesRatherThanGuessing()
    {
        // The quarantine path. Dispatch cannot infer a skill from a free-text
        // problem description, and guessing one would assign work to a technician
        // who is not qualified for it - so an unmapped category is refused
        // outright and the event is quarantined.
        var resolver = Resolver(CompleteMapping);

        Assert.False(resolver.TryResolve("PRESSURE_WASHING", out var skill));
        Assert.Equal(string.Empty, skill);
    }

    [Fact]
    public void TryResolve_WithAnIncompleteMapping_RefusesTheMissingCategories()
    {
        // Arrange - an environment configured the way the repository shipped
        // before this change: REPAIR mapped, the other four absent.
        var resolver = Resolver(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["REPAIR"] = "AC",
        });

        // Act + Assert - REPAIR works, and the other four are silently
        // quarantined. This is the failure the complete mapping prevents, pinned
        // so the consequence of an incomplete configuration is explicit.
        Assert.True(resolver.TryResolve("REPAIR", out _));

        foreach (var category in AllServiceCategories.Where(category => category != "REPAIR"))
        {
            Assert.False(resolver.TryResolve(category, out _));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryResolve_WithNoCategory_Refuses(string? category)
    {
        var resolver = Resolver(CompleteMapping);

        Assert.False(resolver.TryResolve(category!, out var skill));
        Assert.Equal(string.Empty, skill);
    }

    [Fact]
    public void TryResolve_WithABlankConfiguredSkill_Refuses()
    {
        // A category present in configuration but mapped to an empty string is
        // the same as unmapped: there is no skill to match a technician against.
        // Treating it as a match would select on an empty skill and could assign
        // anyone.
        var resolver = Resolver(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["REPAIR"] = "   ",
        });

        Assert.False(resolver.TryResolve("REPAIR", out var skill));
        Assert.Equal(string.Empty, skill);
    }
}
