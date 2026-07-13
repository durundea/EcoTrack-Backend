# Recycling API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the backend recycling API with batch management, stage tracking, product conversions, and idempotent inventory sync.

**Architecture:** Domain entities for recycling workflow → Application services for business logic → API controllers with error handling. Segregation record auto-creates recycling batches. Manual sync endpoint pushes conversions to inventory with deduplication by conversion ID.

**Tech Stack:** Entity Framework Core, PostgreSQL, ASP.NET Core minimal API patterns, xUnit for tests.

---

## File Structure

### Domain Layer (New)
- `src/EcoTrack.Domain/Inventory/RecyclingBatch.cs` - Entity for recycling workflow
- `src/EcoTrack.Domain/Inventory/ProductConversion.cs` - Entity for finished products
- `src/EcoTrack.Domain/Inventory/RecyclingBatchStage.cs` - Enum for stage transitions

### Application Layer (New & Extended)
- `src/EcoTrack.Application/Recycling/RecyclingService.cs` - Service for batch management
- `src/EcoTrack.Application/Recycling/InventorySyncService.cs` - Service for inventory sync
- `src/EcoTrack.Application/Recycling/Contracts/GetRecyclingBatchesQueryRequest.cs`
- `src/EcoTrack.Application/Recycling/Contracts/RecyclingBatchListItemResponse.cs`
- `src/EcoTrack.Application/Recycling/Contracts/RecyclingBatchDetailResponse.cs`
- `src/EcoTrack.Application/Recycling/Contracts/AdvanceStageRequest.cs`
- `src/EcoTrack.Application/Recycling/Contracts/CreateConversionRequest.cs`
- `src/EcoTrack.Application/Recycling/Contracts/ProductConversionResponse.cs`
- `src/EcoTrack.Application/Recycling/Contracts/InventorySyncResponse.cs`
- Modified: `src/EcoTrack.Application/Segregation/Contracts/SegmentationBatchDetailResponse.cs` - Add recycling batch IDs

### API Layer (New & Extended)
- `src/EcoTrack.Api/Controllers/RecyclingController.cs` - New controller
- Modified: `src/EcoTrack.Api/Controllers/SegregationController.cs` - Already calls updated RecordAsync

### Database
- `src/EcoTrack.Infrastructure/Persistence/Migrations/[timestamp]_AddRecyclingTables.cs` - Migration

### Tests (New)
- `tests/EcoTrack.IntegrationTests/Recycling/RecyclingEndpointsTests.cs` - Integration tests
- `tests/EcoTrack.UnitTests/Recycling/RecyclingServiceTests.cs` - Service unit tests
- `tests/EcoTrack.UnitTests/Recycling/InventorySyncServiceTests.cs` - Sync service unit tests

---

## Task 1: Create RecyclingBatchStage Enum

**Files:**
- Create: `src/EcoTrack.Domain/Inventory/RecyclingBatchStage.cs`

- [ ] **Step 1: Create the enum file**

```csharp
namespace EcoTrack.Domain.Inventory;

public enum RecyclingBatchStage
{
    Collected = 0,
    Segregated = 1,
    Processing = 2,
    Converted = 3
}
```

- [ ] **Step 2: Commit**

```bash
cd c:\Users\Ashok\Projects\EcoTrack-Backend
git add src/EcoTrack.Domain/Inventory/RecyclingBatchStage.cs
git commit -m "feat: add RecyclingBatchStage enum for workflow tracking"
```

---

## Task 2: Create RecyclingBatch Domain Entity

**Files:**
- Create: `src/EcoTrack.Domain/Inventory/RecyclingBatch.cs`

- [ ] **Step 1: Create the entity**

