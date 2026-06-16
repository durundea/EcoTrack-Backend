using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Inventory;

public class DashboardAnalyticsEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public DashboardAnalyticsEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDashboard_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/analytics/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDashboard_WithAdminToken_ReturnsPayloadShape()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<DashboardAnalyticsContract>();
        payload.Should().NotBeNull();
        payload!.Range.Should().NotBeNull();
        payload.Kpis.Should().NotBeNull();
        payload.WasteByCategory.Should().NotBeNull();
        payload.CategoryDistribution.Should().NotBeNull();
        payload.PendingSalesApprovals.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDashboard_WithCollectorToken_OnlyIncludesOwnSalesInTotals()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var collectorSaleResponse = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 2,
            soldAtUtc = DateTime.UtcNow
        });
        collectorSaleResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await AuthenticateAsAdminAsync();
        var adminSaleResponse = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 7,
            soldAtUtc = DateTime.UtcNow
        });
        adminSaleResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await AuthenticateAsCollectorAsync();
        var collectorDashboard = await _client.GetFromJsonAsync<DashboardAnalyticsContract>("/api/analytics/dashboard");

        await AuthenticateAsAdminAsync();
        var adminDashboard = await _client.GetFromJsonAsync<DashboardAnalyticsContract>("/api/analytics/dashboard");

        collectorDashboard!.Kpis.TotalWasteProcessedKg.Should().BeLessThan(adminDashboard!.Kpis.TotalWasteProcessedKg);
    }

    [Fact]
    public async Task GetDashboard_WithoutRange_UsesLast30DaysLabel()
    {
        await AuthenticateAsAdminAsync();

        var payload = await _client.GetFromJsonAsync<DashboardAnalyticsContract>("/api/analytics/dashboard");

        payload!.Range.Label.Should().Be("Last 30 days");
        payload.Range.ToUtc.Should().BeAfter(payload.Range.FromUtc);
    }

    [Fact]
    public async Task GetDashboard_WithInvalidRange_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard?fromUtc=2026-06-15T00:00:00Z&toUtc=2026-06-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("rawWaste")]
    [InlineData("recycledProduct")]
    public async Task GetDashboard_WithSupportedWasteTypeValues_ReturnsOk(string wasteType)
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync($"/api/analytics/dashboard?wasteType={wasteType}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDashboard_WithUnsupportedWasteType_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard?wasteType=metal");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDashboard_WithEmptyDataWindow_ReturnsZerosAndEmptyCollections()
    {
        await AuthenticateAsAdminAsync();

        var payload = await _client.GetFromJsonAsync<DashboardAnalyticsContract>(
            "/api/analytics/dashboard?fromUtc=2000-01-01T00:00:00Z&toUtc=2000-01-02T00:00:00Z");

        payload!.Kpis.TotalWasteProcessedKg.Should().Be(0);
        payload.Kpis.RevenueInr.Should().Be(0);
        payload.WasteByCategory.Should().BeEmpty();
        payload.CategoryDistribution.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboard_PendingApprovalsCount_IsAccurate()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var sale = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 2,
            soldAtUtc = DateTime.UtcNow
        });
        sale.StatusCode.Should().Be(HttpStatusCode.Created);
        var saleRecord = await sale.Content.ReadFromJsonAsync<SaleContract>();
        var submit = await _client.PostAsync($"/api/inventory/sales/{saleRecord!.Id}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await _client.GetFromJsonAsync<DashboardAnalyticsContract>("/api/analytics/dashboard");

        payload!.PendingSalesApprovals.Count.Should().BeGreaterThanOrEqualTo(1);
        payload.PendingSalesApprovals.IsDataAvailable.Should().BeTrue();
        payload.PendingSalesApprovals.Message.Should().BeNull();
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
    private sealed record SaleContract(Guid Id, Guid InventoryItemId, int QuantitySold, decimal RevenueInr, string ApprovalStatus);

    private sealed record DashboardAnalyticsContract(
        DashboardRangeContract Range,
        DashboardKpisContract Kpis,
        List<CategoryMetricContract> WasteByCategory,
        List<CategoryMetricContract> CategoryDistribution,
        PendingSalesApprovalsContract PendingSalesApprovals);

    private sealed record DashboardRangeContract(DateTime FromUtc, DateTime ToUtc, string Label);
    private sealed record DashboardKpisContract(decimal TotalWasteProcessedKg, decimal RevenueInr, decimal RecyclingEfficiencyPercent, decimal Co2ReductionKg);
    private sealed record CategoryMetricContract(string Category, decimal WeightKg, decimal SharePercent);
    private sealed record PendingSalesApprovalsContract(int Count, bool IsDataAvailable, string? Message);
}
