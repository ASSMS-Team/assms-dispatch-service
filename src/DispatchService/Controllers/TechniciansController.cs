using DispatchService.DTOs;
using DispatchService.Services;
using Microsoft.AspNetCore.Mvc;

namespace DispatchService.Controllers;

[ApiController]
[Route("api/technicians")]
[Produces("application/json")]
public class TechniciansController : ControllerBase
{
    private readonly TechnicianService _technicianService;

    public TechniciansController(TechnicianService technicianService)
    {
        _technicianService = technicianService;
    }

    /// <summary>Creates a Dispatch-owned Technician record. This endpoint never creates a StaffAccount or writes to Customer &amp; Asset Service.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TechnicianResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTechnicianRequest request)
    {
        var result = await _technicianService.CreateAsync(request);
        if (result.Error == TechnicianCreateError.InvalidSkills)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["skills"] = new[] { "Provide at least one non-empty skill of at most 50 characters." },
            }) { Status = StatusCodes.Status400BadRequest });

        if (result.Error == TechnicianCreateError.DuplicateReference)
            return Conflict(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["reference"] = new[] { "A technician already uses this reference." },
            }) { Status = StatusCodes.Status409Conflict });

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>Returns all Dispatch-owned Technicians with their assignment capability fields.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TechnicianResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TechnicianResponse>>> GetAll()
    {
        return Ok(await _technicianService.GetAllAsync());
    }

    /// <summary>Returns a Technician by its Dispatch id.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(TechnicianResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id)
    {
        var technician = await _technicianService.GetByIdAsync(id);
        return technician is null ? NotFound() : Ok(technician);
    }
}
