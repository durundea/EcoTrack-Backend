using EcoTrack.Application.Auth.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace EcoTrack.Infrastructure.Security;

/// <summary>
/// Wraps ASP.NET Core's PasswordHasher to verify passwords hashed during seeding.
/// The <c>purpose</c> parameter must match the value used when hashing (the user's email).
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<string> _hasher = new();

    public bool Verify(string plaintext, string hash, string purpose)
    {
        var result = _hasher.VerifyHashedPassword(purpose, hash, plaintext);
        return result != PasswordVerificationResult.Failed;
    }
}
