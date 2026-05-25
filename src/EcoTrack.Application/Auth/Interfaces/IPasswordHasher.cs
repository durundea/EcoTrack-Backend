namespace EcoTrack.Application.Auth.Interfaces;

public interface IPasswordHasher
{
    /// <summary>
    /// Verifies a plaintext password against the stored hash.
    /// The <paramref name="purpose"/> must match the value used when hashing (typically the user's email).
    /// </summary>
    bool Verify(string plaintext, string hash, string purpose);
}