```csharp
using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class RecyclingBatch : Entity
{
    private List<RecyclingBatchStageHistoryEntry> _stageHistory = new();

    private RecyclingBatch() { }

    public Guid SegregationBatchId { get; private set; }
    public Guid PickupTaskId { get; private set; }
    public string SourceCategory { get; private set; } = null!; // plastic, organic, metal, paper, ewaste
    public decimal SourceWeightKg { get; private set; }
    public RecyclingBatchStage Stage { get; private set; }
    public string OutputProduct { get; private set; } = string.Empty;
    public decimal OutputQuantity { get; private set; }
    public bool InventoryUpdated { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public IReadOnlyList<RecyclingBatchStageHistoryEntry> StageHistory => _stageHistory.AsReadOnly();
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static RecyclingBatch CreateFromSegregation(
        Guid segregationBatchId,
        Guid pickupTaskId,
        string sourceCategory,
        decimal sourceWeightKg,
        Guid createdByUserId,
        DateTime createdAtUtc)
    {
        if (segregationBatchId == Guid.Empty) throw new ArgumentException("SegregationBatchId is required.", nameof(segregationBatchId));
        if (pickupTaskId == Guid.Empty) throw new ArgumentException("PickupTaskId is required.", nameof(pickupTaskId));
        if (string.IsNullOrWhiteSpace(sourceCategory)) throw new ArgumentException("SourceCategory is required.", nameof(sourceCategory));
        if (sourceWeightKg <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWeightKg), "SourceWeightKg must be greater than 0.");
        if (createdByUserId == Guid.Empty) throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));

        var batch = new RecyclingBatch
        {
            Id = Guid.NewGuid(),
            SegregationBatchId = segregationBatchId,
            PickupTaskId = pickupTaskId,
            SourceCategory = sourceCategory,
            SourceWeightKg = sourceWeightKg,
            Stage = RecyclingBatchStage.Segregated,
            OutputProduct = string.Empty,
            OutputQuantity = 0,
            InventoryUpdated = false,
            CreatedByUserId = createdByUserId,
            UpdatedByUserId = null,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        batch._stageHistory.Add(new RecyclingBatchStageHistoryEntry(
            batch.Stage,
            createdAtUtc,
            createdByUserId));

        return batch;
    }

    public void AdvanceStage(RecyclingBatchStage newStage, Guid actorUserId, DateTime transitionAtUtc)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        // Only allow forward transitions: Segregated -> Processing -> Converted
        var validNextStages = Stage switch
        {
            RecyclingBatchStage.Segregated => new[] { RecyclingBatchStage.Processing },
            RecyclingBatchStage.Processing => new[] { RecyclingBatchStage.Converted },
            _ => Array.Empty<RecyclingBatchStage>()
        };

        if (!validNextStages.Contains(newStage))
        {
            throw new InvalidOperationException(
                $"Cannot transition from {Stage} to {newStage}. Valid transitions from {Stage}: {string.Join(", ", validNextStages)}");
        }

        Stage = newStage;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = transitionAtUtc;

        _stageHistory.Add(new RecyclingBatchStageHistoryEntry(
            newStage,
            transitionAtUtc,
            actorUserId));
    }

    public void MarkInventoryUpdated()
    {
        InventoryUpdated = true;
    }
}

public class RecyclingBatchStageHistoryEntry
{
    public RecyclingBatchStageHistoryEntry() { }

    public RecyclingBatchStageHistoryEntry(RecyclingBatchStage stage, DateTime atUtc, Guid byUserId)
    {
        Stage = stage;
        AtUtc = atUtc;
        ByUserId = byUserId;
    }

    public RecyclingBatchStage Stage { get; set; }
    public DateTime AtUtc { get; set; }
    public Guid ByUserId { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/EcoTrack.Domain/Inventory/RecyclingBatch.cs
git commit -m "feat: add RecyclingBatch domain entity with stage tracking"
```

---

## Task 3: Create ProductConversion Domain Entity

**Files:**
- Create: `src/EcoTrack.Domain/Inventory/ProductConversion.cs`

- [ ] **Step 1: Create the entity**

```csharp
using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class ProductConversion : Entity
{
    private ProductConversion() { }

    public Guid RecyclingBatchId { get; private set; }
    public string ProductName { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = null!; // kg or units
    public DateTime? SyncedAtUtc { get; private set; }
    public string? SyncRunId { get; private set; }
    public Guid? SyncedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static ProductConversion Create(
        Guid recyclingBatchId,
        string productName,
        decimal quantity,
        string unit,
        DateTime createdAt)
    {
        if (recyclingBatchId == Guid.Empty) throw new ArgumentException("RecyclingBatchId is required.", nameof(recyclingBatchId));
        if (string.IsNullOrWhiteSpace(productName)) throw new ArgumentException("ProductName is required.", nameof(productName));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than 0.");
        if (string.IsNullOrWhiteSpace(unit)) throw new ArgumentException("Unit is required.", nameof(unit));

        return new ProductConversion
        {
            Id = Guid.NewGuid(),
            RecyclingBatchId = recyclingBatchId,
            ProductName = productName.Trim(),
            Quantity = quantity,
            Unit = unit,
            SyncedAtUtc = null,
            SyncRunId = null,
            SyncedByUserId = null,
            CreatedAt = createdAt
        };
    }

    public void MarkSynced(string syncRunId, Guid syncedByUserId, DateTime syncedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(syncRunId)) throw new ArgumentException("SyncRunId is required.", nameof(syncRunId));
        if (syncedByUserId == Guid.Empty) throw new ArgumentException("SyncedByUserId is required.", nameof(syncedByUserId));

        SyncRunId = syncRunId;
        SyncedByUserId = syncedByUserId;
        SyncedAtUtc = syncedAtUtc;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/EcoTrack.Domain/Inventory/ProductConversion.cs
git commit -m "feat: add ProductConversion domain entity for tracking finished products"
```

---

## Task 4: Update DbContext to Include New Entities

