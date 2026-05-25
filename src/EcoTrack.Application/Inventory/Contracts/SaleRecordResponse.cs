namespace EcoTrack.Application.Inventory.Contracts;

public sealed record SaleRecordResponse(
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
