using DispatchService.DTOs;
using DispatchService.Security;
using DispatchService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DispatchService.Controllers;

/// <summary>
/// Gives an authenticated Technician read-only access to their own current
/// job assignments. No other role may call this endpoint; a Technician cannot
/// reach another Technician's data.
/// </summary>
[ApiController]
[Route("api/my-assignments")]
[Produces("application/json")]
[Authorize(Roles = StaffRoles.Technician)]
public class MyAssignmentsController : ControllerBase
{
    private readonly MyAssignmentsService _service;

    public MyAssignmentsController(MyAssignmentsService service)
    {
        _service = service;
    }

    /// <summary>
    /// Returns all currently assigned jobs for the authenticated Technician.
    /// The caller is identified by the <c>unique_name</c> claim in their JWT,
    /// which must match a Dispatch technician_reference. Returns an empty array
    /// when no live assignments exist — this is not an error condition.
    /// </summary>
    /// <response code="200">
    /// List of live assignments. Empty array when the technician has none.
    /// </response>
    /// <response code="404">
    /// The JWT unique_name claim does not match any Dispatch technician record.
    /// This happens when a Technician account has not yet been provisioned in
    /// Dispatch — the account and the dispatch record are separate entities.
    /// </response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<MyAssignmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyAssignments(CancellationToken cancellationToken)
    {
        // The JWT middleware stores the unique_name claim verbatim under the
        // "unique_name" key (Program.cs sets NameClaimType = JwtRegisteredClaimNames.UniqueName,
        // which means User.Identity.Name also works at runtime, but reading the
        // claim directly by type is safer in unit tests where the name mapping
        // is not configured).
        var technicianReference = User.FindFirst("unique_name")?.Value;
        if (string.IsNullOrWhiteSpace(technicianReference))
            return Unauthorized();

        var result = await _service.GetAssignmentsAsync(technicianReference, cancellationToken);

        if (result.Error == MyAssignmentsError.TechnicianNotFound)
            return NotFound();

        return Ok(result.Assignments);
    }
}
