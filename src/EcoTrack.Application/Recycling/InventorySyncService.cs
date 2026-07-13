using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Recycling.Contracts;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Recycling;

public class InventorySyncService
{
    private readonly IApplicationDbContext _dbContext;

    public InventorySyncService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InventorySyncResponse> SyncConversionsToInventoryAsync(
        Guid syncedByUserId,
        CancellationToken cancellationToken)
    {
        var syncRunId = Guid.NewGuid().ToString();
        var syncTime = DateTime.UtcNow;

        // Get all unsynced conversions
        var unsyncedConversions = await _dbContext.ProductConversions
            .Where(x => x.SyncedAtUtc == null)
            .ToListAsync(cancellationToken);

        var updatedItemsCount = 0;
        var createdItemsCount = 0;
        var skippedCount = 0;

        foreach (var conversion in unsyncedConversions)
        {
            try
            {
                // Find or create inventory item by product name
                var inventoryItem = await _dbContext.InventoryItems
                    .FirstOrDefaultAsync(x => x.Name == conversion.ProductName, cancellationToken);

                if (inventoryItem == null)
                {
                    // Create new inventory item
                    inventoryItem = InventoryItem.Create(
                        conversion.ProductName,
                        InventoryCategory.RecycledProduct,
                        conversion.Quantity,
                        conversion.Unit,
                        0m, // Standard price TBD
                        syncTime);

                    _dbContext.InventoryItems.Add(inventoryItem);
                    createdItemsCount++;
                }
                else
                {
                    // Update quantity
                    inventoryItem.AddQuantity(conversion.Quantity);
                    _dbContext.InventoryItems.Update(inventoryItem);
                    updatedItemsCount++;
                }

                // Mark conversion as synced
                conversion.MarkSynced(syncRunId, syncedByUserId, syncTime);
                _dbContext.ProductConversions.Update(conversion);

                // Mark recycling batch as inventory updated
                var batch = await _dbContext.RecyclingBatches
                    .FirstOrDefaultAsync(x => x.Id == conversion.RecyclingBatchId, cancellationToken);

                if (batch != null && !batch.InventoryUpdated)
                {
                    batch.MarkInventoryUpdated();
                    _dbContext.RecyclingBatches.Update(batch);
                }
            }
            catch (Exception)
            {
                skippedCount++;
                // Continue with next conversion on error
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new InventorySyncResponse(
            updatedItemsCount,
            createdItemsCount,
            skippedCount,
            syncRunId);
    }
}
