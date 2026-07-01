using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Segregation;

public class SegregationEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public SegregationEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBatches_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/segregation/batches");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBatches_WithCollectorToken_ReturnsForbidden()
    {
        await AuthenticateAsCollectorAsync();

        var response = await _client.GetAsync("/api/segregation/batches");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminWorkflow_RecordThenRecycle_ReturnsUpdatedStatuses()
    {
        await AuthenticateAsAdminAsync();
        var pickupId = await CreateSentToSegregationPickupAsync();

        var pending = await _client.GetFromJsonAsync<PagedBatchesContract>("/api/segregation/batches?status=Pending&page=1&pageSize=20");
        var batch = pending!.Items.Single(x => x.PickupTaskId == pickupId);

        var recordResponse = await _client.PostAsJsonAsync($"/api/segregation/batches/{batch.Id}/record", new
        {
            plasticKg = 10m,
            organicKg = 5m,
            metalKg = 2m,
            paperKg = 1m,
            eWasteKg = 0.5m
        });

        recordResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recorded = await recordResponse.Content.ReadFromJsonAsync<BatchDetailContract>();
        recorded!.Status.Should().Be("Recorded");

        var recycleResponse = await _client.PostAsync($"/api/segregation/batches/{batch.Id}/mark-recycled", null);
        recycleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recycled = await recycleResponse.Content.ReadFromJsonAsync<BatchDetailContract>();
        recycled!.Status.Should().Be("Recycled");
    }

    [Fact]
    public async Task SendToSegregation_AutoCreatesPendingBatch()
    {
        await AuthenticateAsAdminAsync();
        var pickupId = await CreateSentToSegregationPickupAsync();

        var pending = await _client.GetFromJsonAsync<PagedBatchesContract>("/api/segregation/batches?status=Pending&page=1&pageSize=20");

        pending!.Items.Should().Contain(x => x.PickupTaskId == pickupId && x.Status == "Pending");
    }

    [Fact]
    public async Task Record_WithAllZeroWeights_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();
        var pickupId = await CreateSentToSegregationPickupAsync();

        var pending = await _client.GetFromJsonAsync<PagedBatchesContract>("/api/segregation/batches?status=Pending&page=1&pageSize=20");
        var batch = pending!.Items.Single(x => x.PickupTaskId == pickupId);

        var response = await _client.PostAsJsonAsync($"/api/segregation/batches/{batch.Id}/record", new
        {
            plasticKg = 0m,
            organicKg = 0m,
            metalKg = 0m,
            paperKg = 0m,
            eWasteKg = 0m
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MarkRecycled_OnPendingBatch_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();
        var pickupId = await CreateSentToSegregationPickupAsync();

        var pending = await _client.GetFromJsonAsync<PagedBatchesContract>("/api/segregation/batches?status=Pending&page=1&pageSize=20");
        var batch = pending!.Items.Single(x => x.PickupTaskId == pickupId);

        var response = await _client.PostAsync($"/api/segregation/batches/{batch.Id}/mark-recycled", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> CreateSentToSegregationPickupAsync()
    {
        await AuthenticateAsAdminAsync();
        var create = await _client.PostAsJsonAsync("/api/collection/pickups", new
        {
            siteName = "Segregation Site",
            siteAddressText = "Warehouse 5",
            scheduledAtUtc = DateTime.UtcNow.AddHours(6),
            estimatedWeightKg = 100m,
            notes = "for segregation"
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var pickup = await create.Content.ReadFromJsonAsync<PickupDetailContract>();

        await _client.PostAsJsonAsync($"/api/collection/pickups/{pickup!.Id}/assign", new
        {
            assignedCollectorUserId = await GetCollectorUserIdAsync(),
            note = "assign for segregation workflow"
        });

        await AuthenticateAsCollectorAsync();
        var markCollected = await _client.PostAsJsonAsync($"/api/collection/pickups/{pickup.Id}/mark-collected", new { collectedWeightKg = 95m });
        markCollected.StatusCode.Should().Be(HttpStatusCode.OK);

        await AuthenticateAsAdminAsync();
        var send = await _client.PostAsync($"/api/collection/pickups/{pickup.Id}/send-to-segregation", null);
        send.StatusCode.Should().Be(HttpStatusCode.OK);

        return pickup.Id;
    }

    private async Task<Guid> GetCollectorUserIdAsync()
    {
        await AuthenticateAsCollectorAsync();
        var me = await _client.GetFromJsonAsync<MeContract>("/api/auth/me");
        await AuthenticateAsAdminAsync();
        return me!.Id;
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
    private sealed record MeContract(Guid Id, string Name, string Email, string Role);
    private sealed record PickupDetailContract(Guid Id);
    private sealed record PagedBatchesContract(List<BatchListContract> Items, int Page, int PageSize, int TotalCount, int TotalPages);

    private sealed record BatchListContract(
        Guid Id,
        Guid PickupTaskId,
        string BatchCode,
        string PickupCode,
        string Status,
        DateTime? RecordedAtUtc,
        DateTime? RecycledAtUtc);

    private sealed record BatchDetailContract(
        Guid Id,
        string BatchCode,
        string Status,
        Guid PickupTaskId,
        string PickupCode,
        string SiteName,
        string SiteAddressText,
        DateTime ScheduledAtUtc,
        decimal CollectedWeightKg,
        decimal? PlasticKg,
        decimal? OrganicKg,
        decimal? MetalKg,
        decimal? PaperKg,
        decimal? EWasteKg,
        Guid? RecordedByUserId,
        DateTime? RecordedAtUtc,
        Guid? RecycledByUserId,
        DateTime? RecycledAtUtc,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc);
}