**Files:**
- Modify: `src/EcoTrack.Infrastructure/Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Read the current DbContext**

Location: `src/EcoTrack.Infrastructure/Persistence/ApplicationDbContext.cs`

Find where DbSets are defined (search for `DbSet<SegregationBatch>`).

- [ ] **Step 2: Add DbSets for new entities**

After the existing `DbSet<SegmentationBatch>` property, add:

```csharp
public DbSet<RecyclingBatch> RecyclingBatches { get; set; }
public DbSet<ProductConversion> ProductConversions { get; set; }
```

Also add to imports at top:

```csharp
using EcoTrack.Domain.Inventory;
```

- [ ] **Step 3: Configure entity mappings in OnModelCreating**

Find the `OnModelCreating` method. After existing configurations, add:

```csharp
// RecyclingBatch configuration
modelBuilder.Entity<RecyclingBatch>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.SourceCategory).HasMaxLength(20).IsRequired();
    entity.Property(e => e.OutputProduct).HasMaxLength(255);
    entity.Property(e => e.SourceWeightKg).HasPrecision(18, 2);
    entity.Property(e => e.OutputQuantity).HasPrecision(18, 2);
    entity.HasIndex(e => e.SegregationBatchId);
    entity.HasIndex(e => e.PickupTaskId);
    entity.HasIndex(e => e.Stage);
    entity.OwnsMany(e => e.StageHistory, a =>
    {
        a.Property(p => p.Stage);
        a.Property(p => p.AtUtc);
        a.Property(p => p.ByUserId);
    });
});

// ProductConversion configuration
modelBuilder.Entity<ProductConversion>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.ProductName).HasMaxLength(255).IsRequired();
    entity.Property(e => e.Unit).HasMaxLength(20).IsRequired();
    entity.Property(e => e.Quantity).HasPrecision(18, 2);
    entity.Property(e => e.SyncRunId).HasMaxLength(100);
    entity.HasIndex(e => e.RecyclingBatchId);
    entity.HasIndex(e => e.SyncedAtUtc);
});
```

- [ ] **Step 4: Commit**

```bash
git add src/EcoTrack.Infrastructure/Persistence/ApplicationDbContext.cs
git commit -m "feat: add DbSets and configurations for RecyclingBatch and ProductConversion"
```

---

## Task 5: Create Migration for New Tables

**Files:**
- Create: `src/EcoTrack.Infrastructure/Persistence/Migrations/[timestamp]_AddRecyclingTables.cs`

- [ ] **Step 1: Generate migration**

```bash
cd c:\Users\Ashok\Projects\EcoTrack-Backend
dotnet ef migrations add AddRecyclingTables --project src/EcoTrack.Infrastructure --startup-project src/EcoTrack.Api
```

Expected output: Migration file created (name will include timestamp).

- [ ] **Step 2: Verify migration file was created**

Check `src/EcoTrack.Infrastructure/Persistence/Migrations/` for the new file.

- [ ] **Step 3: Commit**

```bash
git add "src/EcoTrack.Infrastructure/Persistence/Migrations/*AddRecyclingTables*"
git commit -m "feat: add database migration for recycling tables"
```

---

## Task 6: Create Recycling Service Contracts

**Files:**
- Create: `src/EcoTrack.Application/Recycling/Contracts/GetRecyclingBatchesQueryRequest.cs`
- Create: `src/EcoTrack.Application/Recycling/Contracts/RecyclingBatchListItemResponse.cs`
- Create: `src/EcoTrack.Application/Recycling/Contracts/RecyclingBatchDetailResponse.cs`
- Create: `src/EcoTrack.Application/Recycling/Contracts/AdvanceStageRequest.cs`
- Create: `src/EcoTrack.Application/Recycling/Contracts/CreateConversionRequest.cs`
- Create: `src/EcoTrack.Application/Recycling/Contracts/ProductConversionResponse.cs`
- Create: `src/EcoTrack.Application/Recycling/Contracts/InventorySyncResponse.cs`

- [ ] **Step 1: Create directory**

```bash
mkdir "src\EcoTrack.Application\Recycling\Contracts"
```

- [ ] **Step 2: Create GetRecyclingBatchesQueryRequest.cs**

```csharp
namespace EcoTrack.Application.Recycling.Contracts;

public record GetRecyclingBatchesQueryRequest(
    int Page = 1,
    int PageSize = 20);
```

- [ ] **Step 3: Create RecyclingBatchStageHistoryEntryResponse.cs**

```csharp
namespace EcoTrack.Application.Recycling.Contracts;

public record RecyclingBatchStageHistoryEntryResponse(
    string Stage,
    DateTime AtUtc,
    Guid ByUserId);
```

- [ ] **Step 4: Create RecyclingBatchListItemResponse.cs**

```csharp
namespace EcoTrack.Application.Recycling.Contracts;

public record RecyclingBatchListItemResponse(
    Guid Id,
    Guid SegregationBatchId,
    Guid PickupTaskId,
    string SourceCategory,
    decimal SourceWeightKg,
    string Stage,
    string OutputProduct,
    decimal OutputQuantity,
    bool InventoryUpdated);
```

- [ ] **Step 5: Create RecyclingBatchDetailResponse.cs**

```csharp
namespace EcoTrack.Application.Recycling.Contracts;

