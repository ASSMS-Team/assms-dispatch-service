using DispatchService.DTOs;
using DispatchService.Models;
using DispatchService.Repositories;
using MySqlConnector;

namespace DispatchService.Services;

public enum TechnicianCreateError { None, DuplicateReference, InvalidSkills }
public enum TechnicianUpdateError { None, NotFound, InvalidSkills, InvalidRegion }

public record TechnicianCreateResult(TechnicianResponse? Value, TechnicianCreateError Error);
public record TechnicianUpdateResult(TechnicianResponse? Value, TechnicianUpdateError Error);

public class TechnicianService
{
    private readonly ITechnicianRepository _repository;

    public TechnicianService(ITechnicianRepository repository)
    {
        _repository = repository;
    }

    public async Task<TechnicianCreateResult> CreateAsync(CreateTechnicianRequest request)
    {
        var skills = request.Skills
            .Select(skill => skill?.Trim() ?? string.Empty)
            .Where(skill => skill.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (skills.Length == 0 || skills.Any(skill => skill.Length > 50))
            return new(null, TechnicianCreateError.InvalidSkills);

        var technician = new Technician
        {
            Id = Guid.NewGuid().ToString(),
            Reference = request.Reference.Trim().ToUpperInvariant(),
            FullName = request.FullName.Trim(),
            Region = request.Region.Trim().ToUpperInvariant(),
            Status = request.Status.Trim().ToUpperInvariant(),
            Skills = skills,
            Phone = EmptyToNull(request.Phone),
            Email = EmptyToNull(request.Email),
        };

        if (await _repository.ReferenceExistsAsync(technician.Reference))
            return new(null, TechnicianCreateError.DuplicateReference);

        try
        {
            await _repository.CreateAsync(technician);
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            // The preflight provides the friendly normal path; the unique index
            // remains authoritative when two requests race for the same value.
            return new(null, TechnicianCreateError.DuplicateReference);
        }

        var now = DateTime.UtcNow;
        technician.CreatedAt = now;
        technician.UpdatedAt = now;
        return new(ToResponse(technician), TechnicianCreateError.None);
    }

    public async Task<TechnicianResponse?> GetByIdAsync(string id)
    {
        var technician = await _repository.GetByIdAsync(id);
        return technician is null ? null : ToResponse(technician);
    }

    public async Task<IReadOnlyList<TechnicianResponse>> GetAllAsync()
    {
        var technicians = await _repository.GetAllAsync();
        return technicians.Select(ToResponse).ToList();
    }

    public async Task<TechnicianUpdateResult> UpdateAsync(string id, UpdateTechnicianRequest request)
    {
        var existing = await _repository.GetByIdAsync(id);
        if (existing is null) return new(null, TechnicianUpdateError.NotFound);

        var skills = NormalizeSkills(request.Skills);
        if (skills is null) return new(null, TechnicianUpdateError.InvalidSkills);

        var region = request.Region.Trim().ToUpperInvariant();
        if (!SupportedRegions.Contains(region)) return new(null, TechnicianUpdateError.InvalidRegion);

        existing.FullName = request.FullName.Trim();
        existing.Region = region;
        existing.Skills = skills;
        existing.Status = request.Status.Trim().ToUpperInvariant();
        existing.Phone = EmptyToNull(request.Phone);
        existing.Email = EmptyToNull(request.Email);

        if (!await _repository.UpdateAsync(existing)) return new(null, TechnicianUpdateError.NotFound);

        existing.UpdatedAt = DateTime.UtcNow;
        return new(ToResponse(existing), TechnicianUpdateError.None);
    }

    private static readonly HashSet<string> SupportedRegions = new(StringComparer.Ordinal)
    {
        "WESTERN", "CENTRAL", "SOUTHERN", "NORTHERN", "EASTERN", "NORTH_WESTERN", "NORTH_CENTRAL", "UVA", "SABARAGAMUWA",
    };

    private static string[]? NormalizeSkills(IEnumerable<string>? values)
    {
        var skills = (values ?? [])
            .Select(skill => skill?.Trim() ?? string.Empty)
            .Where(skill => skill.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return skills.Length == 0 || skills.Any(skill => skill.Length > 50) ? null : skills;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static TechnicianResponse ToResponse(Technician technician) => new()
    {
        Id = technician.Id,
        Reference = technician.Reference,
        FullName = technician.FullName,
        Region = technician.Region,
        Status = technician.Status,
        Skills = technician.Skills,
        Phone = technician.Phone,
        Email = technician.Email,
        CreatedAt = technician.CreatedAt,
        UpdatedAt = technician.UpdatedAt,
    };
}
