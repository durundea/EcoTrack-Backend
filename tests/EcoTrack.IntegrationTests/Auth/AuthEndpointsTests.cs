using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Auth;

/// <summary>
/// Tests for POST /api/auth/login and GET /api/auth/me.
/// Requires Docker Desktop running (uses IntegrationTestWebAppFactory with real PostgreSQL).
/// </summary>
public class AuthEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithSeededAdminCredentials_ReturnsJwtToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@ecotrack.local",
            password = "admin123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthResponseContract>();
        payload!.Token.Should().NotBeNullOrWhiteSpace();
        payload.User.Role.Should().Be("admin");
    }

    [Fact]
    public async Task Login_WithSeededCollectorCredentials_ReturnsJwtToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "collector@ecotrack.local",
            password = "collector123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<AuthResponseContract>();
        payload!.Token.Should().NotBeNullOrWhiteSpace();
        payload.User.Role.Should().Be("collector");
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@ecotrack.local",
            password = "wrongpassword"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "nobody@ecotrack.local",
            password = "anything"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithValidAdminToken_ReturnsCurrentUser()
    {
        await AuthenticateAsync("admin@ecotrack.local", "admin123");

        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<CurrentUserContract>();
        user!.Email.Should().Be("admin@ecotrack.local");
        user.Role.Should().Be("admin");
    }

    [Fact]
    public async Task Me_WithValidCollectorToken_ReturnsCurrentUser()
    {
        await AuthenticateAsync("collector@ecotrack.local", "collector123");

        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<CurrentUserContract>();
        user!.Email.Should().Be("collector@ecotrack.local");
        user.Role.Should().Be("collector");
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task AuthenticateAsync(string email, string password)
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var payload = await login.Content.ReadFromJsonAsync<AuthResponseContract>();
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private sealed record AuthResponseContract(string Token, CurrentUserContract User);
    private sealed record CurrentUserContract(Guid Id, string Name, string Email, string Role);
}
