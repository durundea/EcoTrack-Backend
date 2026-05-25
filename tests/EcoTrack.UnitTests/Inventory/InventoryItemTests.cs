using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using FluentAssertions;

namespace EcoTrack.UnitTests.Inventory;

public class InventoryItemTests
{
    [Fact]
    public void UpdateStandardPrice_WhenRoleIsCollector_ThrowsInvalidOperationException()
    {
        var item = InventoryItem.Create("Compost", InventoryCategory.RecycledProduct, 45, "kg", 60m, DateTime.UtcNow);
        var action = () => item.UpdateStandardPrice(80m, UserRole.Collector, DateTime.UtcNow);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateStandardPrice_WhenRoleIsAdmin_UpdatesPrice()
    {
        var item = InventoryItem.Create("Compost", InventoryCategory.RecycledProduct, 45, "kg", 60m, DateTime.UtcNow);
        item.UpdateStandardPrice(80m, UserRole.Admin, DateTime.UtcNow);
        item.StandardPriceInr.Should().Be(80m);
    }

    [Fact]
    public void Constructor_WithEmptyName_ThrowsArgumentException()
    {
        var action = () => InventoryItem.Create("", InventoryCategory.RecycledProduct, 45, "kg", 60m, DateTime.UtcNow);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithNegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        var action = () => InventoryItem.Create("Compost", InventoryCategory.RecycledProduct, -1, "kg", 60m, DateTime.UtcNow);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Update_WithValidInput_UpdatesAllFields()
    {
        var fixedTime = new DateTime(2026, 5, 25, 10, 0, 0, DateTimeKind.Utc);
        var item = InventoryItem.Create("Compost", InventoryCategory.RecycledProduct, 45, "kg", 60m, fixedTime);
        var updatedTime = fixedTime.AddHours(1);

        item.Update("Eco-bricks", InventoryCategory.RawWaste, 30, "units", updatedTime);

        item.Name.Should().Be("Eco-bricks");
        item.Category.Should().Be(InventoryCategory.RawWaste);
        item.QuantityKg.Should().Be(30);
        item.Unit.Should().Be("units");
        item.UpdatedAtUtc.Should().Be(updatedTime);
    }

    [Fact]
    public void Update_WithEmptyName_ThrowsArgumentException()
    {
        var item = InventoryItem.Create("Compost", InventoryCategory.RecycledProduct, 45, "kg", 60m, DateTime.UtcNow);
        var action = () => item.Update("", InventoryCategory.RecycledProduct, 45, "kg", DateTime.UtcNow);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_WithNegativeQuantity_ThrowsArgumentOutOfRangeException()
    {
        var item = InventoryItem.Create("Compost", InventoryCategory.RecycledProduct, 45, "kg", 60m, DateTime.UtcNow);
        var action = () => item.Update("Compost", InventoryCategory.RecycledProduct, -1, "kg", DateTime.UtcNow);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