public record RecyclingBatchDetailResponse(
    Guid Id,
    Guid SegregationBatchId,
    Guid PickupTaskId,
    string SourceCategory,
    decimal SourceWeightKg,
    string Stage,
    string OutputProduct,
    decimal OutputQuantity,
    bool InventoryUpdated,
    Guid CreatedByUserId,
    Guid? UpdatedByUserId,
    List<RecyclingBatchStageHistoryEntryResponse> StageHistory,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
```

- [ ] **Step 6: Create AdvanceStageRequest.cs**

```csharp
namespace EcoTrack.Application.Recycling.Contracts;

public record AdvanceStageRequest(
    string Stage); // "processing" or "converted"
```

- [ ] **Step 7: Create CreateConversionRequest.cs**

```csharp
namespace EcoTrack.Application.Recycling.Contracts;

public record CreateConversionRequest(
    string ProductName,
    decimal Quantity,
    string Unit); // "kg" or "units"
```

- [ ] **Step 8: Create ProductConversionResponse.cs**

```csharp
namespace EcoTrack.Application.Recycling.Contracts;

public record ProductConversionResponse(
    Guid Id,
    Guid RecyclingBatchId,
    string ProductName,
    decimal Quantity,
    string Unit,
    DateTime? SyncedAtUtc,
    string? SyncRunId,
    Guid? SyncedByUserId,
    DateTime CreatedAt);
```

- [ ] **Step 9: Create InventorySyncResponse.cs**

```csharp
namespace EcoTrack.Application.Recycling.Contracts;

public record InventorySyncResponse(
    int UpdatedItemsCount,
    int CreatedItemsCount,
    int SkippedCount,
    string SyncRunId);
```

- [ ] **Step 10: Commit**

```bash
git add "src/EcoTrack.Application/Recycling/Contracts/"
git commit -m "feat: add recycling service contracts and DTOs"
```

---

## Task 7: Extend SegregationBatchDetailResponse with Recycling Batch IDs

**Files:**
- Modify: `src/EcoTrack.Application/Segregation/Contracts/SegregationBatchDetailResponse.cs`

- [ ] **Step 1: Read the current file**

Location: `src/EcoTrack.Application/Segregation/Contracts/SegregationBatchDetailResponse.cs`

- [ ] **Step 2: Add recycling batch fields**

Find the record definition and add these two properties before the closing parenthesis:

```csharp
List<Guid> CreatedRecyclingBatchIds,
int CreatedRecyclingCount
```

The updated record should look like:

```csharp
public record SegregationBatchDetailResponse(
    Guid Id,
    Guid PickupTaskId,
    string BatchCode,
    string PickupCode,
    string Status,
    decimal? PlasticKg,
    decimal? OrganicKg,
    decimal? MetalKg,
    decimal? PaperKg,
    decimal? EWasteKg,
    Guid? RecordedByUserId,
    DateTime? RecordedAtUtc,
    Guid? RecycledByUserId,
    DateTime? RecycledAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    List<Guid> CreatedRecyclingBatchIds,
    int CreatedRecyclingCount);
```

- [ ] **Step 3: Commit**

```bash
git add src/EcoTrack.Application/Segregation/Contracts/SegregationBatchDetailResponse.cs
git commit -m "feat: extend SegregationBatchDetailResponse with recycling batch IDs"
```

---

## Task 8: Create RecyclingService

**Files:**
- Create: `src/EcoTrack.Application/Recycling/RecyclingService.cs`

- [ ] **Step 1: Create the service directory**

```bash
mkdir "src\EcoTrack.Application\Recycling"
```

- [ ] **Step 2: Create RecyclingService.cs**

```csharp
using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Application.Recycling.Contracts;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Recycling;

public class RecyclingService
{
    private readonly IApplicationDbContext _dbContext;

    public RecyclingService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<RecyclingBatchListItemResponse>> GetBatchesAsync(
        GetRecyclingBatchesQueryRequest request,
        CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? throw new BadRequestException("Page must be greater than or equal to 1.") : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? throw new BadRequestException("PageSize must be between 1 and 100.") : request.PageSize;

        var query = _dbContext.RecyclingBatches.AsNoTracking();

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new RecyclingBatchListItemResponse(
                x.Id,
                x.SegregationBatchId,
                x.PickupTaskId,
                x.SourceCategory,
                x.SourceWeightKg,
                x.Stage.ToString(),
                x.OutputProduct,
                x.OutputQuantity,
                x.InventoryUpdated))
            .ToListAsync(cancellationToken);

        return new PagedResponse<RecyclingBatchListItemResponse>(
            items,
            page,
            pageSize,
            totalCount);
    }

    public async Task<RecyclingBatchDetailResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var batch = await _dbContext.RecyclingBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException($"RecyclingBatch with ID '{id}' not found.");

        return MapToDetailResponse(batch);
    }

    public async Task<RecyclingBatchDetailResponse> AdvanceStageAsync(
        Guid batchId,
        AdvanceStageRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RecyclingBatchStage>(request.Stage, true, out var newStage))
        {
            throw new BadRequestException($"Invalid stage '{request.Stage}'. Valid stages: Processing, Converted.");
        }

        var batch = await _dbContext.RecyclingBatches
            .FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new NotFoundException($"RecyclingBatch with ID '{batchId}' not found.");

        try
        {
            batch.AdvanceStage(newStage, actorUserId, DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException($"RECYCLING_INVALID_STAGE_TRANSITION: {ex.Message}");
        }

        _dbContext.RecyclingBatches.Update(batch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDetailResponse(batch);
    }

    public async Task<ProductConversionResponse> CreateConversionAsync(
        Guid batchId,
        CreateConversionRequest request,
        CancellationToken cancellationToken)
    {
        var batch = await _dbContext.RecyclingBatches
            .FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken)
            ?? throw new NotFoundException($"RecyclingBatch with ID '{batchId}' not found.");

        if (batch.Stage != RecyclingBatchStage.Converted)
        {
            throw new BadRequestException(
                $"CONVERSION_REQUIRES_CONVERTED_STAGE: Can only create conversion when stage is 'Converted', but current stage is '{batch.Stage}'.");
        }

        try
        {
            var conversion = ProductConversion.Create(
                batchId,
                request.ProductName,
                request.Quantity,
                request.Unit,
                DateTime.UtcNow);

            _dbContext.ProductConversions.Add(conversion);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return MapToResponse(conversion);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException($"CONVERSION_INVALID_INPUT: {ex.Message}");
        }
    }

    private RecyclingBatchDetailResponse MapToDetailResponse(RecyclingBatch batch)
    {
        return new RecyclingBatchDetailResponse(
            batch.Id,
            batch.SegregationBatchId,
            batch.PickupTaskId,
            batch.SourceCategory,
            batch.SourceWeightKg,
            batch.Stage.ToString(),
            batch.OutputProduct,
            batch.OutputQuantity,
            batch.InventoryUpdated,
            batch.CreatedByUserId,
            batch.UpdatedByUserId,
            batch.StageHistory
                .Select(h => new RecyclingBatchStageHistoryEntryResponse(
                    h.Stage.ToString(),
                    h.AtUtc,
                    h.ByUserId))
                .ToList(),
            batch.CreatedAtUtc,
            batch.UpdatedAtUtc);
    }

    private ProductConversionResponse MapToResponse(ProductConversion conversion)
    {
        return new ProductConversionResponse(
            conversion.Id,
            conversion.RecyclingBatchId,
            conversion.ProductName,
            conversion.Quantity,
            conversion.Unit,
            conversion.SyncedAtUtc,
            conversion.SyncRunId,
            conversion.SyncedByUserId,
            conversion.CreatedAt);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add src/EcoTrack.Application/Recycling/RecyclingService.cs
git commit -m "feat: add RecyclingService with batch management and conversion creation"
```

---

## Task 9: Create InventorySyncService

**Files:**
- Create: `src/EcoTrack.Application/Recycling/InventorySyncService.cs`

- [ ] **Step 1: Create InventorySyncService.cs**

```csharp
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
```

- [ ] **Step 2: Commit**

```bash
git add src/EcoTrack.Application/Recycling/InventorySyncService.cs
git commit -m "feat: add InventorySyncService for idempotent inventory sync"
```

---

## Task 10: Extend SegregationService to Auto-Create Recycling Batches

**Files:**
- Modify: `src/EcoTrack.Application/Segregation/SegregationService.cs`

- [ ] **Step 1: Read the current RecordAsync method in SegregationService**

Location: `src/EcoTrack.Application/Segregation/SegregationService.cs`

Find the `RecordAsync` method that handles segregation recording.

- [ ] **Step 2: Update the method signature and implementation**

Replace the entire `RecordAsync` method with:

```csharp
public async Task<SegregationBatchDetailResponse> RecordAsync(
    Guid batchId,
    RecordSegregationDataRequest request,
    Guid actorUserId,
    CancellationToken cancellationToken)
{
    var batch = await _dbContext.SegregationBatches
        .FirstOrDefaultAsync(x => x.Id == batchId, cancellationToken)
        ?? throw new NotFoundException($"SegregationBatch with ID '{batchId}' not found.");

    batch.Record(
        request.PlasticKg,
        request.OrganicKg,
        request.MetalKg,
        request.PaperKg,
        request.EWasteKg,
        actorUserId,
        DateTime.UtcNow);

    _dbContext.SegregationBatches.Update(batch);

    // Auto-create recycling batches for each non-zero category
    var createdBatchIds = new List<Guid>();
    var categories = new[] 
    {
        ("plastic", request.PlasticKg),
        ("organic", request.OrganicKg),
        ("metal", request.MetalKg),
        ("paper", request.PaperKg),
        ("ewaste", request.EWasteKg)
    };

    foreach (var (category, weight) in categories)
    {
        if (weight > 0)
        {
            var recyclingBatch = RecyclingBatch.CreateFromSegregation(
                batch.Id,
                batch.PickupTaskId,
                category,
                weight,
                actorUserId,
                DateTime.UtcNow);

            _dbContext.RecyclingBatches.Add(recyclingBatch);
            createdBatchIds.Add(recyclingBatch.Id);
        }
    }

    await _dbContext.SaveChangesAsync(cancellationToken);

    // Map to response
    return await MapToDetailResponseAsync(batch, createdBatchIds, cancellationToken);
}
```

- [ ] **Step 3: Add the mapping method**

Add this new private method to the SegregationService class:

```csharp
private async Task<SegregationBatchDetailResponse> MapToDetailResponseAsync(
    SegregationBatch batch,
    List<Guid> createdRecyclingBatchIds,
    CancellationToken cancellationToken)
{
    var pickupTask = await _dbContext.PickupTasks
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Id == batch.PickupTaskId, cancellationToken);

    return new SegregationBatchDetailResponse(
        batch.Id,
        batch.PickupTaskId,
        batch.BatchCode,
        pickupTask?.PickupCode ?? string.Empty,
        batch.Status.ToString(),
        batch.PlasticKg,
        batch.OrganicKg,
        batch.MetalKg,
        batch.PaperKg,
        batch.EWasteKg,
        batch.RecordedByUserId,
        batch.RecordedAtUtc,
        batch.RecycledByUserId,
        batch.RecycledAtUtc,
        batch.CreatedAtUtc,
        batch.UpdatedAtUtc,
        createdRecyclingBatchIds,
        createdRecyclingBatchIds.Count);
}
```

- [ ] **Step 4: Add using statement if not present**

Ensure this import exists at the top:

```csharp
using EcoTrack.Domain.Inventory;
```

- [ ] **Step 5: Commit**

```bash
git add src/EcoTrack.Application/Segregation/SegregationService.cs
git commit -m "feat: extend SegregationService to auto-create recycling batches on record"
```

---

## Task 11: Create RecyclingController

**Files:**
- Create: `src/EcoTrack.Api/Controllers/RecyclingController.cs`

- [ ] **Step 1: Create RecyclingController.cs**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Recycling;
using EcoTrack.Application.Recycling.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/recycling")]
[Authorize(Roles = "admin")]
public class RecyclingController : ControllerBase
{
    [HttpGet("batches")]
    public async Task<ActionResult<PagedResponse<RecyclingBatchListItemResponse>>> GetBatches(
        [FromServices] RecyclingService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await service.GetBatchesAsync(
            new GetRecyclingBatchesQueryRequest(page, pageSize),
            cancellationToken));
    }

    [HttpGet("batches/{id:guid}")]
    public async Task<ActionResult<RecyclingBatchDetailResponse>> GetBatchById(
        Guid id,
        [FromServices] RecyclingService service,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetByIdAsync(id, cancellationToken));
    }

    [HttpPost("batches/{id:guid}/advance-stage")]
    public async Task<ActionResult<RecyclingBatchDetailResponse>> AdvanceStage(
        Guid id,
        [FromBody] AdvanceStageRequest request,
        [FromServices] RecyclingService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.AdvanceStageAsync(id, request, actorUserId, cancellationToken));
    }

    [HttpPost("batches/{id:guid}/conversions")]
    public async Task<ActionResult<ProductConversionResponse>> CreateConversion(
        Guid id,
        [FromBody] CreateConversionRequest request,
        [FromServices] RecyclingService service,
        CancellationToken cancellationToken)
    {
        return Ok(await service.CreateConversionAsync(id, request, cancellationToken));
    }

    [HttpPost("conversions/sync-inventory")]
    public async Task<ActionResult<InventorySyncResponse>> SyncInventory(
        [FromServices] InventorySyncService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.SyncConversionsToInventoryAsync(actorUserId, cancellationToken));
    }
}
```

