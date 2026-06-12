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

    [Fact]
    public async Task GetSales_WithAdminToken_ReturnsPagedSales()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var first = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 2,
            soldAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstSale = await first.Content.ReadFromJsonAsync<SaleRecordContract>();

        var second = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 3,
            soldAtUtc = DateTime.UtcNow
        });
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondSale = await second.Content.ReadFromJsonAsync<SaleRecordContract>();

        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/inventory/sales?page=1&pageSize=10&sortBy=soldAtUtc&sortDirection=desc");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PagedSalesContract>();
        payload.Should().NotBeNull();
        payload!.Items.Count.Should().BeGreaterThanOrEqualTo(2);
        payload.Items.Should().Contain(x => x.Id == firstSale!.Id);
        payload.Items.Should().Contain(x => x.Id == secondSale!.Id);
        payload.Page.Should().Be(1);
        payload.PageSize.Should().Be(10);
        payload.TotalCount.Should().BeGreaterThanOrEqualTo(payload.Items.Count);

        var newerSaleIndex = payload.Items.FindIndex(x => x.Id == secondSale!.Id);
        var olderSaleIndex = payload.Items.FindIndex(x => x.Id == firstSale!.Id);
        newerSaleIndex.Should().BeGreaterThanOrEqualTo(0);
        olderSaleIndex.Should().BeGreaterThanOrEqualTo(0);
        newerSaleIndex.Should().BeLessThan(olderSaleIndex);
    }

    [Fact]
    public async Task GetSales_WithCollectorToken_ReturnsOnlyOwnSales()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var create = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 1,
            soldAtUtc = DateTime.UtcNow
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var collectorSale = await create.Content.ReadFromJsonAsync<SaleRecordContract>();

        await AuthenticateAsAdminAsync();
        var adminCreate = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 7,
            soldAtUtc = DateTime.UtcNow
        });
        adminCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var adminSale = await adminCreate.Content.ReadFromJsonAsync<SaleRecordContract>();

        await AuthenticateAsCollectorAsync();

        var response = await _client.GetAsync("/api/inventory/sales");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PagedSalesContract>();
        payload.Should().NotBeNull();
        payload!.Items.Should().NotContain(x => x.Id == adminSale!.Id);
        payload.Items.Should().Contain(x => x.Id == collectorSale!.Id);
    }

    [Fact]
    public async Task GetSaleById_WithCollectorToken_ForAnotherUsersSale_ReturnsNotFound()
    {
        await AuthenticateAsAdminAsync();
        var itemId = await GetFirstInventoryItemIdAsync();
        var create = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 4,
            soldAtUtc = DateTime.UtcNow
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var sale = await create.Content.ReadFromJsonAsync<SaleRecordContract>();

        await AuthenticateAsCollectorAsync();

        var response = await _client.GetAsync($"/api/inventory/sales/{sale!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSales_WithInvalidRange_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/inventory/sales?fromSoldAtUtc=2026-06-10T00:00:00Z&toSoldAtUtc=2026-06-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetSales_WithStatusFilter_ReturnsOnlyMatchingStatus()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var createDraft = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 1,
            soldAtUtc = DateTime.UtcNow
        });
        createDraft.StatusCode.Should().Be(HttpStatusCode.Created);

        var createPending = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 2,
            soldAtUtc = DateTime.UtcNow.AddMinutes(1)
        });
        createPending.StatusCode.Should().Be(HttpStatusCode.Created);
        var pendingSale = await createPending.Content.ReadFromJsonAsync<SaleRecordContract>();

        var submit = await _client.PostAsync($"/api/inventory/sales/{pendingSale!.Id}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/inventory/sales?status=Draft");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PagedSalesContract>();
        payload.Should().NotBeNull();
        payload!.Items.Should().NotBeEmpty();
        payload.Items.Should().OnlyContain(x => x.ApprovalStatus == "Draft");
    }

    [Fact]
    public async Task GetSales_WithSortAsc_ReturnsAscendingBySoldAtUtc()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var first = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 1,
            soldAtUtc = DateTime.UtcNow.AddHours(-2)
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 1,
            soldAtUtc = DateTime.UtcNow.AddHours(-1)
        });
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        await AuthenticateAsAdminAsync();
        var response = await _client.GetAsync("/api/inventory/sales?sortBy=soldAtUtc&sortDirection=asc&page=1&pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<PagedSalesContract>();
        payload.Should().NotBeNull();

        var timestamps = payload!.Items.Select(x => x.SoldAtUtc).ToList();
        timestamps.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetSales_WithInvalidSortDirection_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/inventory/sales?sortBy=soldAtUtc&sortDirection=sideways");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiErrorContract>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be(400);
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
    private sealed record PagedSalesContract(List<SaleRecordDetailContract> Items, int Page, int PageSize, int TotalCount, int TotalPages);
    private sealed record SaleRecordDetailContract(
        Guid Id,
        Guid InventoryItemId,
        int QuantitySold,
        decimal RevenueInr,
        DateTime SoldAtUtc,
        string ApprovalStatus,
        Guid RequestedByUserId,
        Guid? ApprovedByUserId,
        DateTime? ApprovedAtUtc,
        string? RejectionReason);
    private sealed record ApiErrorContract(int Status, string Message);
}
