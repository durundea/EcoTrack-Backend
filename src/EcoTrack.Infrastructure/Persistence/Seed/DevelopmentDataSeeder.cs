using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using EcoTrack.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Infrastructure.Persistence.Seed;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(AppDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Users.AnyAsync(cancellationToken)) return;

        var hasher = new PasswordHasher<string>();
        var now = DateTime.UtcNow;

        var admin = User.Create("Admin User", "admin@ecotrack.local",
            hasher.HashPassword("admin@ecotrack.local", "admin123"), UserRole.Admin, now);
        var collector = User.Create("Collector User", "collector@ecotrack.local",
            hasher.HashPassword("collector@ecotrack.local", "collector123"), UserRole.Collector, now);

        dbContext.Users.AddRange(admin, collector);

        var item1 = InventoryItem.Create("Compost (Organic)", InventoryCategory.RecycledProduct, 45m, "kg", 60m, now);
        var item2 = InventoryItem.Create("Eco-bricks (Plastic)", InventoryCategory.RecycledProduct, 60m, "units", 35m, now);
        var item3 = InventoryItem.Create("Raw Scrap Metal", InventoryCategory.RawWaste, 20m, "kg", 48m, now);

        dbContext.InventoryItems.AddRange(item1, item2, item3);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
