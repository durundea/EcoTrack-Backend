namespace EcoTrack.Application.Inventory.Contracts;

public sealed record InventoryItemResponse(
    Guid Id,
    string Name,
    string Category,
    decimal QuantityKg,
    string Unit,
    decimal StandardPriceInr);
