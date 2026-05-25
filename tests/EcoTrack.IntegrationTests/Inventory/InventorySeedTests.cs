using System.Net;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Inventory;

public class InventorySeedTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public InventorySeedTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetInventoryItems_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/inventory/items");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
