using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Application.Recycling.Contracts;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Recycling;

public class RecyclingService
{
    private readonly IApplicationDbContext _dbContext;

    public RecyclingService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<RecyclingBatchListItemResponse>> GetBatchesAsync(
        GetRecyclingBatchesQueryRequest request,
        CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? throw new BadRequestException("Page must be greater than or equal to 1.") : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? throw new BadRequestException("PageSize must be between 1 and 100.") : request.PageSize;

        var query = _dbContext.RecyclingBatches.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (totalCount + pageSize - 1) / pageSize;
        
        var items = await query
            .OrderBy(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new RecyclingBatchListItemResponse(
                x.Id,
                x.SegregationBatchId,
                x.PickupTaskId,
                x.SourceCategory,
                x.SourceWeightKg,
                x.Stage.ToString(),
                x.OutputProduct,
                x.OutputQuantity,
                x.InventoryUpdated))
            .ToListAsync(cancellationToken);

        return new PagedResponse<RecyclingBatchListItemResponse>(
            items,
            page,
            pageSize,
            totalCount,
            totalPages);
    }

    public async Task<RecyclingBatchDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var batch = await _dbContext.RecyclingBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException($"RecyclingBatch with ID '{id}' not found.");

        return MapToDetailResponse(batch);
    }

    public async Task<RecyclingBatchDetailResponse> AdvanceStageAsync(
        Guid batchId,
        AdvanceStageRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RecyclingBatchStage>(request.Stage, true, out var newStage))
        {
            throw new BadRequestException($"Invalid stage '{request.Stage}'. Valid stages: Processing, Converted.");
        }

        var batch = await _dbContext.RecyclingBatches
            .FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new NotFoundException($"RecyclingBatch with ID '{batchId}' not found.");

        try
        {
            batch.AdvanceStage(newStage, actorUserId, DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException($"RECYCLING_INVALID_STAGE_TRANSITION: {ex.Message}");
        }

        _dbContext.RecyclingBatches.Update(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(batch);
    }

    public async Task<ProductConversionResponse> CreateConversionAsync(
        Guid batchId,
        CreateConversionRequest request,
        CancellationToken cancellationToken)
    {
        var batch = await _dbContext.RecyclingBatches
            .FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new NotFoundException($"RecyclingBatch with ID '{batchId}' not found.");

        if (batch.Stage != RecyclingBatchStage.Converted)
        {
            throw new BadRequestException(
                $"CONVERSION_REQUIRES_CONVERTED_STAGE: Can only create conversion when stage is 'Converted', but current stage is '{batch.Stage}'.");
        }

        try
        {
            var conversion = ProductConversion.Create(
                batchId,
                request.ProductName,
                request.Quantity,
                request.Unit,
                DateTime.UtcNow);

            _dbContext.ProductConversions.Add(conversion);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return MapToResponse(conversion);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException($"CONVERSION_INVALID_INPUT: {ex.Message}");
        }
    }

    private RecyclingBatchDetailResponse MapToDetailResponse(RecyclingBatch batch)
    {
        return new RecyclingBatchDetailResponse(
            batch.Id,
            batch.SegregationBatchId,
            batch.PickupTaskId,
            batch.SourceCategory,
            batch.SourceWeightKg,
            batch.Stage.ToString(),
            batch.OutputProduct,
            batch.OutputQuantity,
            batch.InventoryUpdated,
            batch.CreatedByUserId,
            batch.UpdatedByUserId,
            batch.StageHistory
                .Select(h => new RecyclingBatchStageHistoryEntryResponse(
                    h.Stage.ToString(),
                    h.AtUtc,
                    h.ByUserId))
                .ToList(),
            batch.CreatedAtUtc,
            batch.UpdatedAtUtc);
    }

    private ProductConversionResponse MapToResponse(ProductConversion conversion)
    {
        return new ProductConversionResponse(
            conversion.Id,
            conversion.RecyclingBatchId,
            conversion.ProductName,
            conversion.Quantity,
            conversion.Unit,
            conversion.SyncedAtUtc,
            conversion.SyncRunId,
            conversion.SyncedByUserId,
            conversion.CreatedAt);
    }
}
