using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests;

public class HealthEndpointTests : IClassFixture<LightWebAppFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(LightWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        payload!.Status.Should().Be("healthy");
    }

    private sealed record HealthResponse(string Status);
}