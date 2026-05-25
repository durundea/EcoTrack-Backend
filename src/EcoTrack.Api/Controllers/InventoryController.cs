using EcoTrack.Application.Inventory;
using EcoTrack.Application.Inventory.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/inventory/items")]
[Authorize]
public class InventoryController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<InventoryItemResponse>>> Get(
        [FromServices] InventoryService service,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetItemsAsync(cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<InventoryItemResponse>> Post(
        [FromServices] InventoryService service,
        [FromBody] CreateInventoryItemRequest request,
        CancellationToken cancellationToken)
    {
        var created = await service.CreateItemAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id:guid}/price")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<InventoryItemResponse>> PatchPrice(
        Guid id,
        [FromServices] InventoryService service,
        [FromBody] UpdateInventoryPriceRequest request,
        CancellationToken cancellationToken)
    {
        var actorRole = User.FindFirstValue(ClaimTypes.Role)!;
        var updated = await service.UpdatePriceAsync(id, request, actorRole, cancellationToken);
        return Ok(updated);
    }
}
