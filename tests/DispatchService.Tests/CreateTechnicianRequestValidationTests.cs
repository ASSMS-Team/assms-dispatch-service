using System.ComponentModel.DataAnnotations;
using DispatchService.DTOs;

namespace DispatchService.Tests;

public class CreateTechnicianRequestValidationTests
{
    private static IList<ValidationResult> Validate(CreateTechnicianRequest request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request, null, null);
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }

    private static CreateTechnicianRequest ValidRequest(string reference) => new()
    {
        Reference = reference,
        FullName = "Valid Technician",
        Region = "WESTERN",
        Skills = ["Electrical"],
        Status = "ACTIVE",
    };

    [Theory]
    [InlineData("technician.local")]
    [InlineData("sawan.m")]
    [InlineData("first.last")]
    [InlineData("john.doe.local")]
    [InlineData("TEC-032")]
    [InlineData("tec-001")]
    [InlineData("T-1.2")]
    [InlineData("A1")]
    public void Reference_WhenValidWithDotsHyphensAlphanumeric_PassesValidation(string reference)
    {
        var request = ValidRequest(reference);
        var results = Validate(request);
        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(CreateTechnicianRequest.Reference)));
    }

    [Theory]
    [InlineData(".technician")]          // Leading dot
    [InlineData("-technician")]          // Leading hyphen
    [InlineData("tech@local")]           // Disallowed @ symbol
    [InlineData("tech space")]           // Space
    [InlineData("t")]                    // Single character (minimum is 2)
    [InlineData("1234567890123456789012345678901")] // 31 characters (max 30)
    [InlineData("")]                     // Empty
    public void Reference_WhenInvalid_FailsValidation(string reference)
    {
        var request = ValidRequest(reference);
        var results = Validate(request);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateTechnicianRequest.Reference)));
    }

    [Fact]
    public void Reference_WhenPatternDoesNotMatch_ReturnsSpecificErrorMessageWithDots()
    {
        var request = ValidRequest(".invalid.start");
        var results = Validate(request);
        var error = Assert.Single(results, r => r.MemberNames.Contains(nameof(CreateTechnicianRequest.Reference)));
        Assert.Equal("Reference must contain only letters, numbers, hyphens and dots.", error.ErrorMessage);
    }
}