- [ ] **Step 2: Register services in Program.cs**

Location: `src/EcoTrack.Api/Program.cs`

Find where services are registered (look for `.AddScoped` or similar).

Add these lines with other service registrations:

```csharp
builder.Services.AddScoped<RecyclingService>();
builder.Services.AddScoped<InventorySyncService>();
```

Ensure the using statement is present:

```csharp
using EcoTrack.Application.Recycling;
```

- [ ] **Step 3: Commit**

```bash
git add src/EcoTrack.Api/Controllers/RecyclingController.cs
git add src/EcoTrack.Api/Program.cs
git commit -m "feat: add RecyclingController with all recycling endpoints"
```

---

## Task 12: Add InventoryItem.AddQuantity Method

**Files:**
- Modify: `src/EcoTrack.Domain/Inventory/InventoryItem.cs`

- [ ] **Step 1: Add AddQuantity method**

Find the InventoryItem class and add this method:

```csharp
public void AddQuantity(decimal amount)
{
    if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
    QuantityKg += amount;
    UpdatedAtUtc = DateTime.UtcNow;
}
```

- [ ] **Step 2: Commit**

```bash
git add src/EcoTrack.Domain/Inventory/InventoryItem.cs
git commit -m "feat: add AddQuantity method to InventoryItem for inventory sync"
```

