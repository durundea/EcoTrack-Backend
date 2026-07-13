using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Application.Recycling;
using EcoTrack.Application.Recycling.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/recycling")]
[Authorize(Roles = "admin")]
public class RecyclingController : ControllerBase
{
    [HttpGet("batches")]
    public async Task<ActionResult<PagedResponse<RecyclingBatchListItemResponse>>> GetBatches(
        [FromServices] RecyclingService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await service.GetBatchesAsync(
            new GetRecyclingBatchesQueryRequest(page, pageSize),
            cancellationToken));
    }

    [HttpGet("batches/{id:guid}")]
    public async Task<ActionResult<RecyclingBatchDetailResponse>> GetBatchById(
        Guid id,
        [FromServices] RecyclingService service,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetByIdAsync(id, cancellationToken));
    }

    [HttpPost("batches/{id:guid}/advance-stage")]
    public async Task<ActionResult<RecyclingBatchDetailResponse>> AdvanceStage(
        Guid id,
        [FromBody] AdvanceStageRequest request,
        [FromServices] RecyclingService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.AdvanceStageAsync(id, request, actorUserId, cancellationToken));
    }

    [HttpPost("batches/{id:guid}/conversions")]
    public async Task<ActionResult<ProductConversionResponse>> CreateConversion(
        Guid id,
        [FromBody] CreateConversionRequest request,
        [FromServices] RecyclingService service,
        CancellationToken cancellationToken)
    {
        return Ok(await service.CreateConversionAsync(id, request, cancellationToken));
    }

    [HttpPost("conversions/sync-inventory")]
    public async Task<ActionResult<InventorySyncResponse>> SyncInventory(
        [FromServices] InventorySyncService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.SyncConversionsToInventoryAsync(actorUserId, cancellationToken));
    }
}
