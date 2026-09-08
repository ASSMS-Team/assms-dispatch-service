namespace DispatchService.Models;

// Dispatch-owned field-service record. It deliberately has no StaffAccount id:
// login accounts belong to Customer & Asset Service and are linked only by a
// future provisioning story.
public class Technician
{
    public string Id { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE";
    public IReadOnlyList<string> Skills { get; set; } = Array.Empty<string>();
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
