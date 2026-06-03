using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Inventory;
using EcoTrack.Application.Inventory.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/inventory/sales")]
[Authorize]
public class SalesController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<SaleRecordResponse>>> Get(
        [FromServices] SalesService service,
        [FromQuery] GetSalesQueryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        var result = await service.GetSalesAsync(request, userId, role, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleRecordResponse>> GetById(
        Guid id,
        [FromServices] SalesService service,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        var result = await service.GetByIdAsync(id, userId, role, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<SaleRecordResponse>> Post(
        [FromServices] SalesService service,
        [FromBody] CreateSaleRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var created = await service.CreateDraftAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(Post), new { id = created.Id }, created);
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult<SaleRecordResponse>> Submit(
        Guid id,
        [FromServices] SalesService service,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.SubmitAsync(id, userId, role, cancellationToken));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<SaleRecordResponse>> Approve(
        Guid id,
        [FromServices] SalesService service,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.ApproveAsync(id, userId, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SaleRecordResponse>> Put(
        Guid id,
        [FromServices] SalesService service,
        [FromBody] UpdateSaleRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await service.UpdateDraftAsync(id, request, cancellationToken));
    }
}
