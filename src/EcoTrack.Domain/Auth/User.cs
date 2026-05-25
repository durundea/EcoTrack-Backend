namespace EcoTrack.Domain.Auth;

public class User
{
    private User() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static User Create(string name, string email, string passwordHash, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name is required.");
        if (string.IsNullOrWhiteSpace(email)) throw new InvalidOperationException("Email is required.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new InvalidOperationException("Password hash is required.");

        return new User
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }
}
