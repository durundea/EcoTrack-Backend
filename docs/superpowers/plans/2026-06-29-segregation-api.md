# Segregation API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build segregation batch APIs and workflow so admins can list/view batches, record category weights, and mark batches as recycled, with automatic batch creation when pickups are sent to segregation.

**Architecture:** Add a Segregation aggregate in the Domain layer and a SegregationService in the Application layer, matching existing Collection patterns. Persist segregation batches with EF Core configuration + migration, and expose authenticated admin-only endpoints via a dedicated controller. Integrate the auto-create behavior into the existing collection transition method so every SentToSegregation pickup has exactly one pending segregation batch.

**Tech Stack:** ASP.NET Core Web API (.NET 10), EF Core 10 + Npgsql, JWT role authorization, xUnit, FluentAssertions, Testcontainers PostgreSQL.

---

## Scope Check

The specification is a single coherent subsystem (segregation batch lifecycle plus collection integration), so one implementation plan is appropriate.

## File Structure

- Create: `src/EcoTrack.Domain/Inventory/SegregationBatchStatus.cs`
  - Status enum (`Pending`, `Recorded`, `Recycled`).
- Create: `src/EcoTrack.Domain/Inventory/SegregationBatch.cs`
  - Aggregate, transition methods, and validation rules.
- Create: `src/EcoTrack.Application/Segregation/SegregationService.cs`
  - Query/filter logic, workflow orchestration, and response mapping.
- Create: `src/EcoTrack.Application/Segregation/Contracts/GetSegregationBatchesQueryRequest.cs`
- Create: `src/EcoTrack.Application/Segregation/Contracts/RecordSegregationDataRequest.cs`
- Create: `src/EcoTrack.Application/Segregation/Contracts/SegregationBatchListItemResponse.cs`
- Create: `src/EcoTrack.Application/Segregation/Contracts/SegregationBatchDetailResponse.cs`
- Create: `src/EcoTrack.Api/Controllers/SegregationController.cs`
  - Segregation endpoints.
- Modify: `src/EcoTrack.Application/Common/Interfaces/IApplicationDbContext.cs`
  - Add `DbSet<SegregationBatch>`.
- Modify: `src/EcoTrack.Infrastructure/Persistence/AppDbContext.cs`
  - Add `DbSet<SegregationBatch>` implementation.
- Create: `src/EcoTrack.Infrastructure/Persistence/Configurations/SegregationBatchConfiguration.cs`
  - Table, precision, relationships, indexes.
- Modify: `src/EcoTrack.Infrastructure/DependencyInjection.cs`
  - Register `SegregationService`.
- Modify: `src/EcoTrack.Application/Collection/CollectionService.cs`
  - Auto-create pending segregation batch inside `SendToSegregationAsync`.
- Modify (generated): `src/EcoTrack.Infrastructure/Migrations/*AddSegregationBatches*.cs`
- Modify (generated): `src/EcoTrack.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- Create: `tests/EcoTrack.UnitTests/Segregation/SegregationBatchTests.cs`
  - Aggregate transitions + guard validation tests.
- Create: `tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs`
  - API behavior, authorization, validation, workflow, and collection integration tests.
- Modify: `README.md`
  - Add segregation endpoints.

### Task 1: Add Failing Unit Tests for Segregation Domain Rules

**Files:**
- Create: `tests/EcoTrack.UnitTests/Segregation/SegregationBatchTests.cs`
- Test: `tests/EcoTrack.UnitTests/Segregation/SegregationBatchTests.cs`

- [ ] **Step 1: Write the failing unit tests**

```csharp
using EcoTrack.Domain.Inventory;
using FluentAssertions;

namespace EcoTrack.UnitTests.Segregation;

