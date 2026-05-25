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
