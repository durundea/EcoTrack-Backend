# Collection API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement collection pickup CRUD, assignment history, and workflow transition APIs for the v1 Collection page with role-aware access control.

**Architecture:** Add a dedicated Collection module in the Application layer with a service that coordinates validation, role checks, and state transitions on a new PickupTask aggregate in Domain. Persist PickupTask and PickupAssignmentEvent using EF Core configurations and migrations in Infrastructure. Expose resource and action endpoints through a new CollectionController and verify behavior primarily through integration tests, with unit tests for domain transition rules.

**Tech Stack:** ASP.NET Core Web API (.NET 10), EF Core 10 + Npgsql, JWT auth, xUnit, FluentAssertions, Testcontainers PostgreSQL.

---

## Scope Check

The approved spec is one coherent subsystem (Collection pickup scheduling + workflow), so a single implementation plan is appropriate.

## File Structure

- Create: `src/EcoTrack.Domain/Inventory/PickupStatus.cs`
  - Collection workflow enum values.
- Create: `src/EcoTrack.Domain/Inventory/PickupAssignmentEvent.cs`
  - Assignment/reassignment history model.
- Create: `src/EcoTrack.Domain/Inventory/PickupTask.cs`
  - Aggregate root with transition and mutation rules.
- Modify: `src/EcoTrack.Application/Common/Interfaces/IApplicationDbContext.cs`
  - Add DbSet properties for new entities.
- Create: `src/EcoTrack.Application/Collection/CollectionService.cs`
  - Query composition, role checks, transition orchestration.
- Create: `src/EcoTrack.Application/Collection/Contracts/GetPickupsQueryRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/CreatePickupRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/UpdatePickupByAdminRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/UpdatePickupNotesRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/AssignPickupRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/MarkCollectedRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/CancelPickupRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/PickupResponse.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/PickupDetailResponse.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/PickupAssignmentEventResponse.cs`
- Create: `src/EcoTrack.Api/Controllers/CollectionController.cs`
  - All collection endpoints.
- Modify: `src/EcoTrack.Infrastructure/Persistence/AppDbContext.cs`
  - Add DbSet implementations.
- Create: `src/EcoTrack.Infrastructure/Persistence/Configurations/PickupTaskConfiguration.cs`
- Create: `src/EcoTrack.Infrastructure/Persistence/Configurations/PickupAssignmentEventConfiguration.cs`
- Modify: `src/EcoTrack.Infrastructure/DependencyInjection.cs`
  - Register CollectionService.
- Create: `tests/EcoTrack.UnitTests/Collection/PickupTaskTests.cs`
  - Domain transition and guard tests.
- Create: `tests/EcoTrack.IntegrationTests/Collection/CollectionEndpointsTests.cs`
  - End-to-end HTTP behavior tests.
- Modify: `README.md`
  - Endpoint and query parameter documentation.
- Create: `src/EcoTrack.Infrastructure/Migrations/<timestamp>_AddCollectionPickups.cs`
- Create: `src/EcoTrack.Infrastructure/Migrations/<timestamp>_AddCollectionPickups.Designer.cs`
- Modify: `src/EcoTrack.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`

### Task 1: Add Failing Integration Tests for Collection API Surface

**Files:**
- Create: `tests/EcoTrack.IntegrationTests/Collection/CollectionEndpointsTests.cs`

- [ ] **Step 1: Write failing tests for core routes and auth**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Collection;

