namespace EcoTrack.Application.Inventory.Contracts;

public sealed record GetSalesQueryRequest(
    string? Status,
    Guid? RequestedByUserId,
    DateTime? FromSoldAtUtc,
    DateTime? ToSoldAtUtc,
    Guid? InventoryItemId,
    string? SortBy,
    string? SortDirection,
    int Page = 1,
    int PageSize = 20);
