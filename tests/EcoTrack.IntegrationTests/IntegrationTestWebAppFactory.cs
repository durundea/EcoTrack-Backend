using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Domain.Inventory;
using EcoTrack.Infrastructure.Persistence;
using EcoTrack.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace EcoTrack.IntegrationTests;

public class IntegrationTestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("ecotrack_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await DevelopmentDataSeeder.SeedAsync(db);
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    public async Task<SegregationBatch> CreateSegregationBatchWithRecordingAsync(
        decimal plasticKg = 0,
        decimal organicKg = 0,
        decimal metalKg = 0,
        decimal paperKg = 0,
        decimal eWasteKg = 0)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // Create pickup task
        var nowUtc = DateTime.UtcNow;
        var pickupTask = PickupTask.CreateScheduled(
            siteName: "Location A",
            siteAddressText: "Address A",
            scheduledAtUtc: nowUtc,
            estimatedWeightKg: 30m,
            notes: null,
            createdByUserId: Guid.NewGuid(),
            createdAtUtc: nowUtc,
            pickupCode: "pickup-001");

        dbContext.PickupTasks.Add(pickupTask);

        // Create segregation batch
        var segregationBatch = SegregationBatch.CreatePending(
            pickupTask.Id,
            "SB-001",
            DateTime.UtcNow);

        dbContext.SegregationBatches.Add(segregationBatch);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        // Record segregation data
        segregationBatch.Record(
            plasticKg,
            organicKg,
            metalKg,
            paperKg,
            eWasteKg,
            Guid.NewGuid(),
            DateTime.UtcNow);

        dbContext.SegregationBatches.Update(segregationBatch);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return segregationBatch;
    }
}