public class CollectionEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public CollectionEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetPickups_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/collection/pickups");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminCanCreateAndListPickups()
    {
        await AuthenticateAsAdminAsync();

        var createResponse = await _client.PostAsJsonAsync("/api/collection/pickups", new
        {
            siteName = "Green Residency",
            siteAddressText = "Block A",
            scheduledAtUtc = DateTime.UtcNow.AddDays(1),
            estimatedWeightKg = 120.0m,
            notes = "Morning slot"
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResponse = await _client.GetAsync("/api/collection/pickups?page=1&pageSize=20&sortBy=scheduledAtUtc&sortDirection=desc");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await listResponse.Content.ReadFromJsonAsync<PagedPickupsContract>();
        payload.Should().NotBeNull();
        payload!.Items.Should().Contain(x => x.SiteName == "Green Residency");
    }

    private async Task AuthenticateAsAdminAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@ecotrack.local", password = "admin123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private sealed record AuthPayload(string Token);
    private sealed record PagedPickupsContract(List<PickupContract> Items, int Page, int PageSize, int TotalCount, int TotalPages);
    private sealed record PickupContract(Guid Id, string PickupCode, string SiteName, string SiteAddressText, DateTime ScheduledAtUtc, decimal EstimatedWeightKg, decimal? CollectedWeightKg, string Status, Guid? AssignedCollectorUserId, string? AssignedCollectorDisplayName, string? Notes);
}
```

- [ ] **Step 2: Run the new tests to verify endpoint failures**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~CollectionEndpointsTests"`
Expected: FAIL with `404 NotFound` for `/api/collection/pickups` routes.

- [ ] **Step 3: Commit failing test baseline**

```bash
git add tests/EcoTrack.IntegrationTests/Collection/CollectionEndpointsTests.cs
git commit -m "test: add failing collection endpoint integration tests"
```

### Task 2: Add Domain Model with Transition Guards (TDD)

**Files:**
- Create: `tests/EcoTrack.UnitTests/Collection/PickupTaskTests.cs`
- Create: `src/EcoTrack.Domain/Inventory/PickupStatus.cs`
- Create: `src/EcoTrack.Domain/Inventory/PickupAssignmentEvent.cs`
- Create: `src/EcoTrack.Domain/Inventory/PickupTask.cs`

- [ ] **Step 1: Write failing unit tests for transitions and constraints**

```csharp
using EcoTrack.Domain.Inventory;
using FluentAssertions;

namespace EcoTrack.UnitTests.Collection;

public class PickupTaskTests
{
    [Fact]
    public void Assign_FromScheduled_MovesToAssigned_AndAddsEvent()
    {
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", DateTime.UtcNow.AddDays(1), 120m, "note", Guid.NewGuid(), DateTime.UtcNow, "P-1001");

        var adminId = Guid.NewGuid();
        var collectorId = Guid.NewGuid();

        pickup.AssignCollector(collectorId, adminId, DateTime.UtcNow, "initial");

        pickup.Status.Should().Be(PickupStatus.Assigned);
        pickup.AssignmentEvents.Should().HaveCount(1);
        pickup.AssignedCollectorUserId.Should().Be(collectorId);
    }

    [Fact]
    public void Cancel_AfterCollected_ThrowsInvalidOperationException()
    {
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", DateTime.UtcNow.AddDays(1), 120m, null, Guid.NewGuid(), DateTime.UtcNow, "P-1001");
        var adminId = Guid.NewGuid();
        var collectorId = Guid.NewGuid();

        pickup.AssignCollector(collectorId, adminId, DateTime.UtcNow, null);
        pickup.MarkCollected(115m, collectorId, DateTime.UtcNow);

        Action act = () => pickup.Cancel(adminId, DateTime.UtcNow, "late cancel");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SendToSegregation_WithoutCollectedStatus_ThrowsInvalidOperationException()
    {
        var pickup = PickupTask.CreateScheduled("Green Residency", "Block A", DateTime.UtcNow.AddDays(1), 120m, null, Guid.NewGuid(), DateTime.UtcNow, "P-1001");

        Action act = () => pickup.SendToSegregation(Guid.NewGuid(), DateTime.UtcNow);
        act.Should().Throw<InvalidOperationException>();
    }
}
```

- [ ] **Step 2: Run unit tests and confirm failures due to missing domain types**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~PickupTaskTests"`
Expected: FAIL with compile errors for missing `PickupTask` and `PickupStatus`.

- [ ] **Step 3: Add PickupStatus enum**

```csharp
namespace EcoTrack.Domain.Inventory;

public enum PickupStatus
{
    Scheduled = 1,
    Assigned = 2,
    Collected = 3,
    SentToSegregation = 4,
    Cancelled = 5,
}
```

- [ ] **Step 4: Add PickupAssignmentEvent model**

```csharp
using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class PickupAssignmentEvent : Entity
{
    private PickupAssignmentEvent() { }

    public Guid PickupTaskId { get; private set; }
    public Guid? PreviousCollectorUserId { get; private set; }
    public Guid NewCollectorUserId { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public string? Note { get; private set; }

    public static PickupAssignmentEvent Create(Guid pickupTaskId, Guid? previousCollectorUserId, Guid newCollectorUserId, Guid changedByUserId, DateTime changedAtUtc, string? note)
    {
        return new PickupAssignmentEvent
        {
            Id = Guid.NewGuid(),
            PickupTaskId = pickupTaskId,
            PreviousCollectorUserId = previousCollectorUserId,
            NewCollectorUserId = newCollectorUserId,
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = changedAtUtc,
            Note = note,
        };
    }
}
```

- [ ] **Step 5: Add PickupTask aggregate with transition methods**

```csharp
using EcoTrack.Domain.Common;

namespace EcoTrack.Domain.Inventory;

public class PickupTask : Entity
{
    private readonly List<PickupAssignmentEvent> _assignmentEvents = new();

    private PickupTask() { }

    public string PickupCode { get; private set; } = null!;
    public string SiteName { get; private set; } = null!;
    public string SiteAddressText { get; private set; } = null!;
    public DateTime ScheduledAtUtc { get; private set; }
    public decimal EstimatedWeightKg { get; private set; }
    public decimal? CollectedWeightKg { get; private set; }
    public PickupStatus Status { get; private set; }
    public Guid? AssignedCollectorUserId { get; private set; }
    public DateTime? AssignedAtUtc { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyCollection<PickupAssignmentEvent> AssignmentEvents => _assignmentEvents;

    public static PickupTask CreateScheduled(string siteName, string siteAddressText, DateTime scheduledAtUtc, decimal estimatedWeightKg, string? notes, Guid createdByUserId, DateTime createdAtUtc, string pickupCode)
    {
        if (string.IsNullOrWhiteSpace(siteName)) throw new ArgumentException("SiteName is required.", nameof(siteName));
        if (string.IsNullOrWhiteSpace(siteAddressText)) throw new ArgumentException("SiteAddressText is required.", nameof(siteAddressText));
        if (estimatedWeightKg <= 0m) throw new ArgumentOutOfRangeException(nameof(estimatedWeightKg), "EstimatedWeightKg must be greater than zero.");

        return new PickupTask
        {
            Id = Guid.NewGuid(),
            PickupCode = pickupCode,
            SiteName = siteName,
            SiteAddressText = siteAddressText,
            ScheduledAtUtc = scheduledAtUtc,
            EstimatedWeightKg = estimatedWeightKg,
            Notes = notes,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            Status = PickupStatus.Scheduled,
        };
    }

    public void AssignCollector(Guid newCollectorUserId, Guid changedByUserId, DateTime changedAtUtc, string? note)
    {
        if (Status is PickupStatus.Cancelled or PickupStatus.SentToSegregation or PickupStatus.Collected)
            throw new InvalidOperationException("Pickup cannot be assigned in current status.");

        var previous = AssignedCollectorUserId;
        AssignedCollectorUserId = newCollectorUserId;
        AssignedAtUtc = changedAtUtc;
        Status = PickupStatus.Assigned;
        UpdatedAtUtc = changedAtUtc;

        _assignmentEvents.Add(PickupAssignmentEvent.Create(Id, previous, newCollectorUserId, changedByUserId, changedAtUtc, note));
    }

    public void MarkCollected(decimal collectedWeightKg, Guid actorUserId, DateTime collectedAtUtc)
    {
        if (Status != PickupStatus.Assigned) throw new InvalidOperationException("Only assigned pickups can be collected.");
        if (AssignedCollectorUserId.HasValue && AssignedCollectorUserId != actorUserId)
            throw new InvalidOperationException("Only assigned collector can mark pickup as collected.");
        if (collectedWeightKg <= 0m) throw new ArgumentOutOfRangeException(nameof(collectedWeightKg), "CollectedWeightKg must be greater than zero.");

        CollectedWeightKg = collectedWeightKg;
        Status = PickupStatus.Collected;
        UpdatedAtUtc = collectedAtUtc;
    }

    public void SendToSegregation(Guid actorUserId, DateTime movedAtUtc)
    {
        if (Status != PickupStatus.Collected) throw new InvalidOperationException("Only collected pickups can be sent to segregation.");
        Status = PickupStatus.SentToSegregation;
        UpdatedAtUtc = movedAtUtc;
    }

    public void Cancel(Guid cancelledByUserId, DateTime cancelledAtUtc, string? reason)
    {
        if (Status != PickupStatus.Scheduled && Status != PickupStatus.Assigned)
            throw new InvalidOperationException("Only scheduled or assigned pickups can be cancelled.");

        Status = PickupStatus.Cancelled;
        CancelledByUserId = cancelledByUserId;
        CancelledAtUtc = cancelledAtUtc;
        CancelReason = reason;
        UpdatedAtUtc = cancelledAtUtc;
    }

    public void UpdateByAdmin(string siteName, string siteAddressText, DateTime scheduledAtUtc, decimal estimatedWeightKg, string? notes, DateTime updatedAtUtc)
    {
        if (Status is PickupStatus.Cancelled or PickupStatus.SentToSegregation)
            throw new InvalidOperationException("Terminal pickups cannot be edited.");
        if (estimatedWeightKg <= 0m)
            throw new ArgumentOutOfRangeException(nameof(estimatedWeightKg), "EstimatedWeightKg must be greater than zero.");

        SiteName = siteName;
        SiteAddressText = siteAddressText;
        ScheduledAtUtc = scheduledAtUtc;
        EstimatedWeightKg = estimatedWeightKg;
        Notes = notes;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void UpdateNotes(string? notes, DateTime updatedAtUtc)
    {
        if (Status is PickupStatus.Cancelled or PickupStatus.SentToSegregation)
            throw new InvalidOperationException("Terminal pickups cannot be edited.");

        Notes = notes;
        UpdatedAtUtc = updatedAtUtc;
    }
}
```

- [ ] **Step 6: Run unit tests to verify domain rules pass**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~PickupTaskTests"`
Expected: PASS.

- [ ] **Step 7: Commit domain model and tests**

```bash
git add tests/EcoTrack.UnitTests/Collection/PickupTaskTests.cs src/EcoTrack.Domain/Inventory/PickupStatus.cs src/EcoTrack.Domain/Inventory/PickupAssignmentEvent.cs src/EcoTrack.Domain/Inventory/PickupTask.cs
git commit -m "feat: add pickup domain model and transition rules"
```

### Task 3: Wire Persistence, DbContext, and Migration

**Files:**
- Modify: `src/EcoTrack.Application/Common/Interfaces/IApplicationDbContext.cs`
- Modify: `src/EcoTrack.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/EcoTrack.Infrastructure/Persistence/Configurations/PickupTaskConfiguration.cs`
- Create: `src/EcoTrack.Infrastructure/Persistence/Configurations/PickupAssignmentEventConfiguration.cs`
- Create: `src/EcoTrack.Infrastructure/Migrations/<timestamp>_AddCollectionPickups.cs`
- Create: `src/EcoTrack.Infrastructure/Migrations/<timestamp>_AddCollectionPickups.Designer.cs`
- Modify: `src/EcoTrack.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`

- [ ] **Step 1: Add DbSets to application context interface and AppDbContext**

```csharp
// IApplicationDbContext.cs
DbSet<PickupTask> PickupTasks { get; }
DbSet<PickupAssignmentEvent> PickupAssignmentEvents { get; }

// AppDbContext.cs
public DbSet<PickupTask> PickupTasks => Set<PickupTask>();
public DbSet<PickupAssignmentEvent> PickupAssignmentEvents => Set<PickupAssignmentEvent>();
```

- [ ] **Step 2: Add EF configurations for new tables**

```csharp
// PickupTaskConfiguration.cs
builder.ToTable("PickupTasks");
builder.HasKey(x => x.Id);
builder.Property(x => x.PickupCode).HasMaxLength(32).IsRequired();
builder.HasIndex(x => x.PickupCode).IsUnique();
builder.Property(x => x.SiteName).HasMaxLength(200).IsRequired();
builder.Property(x => x.SiteAddressText).HasMaxLength(500).IsRequired();
builder.Property(x => x.EstimatedWeightKg).HasPrecision(18, 3).IsRequired();
builder.Property(x => x.CollectedWeightKg).HasPrecision(18, 3);
builder.Property(x => x.Status).HasConversion<string>().IsRequired();
builder.Property(x => x.Notes).HasMaxLength(2000);
builder.Property(x => x.CancelReason).HasMaxLength(1000);

// PickupAssignmentEventConfiguration.cs
builder.ToTable("PickupAssignmentEvents");
builder.HasKey(x => x.Id);
builder.Property(x => x.Note).HasMaxLength(1000);
builder.HasOne<PickupTask>()
    .WithMany(x => x.AssignmentEvents)
    .HasForeignKey(x => x.PickupTaskId)
    .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 3: Create migration for pickup tables**

Run: `dotnet ef migrations add AddCollectionPickups --project src/EcoTrack.Infrastructure --startup-project src/EcoTrack.Api`
Expected: SUCCESS; new migration files and snapshot update generated.

- [ ] **Step 4: Build solution to verify schema wiring compiles**

Run: `dotnet build EcoTrack-Backend.slnx`
Expected: SUCCESS.

- [ ] **Step 5: Commit persistence and migration changes**

```bash
git add src/EcoTrack.Application/Common/Interfaces/IApplicationDbContext.cs src/EcoTrack.Infrastructure/Persistence/AppDbContext.cs src/EcoTrack.Infrastructure/Persistence/Configurations/PickupTaskConfiguration.cs src/EcoTrack.Infrastructure/Persistence/Configurations/PickupAssignmentEventConfiguration.cs src/EcoTrack.Infrastructure/Migrations
git commit -m "feat: add pickup persistence model and migration"
```

### Task 4: Add Collection Contracts and Failing Service-Level Tests

**Files:**
- Create: `src/EcoTrack.Application/Collection/Contracts/GetPickupsQueryRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/CreatePickupRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/UpdatePickupByAdminRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/UpdatePickupNotesRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/AssignPickupRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/MarkCollectedRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/CancelPickupRequest.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/PickupResponse.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/PickupDetailResponse.cs`
- Create: `src/EcoTrack.Application/Collection/Contracts/PickupAssignmentEventResponse.cs`
- Create: `tests/EcoTrack.UnitTests/Collection/CollectionServiceTests.cs`

- [ ] **Step 1: Create contracts used by controller/service**

```csharp
// GetPickupsQueryRequest.cs
public sealed record GetPickupsQueryRequest(
    string? Status,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = "scheduledAtUtc",
    string? SortDirection = "desc");

// CreatePickupRequest.cs
public sealed record CreatePickupRequest(
    string SiteName,
    string SiteAddressText,
    DateTime ScheduledAtUtc,
    decimal EstimatedWeightKg,
    string? Notes);

// AssignPickupRequest.cs
public sealed record AssignPickupRequest(Guid AssignedCollectorUserId, string? Note);

// MarkCollectedRequest.cs
public sealed record MarkCollectedRequest(decimal CollectedWeightKg);
```

- [ ] **Step 2: Add failing unit tests for service query validation**

```csharp
[Fact]
public async Task GetPickups_WithInvalidPage_ThrowsBadRequestException()
{
    var service = new CollectionService(_dbContext);
    var request = new GetPickupsQueryRequest(Status: null, Page: 0, PageSize: 20, SortBy: "scheduledAtUtc", SortDirection: "desc");

    Func<Task> act = async () => await service.GetPickupsAsync(request, Guid.NewGuid(), "admin", CancellationToken.None);

    await act.Should().ThrowAsync<BadRequestException>();
}
```

- [ ] **Step 3: Run unit tests to ensure failure before service exists**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~CollectionServiceTests"`
Expected: FAIL with missing `CollectionService` type.

- [ ] **Step 4: Commit contracts and failing service tests**

```bash
git add src/EcoTrack.Application/Collection/Contracts tests/EcoTrack.UnitTests/Collection/CollectionServiceTests.cs
git commit -m "test: add collection service contract tests and dto contracts"
```

### Task 5: Implement CollectionService and Register DI

**Files:**
- Create: `src/EcoTrack.Application/Collection/CollectionService.cs`
- Modify: `src/EcoTrack.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Implement list/detail/create/update/cancel service methods**

```csharp
public async Task<PagedResponse<PickupResponse>> GetPickupsAsync(GetPickupsQueryRequest request, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
{
    var page = request.Page <= 0 ? throw new BadRequestException("Page must be greater than or equal to 1.") : request.Page;
    var pageSize = request.PageSize is < 1 or > 100 ? throw new BadRequestException("PageSize must be between 1 and 100.") : request.PageSize;

    var query = _dbContext.PickupTasks.AsNoTracking().Where(x => x.Status != PickupStatus.Cancelled);

    if (!string.Equals(actorRole, "admin", StringComparison.OrdinalIgnoreCase))
    {
        query = query.Where(x => x.AssignedCollectorUserId == actorUserId);
    }

    if (!string.IsNullOrWhiteSpace(request.Status))
    {
        if (!Enum.TryParse<PickupStatus>(request.Status, true, out var status))
            throw new BadRequestException("Invalid status value.");
        query = query.Where(x => x.Status == status);
    }

    var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "scheduledAtUtc" : request.SortBy;
    if (!string.Equals(sortBy, "scheduledAtUtc", StringComparison.OrdinalIgnoreCase))
        throw new BadRequestException("SortBy must be scheduledAtUtc.");

    var direction = string.IsNullOrWhiteSpace(request.SortDirection) ? "desc" : request.SortDirection;
    query = direction.Equals("asc", StringComparison.OrdinalIgnoreCase)
        ? query.OrderBy(x => x.ScheduledAtUtc)
        : direction.Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderByDescending(x => x.ScheduledAtUtc)
            : throw new BadRequestException("SortDirection must be asc or desc.");

    var totalCount = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(ToPickupResponse).ToListAsync(cancellationToken);

    return new PagedResponse<PickupResponse>(items, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize));
}
```

- [ ] **Step 2: Implement action methods for assign, mark collected, send to segregation, history**

```csharp
public async Task<PickupDetailResponse> AssignAsync(Guid id, AssignPickupRequest request, Guid actorUserId, string actorRole, CancellationToken cancellationToken)
{
    EnsureAdmin(actorRole);

    var pickup = await _dbContext.PickupTasks.Include(x => x.AssignmentEvents).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new NotFoundException("Pickup not found.");

    pickup.AssignCollector(request.AssignedCollectorUserId, actorUserId, DateTime.UtcNow, request.Note);
    await _dbContext.SaveChangesAsync(cancellationToken);

    return ToPickupDetailResponse(pickup);
}
```

- [ ] **Step 3: Register CollectionService in dependency injection**

```csharp
services.AddScoped<CollectionService>();
```

- [ ] **Step 4: Run focused unit tests for service behavior**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~CollectionServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit application service implementation**

```bash
git add src/EcoTrack.Application/Collection/CollectionService.cs src/EcoTrack.Infrastructure/DependencyInjection.cs
git commit -m "feat: implement collection service with validation and transitions"
```

### Task 6: Add CollectionController Endpoints and Authorization

**Files:**
- Create: `src/EcoTrack.Api/Controllers/CollectionController.cs`

- [ ] **Step 1: Add controller with route and list/detail/create endpoints**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Collection;
using EcoTrack.Application.Collection.Contracts;
using EcoTrack.Application.Inventory.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/collection/pickups")]
[Authorize]
public class CollectionController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<PickupResponse>>> Get(
        [FromServices] CollectionService service,
        [FromQuery] GetPickupsQueryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.GetPickupsAsync(request, userId, role, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PickupDetailResponse>> GetById(Guid id, [FromServices] CollectionService service, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        return Ok(await service.GetByIdAsync(id, userId, role, cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<PickupDetailResponse>> Post([FromServices] CollectionService service, [FromBody] CreatePickupRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var created = await service.CreateAsync(request, userId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
```

- [ ] **Step 2: Add update/delete/action endpoints**

```csharp
[HttpPut("{id:guid}")]
public async Task<ActionResult<PickupDetailResponse>> Put(Guid id, [FromServices] CollectionService service, [FromBody] UpdatePickupByAdminRequest request, CancellationToken cancellationToken) { ... }

[HttpPatch("{id:guid}/notes")]
[Authorize(Roles = "collector")]
public async Task<ActionResult<PickupDetailResponse>> PatchNotes(Guid id, [FromServices] CollectionService service, [FromBody] UpdatePickupNotesRequest request, CancellationToken cancellationToken) { ... }

[HttpDelete("{id:guid}")]
[Authorize(Roles = "admin")]
public async Task<ActionResult<PickupDetailResponse>> Delete(Guid id, [FromServices] CollectionService service, [FromBody] CancelPickupRequest? request, CancellationToken cancellationToken) { ... }

[HttpPost("{id:guid}/assign")]
[Authorize(Roles = "admin")]
public async Task<ActionResult<PickupDetailResponse>> Assign(Guid id, [FromServices] CollectionService service, [FromBody] AssignPickupRequest request, CancellationToken cancellationToken) { ... }

[HttpPost("{id:guid}/mark-collected")]
public async Task<ActionResult<PickupDetailResponse>> MarkCollected(Guid id, [FromServices] CollectionService service, [FromBody] MarkCollectedRequest request, CancellationToken cancellationToken) { ... }

[HttpPost("{id:guid}/send-to-segregation")]
[Authorize(Roles = "admin")]
public async Task<ActionResult<PickupDetailResponse>> SendToSegregation(Guid id, [FromServices] CollectionService service, CancellationToken cancellationToken) { ... }

[HttpGet("{id:guid}/assignment-history")]
public async Task<ActionResult<IReadOnlyList<PickupAssignmentEventResponse>>> GetAssignmentHistory(Guid id, [FromServices] CollectionService service, CancellationToken cancellationToken) { ... }
```

- [ ] **Step 3: Run integration tests and verify green for currently implemented scenarios**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~CollectionEndpointsTests"`
Expected: Some tests PASS and others FAIL until all endpoints and transitions are implemented.

- [ ] **Step 4: Commit controller endpoints**

```bash
git add src/EcoTrack.Api/Controllers/CollectionController.cs
git commit -m "feat: add collection controller endpoints"
```

### Task 7: Complete Integration Test Matrix for Permissions and Workflow

**Files:**
- Modify: `tests/EcoTrack.IntegrationTests/Collection/CollectionEndpointsTests.cs`

- [ ] **Step 1: Add role and transition tests from spec**

```csharp
[Fact]
public async Task CollectorCannotAssignPickup_ReturnsForbidden()
{
    await AuthenticateAsCollectorAsync();
    var pickupId = await CreatePickupAsAdminAsync();

    var response = await _client.PostAsJsonAsync($"/api/collection/pickups/{pickupId}/assign", new
    {
        assignedCollectorUserId = Guid.NewGuid(),
        note = "try assign"
    });

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}

[Fact]
public async Task InvalidTransition_SendToSegregationBeforeCollected_ReturnsConflict()
{
    await AuthenticateAsAdminAsync();
    var pickupId = await CreatePickupAsAdminAsync();

    var response = await _client.PostAsync($"/api/collection/pickups/{pickupId}/send-to-segregation", null);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
}
```

- [ ] **Step 2: Add assignment history and notes-update tests**

```csharp
[Fact]
public async Task Reassign_AppendsAssignmentHistoryEvents()
{
    await AuthenticateAsAdminAsync();
    var pickupId = await CreatePickupAsAdminAsync();
    var collectors = await GetCollectorUserIdsAsync();
    collectors.Should().HaveCountGreaterOrEqualTo(2);
    var firstCollector = collectors[0];
    var secondCollector = collectors[1];

    await _client.PostAsJsonAsync($"/api/collection/pickups/{pickupId}/assign", new { assignedCollectorUserId = firstCollector, note = "initial" });
    await _client.PostAsJsonAsync($"/api/collection/pickups/{pickupId}/assign", new { assignedCollectorUserId = secondCollector, note = "handover" });

    var history = await _client.GetFromJsonAsync<AssignmentHistoryContract>($"/api/collection/pickups/{pickupId}/assignment-history");
    history!.Events.Should().HaveCount(2);
}
```

- [ ] **Step 3: Run full collection integration suite**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~CollectionEndpointsTests"`
Expected: PASS.

- [ ] **Step 4: Commit integration test completion**

```bash
git add tests/EcoTrack.IntegrationTests/Collection/CollectionEndpointsTests.cs
git commit -m "test: add collection permissions transitions and history coverage"
```

### Task 8: Update API Documentation and Run Final Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add Collection endpoint entries to API table**

```markdown
| `GET` | `/api/collection/pickups` | Bearer | List pickups (status, paging, sorting) |
| `GET` | `/api/collection/pickups/{id}` | Bearer | Get pickup detail |
| `POST` | `/api/collection/pickups` | Admin | Create pickup |
| `PUT` | `/api/collection/pickups/{id}` | Bearer | Update pickup (admin full, collector notes via dedicated endpoint) |
| `PATCH` | `/api/collection/pickups/{id}/notes` | Collector | Update notes for own assigned pickup |
| `DELETE` | `/api/collection/pickups/{id}` | Admin | Soft delete (cancel) pickup |
| `POST` | `/api/collection/pickups/{id}/assign` | Admin | Assign or reassign collector |
| `POST` | `/api/collection/pickups/{id}/mark-collected` | Bearer | Mark pickup as collected |
| `POST` | `/api/collection/pickups/{id}/send-to-segregation` | Admin | Move collected pickup to segregation state |
| `GET` | `/api/collection/pickups/{id}/assignment-history` | Bearer | Assignment/reassignment timeline |
```

- [ ] **Step 2: Document collection list query parameters**

```markdown
`GET /api/collection/pickups` supports query params: `status`, `page`, `pageSize`, `sortBy`, `sortDirection`.

- `status`: `Scheduled`, `Assigned`, `Collected`, `SentToSegregation`, `Cancelled`
- `sortBy`: `scheduledAtUtc` only in v1
- `sortDirection`: `asc` or `desc`
```

- [ ] **Step 3: Run complete verification sequence**

Run: `dotnet test tests/EcoTrack.UnitTests`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~HealthEndpointTests"`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~CollectionEndpointsTests"`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests`
Expected: PASS (Docker Desktop required).

- [ ] **Step 4: Commit documentation and verification-ready state**

```bash
git add README.md
git commit -m "docs: add collection api endpoints and query documentation"
```

## Self-Review Checklist

- Spec coverage confirmed:
  - CRUD endpoints covered by Tasks 5 and 7.
  - Role behavior covered by Tasks 5 and 7.
  - Status transitions and conflicts covered by Tasks 2, 5, and 7.
  - Assignment history covered by Tasks 2, 5, and 7.
  - Soft delete/cancel covered by Tasks 2, 5, and 7.
  - List filtering/paging/sort validation covered by Tasks 5 and 7.
  - Persistence schema and migration covered by Task 3.
  - README updates covered by Task 8.
- Placeholder scan:
  - No TODO/TBD placeholders in tasks.
- Type consistency checks:
  - Contract names, endpoint routes, and service method names are consistent across tasks.
