using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Collection;

public class CollectionEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public CollectionEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetPickups_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/collection/pickups");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminCanCreateAndListPickups()
    {
        await AuthenticateAsAdminAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/collection/pickups", new
        {
            siteName = "Green Residency",
            siteAddressText = "Block A",
            scheduledAtUtc = DateTime.UtcNow.AddDays(1),
            estimatedWeightKg = 120.0m,
            notes = "Morning slot"
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await _client.GetAsync("/api/collection/pickups?page=1&pageSize=20&sortBy=scheduledAtUtc&sortDirection=desc");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await listResponse.Content.ReadFromJsonAsync<PagedPickupsContract>();
        payload.Should().NotBeNull();
        payload!.Items.Should().Contain(x => x.SiteName == "Green Residency");
    }

    [Fact]
    public async Task AdminCanAssignPickupToCollector()
    {
        await AuthenticateAsAdminAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/collection/pickups", new
        {
            siteName = "Assignment Site",
            siteAddressText = "Block B",
            scheduledAtUtc = DateTime.UtcNow.AddDays(1),
            estimatedWeightKg = 80.0m,
            notes = "Assign flow"
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var pickup = await createResponse.Content.ReadFromJsonAsync<PickupDetailContract>();
        pickup.Should().NotBeNull();

        var collectorId = await GetCollectorUserIdAsync();

        var assignResponse = await _client.PostAsJsonAsync($"/api/collection/pickups/{pickup!.Id}/assign", new
        {
            assignedCollectorUserId = collectorId,
            note = "Assigned for pickup run"
        });

        assignResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var assigned = await assignResponse.Content.ReadFromJsonAsync<PickupDetailContract>();
        assigned.Should().NotBeNull();
        assigned!.AssignedCollectorUserId.Should().Be(collectorId);
        assigned.Status.Should().Be("Assigned");
        assigned.AssignmentEvents.Should().ContainSingle();
    }

    private async Task AuthenticateAsAdminAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@ecotrack.local", password = "admin123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private async Task<Guid> GetCollectorUserIdAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "collector@ecotrack.local", password = "collector123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);

        var me = await _client.GetFromJsonAsync<MeContract>("/api/auth/me");
        await AuthenticateAsAdminAsync();

        return me!.Id;
    }

    private sealed record AuthPayload(string Token);

    private sealed record MeContract(Guid Id, string Name, string Email, string Role);

    private sealed record PagedPickupsContract(
        List<PickupContract> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);

    private sealed record PickupContract(
        Guid Id,
        string PickupCode,
        string SiteName,
        string SiteAddressText,
        DateTime ScheduledAtUtc,
        decimal EstimatedWeightKg,
        decimal? CollectedWeightKg,
        string Status,
        Guid? AssignedCollectorUserId,
        string? AssignedCollectorDisplayName,
        string? Notes);

    private sealed record PickupDetailContract(
        Guid Id,
        string PickupCode,
        string SiteName,
        string SiteAddressText,
        DateTime ScheduledAtUtc,
        decimal EstimatedWeightKg,
        decimal? CollectedWeightKg,
        string Status,
        Guid? AssignedCollectorUserId,
        string? AssignedCollectorDisplayName,
        string? Notes,
        Guid CreatedByUserId,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        Guid? CancelledByUserId,
        DateTime? CancelledAtUtc,
        string? CancelReason,
        List<AssignmentEventContract> AssignmentEvents);

    private sealed record AssignmentEventContract(
        Guid Id,
        Guid PickupTaskId,
        Guid? PreviousCollectorUserId,
        Guid NewCollectorUserId,
        Guid ChangedByUserId,
        DateTime ChangedAtUtc,
        string? Note);
}
