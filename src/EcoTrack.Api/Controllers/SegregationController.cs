using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Application.Segregation;
using EcoTrack.Application.Segregation.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/segregation/batches")]
[Authorize(Roles = "admin")]
public class SegregationController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<SegregationBatchListItemResponse>>> Get(
        [FromServices] SegregationService service,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await service.GetBatchesAsync(new GetSegregationBatchesQueryRequest(status, page, pageSize), cancellationToken));
    }

    [HttpGet("pending")]
    public async Task<ActionResult<PagedResponse<SegregationBatchListItemResponse>>> GetPending(
        [FromServices] SegregationService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await service.GetBatchesAsync(new GetSegregationBatchesQueryRequest("Pending", page, pageSize), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SegregationBatchDetailResponse>> GetById(
        Guid id,
        [FromServices] SegregationService service,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetByIdAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/record")]
    public async Task<ActionResult<SegregationBatchDetailResponse>> Record(
        Guid id,
        [FromBody] RecordSegregationDataRequest request,
        [FromServices] SegregationService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.RecordAsync(id, request, actorUserId, cancellationToken));
    }

    [HttpPost("{id:guid}/mark-recycled")]
    public async Task<ActionResult<SegregationBatchDetailResponse>> MarkRecycled(
        Guid id,
        [FromServices] SegregationService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.MarkRecycledAsync(id, actorUserId, cancellationToken));
    }
}
