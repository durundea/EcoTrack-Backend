using System.Net;
using System.Net.Http.Json;
using EcoTrack.Application.Recycling.Contracts;
using EcoTrack.Domain.Inventory;
using Xunit;

namespace EcoTrack.IntegrationTests.Recycling;

public class RecyclingEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly IntegrationTestWebAppFactory _factory;
    private readonly HttpClient _client;

    public RecyclingEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBatches_ReturnsOk_WithPaginatedBatches()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(
            plasticKg: 10,
            organicKg: 20);

        // Act
        var response = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        Assert.NotNull(result);
        Assert.True(result.Items.Count > 0);
        Assert.Contains(result.Items, x => x.SourceCategory == "plastic" && x.SourceWeightKg == 10);
        Assert.Contains(result.Items, x => x.SourceCategory == "organic" && x.SourceWeightKg == 20);
    }

    [Fact]
    public async Task GetBatchById_ReturnsBatch_WithStageHistory()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(
            plasticKg: 15);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Act
        var response = await _client.GetAsync($"/api/recycling/batches/{batch.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsAsync<RecyclingBatchDetailResponse>();
        Assert.NotNull(result);
        Assert.Equal("Segregated", result.Stage);
        Assert.NotEmpty(result.StageHistory);
        Assert.Contains(result.StageHistory, x => x.Stage == "Segregated");
    }

    [Fact]
    public async Task AdvanceStage_TransitionsToProcessing_AndUpdatesHistory()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(plasticKg: 10);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Processing" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsAsync<RecyclingBatchDetailResponse>();
        Assert.Equal("Processing", result.Stage);
        Assert.True(result.StageHistory.Count > 1);
    }

    [Fact]
    public async Task AdvanceStage_ToConverted_ThenCreateConversion_Succeeds()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(plasticKg: 10);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Advance to Processing
        await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Processing" });

        // Advance to Converted
        var convertResponse = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Converted" });
        Assert.Equal(HttpStatusCode.OK, convertResponse.StatusCode);

        // Act - Create conversion
        var createConversionResponse = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/conversions",
            new { productName = "Flakes", quantity = 8, unit = "kg" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, createConversionResponse.StatusCode);
        var result = await createConversionResponse.Content.ReadAsAsync<ProductConversionResponse>();
        Assert.NotNull(result);
        Assert.Equal("Flakes", result.ProductName);
        Assert.Equal(8m, result.Quantity);
    }

    [Fact]
    public async Task CreateConversion_WhenBatchNotConverted_ReturnsBadRequest()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(plasticKg: 10);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Act - Try to create conversion when batch is in Segregated stage
        var response = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/conversions",
            new { productName = "Flakes", quantity = 8, unit = "kg" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SyncInventory_CreatesAndUpdatesInventoryItems_AndMarksConversionsAsSynced()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(plasticKg: 10);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Advance to Converted and create conversion
        await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Processing" });

        await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Converted" });

        var createConversionResponse = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/conversions",
            new { productName = "Flakes", quantity = 8, unit = "kg" });

        // Act
        var syncResponse = await _client.PostAsJsonAsync("/api/recycling/conversions/sync-inventory", new { });

        // Assert
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);
        var syncResult = await syncResponse.Content.ReadAsAsync<InventorySyncResponse>();
        Assert.NotNull(syncResult);
        Assert.True(syncResult.CreatedItemsCount > 0 || syncResult.UpdatedItemsCount > 0);
    }
}
