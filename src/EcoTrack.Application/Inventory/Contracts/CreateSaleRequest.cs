namespace EcoTrack.Application.Inventory.Contracts;

public sealed record CreateSaleRequest(
    Guid InventoryItemId,
    int QuantitySold,
    DateTime SoldAtUtc);
