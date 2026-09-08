using System.ComponentModel.DataAnnotations;

namespace DispatchService.DTOs;

public class CreateTechnicianRequest
{
    /// <summary>Unique Dispatch reference, for example TEC-032. Case-insensitive and stored in uppercase.</summary>
    [Required]
    [MaxLength(30)]
    [RegularExpression("^[A-Za-z0-9][A-Za-z0-9-]{1,29}$", ErrorMessage = "Reference must contain only letters, numbers and hyphens.")]
    public string Reference { get; set; } = string.Empty;

    /// <summary>Technician's full name. Required, up to 150 characters.</summary>
    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Coverage province. Must use the same nine region values as Job Service.</summary>
    [Required]
    [RegularExpression("^(WESTERN|CENTRAL|SOUTHERN|NORTHERN|EASTERN|NORTH_WESTERN|NORTH_CENTRAL|UVA|SABARAGAMUWA)$", ErrorMessage = "Region must be one of the nine supported provinces.")]
    public string Region { get; set; } = string.Empty;

    /// <summary>One or more skills used for later assignment evaluation. Each value is a non-empty label of at most 50 characters.</summary>
    [Required]
    [MinLength(1)]
    public List<string> Skills { get; set; } = [];

    /// <summary>ACTIVE by default. INACTIVE records are retained but are not eligible for later assignment.</summary>
    [Required]
    [RegularExpression("^(ACTIVE|INACTIVE)$", ErrorMessage = "Status must be ACTIVE or INACTIVE.")]
    public string Status { get; set; } = "ACTIVE";

    [MaxLength(30)]
    public string? Phone { get; set; }

    [EmailAddress]
    [MaxLength(254)]
    public string? Email { get; set; }
}
