using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Auth.Contracts;
using EcoTrack.Application.Auth.Login;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromServices] LoginService service,
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.LoginAsync(request, cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<CurrentUserResponse> Me()
    {
        var id = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var name = User.FindFirst(JwtRegisteredClaimNames.Name)?.Value;
        var email = User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        var role = User.FindFirst(ClaimTypes.Role)?.Value;

        return Ok(new CurrentUserResponse(Guid.Parse(id!), name!, email!, role!));
    }
}
