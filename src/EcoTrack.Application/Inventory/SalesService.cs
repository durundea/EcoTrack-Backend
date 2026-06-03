using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Inventory;

public class SalesService
{
    private readonly IApplicationDbContext _dbContext;

    public SalesService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<SaleRecordResponse> CreateDraftAsync(
        CreateSaleRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var item = await _dbContext.InventoryItems
            .SingleOrDefaultAsync(x => x.Id == request.InventoryItemId, cancellationToken)
            ?? throw new NotFoundException("Inventory item not found.");

        var revenue = item.StandardPriceInr * request.QuantitySold;
        var sale = SaleRecord.CreateDraft(
            item.Id,
            actorUserId,
            request.QuantitySold,
            revenue,
            request.SoldAtUtc,
            DateTime.UtcNow);

        _dbContext.SaleRecords.Add(sale);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(sale);
    }

    public async Task<SaleRecordResponse> SubmitAsync(
        Guid id,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var sale = await _dbContext.SaleRecords
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Sale record not found.");

        sale.SubmitForApproval(actorUserId, Enum.Parse<UserRole>(actorRole, ignoreCase: true), DateTime.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(sale);
    }

    public async Task<SaleRecordResponse> ApproveAsync(
        Guid id,
        Guid approverUserId,
        CancellationToken cancellationToken)
    {
        var sale = await _dbContext.SaleRecords
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Sale record not found.");

        sale.Approve(approverUserId, UserRole.Admin, DateTime.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(sale);
    }

    public async Task<SaleRecordResponse> UpdateDraftAsync(
        Guid id,
        UpdateSaleRequest request,
        CancellationToken cancellationToken)
    {
        var sale = await _dbContext.SaleRecords
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Sale record not found.");

        try
        {
            sale.UpdateDraft(request.QuantitySold, request.SoldAtUtc, DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(sale);
    }

    public async Task<PagedResponse<SaleRecordResponse>> GetSalesAsync(
        GetSalesQueryRequest request,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var page = request.Page <= 0
            ? throw new BadRequestException("Page must be greater than or equal to 1.")
            : request.Page;

        var pageSize = request.PageSize is < 1 or > 100
            ? throw new BadRequestException("PageSize must be between 1 and 100.")
            : request.PageSize;

        if (request.FromSoldAtUtc.HasValue && request.ToSoldAtUtc.HasValue && request.FromSoldAtUtc > request.ToSoldAtUtc)
        {
            throw new BadRequestException("FromSoldAtUtc must be less than or equal to ToSoldAtUtc.");
        }

        var query = _dbContext.SaleRecords.AsNoTracking().AsQueryable();

        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.RequestedByUserId == actorUserId);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<SaleApprovalStatus>(request.Status, ignoreCase: true, out var status))
            {
                throw new BadRequestException("Invalid status value.");
            }

            query = query.Where(x => x.ApprovalStatus == status);
        }

        if (request.RequestedByUserId.HasValue)
        {
            query = query.Where(x => x.RequestedByUserId == request.RequestedByUserId.Value);
        }

        if (request.FromSoldAtUtc.HasValue)
        {
            query = query.Where(x => x.SoldAtUtc >= request.FromSoldAtUtc.Value);
        }

        if (request.ToSoldAtUtc.HasValue)
        {
            query = query.Where(x => x.SoldAtUtc <= request.ToSoldAtUtc.Value);
        }

        if (request.InventoryItemId.HasValue)
        {
            query = query.Where(x => x.InventoryItemId == request.InventoryItemId.Value);
        }

        var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "soldAtUtc" : request.SortBy;
        if (!string.Equals(sortBy, "soldAtUtc", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("SortBy must be soldAtUtc.");
        }

        var sortDirection = string.IsNullOrWhiteSpace(request.SortDirection) ? "desc" : request.SortDirection;
        query = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderBy(x => x.SoldAtUtc)
            : sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
                ? query.OrderByDescending(x => x.SoldAtUtc)
                : throw new BadRequestException("SortDirection must be asc or desc.");

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToResponse(x))
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedResponse<SaleRecordResponse>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<SaleRecordResponse> GetByIdAsync(
        Guid id,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.Id == id);

        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.RequestedByUserId == actorUserId);
        }

        var sale = await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Sale record not found.");

        return ToResponse(sale);
    }

    private static SaleRecordResponse ToResponse(SaleRecord sale) =>
        new(sale.Id,
            sale.InventoryItemId,
            sale.QuantitySold,
            sale.RevenueInr,
            sale.SoldAtUtc,
            sale.ApprovalStatus.ToString(),
            sale.RequestedByUserId,
            sale.ApprovedByUserId,
            sale.ApprovedAtUtc,
            sale.RejectionReason);
}
