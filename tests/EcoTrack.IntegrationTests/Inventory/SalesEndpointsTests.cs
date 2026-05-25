using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Inventory;

/// <summary>
/// Tests for the sales workflow: draft → submit → approve/reject.
/// Requires Docker Desktop running (uses IntegrationTestWebAppFactory with real PostgreSQL).
/// </summary>
public class SalesEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public SalesEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CollectorCanCreateDraftAndSubmitForApproval()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 2,
            soldAtUtc = DateTime.UtcNow
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<SaleRecordContract>();
        created!.ApprovalStatus.Should().Be("Draft");

        var submitResponse = await _client.PostAsync($"/api/inventory/sales/{created.Id}/submit", null);
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<SaleRecordContract>();
        submitted!.ApprovalStatus.Should().Be("PendingApproval");
    }

    [Fact]
    public async Task AdminCanApprovePendingSale()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 1,
            soldAtUtc = DateTime.UtcNow
        });
        var created = await createResponse.Content.ReadFromJsonAsync<SaleRecordContract>();
        await _client.PostAsync($"/api/inventory/sales/{created!.Id}/submit", null);

        await AuthenticateAsAdminAsync();
        var approveResponse = await _client.PostAsync($"/api/inventory/sales/{created.Id}/approve", null);

        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResponse.Content.ReadFromJsonAsync<SaleRecordContract>();
        approved!.ApprovalStatus.Should().Be("Approved");
    }

    [Fact]
    public async Task ApprovedSaleCannotBeEdited_ReturnsConflict()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 1,
            soldAtUtc = DateTime.UtcNow
        });
        var created = await createResponse.Content.ReadFromJsonAsync<SaleRecordContract>();
        await _client.PostAsync($"/api/inventory/sales/{created!.Id}/submit", null);

        await AuthenticateAsAdminAsync();
        await _client.PostAsync($"/api/inventory/sales/{created.Id}/approve", null);

        var updateResponse = await _client.PutAsJsonAsync($"/api/inventory/sales/{created.Id}", new
        {
            quantitySold = 3,
            soldAtUtc = DateTime.UtcNow
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var errorPayload = await updateResponse.Content.ReadFromJsonAsync<ApiErrorContract>();
        errorPayload!.Status.Should().Be(409);
    }

    [Fact]
    public async Task CollectorCannotApprove_ReturnsForbidden()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();
        var createResponse = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 1,
            soldAtUtc = DateTime.UtcNow
        });
        var created = await createResponse.Content.ReadFromJsonAsync<SaleRecordContract>();
        await _client.PostAsync($"/api/inventory/sales/{created!.Id}/submit", null);

        var approveResponse = await _client.PostAsync($"/api/inventory/sales/{created.Id}/approve", null);

        approveResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> GetFirstInventoryItemIdAsync()
    {
        var response = await _client.GetFromJsonAsync<List<InventoryItemContract>>("/api/inventory/items");
        return response!.First().Id;
    }

    private async Task AuthenticateAsCollectorAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "collector@ecotrack.local", password = "collector123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private async Task AuthenticateAsAdminAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@ecotrack.local", password = "admin123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private sealed record AuthPayload(string Token);
    private sealed record InventoryItemContract(Guid Id, string Name, string Category, decimal QuantityKg, string Unit, decimal StandardPriceInr);
    private sealed record SaleRecordContract(Guid Id, Guid InventoryItemId, int QuantitySold, decimal RevenueInr, string ApprovalStatus);
    private sealed record ApiErrorContract(int Status, string Message);
}
