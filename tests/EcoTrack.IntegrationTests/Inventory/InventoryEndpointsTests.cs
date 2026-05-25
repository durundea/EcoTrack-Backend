using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Inventory;

/// <summary>
/// Tests for GET /api/inventory/items and POST /api/inventory/items.
/// Requires Docker Desktop running (uses IntegrationTestWebAppFactory with real PostgreSQL).
/// </summary>
public class InventoryEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public InventoryEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetItems_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/inventory/items");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetItems_WithAdminToken_ReturnsSeededItems()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/inventory/items");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<InventoryItemContract>>();
        items.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetItems_WithCollectorToken_ReturnsSeededItems()
    {
        await AuthenticateAsCollectorAsync();

        var response = await _client.GetAsync("/api/inventory/items");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<InventoryItemContract>>();
        items.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PostItem_WithAdminToken_CreatesInventoryItem()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.PostAsJsonAsync("/api/inventory/items", new
        {
            name = "Sorted Paper Bale",
            category = "rawWaste",
            quantityKg = 35,
            unit = "kg",
            standardPriceInr = 12
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<InventoryItemContract>();
        created!.Name.Should().Be("Sorted Paper Bale");
        created.Category.Should().Be("RawWaste");
    }

    [Fact]
    public async Task PostItem_WithCollectorToken_ReturnsForbidden()
    {
        await AuthenticateAsCollectorAsync();

        var response = await _client.PostAsJsonAsync("/api/inventory/items", new
        {
            name = "Unauthorised Item",
            category = "rawWaste",
            quantityKg = 5,
            unit = "kg",
            standardPriceInr = 5
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PatchPrice_WithAdminToken_UpdatesStandardPrice()
    {
        await AuthenticateAsAdminAsync();
        var firstItem = (await _client.GetFromJsonAsync<List<InventoryItemContract>>("/api/inventory/items"))!.First();

        var response = await _client.PatchAsJsonAsync($"/api/inventory/items/{firstItem.Id}/price", new
        {
            standardPriceInr = 77
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<InventoryItemContract>();
        updated!.StandardPriceInr.Should().Be(77);
    }

    [Fact]
    public async Task PatchPrice_WithCollectorToken_ReturnsForbidden()
    {
        await AuthenticateAsCollectorAsync();

        var response = await _client.PatchAsJsonAsync(
            $"/api/inventory/items/{Guid.NewGuid()}/price",
            new { standardPriceInr = 77 });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task AuthenticateAsAdminAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@ecotrack.local", password = "admin123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private async Task AuthenticateAsCollectorAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "collector@ecotrack.local", password = "collector123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private sealed record AuthPayload(string Token);
    private sealed record InventoryItemContract(Guid Id, string Name, string Category, decimal QuantityKg, string Unit, decimal StandardPriceInr);
}