---

## Task 13: Create Integration Tests for Recycling Endpoints

**Files:**
- Create: `tests/EcoTrack.IntegrationTests/Recycling/RecyclingEndpointsTests.cs`

- [ ] **Step 1: Create directory**

```bash
mkdir "tests\EcoTrack.IntegrationTests\Recycling"
```

- [ ] **Step 2: Create test file**

```csharp
using System.Net;
using System.Net.Http.Json;
using EcoTrack.Application.Recycling.Contracts;
using EcoTrack.Domain.Inventory;
using Xunit;

namespace EcoTrack.IntegrationTests.Recycling;

public class RecyclingEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly IntegrationTestWebAppFactory _factory;
    private readonly HttpClient _client;

    public RecyclingEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBatches_ReturnsOk_WithPaginatedBatches()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(
            plasticKg: 10,
            organicKg: 20);

        // Act
        var response = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        Assert.NotNull(result);
        Assert.True(result.Items.Count > 0);
        Assert.Contains(result.Items, x => x.SourceCategory == "plastic" && x.SourceWeightKg == 10);
        Assert.Contains(result.Items, x => x.SourceCategory == "organic" && x.SourceWeightKg == 20);
    }

    [Fact]
    public async Task GetBatchById_ReturnsBatch_WithStageHistory()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(
            plasticKg: 15);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Act
        var response = await _client.GetAsync($"/api/recycling/batches/{batch.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsAsync<RecyclingBatchDetailResponse>();
        Assert.NotNull(result);
        Assert.Equal("Segregated", result.Stage);
        Assert.NotEmpty(result.StageHistory);
        Assert.Contains(result.StageHistory, x => x.Stage == "Segregated");
    }

    [Fact]
    public async Task AdvanceStage_TransitionsToProcessing_AndUpdatesHistory()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(plasticKg: 10);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Processing" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsAsync<RecyclingBatchDetailResponse>();
        Assert.Equal("Processing", result.Stage);
        Assert.True(result.StageHistory.Count > 1);
    }

    [Fact]
    public async Task AdvanceStage_ToConverted_ThenCreateConversion_Succeeds()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(plasticKg: 10);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Advance to Processing
        await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Processing" });

        // Advance to Converted
        var convertResponse = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Converted" });
        Assert.Equal(HttpStatusCode.OK, convertResponse.StatusCode);

        // Act - Create conversion
        var createConversionResponse = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/conversions",
            new { productName = "Flakes", quantity = 8, unit = "kg" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, createConversionResponse.StatusCode);
        var result = await createConversionResponse.Content.ReadAsAsync<ProductConversionResponse>();
        Assert.NotNull(result);
        Assert.Equal("Flakes", result.ProductName);
        Assert.Equal(8m, result.Quantity);
    }

    [Fact]
    public async Task CreateConversion_WhenBatchNotConverted_ReturnsBadRequest()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(plasticKg: 10);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Act - Try to create conversion when batch is in Segregated stage
        var response = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/conversions",
            new { productName = "Flakes", quantity = 8, unit = "kg" });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SyncInventory_CreatesAndUpdatesInventoryItems_AndMarksConversionsAsSynced()
    {
        // Arrange
        var segregationBatch = await _factory.CreateSegregationBatchWithRecordingAsync(plasticKg: 10);

        var listResponse = await _client.GetAsync("/api/recycling/batches?page=1&pageSize=20");
        var batchesList = await listResponse.Content.ReadAsAsync<PagedResponse<RecyclingBatchListItemResponse>>();
        var batch = batchesList.Items.First(x => x.SourceCategory == "plastic");

        // Advance to Converted and create conversion
        await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Processing" });

        await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/advance-stage",
            new { stage = "Converted" });

        var createConversionResponse = await _client.PostAsJsonAsync(
            $"/api/recycling/batches/{batch.Id}/conversions",
            new { productName = "Flakes", quantity = 8, unit = "kg" });

        // Act
        var syncResponse = await _client.PostAsJsonAsync("/api/recycling/conversions/sync-inventory", new { });

        // Assert
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);
        var syncResult = await syncResponse.Content.ReadAsAsync<InventorySyncResponse>();
        Assert.NotNull(syncResult);
        Assert.True(syncResult.CreatedItemsCount > 0 || syncResult.UpdatedItemsCount > 0);
    }
}
```

