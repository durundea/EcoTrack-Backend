namespace EcoTrack.Application.Inventory.Contracts;

public sealed record CreateInventoryItemRequest(
    string Name,
    string Category,
    decimal QuantityKg,
    string Unit,
    decimal StandardPriceInr);
