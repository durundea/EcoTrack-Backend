using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Application.Segregation.Contracts;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Segregation;

public class SegregationService
{
    private readonly IApplicationDbContext _dbContext;

    public SegregationService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<SegregationBatchListItemResponse>> GetBatchesAsync(
        GetSegregationBatchesQueryRequest request,
        CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? throw new BadRequestException("Page must be greater than or equal to 1.") : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? throw new BadRequestException("PageSize must be between 1 and 100.") : request.PageSize;

        var query = _dbContext.SegregationBatches
            .AsNoTracking()
            .Join(_dbContext.PickupTasks.AsNoTracking(), b => b.PickupTaskId, p => p.Id, (b, p) => new { Batch = b, Pickup = p });

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<SegregationBatchStatus>(request.Status, true, out var status))
            {
                throw new BadRequestException("Invalid status value.");
            }

            query = query.Where(x => x.Batch.Status == status);
        }

        query = query.OrderBy(x => x.Batch.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SegregationBatchListItemResponse(
                x.Batch.Id,
                x.Batch.PickupTaskId,
                x.Batch.BatchCode,
                x.Pickup.PickupCode,
                x.Batch.Status.ToString(),
                x.Batch.RecordedAtUtc,
                x.Batch.RecycledAtUtc))
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PagedResponse<SegregationBatchListItemResponse>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<SegregationBatchDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await BuildDetailQuery(id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Segregation batch not found");

        return detail;
    }

    public async Task<SegregationBatchDetailResponse> RecordAsync(
        Guid id,
        RecordSegregationDataRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var batch = await _dbContext.SegregationBatches.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Segregation batch not found");

        try
        {
            batch.Record(
                request.PlasticKg,
                request.OrganicKg,
                request.MetalKg,
                request.PaperKg,
                request.EWasteKg,
                actorUserId,
                DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException(ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BadRequestException(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<SegregationBatchDetailResponse> MarkRecycledAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.SegregationBatches.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Segregation batch not found");

        try
        {
            batch.MarkRecycled(actorUserId, DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    private IQueryable<SegregationBatchDetailResponse> BuildDetailQuery(Guid id)
    {
        return _dbContext.SegregationBatches
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Join(_dbContext.PickupTasks.AsNoTracking(), b => b.PickupTaskId, p => p.Id, (b, p) =>
                new SegregationBatchDetailResponse(
                    b.Id,
                    b.BatchCode,
                    b.Status.ToString(),
                    b.PickupTaskId,
                    p.PickupCode,
                    p.SiteName,
                    p.SiteAddressText,
                    p.ScheduledAtUtc,
                    p.CollectedWeightKg ?? 0m,
                    b.PlasticKg,
                    b.OrganicKg,
                    b.MetalKg,
                    b.PaperKg,
                    b.EWasteKg,
                    b.RecordedByUserId,
                    b.RecordedAtUtc,
                    b.RecycledByUserId,
                    b.RecycledAtUtc,
                    b.CreatedAtUtc,
                    b.UpdatedAtUtc));
    }
}