- [ ] **Step 3: Create test helper in IntegrationTestWebAppFactory**

Location: `tests/EcoTrack.IntegrationTests/IntegrationTestWebAppFactory.cs`

Add this method to the factory class:

```csharp
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
    var pickupTask = PickupTask.Create(
        "pickup-001",
        "Location A",
        "team-1",
        DateTime.UtcNow);

    dbContext.PickupTasks.Add(pickupTask);

    // Create segregation batch
    var segregationBatch = SegregationBatch.CreatePending(
        pickupTask.Id,
        "SB-001",
        DateTime.UtcNow);

    dbContext.SegregationBatches.Add(segregationBatch);
    await dbContext.SaveChangesAsync();

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
    await dbContext.SaveChangesAsync();

    return segregationBatch;
}
```

- [ ] **Step 4: Commit**

```bash
git add tests/EcoTrack.IntegrationTests/Recycling/RecyclingEndpointsTests.cs
git add tests/EcoTrack.IntegrationTests/IntegrationTestWebAppFactory.cs
git commit -m "test: add comprehensive integration tests for recycling endpoints"
```

---

## Task 14: Create Unit Tests for RecyclingService

**Files:**
- Create: `tests/EcoTrack.UnitTests/Recycling/RecyclingServiceTests.cs`