public class SegregationBatchTests
{
    [Fact]
    public void Record_FromPending_WithValidWeights_MovesToRecorded()
    {
        var now = DateTime.UtcNow;
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", now);
        var actorId = Guid.NewGuid();

        batch.Record(50m, 30m, 20m, 15m, 5m, actorId, now.AddMinutes(10));

        batch.Status.Should().Be(SegregationBatchStatus.Recorded);
        batch.RecordedByUserId.Should().Be(actorId);
        batch.RecordedAtUtc.Should().NotBeNull();
        batch.PlasticKg.Should().Be(50m);
        batch.OrganicKg.Should().Be(30m);
        batch.MetalKg.Should().Be(20m);
        batch.PaperKg.Should().Be(15m);
        batch.EWasteKg.Should().Be(5m);
    }

    [Fact]
    public void Record_WithAllZeroWeights_ThrowsArgumentException()
    {
        var now = DateTime.UtcNow;
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", now);

        Action act = () => batch.Record(0m, 0m, 0m, 0m, 0m, Guid.NewGuid(), now.AddMinutes(5));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_WithNegativeWeight_ThrowsArgumentOutOfRangeException()
    {
        var now = DateTime.UtcNow;
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", now);

        Action act = () => batch.Record(-1m, 0m, 0m, 1m, 0m, Guid.NewGuid(), now.AddMinutes(5));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MarkRecycled_FromRecorded_MovesToRecycled()
    {
        var now = DateTime.UtcNow;
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", now);
        var recorderId = Guid.NewGuid();
        var recyclerId = Guid.NewGuid();

        batch.Record(10m, 0m, 0m, 0m, 0m, recorderId, now.AddMinutes(10));
        batch.MarkRecycled(recyclerId, now.AddMinutes(20));

        batch.Status.Should().Be(SegregationBatchStatus.Recycled);
        batch.RecycledByUserId.Should().Be(recyclerId);
        batch.RecycledAtUtc.Should().NotBeNull();
    }

    [Fact]
    public void MarkRecycled_FromPending_ThrowsInvalidOperationException()
    {
        var batch = SegregationBatch.CreatePending(Guid.NewGuid(), "SB-0001", DateTime.UtcNow);

        Action act = () => batch.MarkRecycled(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(5));

        act.Should().Throw<InvalidOperationException>();
    }
}
```

- [ ] **Step 2: Run unit tests to confirm failure**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~SegregationBatchTests"`
Expected: FAIL with compile errors for missing `SegregationBatch` and `SegregationBatchStatus`.

- [ ] **Step 3: Commit failing test baseline**

```bash
git add tests/EcoTrack.UnitTests/Segregation/SegregationBatchTests.cs
git commit -m "test: add failing segregation domain tests"
```

### Task 2: Implement Segregation Domain Aggregate (Minimal to Pass)

**Files:**
- Create: `src/EcoTrack.Domain/Inventory/SegregationBatchStatus.cs`
- Create: `src/EcoTrack.Domain/Inventory/SegregationBatch.cs`
- Test: `tests/EcoTrack.UnitTests/Segregation/SegregationBatchTests.cs`

- [ ] **Step 1: Add status enum**

```csharp
namespace EcoTrack.Domain.Inventory;

public enum SegregationBatchStatus
{
    Pending = 1,
    Recorded = 2,
    Recycled = 3,
}
```

- [ ] **Step 2: Add aggregate with transition guards**

```csharp
using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class SegregationBatch : Entity
{
    private SegregationBatch() { }

    public Guid PickupTaskId { get; private set; }
    public string BatchCode { get; private set; } = null!;
    public SegregationBatchStatus Status { get; private set; }
    public decimal? PlasticKg { get; private set; }
    public decimal? OrganicKg { get; private set; }
    public decimal? MetalKg { get; private set; }
    public decimal? PaperKg { get; private set; }
    public decimal? EWasteKg { get; private set; }
    public Guid? RecordedByUserId { get; private set; }
    public DateTime? RecordedAtUtc { get; private set; }
    public Guid? RecycledByUserId { get; private set; }
    public DateTime? RecycledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SegregationBatch CreatePending(Guid pickupTaskId, string batchCode, DateTime createdAtUtc)
    {
        if (pickupTaskId == Guid.Empty) throw new ArgumentException("PickupTaskId is required.", nameof(pickupTaskId));
        if (string.IsNullOrWhiteSpace(batchCode)) throw new ArgumentException("BatchCode is required.", nameof(batchCode));

        return new SegregationBatch
        {
            Id = Guid.NewGuid(),
            PickupTaskId = pickupTaskId,
            BatchCode = batchCode,
            Status = SegregationBatchStatus.Pending,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
        };
    }

    public void Record(decimal plasticKg, decimal organicKg, decimal metalKg, decimal paperKg, decimal eWasteKg, Guid actorUserId, DateTime recordedAtUtc)
    {
        if (Status != SegregationBatchStatus.Pending)
            throw new InvalidOperationException($"Cannot record segregation data on batch in {Status} status.");
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        ValidateWeight(plasticKg, nameof(plasticKg));
        ValidateWeight(organicKg, nameof(organicKg));
        ValidateWeight(metalKg, nameof(metalKg));
        ValidateWeight(paperKg, nameof(paperKg));
        ValidateWeight(eWasteKg, nameof(eWasteKg));

        if (plasticKg + organicKg + metalKg + paperKg + eWasteKg <= 0m)
            throw new ArgumentException("At least one waste category must be greater than zero.");

        PlasticKg = plasticKg;
        OrganicKg = organicKg;
        MetalKg = metalKg;
        PaperKg = paperKg;
        EWasteKg = eWasteKg;
        RecordedByUserId = actorUserId;
        RecordedAtUtc = recordedAtUtc;
        Status = SegregationBatchStatus.Recorded;
        UpdatedAtUtc = recordedAtUtc;
    }

    public void MarkRecycled(Guid actorUserId, DateTime recycledAtUtc)
    {
        if (Status != SegregationBatchStatus.Recorded)
            throw new InvalidOperationException($"Cannot mark recycled on batch in {Status} status.");
        if (actorUserId == Guid.Empty)
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        RecycledByUserId = actorUserId;
        RecycledAtUtc = recycledAtUtc;
        Status = SegregationBatchStatus.Recycled;
        UpdatedAtUtc = recycledAtUtc;
    }

    private static void ValidateWeight(decimal value, string paramName)
    {
        if (value < 0m)
            throw new ArgumentOutOfRangeException(paramName, "Weight must be greater than or equal to zero.");
    }
}
```

- [ ] **Step 3: Run unit tests and verify pass**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~SegregationBatchTests"`
Expected: PASS.

- [ ] **Step 4: Commit domain implementation**

```bash
git add src/EcoTrack.Domain/Inventory/SegregationBatchStatus.cs src/EcoTrack.Domain/Inventory/SegregationBatch.cs tests/EcoTrack.UnitTests/Segregation/SegregationBatchTests.cs
git commit -m "feat: add segregation batch domain model"
```

### Task 3: Add Failing Integration Tests for Segregation API

**Files:**
- Create: `tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs`
- Test: `tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs`

- [ ] **Step 1: Write failing endpoint tests for auth, list, record, and recycle**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Segregation;

public class SegregationEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public SegregationEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBatches_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/segregation/batches");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBatches_WithCollectorToken_ReturnsForbidden()
    {
        await AuthenticateAsCollectorAsync();

        var response = await _client.GetAsync("/api/segregation/batches");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminWorkflow_RecordThenRecycle_ReturnsUpdatedStatuses()
    {
        await AuthenticateAsAdminAsync();
        var pickupId = await CreateSentToSegregationPickupAsync();

        var pending = await _client.GetFromJsonAsync<PagedBatchesContract>("/api/segregation/batches?status=Pending&page=1&pageSize=20");
        var batch = pending!.Items.Single(x => x.PickupTaskId == pickupId);

        var recordResponse = await _client.PostAsJsonAsync($"/api/segregation/batches/{batch.Id}/record", new
        {
            plasticKg = 10m,
            organicKg = 5m,
            metalKg = 2m,
            paperKg = 1m,
            eWasteKg = 0.5m
        });

        recordResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recorded = await recordResponse.Content.ReadFromJsonAsync<BatchDetailContract>();
        recorded!.Status.Should().Be("Recorded");

        var recycleResponse = await _client.PostAsync($"/api/segregation/batches/{batch.Id}/mark-recycled", null);
        recycleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recycled = await recycleResponse.Content.ReadFromJsonAsync<BatchDetailContract>();
        recycled!.Status.Should().Be("Recycled");
    }

    private async Task<Guid> CreateSentToSegregationPickupAsync()
    {
        var create = await _client.PostAsJsonAsync("/api/collection/pickups", new
        {
            siteName = "Segregation Site",
            siteAddressText = "Warehouse 5",
            scheduledAtUtc = DateTime.UtcNow.AddHours(6),
            estimatedWeightKg = 100m,
            notes = "for segregation"
        });

        var pickup = await create.Content.ReadFromJsonAsync<PickupDetailContract>();

        await _client.PostAsJsonAsync($"/api/collection/pickups/{pickup!.Id}/assign", new
        {
            assignedCollectorUserId = await GetCollectorUserIdAsync(),
            note = "assign for segregation workflow"
        });

        await AuthenticateAsCollectorAsync();
        await _client.PostAsJsonAsync($"/api/collection/pickups/{pickup.Id}/mark-collected", new { collectedWeightKg = 95m });

        await AuthenticateAsAdminAsync();
        await _client.PostAsync($"/api/collection/pickups/{pickup.Id}/send-to-segregation", null);

        return pickup.Id;
    }

    private async Task<Guid> GetCollectorUserIdAsync()
    {
        await AuthenticateAsCollectorAsync();
        var me = await _client.GetFromJsonAsync<MeContract>("/api/auth/me");
        await AuthenticateAsAdminAsync();
        return me!.Id;
    }

    private async Task AuthenticateAsAdminAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@ecotrack.local", password = "admin123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private async Task AuthenticateAsCollectorAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "collector@ecotrack.local", password = "collector123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private sealed record AuthPayload(string Token);
    private sealed record MeContract(Guid Id, string Name, string Email, string Role);
    private sealed record PickupDetailContract(Guid Id);
    private sealed record PagedBatchesContract(List<BatchListContract> Items, int Page, int PageSize, int TotalCount, int TotalPages);
    private sealed record BatchListContract(Guid Id, Guid PickupTaskId, string BatchCode, string PickupCode, string Status, DateTime? RecordedAtUtc, DateTime? RecycledAtUtc);
    private sealed record BatchDetailContract(Guid Id, string BatchCode, string Status, Guid PickupTaskId, string PickupCode, string SiteName, string SiteAddressText, DateTime ScheduledAtUtc, decimal CollectedWeightKg, decimal? PlasticKg, decimal? OrganicKg, decimal? MetalKg, decimal? PaperKg, decimal? EWasteKg, Guid? RecordedByUserId, DateTime? RecordedAtUtc, Guid? RecycledByUserId, DateTime? RecycledAtUtc, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
}
```

- [ ] **Step 2: Run segregation integration tests to confirm route failures**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~SegregationEndpointsTests"`
Expected: FAIL with `404 NotFound` for segregation routes.

- [ ] **Step 3: Commit failing integration tests**

```bash
git add tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs
git commit -m "test: add failing segregation endpoint integration tests"
```

### Task 4: Implement Segregation Application + API Endpoints

**Files:**
- Create: `src/EcoTrack.Application/Segregation/Contracts/GetSegregationBatchesQueryRequest.cs`
- Create: `src/EcoTrack.Application/Segregation/Contracts/RecordSegregationDataRequest.cs`
- Create: `src/EcoTrack.Application/Segregation/Contracts/SegregationBatchListItemResponse.cs`
- Create: `src/EcoTrack.Application/Segregation/Contracts/SegregationBatchDetailResponse.cs`
- Create: `src/EcoTrack.Application/Segregation/SegregationService.cs`
- Create: `src/EcoTrack.Api/Controllers/SegregationController.cs`
- Modify: `src/EcoTrack.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Add query/request/response contracts**

```csharp
namespace EcoTrack.Application.Segregation.Contracts;

public sealed record GetSegregationBatchesQueryRequest(
    string? Status,
    int Page = 1,
    int PageSize = 20);

public sealed record RecordSegregationDataRequest(
    decimal PlasticKg,
    decimal OrganicKg,
    decimal MetalKg,
    decimal PaperKg,
    decimal EWasteKg);

public sealed record SegregationBatchListItemResponse(
    Guid Id,
    Guid PickupTaskId,
    string BatchCode,
    string PickupCode,
    string Status,
    DateTime? RecordedAtUtc,
    DateTime? RecycledAtUtc);

public sealed record SegregationBatchDetailResponse(
    Guid Id,
    string BatchCode,
    string Status,
    Guid PickupTaskId,
    string PickupCode,
    string SiteName,
    string SiteAddressText,
    DateTime ScheduledAtUtc,
    decimal CollectedWeightKg,
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
    DateTime UpdatedAtUtc);
```

- [ ] **Step 2: Implement SegregationService methods**

```csharp
using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Application.Segregation.Contracts;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EcoTrack.Application.Segregation;

public class SegregationService
{
    private readonly IApplicationDbContext _dbContext;

    public SegregationService(IApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResponse<SegregationBatchListItemResponse>> GetBatchesAsync(GetSegregationBatchesQueryRequest request, CancellationToken cancellationToken)
    {
        var page = request.Page <= 0 ? throw new BadRequestException("Page must be greater than or equal to 1.") : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? throw new BadRequestException("PageSize must be between 1 and 100.") : request.PageSize;

        var query = _dbContext.SegregationBatches
            .AsNoTracking()
            .Join(_dbContext.PickupTasks.AsNoTracking(), b => b.PickupTaskId, p => p.Id, (b, p) => new { b, p });

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (!Enum.TryParse<SegregationBatchStatus>(request.Status, true, out var status))
                throw new BadRequestException("Invalid status value.");

            query = query.Where(x => x.b.Status == status);
        }

        query = query.OrderBy(x => x.b.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SegregationBatchListItemResponse(
                x.b.Id,
                x.b.PickupTaskId,
                x.b.BatchCode,
                x.p.PickupCode,
                x.b.Status.ToString(),
                x.b.RecordedAtUtc,
                x.b.RecycledAtUtc))
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return new PagedResponse<SegregationBatchListItemResponse>(items, page, pageSize, totalCount, totalPages);
    }

    public async Task<SegregationBatchDetailResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var model = await BuildDetailQuery(id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Segregation batch not found.");

        return model;
    }

    public async Task<SegregationBatchDetailResponse> RecordAsync(Guid id, RecordSegregationDataRequest request, Guid actorUserId, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.SegregationBatches.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Segregation batch not found.");

        try
        {
            batch.Record(request.PlasticKg, request.OrganicKg, request.MetalKg, request.PaperKg, request.EWasteKg, actorUserId, DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException(ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BadRequestException(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<SegregationBatchDetailResponse> MarkRecycledAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken)
    {
        var batch = await _dbContext.SegregationBatches.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new NotFoundException("Segregation batch not found.");

        try
        {
            batch.MarkRecycled(actorUserId, DateTime.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            throw new BadRequestException(ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw new BadRequestException(ex.Message);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    private IQueryable<SegregationBatchDetailResponse> BuildDetailQuery(Guid id)
    {
        return _dbContext.SegregationBatches
            .AsNoTracking()
            .Where(b => b.Id == id)
            .Join(_dbContext.PickupTasks.AsNoTracking(), b => b.PickupTaskId, p => p.Id, (b, p) => new SegregationBatchDetailResponse(
                b.Id,
                b.BatchCode,
                b.Status.ToString(),
                b.PickupTaskId,
                p.PickupCode,
                p.SiteName,
                p.SiteAddressText,
                p.ScheduledAtUtc,
                p.CollectedWeightKg ?? 0m,
                b.PlasticKg,
                b.OrganicKg,
                b.MetalKg,
                b.PaperKg,
                b.EWasteKg,
                b.RecordedByUserId,
                b.RecordedAtUtc,
                b.RecycledByUserId,
                b.RecycledAtUtc,
                b.CreatedAtUtc,
                b.UpdatedAtUtc));
    }
}
```

- [ ] **Step 3: Add Segregation controller routes with admin-only authorization**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Application.Segregation;
using EcoTrack.Application.Segregation.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/segregation/batches")]
[Authorize(Roles = "admin")]
public class SegregationController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<SegregationBatchListItemResponse>>> Get(
        [FromServices] SegregationService service,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await service.GetBatchesAsync(new GetSegregationBatchesQueryRequest(status, page, pageSize), cancellationToken));
    }

    [HttpGet("pending")]
    public async Task<ActionResult<PagedResponse<SegregationBatchListItemResponse>>> GetPending(
        [FromServices] SegregationService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return Ok(await service.GetBatchesAsync(new GetSegregationBatchesQueryRequest("Pending", page, pageSize), cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SegregationBatchDetailResponse>> GetById(
        Guid id,
        [FromServices] SegregationService service,
        CancellationToken cancellationToken)
    {
        return Ok(await service.GetByIdAsync(id, cancellationToken));
    }

    [HttpPost("{id:guid}/record")]
    public async Task<ActionResult<SegregationBatchDetailResponse>> Record(
        Guid id,
        [FromBody] RecordSegregationDataRequest request,
        [FromServices] SegregationService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.RecordAsync(id, request, actorUserId, cancellationToken));
    }

    [HttpPost("{id:guid}/mark-recycled")]
    public async Task<ActionResult<SegregationBatchDetailResponse>> MarkRecycled(
        Guid id,
        [FromServices] SegregationService service,
        CancellationToken cancellationToken)
    {
        var actorUserId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        return Ok(await service.MarkRecycledAsync(id, actorUserId, cancellationToken));
    }
}
```

- [ ] **Step 4: Register SegregationService in dependency injection**

```csharp
services.AddScoped<SegregationService>();
```

- [ ] **Step 5: Run integration tests and observe persistence-related failures**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~SegregationEndpointsTests"`
Expected: FAIL due to missing `SegregationBatch` persistence wiring/migration.

- [ ] **Step 6: Commit application + API layer**

```bash
git add src/EcoTrack.Application/Segregation src/EcoTrack.Api/Controllers/SegregationController.cs src/EcoTrack.Infrastructure/DependencyInjection.cs
git commit -m "feat: add segregation service and controller endpoints"
```

### Task 5: Add Persistence Model + Migration

**Files:**
- Modify: `src/EcoTrack.Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/EcoTrack.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/EcoTrack.Infrastructure/Persistence/Configurations/SegregationBatchConfiguration.cs`
- Modify (generated): `src/EcoTrack.Infrastructure/Migrations/*AddSegregationBatches*.cs`
- Modify (generated): `src/EcoTrack.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`

- [ ] **Step 1: Add SegregationBatch DbSet to application DB abstraction**

```csharp
DbSet<SegregationBatch> SegregationBatches { get; }
```

- [ ] **Step 2: Add SegregationBatch DbSet to AppDbContext**

```csharp
public DbSet<SegregationBatch> SegregationBatches => Set<SegregationBatch>();
```

- [ ] **Step 3: Add entity configuration for table and constraints**

```csharp
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrack.Infrastructure.Persistence.Configurations;

public class SegregationBatchConfiguration : IEntityTypeConfiguration<SegregationBatch>
{
    public void Configure(EntityTypeBuilder<SegregationBatch> builder)
    {
        builder.ToTable("SegregationBatches");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PickupTaskId).IsRequired();
        builder.Property(x => x.BatchCode).IsRequired().HasMaxLength(32);
        builder.Property(x => x.Status).HasConversion<string>().IsRequired();

        builder.Property(x => x.PlasticKg).HasPrecision(18, 3);
        builder.Property(x => x.OrganicKg).HasPrecision(18, 3);
        builder.Property(x => x.MetalKg).HasPrecision(18, 3);
        builder.Property(x => x.PaperKg).HasPrecision(18, 3);
        builder.Property(x => x.EWasteKg).HasPrecision(18, 3);

        builder.Property(x => x.RecordedByUserId);
        builder.Property(x => x.RecordedAtUtc);
        builder.Property(x => x.RecycledByUserId);
        builder.Property(x => x.RecycledAtUtc);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.BatchCode).IsUnique();
        builder.HasIndex(x => x.PickupTaskId).IsUnique();

        builder.HasOne<PickupTask>()
            .WithMany()
            .HasForeignKey(x => x.PickupTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Create EF migration**

Run: `dotnet ef migrations add AddSegregationBatches --project src/EcoTrack.Infrastructure --startup-project src/EcoTrack.Api`
Expected: PASS and new migration files created under `src/EcoTrack.Infrastructure/Migrations`.

- [ ] **Step 5: Apply migration locally**

Run: `dotnet ef database update --project src/EcoTrack.Infrastructure --startup-project src/EcoTrack.Api`
Expected: PASS and schema includes `SegregationBatches` table.

- [ ] **Step 6: Commit persistence changes**

```bash
git add src/EcoTrack.Application/Common/Interfaces/IApplicationDbContext.cs src/EcoTrack.Infrastructure/Persistence/AppDbContext.cs src/EcoTrack.Infrastructure/Persistence/Configurations/SegregationBatchConfiguration.cs src/EcoTrack.Infrastructure/Migrations
git commit -m "feat: persist segregation batches with EF migration"
```

### Task 6: Integrate Collection Transition with Auto-Batch Creation

**Files:**
- Modify: `src/EcoTrack.Application/Collection/CollectionService.cs`
- Test: `tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs`

- [ ] **Step 1: Add failing integration assertion for auto-created pending batch**

```csharp
[Fact]
public async Task SendToSegregation_AutoCreatesPendingBatch()
{
    await AuthenticateAsAdminAsync();
    var pickupId = await CreateSentToSegregationPickupAsync();

    var pending = await _client.GetFromJsonAsync<PagedBatchesContract>("/api/segregation/batches?status=Pending&page=1&pageSize=20");

    pending!.Items.Should().Contain(x => x.PickupTaskId == pickupId && x.Status == "Pending");
}
```

- [ ] **Step 2: Run targeted integration test and confirm failure before implementation**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~SendToSegregation_AutoCreatesPendingBatch"`
Expected: FAIL because no batch is created by `SendToSegregationAsync` yet.

- [ ] **Step 3: Update collection transition logic to create segregation batch**

```csharp
public async Task<PickupDetailResponse> SendToSegregationAsync(Guid id, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
{
    if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        throw new ForbiddenException("Only admins can send pickups to segregation.");

    var pickup = await _dbContext.PickupTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new NotFoundException("Pickup not found.");

    try
    {
        pickup.SendToSegregation(DateTime.UtcNow);

        var existingBatch = await _dbContext.SegregationBatches
            .AsNoTracking()
            .AnyAsync(x => x.PickupTaskId == pickup.Id, cancellationToken);

        if (!existingBatch)
        {
            var nextCode = await GenerateSegregationBatchCodeAsync(cancellationToken);
            var batch = SegregationBatch.CreatePending(pickup.Id, nextCode, DateTime.UtcNow);
            _dbContext.SegregationBatches.Add(batch);
        }
    }
    catch (InvalidOperationException ex)
    {
        throw new ConflictException(ex.Message);
    }

    await _dbContext.SaveChangesAsync(cancellationToken);
    return await ToDetailResponseAsync(pickup, cancellationToken);
}

private async Task<string> GenerateSegregationBatchCodeAsync(CancellationToken cancellationToken)
{
    var lastCode = await _dbContext.SegregationBatches
        .AsNoTracking()
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => x.BatchCode)
        .FirstOrDefaultAsync(cancellationToken);

    var nextNumber = 1;
    if (!string.IsNullOrWhiteSpace(lastCode) && lastCode.StartsWith("SB-", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(lastCode[3..], out var parsed))
    {
        nextNumber = parsed + 1;
    }

    return $"SB-{nextNumber:D4}";
}
```

- [ ] **Step 4: Run segregation integration tests and verify pass**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~SegregationEndpointsTests"`
Expected: PASS.

- [ ] **Step 5: Commit collection integration**

```bash
git add src/EcoTrack.Application/Collection/CollectionService.cs tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs
git commit -m "feat: auto-create segregation batch when pickup sent to segregation"
```

### Task 7: Finalize Error Cases, API Docs, and Full Verification

**Files:**
- Modify: `tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Add integration tests for invalid transition and validation errors**

```csharp
[Fact]
public async Task Record_WithAllZeroWeights_ReturnsBadRequest()
{
    await AuthenticateAsAdminAsync();
    var pickupId = await CreateSentToSegregationPickupAsync();

    var pending = await _client.GetFromJsonAsync<PagedBatchesContract>("/api/segregation/batches?status=Pending&page=1&pageSize=20");
    var batch = pending!.Items.Single(x => x.PickupTaskId == pickupId);

    var response = await _client.PostAsJsonAsync($"/api/segregation/batches/{batch.Id}/record", new
    {
        plasticKg = 0m,
        organicKg = 0m,
        metalKg = 0m,
        paperKg = 0m,
        eWasteKg = 0m
    });

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}

[Fact]
public async Task MarkRecycled_OnPendingBatch_ReturnsBadRequest()
{
    await AuthenticateAsAdminAsync();
    var pickupId = await CreateSentToSegregationPickupAsync();

    var pending = await _client.GetFromJsonAsync<PagedBatchesContract>("/api/segregation/batches?status=Pending&page=1&pageSize=20");
    var batch = pending!.Items.Single(x => x.PickupTaskId == pickupId);

    var response = await _client.PostAsync($"/api/segregation/batches/{batch.Id}/mark-recycled", null);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

- [ ] **Step 2: Add segregation routes to README API table**

```markdown
| `GET` | `/api/segregation/batches` | Admin | List segregation batches (status, paging) |
| `GET` | `/api/segregation/batches/pending` | Admin | List pending batches for dropdown |
| `GET` | `/api/segregation/batches/{id}` | Admin | Get segregation batch detail |
| `POST` | `/api/segregation/batches/{id}/record` | Admin | Record segregation category weights |
| `POST` | `/api/segregation/batches/{id}/mark-recycled` | Admin | Mark recorded batch as recycled |
```

- [ ] **Step 3: Run full unit and integration test suites**

Run: `dotnet test tests/EcoTrack.UnitTests`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests`
Expected: PASS (Docker Desktop must be running, and no locked `EcoTrack.Api` process).

- [ ] **Step 4: Commit final polish**

```bash
git add tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs README.md
git commit -m "test: cover segregation validation errors and document endpoints"
```

## Self-Review Checklist (Completed)

### 1. Spec coverage

- List segregation batches with status filter and pagination: covered in Task 4 (`GetBatchesAsync`, `GET /api/segregation/batches`) and Task 3/7 integration tests.
- Pending dropdown data: covered in Task 4 (`GET /api/segregation/batches?status=Pending`) and optional alias route `GET /api/segregation/batches/pending`.
- Get batch details with pickup linkage: covered in Task 4 detail projection and Task 3 tests.
- Record segregation data with 5 categories and validation: covered in Task 2 domain guards, Task 4 `RecordAsync`, and Task 7 validation tests.
- Mark recycled transition rules: covered in Task 2 and Task 4/7.
- Auto-create on collection `SendToSegregation`: covered in Task 6 with dedicated failing-then-passing test.
- Admin-only access: covered in Task 4 controller authorization and Task 3 unauthorized/forbidden tests.
- Middleware error shape compatibility: covered by mapping domain/service exceptions to existing `BadRequestException`/`NotFoundException` flow in Task 4.

### 2. Placeholder scan

- Removed placeholders/TODOs.
- Every code step contains concrete code.
- Every test/run step includes exact command and expected outcome.

### 3. Type consistency

- Consistent names used across tasks: `SegregationBatch`, `SegregationBatchStatus`, `RecordSegregationDataRequest`, `SegregationBatchDetailResponse`, `SegregationService`.
- Status strings align with enum values (`Pending`, `Recorded`, `Recycled`).
- Batch code format consistently `SB-{number:D4}`.

---

Plan complete and saved to `docs/superpowers/plans/2026-06-29-segregation-api.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
