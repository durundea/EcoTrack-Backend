using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Inventory;
using EcoTrack.Application.Inventory.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Roles = "admin,collector")]
public class AnalyticsController : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardAnalyticsResponse>> GetDashboard(
        [FromServices] DashboardAnalyticsService service,
        [FromQuery] GetDashboardAnalyticsQueryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await service.GetDashboardAsync(request, userId, role, cancellationToken);
        return Ok(result);
    }
}
