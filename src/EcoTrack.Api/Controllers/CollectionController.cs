using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Collection;
using EcoTrack.Application.Collection.Contracts;
using EcoTrack.Application.Inventory.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/collection/pickups")]
[Authorize]
public class CollectionController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<PickupListItemResponse>>> Get(
        [FromServices] CollectionService service,
        [FromQuery] GetPickupsQueryRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.GetPickupsAsync(request, actorUserId, actorRole, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PickupDetailResponse>> GetById(
        Guid id,
        [FromServices] CollectionService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.GetByIdAsync(id, actorUserId, actorRole, cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<PickupDetailResponse>> Post(
        [FromServices] CollectionService service,
        [FromBody] CreatePickupRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var created = await service.CreateAsync(request, actorUserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PickupDetailResponse>> Put(
        Guid id,
        [FromServices] CollectionService service,
        [FromBody] UpdatePickupRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.UpdateAsync(id, request, actorUserId, actorRole, cancellationToken));
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<PickupDetailResponse>> Assign(
        Guid id,
        [FromServices] CollectionService service,
        [FromBody] AssignPickupRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.AssignAsync(id, request, actorUserId, actorRole, cancellationToken));
    }

    [HttpPost("{id:guid}/mark-collected")]
    public async Task<ActionResult<PickupDetailResponse>> MarkCollected(
        Guid id,
        [FromServices] CollectionService service,
        [FromBody] MarkCollectedRequest request,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.MarkCollectedAsync(id, request, actorUserId, actorRole, cancellationToken));
    }

    [HttpPost("{id:guid}/send-to-segregation")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<PickupDetailResponse>> SendToSegregation(
        Guid id,
        [FromServices] CollectionService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.SendToSegregationAsync(id, actorUserId, actorRole, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<PickupDetailResponse>> Delete(
        Guid id,
        [FromServices] CollectionService service,
        [FromBody] CancelPickupRequest? request,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.CancelAsync(id, request ?? new CancelPickupRequest(null), actorUserId, actorRole, cancellationToken));
    }

    [HttpGet("{id:guid}/assignment-history")]
    public async Task<ActionResult<PickupHistoryResponse>> GetAssignmentHistory(
        Guid id,
        [FromServices] CollectionService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.GetAssignmentHistoryAsync(id, actorUserId, actorRole, cancellationToken));
    }
}