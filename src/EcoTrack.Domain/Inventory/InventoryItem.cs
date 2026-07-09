using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class InventoryItem : Entity
{
    private InventoryItem() { }

    public string Name { get; private set; } = string.Empty;
    public InventoryCategory Category { get; private set; }
    public decimal QuantityKg { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal StandardPriceInr { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static InventoryItem Create(string name, InventoryCategory category, decimal quantityKg, string unit, decimal standardPriceInr, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (quantityKg < 0) throw new ArgumentOutOfRangeException(nameof(quantityKg), "Quantity cannot be negative.");
        if (standardPriceInr < 0) throw new ArgumentOutOfRangeException(nameof(standardPriceInr), "Price cannot be negative.");
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.", nameof(unit));

        return new InventoryItem
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Category = category,
            QuantityKg = quantityKg,
            Unit = unit,
            StandardPriceInr = standardPriceInr,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    public void UpdateStandardPrice(decimal newPrice, UserRole actorRole, DateTime updatedAtUtc)
    {
        if (actorRole != UserRole.Admin) throw new InvalidOperationException("Only admins can update price.");
        if (newPrice < 0) throw new ArgumentOutOfRangeException(nameof(newPrice), "Price cannot be negative.");
        StandardPriceInr = newPrice;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Update(string name, InventoryCategory category, decimal quantityKg, string unit, DateTime updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.", nameof(unit));
        if (quantityKg < 0) throw new ArgumentOutOfRangeException(nameof(quantityKg), "Quantity cannot be negative.");
        Name = name.Trim();
        Category = category;
        QuantityKg = quantityKg;
        Unit = unit;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void AddQuantity(decimal amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        QuantityKg += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
