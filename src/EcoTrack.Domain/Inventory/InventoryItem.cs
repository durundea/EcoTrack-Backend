using EcoTrack.Domain.Auth;

namespace EcoTrack.Domain.Inventory;

public class InventoryItem
{
    private InventoryItem() { }

    public InventoryItem(Guid id, string name, InventoryCategory category, decimal quantityKg, string unit, decimal standardPriceInr)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name is required.");
        if (quantityKg < 0) throw new InvalidOperationException("Quantity cannot be negative.");
        if (standardPriceInr < 0) throw new InvalidOperationException("Price cannot be negative.");

        Id = id;
        Name = name.Trim();
        Category = category;
        QuantityKg = quantityKg;
        Unit = unit;
        StandardPriceInr = standardPriceInr;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public InventoryCategory Category { get; private set; }
    public decimal QuantityKg { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal StandardPriceInr { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void UpdateStandardPrice(decimal newPrice, UserRole actorRole, DateTime updatedAtUtc)
    {
        if (actorRole != UserRole.Admin) throw new InvalidOperationException("Only admins can update price.");
        if (newPrice < 0) throw new InvalidOperationException("Price cannot be negative.");
        StandardPriceInr = newPrice;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Update(string name, InventoryCategory category, decimal quantityKg, string unit, DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Name is required.");
        if (quantityKg < 0) throw new InvalidOperationException("Quantity cannot be negative.");
        Name = name.Trim();
        Category = category;
        QuantityKg = quantityKg;
        Unit = unit;
        UpdatedAtUtc = updatedAtUtc;
    }
}
