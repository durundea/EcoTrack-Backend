namespace EcoTrack.Application.Auth.Contracts;

public sealed record AuthResponse(string Token, CurrentUserResponse User);
