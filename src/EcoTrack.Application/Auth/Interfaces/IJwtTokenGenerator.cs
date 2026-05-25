using EcoTrack.Domain.Auth;

namespace EcoTrack.Application.Auth.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(User user);
}
