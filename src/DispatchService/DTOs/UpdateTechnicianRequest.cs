using System.ComponentModel.DataAnnotations;

namespace DispatchService.DTOs;

public class UpdateTechnicianRequest
{
    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(WESTERN|CENTRAL|SOUTHERN|NORTHERN|EASTERN|NORTH_WESTERN|NORTH_CENTRAL|UVA|SABARAGAMUWA)$", ErrorMessage = "Region must be one of the nine supported provinces.")]
    public string Region { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public List<string> Skills { get; set; } = [];

    [Required]
    [RegularExpression("^(ACTIVE|INACTIVE)$", ErrorMessage = "Status must be ACTIVE or INACTIVE.")]
    public string Status { get; set; } = "ACTIVE";

    [MaxLength(30)]
    public string? Phone { get; set; }

    [EmailAddress]
    [MaxLength(254)]
    public string? Email { get; set; }
}
