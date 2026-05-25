using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using FluentAssertions;

namespace EcoTrack.UnitTests.Inventory;

public class InventoryItemTests
{
    [Fact]
    public void UpdateStandardPrice_WhenRoleIsCollector_ThrowsInvalidOperationException()
    {
        var item = new InventoryItem(Guid.NewGuid(), "Compost", InventoryCategory.RecycledProduct, 45, "kg", 60m);
        var action = () => item.UpdateStandardPrice(80m, UserRole.Collector, DateTime.UtcNow);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void UpdateStandardPrice_WhenRoleIsAdmin_UpdatesPrice()
    {
        var item = new InventoryItem(Guid.NewGuid(), "Compost", InventoryCategory.RecycledProduct, 45, "kg", 60m);
        item.UpdateStandardPrice(80m, UserRole.Admin, DateTime.UtcNow);
        item.StandardPriceInr.Should().Be(80m);
    }

    [Fact]
    public void Constructor_WithEmptyName_ThrowsInvalidOperationException()
    {
        var action = () => new InventoryItem(Guid.NewGuid(), "", InventoryCategory.RecycledProduct, 45, "kg", 60m);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_WithNegativeQuantity_ThrowsInvalidOperationException()
    {
        var action = () => new InventoryItem(Guid.NewGuid(), "Compost", InventoryCategory.RecycledProduct, -1, "kg", 60m);
        action.Should().Throw<InvalidOperationException>();
    }
}
