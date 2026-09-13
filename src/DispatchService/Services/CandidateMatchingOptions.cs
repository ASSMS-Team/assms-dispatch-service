namespace DispatchService.Services;

/// <summary>
/// Business-owned service-category to required-skill mapping. JobCreated has a
/// service category but not a required skill, so Dispatch cannot safely infer
/// one from a free-text problem description.
/// </summary>
public class CandidateMatchingOptions
{
    public const string SectionName = "CandidateMatching";

    public Dictionary<string, string> RequiredSkillByServiceCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class RequiredSkillResolver
{
    private readonly CandidateMatchingOptions _options;

    public RequiredSkillResolver(Microsoft.Extensions.Options.IOptions<CandidateMatchingOptions> options)
    {
        _options = options.Value;
    }

    public bool TryResolve(string serviceCategory, out string requiredSkill)
    {
        requiredSkill = string.Empty;
        if (string.IsNullOrWhiteSpace(serviceCategory)) return false;

        if (!_options.RequiredSkillByServiceCategory.TryGetValue(serviceCategory.Trim(), out var configuredSkill) ||
            string.IsNullOrWhiteSpace(configuredSkill))
            return false;

        requiredSkill = configuredSkill.Trim();
        return true;
    }
}