- [ ] **Step 1: Create directory**

```bash
mkdir "tests\EcoTrack.UnitTests\Recycling"
```

- [ ] **Step 2: Create test file**

```csharp
using EcoTrack.Application.Recycling;
using EcoTrack.Application.Recycling.Contracts;
using EcoTrack.Domain.Inventory;
using Xunit;
using Moq;
using EcoTrack.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.UnitTests.Recycling;

public class RecyclingServiceTests
{
    [Fact]
    public async Task AdvanceStage_FromSegregatedToProcessing_Succeeds()
    {
        // Arrange
        var batchId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var batch = RecyclingBatch.CreateFromSegregation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "plastic",
            10,
            actorUserId,
            DateTime.UtcNow);

        var mockDbContext = CreateMockDbContext(new[] { batch });
        var service = new RecyclingService(mockDbContext);

        // Act
        var result = await service.AdvanceStageAsync(
            batch.Id,
            new AdvanceStageRequest("Processing"),
            actorUserId,
            CancellationToken.None);

        // Assert
        Assert.Equal("Processing", result.Stage);
        Assert.True(result.StageHistory.Count >= 2);
    }

    [Fact]
    public async Task AdvanceStage_InvalidTransition_ThrowsBadRequestException()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var batch = RecyclingBatch.CreateFromSegregation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "plastic",
            10,
            actorUserId,
            DateTime.UtcNow);

        var mockDbContext = CreateMockDbContext(new[] { batch });
        var service = new RecyclingService(mockDbContext);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () =>
            await service.AdvanceStageAsync(
                batch.Id,
                new AdvanceStageRequest("Collected"), // Invalid: can't go backwards
                actorUserId,
                CancellationToken.None));
    }

    private IApplicationDbContext CreateMockDbContext(IEnumerable<RecyclingBatch> batches)
    {
        var batchList = batches.ToList();
        var mockDbSet = new Mock<DbSet<RecyclingBatch>>();

        mockDbSet
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<RecyclingBatch, bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<System.Linq.Expressions.Expression<Func<RecyclingBatch, bool>>, CancellationToken>((predicate, ct) =>
            {
                var batch = batchList.FirstOrDefault(predicate.Compile());
                return Task.FromResult(batch);
            });

        mockDbSet
            .Setup(x => x.AsNoTracking())
            .Returns(mockDbSet.Object);

        var mockContext = new Mock<IApplicationDbContext>();
        mockContext.Setup(x => x.RecyclingBatches).Returns(mockDbSet.Object);
        mockContext.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return mockContext.Object;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add tests/EcoTrack.UnitTests/Recycling/RecyclingServiceTests.cs
git commit -m "test: add unit tests for RecyclingService stage transitions"
```

---

## Task 15: Run All Tests and Verify

**Files:** None to modify

- [ ] **Step 1: Restore dependencies**

```bash
cd c:\Users\Ashok\Projects\EcoTrack-Backend
dotnet restore
```

Expected: Successful restore with no errors.

- [ ] **Step 2: Build solution**

```bash
dotnet build
```

Expected: Build succeeds with no errors.

- [ ] **Step 3: Run unit tests**

```bash
dotnet test tests/EcoTrack.UnitTests/EcoTrack.UnitTests.csproj -v normal
```

Expected: All unit tests pass.

- [ ] **Step 4: Run integration tests**

```bash
dotnet test tests/EcoTrack.IntegrationTests/EcoTrack.IntegrationTests.csproj -v normal
```

Expected: All integration tests pass (ensure Docker Desktop is running for Testcontainers).

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "chore: all recycling feature tests passing"
```

---

## Summary

This plan implements the complete recycling API with:

✅ Domain entities (RecyclingBatch, ProductConversion)  
✅ Auto-creation of recycling batches on segregation record  
✅ Stage management with immutable history  
✅ Product conversion tracking  
✅ Idempotent inventory sync  
✅ Full API with 4 new endpoints  
✅ Comprehensive integration and unit tests  
✅ Database migrations  

All endpoints follow existing patterns and error handling conventions.

---

**Plan complete and saved to `docs/superpowers/plans/2026-07-09-recycling-api.md`.**

**Two execution options:**

**1. Subagent-Driven (recommended)** - Fresh subagent per task, fast iteration with review checkpoints

**2. Inline Execution** - Execute tasks in this session using executing-plans

**Which approach?**