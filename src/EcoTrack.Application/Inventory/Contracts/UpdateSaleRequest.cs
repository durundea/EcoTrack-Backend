namespace EcoTrack.Application.Inventory.Contracts;

public sealed record UpdateSaleRequest(int QuantitySold, DateTime SoldAtUtc);
