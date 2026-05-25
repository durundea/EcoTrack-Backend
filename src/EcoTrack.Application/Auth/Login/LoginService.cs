using EcoTrack.Application.Auth.Contracts;
using EcoTrack.Application.Auth.Interfaces;
using EcoTrack.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Auth.Login;

public class LoginService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginService(
        IApplicationDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.ToLowerInvariant();

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(x => x.Email == email, cancellationToken)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (!user.IsActive || !_passwordHasher.Verify(request.Password, user.PasswordHash, user.Email))
            throw new UnauthorizedAccessException("Invalid credentials.");

        var token = _jwtTokenGenerator.GenerateToken(user);

        return new AuthResponse(
            token,
            new CurrentUserResponse(user.Id, user.Name, user.Email, user.Role.ToString().ToLowerInvariant()));
    }
}
