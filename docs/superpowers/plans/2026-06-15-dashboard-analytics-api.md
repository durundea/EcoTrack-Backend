# Dashboard Analytics API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a single authenticated, role-aware dashboard analytics endpoint that returns KPI cards, category charts/table data, and pending approval summary for the v1 dashboard.

**Architecture:** Add one API action in a new analytics controller that delegates all business logic to a focused application service. The service will normalize date ranges, validate filters, apply role visibility, aggregate sales and inventory data with EF Core, and map to response contracts. CO2 factors will be provided through typed options bound from appsettings to keep formulas configurable without schema changes.

**Tech Stack:** ASP.NET Core Web API (.NET 10), Entity Framework Core 10, Options pattern, xUnit, FluentAssertions, Testcontainers PostgreSQL.

---

## Scope Check

The approved spec is a single coherent subsystem (dashboard read analytics) with one endpoint and one response payload. It does not need to be split into separate plans.

## File Structure

- Create: `src/EcoTrack.Api/Controllers/AnalyticsController.cs`
  - New authenticated endpoint `GET /api/analytics/dashboard`.
- Create: `src/EcoTrack.Application/Inventory/DashboardAnalyticsService.cs`
  - Range normalization, validation, role visibility, filter application, aggregation, and response mapping.
- Create: `src/EcoTrack.Application/Inventory/DashboardAnalyticsOptions.cs`
  - Typed options for category CO2 factors.
- Create: `src/EcoTrack.Application/Inventory/Contracts/GetDashboardAnalyticsQueryRequest.cs`
  - Query contract (`fromUtc`, `toUtc`, `wasteType`).
- Create: `src/EcoTrack.Application/Inventory/Contracts/DashboardAnalyticsResponse.cs`
  - Root response contract and nested sections (`range`, `kpis`, category arrays, pending approvals).
- Modify: `src/EcoTrack.Infrastructure/DependencyInjection.cs`
  - Register `DashboardAnalyticsService` and bind `DashboardAnalyticsOptions`.
- Modify: `src/EcoTrack.Api/appsettings.json`
  - Add `DashboardAnalytics` section with `Co2FactorsKgPerKgByCategory`.
- Modify: `src/EcoTrack.Api/appsettings.Development.json`
  - Add/override `DashboardAnalytics` section for local defaults.
- Create: `tests/EcoTrack.IntegrationTests/Inventory/DashboardAnalyticsEndpointsTests.cs`
  - Endpoint behavior tests (auth, role visibility, range defaults, filters, empty windows, pending approvals, bad requests).
- Modify: `tests/EcoTrack.UnitTests/EcoTrack.UnitTests.csproj`
  - Add project/package references needed to test application service behavior.
- Create: `tests/EcoTrack.UnitTests/Inventory/DashboardAnalyticsServiceTests.cs`
  - Formula-focused unit tests (efficiency denominator zero, CO2 factors, share percentages).
- Modify: `README.md`
  - Document dashboard endpoint and query params.

### Task 1: Add Failing Integration Tests for Dashboard Endpoint

**Files:**
- Create: `tests/EcoTrack.IntegrationTests/Inventory/DashboardAnalyticsEndpointsTests.cs`

- [ ] **Step 1: Write failing tests for the endpoint contract and key behaviors**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Inventory;

public class DashboardAnalyticsEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public DashboardAnalyticsEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDashboard_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/analytics/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDashboard_WithAdminToken_ReturnsPayloadShape()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<DashboardAnalyticsContract>();
        payload.Should().NotBeNull();
        payload!.Range.Should().NotBeNull();
        payload.Kpis.Should().NotBeNull();
        payload.WasteByCategory.Should().NotBeNull();
        payload.CategoryDistribution.Should().NotBeNull();
        payload.PendingSalesApprovals.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDashboard_WithCollectorToken_OnlyIncludesOwnSalesInTotals()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var collectorSaleResponse = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 2,
            soldAtUtc = DateTime.UtcNow
        });
        collectorSaleResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await AuthenticateAsAdminAsync();
        var adminSaleResponse = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 7,
            soldAtUtc = DateTime.UtcNow
        });
        adminSaleResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        await AuthenticateAsCollectorAsync();
        var collectorDashboard = await _client.GetFromJsonAsync<DashboardAnalyticsContract>("/api/analytics/dashboard");

        await AuthenticateAsAdminAsync();
        var adminDashboard = await _client.GetFromJsonAsync<DashboardAnalyticsContract>("/api/analytics/dashboard");

        collectorDashboard!.Kpis.TotalWasteProcessedKg.Should().BeLessThan(adminDashboard!.Kpis.TotalWasteProcessedKg);
    }

    [Fact]
    public async Task GetDashboard_WithoutRange_UsesLast30DaysLabel()
    {
        await AuthenticateAsAdminAsync();

        var payload = await _client.GetFromJsonAsync<DashboardAnalyticsContract>("/api/analytics/dashboard");

        payload!.Range.Label.Should().Be("Last 30 days");
        payload.Range.ToUtc.Should().BeAfter(payload.Range.FromUtc);
    }

    [Fact]
    public async Task GetDashboard_WithInvalidRange_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard?fromUtc=2026-06-15T00:00:00Z&toUtc=2026-06-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("rawWaste")]
    [InlineData("recycledProduct")]
    public async Task GetDashboard_WithSupportedWasteTypeValues_ReturnsOk(string wasteType)
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync($"/api/analytics/dashboard?wasteType={wasteType}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDashboard_WithUnsupportedWasteType_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard?wasteType=metal");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDashboard_WithEmptyDataWindow_ReturnsZerosAndEmptyCollections()
    {
        await AuthenticateAsAdminAsync();

        var payload = await _client.GetFromJsonAsync<DashboardAnalyticsContract>(
            "/api/analytics/dashboard?fromUtc=2000-01-01T00:00:00Z&toUtc=2000-01-02T00:00:00Z");

        payload!.Kpis.TotalWasteProcessedKg.Should().Be(0);
        payload.Kpis.RevenueInr.Should().Be(0);
        payload.WasteByCategory.Should().BeEmpty();
        payload.CategoryDistribution.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboard_PendingApprovalsCount_IsAccurate()
    {
        await AuthenticateAsCollectorAsync();
        var itemId = await GetFirstInventoryItemIdAsync();

        var sale = await _client.PostAsJsonAsync("/api/inventory/sales", new
        {
            inventoryItemId = itemId,
            quantitySold = 2,
            soldAtUtc = DateTime.UtcNow
        });
        sale.StatusCode.Should().Be(HttpStatusCode.Created);
        var saleRecord = await sale.Content.ReadFromJsonAsync<SaleContract>();
        var submit = await _client.PostAsync($"/api/inventory/sales/{saleRecord!.Id}/submit", null);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await _client.GetFromJsonAsync<DashboardAnalyticsContract>("/api/analytics/dashboard");

        payload!.PendingSalesApprovals.Count.Should().BeGreaterThanOrEqualTo(1);
        payload.PendingSalesApprovals.IsDataAvailable.Should().BeTrue();
        payload.PendingSalesApprovals.Message.Should().BeNull();
    }

    private async Task<Guid> GetFirstInventoryItemIdAsync()
    {
        var response = await _client.GetFromJsonAsync<List<InventoryItemContract>>("/api/inventory/items");
        return response!.First().Id;
    }

    private async Task AuthenticateAsCollectorAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "collector@ecotrack.local", password = "collector123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private async Task AuthenticateAsAdminAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "admin@ecotrack.local", password = "admin123" });
        var payload = await login.Content.ReadFromJsonAsync<AuthPayload>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
    }

    private sealed record AuthPayload(string Token);
    private sealed record InventoryItemContract(Guid Id, string Name, string Category, decimal QuantityKg, string Unit, decimal StandardPriceInr);
    private sealed record SaleContract(Guid Id, Guid InventoryItemId, int QuantitySold, decimal RevenueInr, string ApprovalStatus);

    private sealed record DashboardAnalyticsContract(
        DashboardRangeContract Range,
        DashboardKpisContract Kpis,
        List<CategoryMetricContract> WasteByCategory,
        List<CategoryMetricContract> CategoryDistribution,
        PendingSalesApprovalsContract PendingSalesApprovals);

    private sealed record DashboardRangeContract(DateTime FromUtc, DateTime ToUtc, string Label);
    private sealed record DashboardKpisContract(decimal TotalWasteProcessedKg, decimal RevenueInr, decimal RecyclingEfficiencyPercent, decimal Co2ReductionKg);
    private sealed record CategoryMetricContract(string Category, decimal WeightKg, decimal SharePercent);
    private sealed record PendingSalesApprovalsContract(int Count, bool IsDataAvailable, string? Message);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests"`
Expected: FAIL with 404 (endpoint not found) before implementation.

- [ ] **Step 3: Commit failing tests**

```bash
git add tests/EcoTrack.IntegrationTests/Inventory/DashboardAnalyticsEndpointsTests.cs
git commit -m "test: add failing integration tests for dashboard analytics endpoint"
```

### Task 2: Add Query and Response Contracts for Dashboard Analytics

**Files:**
- Create: `src/EcoTrack.Application/Inventory/Contracts/GetDashboardAnalyticsQueryRequest.cs`
- Create: `src/EcoTrack.Application/Inventory/Contracts/DashboardAnalyticsResponse.cs`

- [ ] **Step 1: Create query request contract**

```csharp
namespace EcoTrack.Application.Inventory.Contracts;

public sealed record GetDashboardAnalyticsQueryRequest(
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? WasteType);
```

- [ ] **Step 2: Create response contracts**

```csharp
namespace EcoTrack.Application.Inventory.Contracts;

public sealed record DashboardAnalyticsResponse(
    DashboardRangeResponse Range,
    DashboardKpisResponse Kpis,
    IReadOnlyList<DashboardCategoryMetricResponse> WasteByCategory,
    IReadOnlyList<DashboardCategoryMetricResponse> CategoryDistribution,
    PendingSalesApprovalsResponse PendingSalesApprovals);

public sealed record DashboardRangeResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    string Label);

public sealed record DashboardKpisResponse(
    decimal TotalWasteProcessedKg,
    decimal RevenueInr,
    decimal RecyclingEfficiencyPercent,
    decimal Co2ReductionKg);

public sealed record DashboardCategoryMetricResponse(
    string Category,
    decimal WeightKg,
    decimal SharePercent);

public sealed record PendingSalesApprovalsResponse(
    int Count,
    bool IsDataAvailable,
    string? Message);
```

- [ ] **Step 3: Run build to verify contracts compile**

Run: `dotnet build EcoTrack-Backend.slnx`
Expected: SUCCESS.

- [ ] **Step 4: Commit contracts**

```bash
git add src/EcoTrack.Application/Inventory/Contracts/GetDashboardAnalyticsQueryRequest.cs src/EcoTrack.Application/Inventory/Contracts/DashboardAnalyticsResponse.cs
git commit -m "feat: add dashboard analytics contracts"
```

### Task 3: Add Typed Options for CO2 Factors and Register Dependencies

**Files:**
- Create: `src/EcoTrack.Application/Inventory/DashboardAnalyticsOptions.cs`
- Modify: `src/EcoTrack.Infrastructure/DependencyInjection.cs`
- Modify: `src/EcoTrack.Api/appsettings.json`
- Modify: `src/EcoTrack.Api/appsettings.Development.json`

- [ ] **Step 1: Create options class for category factors**

```csharp
namespace EcoTrack.Application.Inventory;

public sealed class DashboardAnalyticsOptions
{
    public const string SectionName = "DashboardAnalytics";

    public Dictionary<string, decimal> Co2FactorsKgPerKgByCategory { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Register options and service in dependency injection**

```csharp
services.Configure<DashboardAnalyticsOptions>(
    configuration.GetSection(DashboardAnalyticsOptions.SectionName));
services.AddScoped<DashboardAnalyticsService>();
```

Add both lines in `AddInfrastructure` near existing service registrations.

- [ ] **Step 3: Add configuration in appsettings files**

`src/EcoTrack.Api/appsettings.json`:

```json
"DashboardAnalytics": {
  "Co2FactorsKgPerKgByCategory": {
    "RawWaste": 0.5,
    "RecycledProduct": 0.8
  }
}
```

`src/EcoTrack.Api/appsettings.Development.json`:

```json
"DashboardAnalytics": {
  "Co2FactorsKgPerKgByCategory": {
    "RawWaste": 0.5,
    "RecycledProduct": 0.8
  }
}
```

- [ ] **Step 4: Run build to verify options wiring compiles**

Run: `dotnet build EcoTrack-Backend.slnx`
Expected: SUCCESS.

- [ ] **Step 5: Commit dependency/config updates**

```bash
git add src/EcoTrack.Application/Inventory/DashboardAnalyticsOptions.cs src/EcoTrack.Infrastructure/DependencyInjection.cs src/EcoTrack.Api/appsettings.json src/EcoTrack.Api/appsettings.Development.json
git commit -m "feat: add dashboard analytics options and DI registration"
```

### Task 4: Implement Dashboard Analytics Service (Failing-to-Passing)

**Files:**
- Create: `src/EcoTrack.Application/Inventory/DashboardAnalyticsService.cs`

- [ ] **Step 1: Write minimal service skeleton and method signature**

```csharp
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using Microsoft.Extensions.Options;

namespace EcoTrack.Application.Inventory;

public class DashboardAnalyticsService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly DashboardAnalyticsOptions _options;

    public DashboardAnalyticsService(
        IApplicationDbContext dbContext,
        IOptions<DashboardAnalyticsOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public Task<DashboardAnalyticsResponse> GetDashboardAsync(
        GetDashboardAnalyticsQueryRequest request,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Implement validation, range normalization, filters, and aggregations**

```csharp
using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrack.Application.Inventory;

public class DashboardAnalyticsService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly DashboardAnalyticsOptions _options;

    public DashboardAnalyticsService(
        IApplicationDbContext dbContext,
        IOptions<DashboardAnalyticsOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<DashboardAnalyticsResponse> GetDashboardAsync(
        GetDashboardAnalyticsQueryRequest request,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var (fromUtc, toUtc, label) = NormalizeRange(request.FromUtc, request.ToUtc, now);

        var wasteTypeFilter = ParseWasteType(request.WasteType);

        var salesQuery = _dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.SoldAtUtc >= fromUtc && x.SoldAtUtc <= toUtc)
            .Where(x => x.ApprovalStatus == SaleApprovalStatus.Approved || x.ApprovalStatus == SaleApprovalStatus.PendingApproval);

        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            salesQuery = salesQuery.Where(x => x.RequestedByUserId == actorUserId);
        }

        var salesRows = await salesQuery
            .Join(_dbContext.InventoryItems.AsNoTracking(),
                sale => sale.InventoryItemId,
                item => item.Id,
                (sale, item) => new
                {
                    sale.QuantitySold,
                    sale.RevenueInr,
                    item.Category
                })
            .ToListAsync(cancellationToken);

        if (wasteTypeFilter.HasValue)
        {
            salesRows = salesRows
                .Where(x => x.Category == wasteTypeFilter.Value)
                .ToList();
        }

        var totalWasteProcessedKg = salesRows.Sum(x => (decimal)x.QuantitySold);
        var revenueInr = salesRows.Sum(x => x.RevenueInr);

        var grouped = salesRows
            .GroupBy(x => x.Category)
            .Select(g => new
            {
                Category = g.Key.ToString(),
                WeightKg = g.Sum(v => (decimal)v.QuantitySold)
            })
            .OrderByDescending(x => x.WeightKg)
            .ToList();

        var chartRows = grouped
            .Select(x => new DashboardCategoryMetricResponse(
                x.Category,
                x.WeightKg,
                totalWasteProcessedKg == 0 ? 0 : Math.Round((x.WeightKg / totalWasteProcessedKg) * 100m, 1)))
            .ToList();

        var inventoryQuery = _dbContext.InventoryItems.AsNoTracking().AsQueryable();
        if (wasteTypeFilter.HasValue)
        {
            inventoryQuery = inventoryQuery.Where(x => x.Category == wasteTypeFilter.Value);
        }

        var totalCollectedKgInRangeApprox = await inventoryQuery
            .SumAsync(x => (decimal?)x.QuantityKg, cancellationToken) ?? 0m;

        var recyclingEfficiencyPercent = totalCollectedKgInRangeApprox == 0m
            ? 0m
            : Math.Round((totalWasteProcessedKg / totalCollectedKgInRangeApprox) * 100m, 1);

        var co2ReductionKg = salesRows.Sum(x =>
        {
            var key = x.Category.ToString();
            var factor = _options.Co2FactorsKgPerKgByCategory.TryGetValue(key, out var configured)
                ? configured
                : 0m;
            return (decimal)x.QuantitySold * factor;
        });

        var pendingApprovalsQuery = _dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.ApprovalStatus == SaleApprovalStatus.PendingApproval);

        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            pendingApprovalsQuery = pendingApprovalsQuery.Where(x => x.RequestedByUserId == actorUserId);
        }

        var pendingCount = await pendingApprovalsQuery.CountAsync(cancellationToken);

        return new DashboardAnalyticsResponse(
            new DashboardRangeResponse(fromUtc, toUtc, label),
            new DashboardKpisResponse(totalWasteProcessedKg, revenueInr, recyclingEfficiencyPercent, Math.Round(co2ReductionKg, 1)),
            chartRows,
            chartRows,
            new PendingSalesApprovalsResponse(pendingCount, true, null));
    }

    private static (DateTime FromUtc, DateTime ToUtc, string Label) NormalizeRange(DateTime? fromUtc, DateTime? toUtc, DateTime nowUtc)
    {
        if (!fromUtc.HasValue && !toUtc.HasValue)
        {
            return (nowUtc.AddDays(-30), nowUtc, "Last 30 days");
        }

        if (fromUtc.HasValue && !toUtc.HasValue)
        {
            return (fromUtc.Value, fromUtc.Value.AddDays(30), "Custom range");
        }

        if (!fromUtc.HasValue && toUtc.HasValue)
        {
            return (toUtc.Value.AddDays(-30), toUtc.Value, "Custom range");
        }

        if (fromUtc!.Value > toUtc!.Value)
        {
            throw new BadRequestException("FromUtc must be less than or equal to ToUtc.");
        }

        return (fromUtc.Value, toUtc.Value, "Custom range");
    }

    private static InventoryCategory? ParseWasteType(string? wasteType)
    {
        if (string.IsNullOrWhiteSpace(wasteType) || string.Equals(wasteType, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(wasteType, "rawWaste", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryCategory.RawWaste;
        }

        if (string.Equals(wasteType, "recycledProduct", StringComparison.OrdinalIgnoreCase))
        {
            return InventoryCategory.RecycledProduct;
        }

        throw new BadRequestException("WasteType must be one of: all, rawWaste, recycledProduct.");
    }
}
```

- [ ] **Step 3: Run integration tests to verify endpoint still fails before controller wiring**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests.GetDashboard_WithAdminToken_ReturnsPayloadShape"`
Expected: FAIL with 404 until controller is added.

- [ ] **Step 4: Commit service implementation**

```bash
git add src/EcoTrack.Application/Inventory/DashboardAnalyticsService.cs
git commit -m "feat: implement dashboard analytics aggregation service"
```

### Task 5: Add Analytics Controller Endpoint and Connect to Service

**Files:**
- Create: `src/EcoTrack.Api/Controllers/AnalyticsController.cs`

- [ ] **Step 1: Add failing controller action wiring**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Inventory;
using EcoTrack.Application.Inventory.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Roles = "admin,collector")]
public class AnalyticsController : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardAnalyticsResponse>> GetDashboard(
        [FromServices] DashboardAnalyticsService service,
        [FromQuery] GetDashboardAnalyticsQueryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;
        var response = await service.GetDashboardAsync(request, userId, role, cancellationToken);
        return Ok(response);
    }
}
```

- [ ] **Step 2: Run endpoint integration test to verify pass for route availability**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests.GetDashboard_WithAdminToken_ReturnsPayloadShape"`
Expected: PASS.

- [ ] **Step 3: Commit controller**

```bash
git add src/EcoTrack.Api/Controllers/AnalyticsController.cs
git commit -m "feat: add dashboard analytics api endpoint"
```

### Task 6: Add Service Unit Tests for Formula Correctness

**Files:**
- Modify: `tests/EcoTrack.UnitTests/EcoTrack.UnitTests.csproj`
- Create: `tests/EcoTrack.UnitTests/Inventory/DashboardAnalyticsServiceTests.cs`

- [ ] **Step 1: Update unit test project references for service tests**

`tests/EcoTrack.UnitTests/EcoTrack.UnitTests.csproj` add:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\EcoTrack.Application\EcoTrack.Application.csproj" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.8" />
  <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
</ItemGroup>
```

- [ ] **Step 2: Add focused formula tests for service**

```csharp
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Inventory;
using EcoTrack.Application.Inventory.Contracts;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrack.UnitTests.Inventory;

public class DashboardAnalyticsServiceTests
{
    [Fact]
    public async Task GetDashboardAsync_WhenCollectedDenominatorIsZero_ReturnsZeroRecyclingEfficiency()
    {
        await using var db = CreateDbContext();
        db.InventoryItems.RemoveRange(db.InventoryItems);
        await db.SaveChangesAsync(CancellationToken.None);

        var service = CreateService(db, new Dictionary<string, decimal>
        {
            ["RawWaste"] = 0.5m,
            ["RecycledProduct"] = 0.8m
        });

        var result = await service.GetDashboardAsync(
            new GetDashboardAnalyticsQueryRequest(DateTime.UtcNow.AddDays(-2), DateTime.UtcNow, "all"),
            Guid.NewGuid(),
            UserRole.Admin.ToString(),
            CancellationToken.None);

        result.Kpis.RecyclingEfficiencyPercent.Should().Be(0);
    }

    [Fact]
    public async Task GetDashboardAsync_AppliesConfiguredCo2FactorByCategory()
    {
        await using var db = CreateDbContext();
        var item = db.InventoryItems.First(i => i.Category == InventoryCategory.RawWaste);

        var sale = SaleRecord.CreateDraft(item.Id, Guid.NewGuid(), 4, 100m, DateTime.UtcNow, DateTime.UtcNow);
        sale.SubmitForApproval(sale.RequestedByUserId, UserRole.Admin, DateTime.UtcNow);
        sale.Approve(Guid.NewGuid(), UserRole.Admin, DateTime.UtcNow);
        db.SaleRecords.Add(sale);
        await db.SaveChangesAsync(CancellationToken.None);

        var service = CreateService(db, new Dictionary<string, decimal>
        {
            ["RawWaste"] = 1.5m,
            ["RecycledProduct"] = 0.8m
        });

        var result = await service.GetDashboardAsync(
            new GetDashboardAnalyticsQueryRequest(DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(1), "rawWaste"),
            Guid.NewGuid(),
            UserRole.Admin.ToString(),
            CancellationToken.None);

        result.Kpis.Co2ReductionKg.Should().Be(6.0m);
    }

    [Fact]
    public async Task GetDashboardAsync_ComputesCategorySharePercentages()
    {
        await using var db = CreateDbContext();
        var raw = db.InventoryItems.First(i => i.Category == InventoryCategory.RawWaste);
        var recycled = db.InventoryItems.First(i => i.Category == InventoryCategory.RecycledProduct);

        var rawSale = SaleRecord.CreateDraft(raw.Id, Guid.NewGuid(), 3, 50m, DateTime.UtcNow, DateTime.UtcNow);
        rawSale.SubmitForApproval(rawSale.RequestedByUserId, UserRole.Admin, DateTime.UtcNow);
        rawSale.Approve(Guid.NewGuid(), UserRole.Admin, DateTime.UtcNow);

        var recycledSale = SaleRecord.CreateDraft(recycled.Id, Guid.NewGuid(), 1, 50m, DateTime.UtcNow, DateTime.UtcNow);
        recycledSale.SubmitForApproval(recycledSale.RequestedByUserId, UserRole.Admin, DateTime.UtcNow);
        recycledSale.Approve(Guid.NewGuid(), UserRole.Admin, DateTime.UtcNow);

        db.SaleRecords.AddRange(rawSale, recycledSale);
        await db.SaveChangesAsync(CancellationToken.None);

        var service = CreateService(db, new Dictionary<string, decimal>
        {
            ["RawWaste"] = 0.5m,
            ["RecycledProduct"] = 0.8m
        });

        var result = await service.GetDashboardAsync(
            new GetDashboardAnalyticsQueryRequest(DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(1), "all"),
            Guid.NewGuid(),
            UserRole.Admin.ToString(),
            CancellationToken.None);

        var rawRow = result.WasteByCategory.Single(x => x.Category == "RawWaste");
        var recycledRow = result.WasteByCategory.Single(x => x.Category == "RecycledProduct");

        rawRow.SharePercent.Should().Be(75.0m);
        recycledRow.SharePercent.Should().Be(25.0m);
    }

    private static DashboardAnalyticsService CreateService(TestDbContext db, Dictionary<string, decimal> factors)
    {
        var options = Options.Create(new DashboardAnalyticsOptions
        {
            Co2FactorsKgPerKgByCategory = factors
        });

        return new DashboardAnalyticsService(db, options);
    }

    private static TestDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var db = new TestDbContext(options);

        db.InventoryItems.AddRange(
            InventoryItem.Create("Raw", InventoryCategory.RawWaste, 100m, "kg", 10m, DateTime.UtcNow),
            InventoryItem.Create("Recycled", InventoryCategory.RecycledProduct, 50m, "kg", 20m, DateTime.UtcNow));

        db.SaveChanges();
        return db;
    }

    private sealed class TestDbContext : DbContext, IApplicationDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
        public DbSet<SaleRecord> SaleRecords => Set<SaleRecord>();
    }
}
```

- [ ] **Step 3: Run unit tests to verify they fail then pass after service implementation**

Run (initial): `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~DashboardAnalyticsServiceTests"`
Expected (before final service logic): FAIL.

Run (after logic): `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~DashboardAnalyticsServiceTests"`
Expected: PASS.

- [ ] **Step 4: Commit unit tests and project updates**

```bash
git add tests/EcoTrack.UnitTests/EcoTrack.UnitTests.csproj tests/EcoTrack.UnitTests/Inventory/DashboardAnalyticsServiceTests.cs
git commit -m "test: add dashboard analytics service unit tests"
```

### Task 7: Verify All Integration Behaviors and Fix Gaps

**Files:**
- Modify as needed based on failures:
  - `src/EcoTrack.Api/Controllers/AnalyticsController.cs`
  - `src/EcoTrack.Application/Inventory/DashboardAnalyticsService.cs`

- [ ] **Step 1: Run dashboard integration test suite**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests"`
Expected: PASS for all dashboard endpoint tests.

- [ ] **Step 2: If any test fails, apply minimal code correction**

Potential correction patterns:

```csharp
if (request.FromUtc.HasValue && request.ToUtc.HasValue && request.FromUtc > request.ToUtc)
{
    throw new BadRequestException("FromUtc must be less than or equal to ToUtc.");
}
```

```csharp
if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
{
    salesQuery = salesQuery.Where(x => x.RequestedByUserId == actorUserId);
}
```

```csharp
var pendingCount = await pendingApprovalsQuery.CountAsync(cancellationToken);
```

- [ ] **Step 3: Re-run dashboard integration suite to confirm pass**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests"`
Expected: PASS.

- [ ] **Step 4: Commit final endpoint fixes**

```bash
git add src/EcoTrack.Api/Controllers/AnalyticsController.cs src/EcoTrack.Application/Inventory/DashboardAnalyticsService.cs
git commit -m "fix: finalize dashboard analytics endpoint behavior"
```

### Task 8: Update Documentation and Run Full Regression Set

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add dashboard analytics endpoint docs**

Add to endpoint table:

```markdown
| `GET` | `/api/analytics/dashboard` | Admin, Collector | Dashboard KPIs, category charts/table, and pending approvals summary |
```

Add query parameter note:

```markdown
`GET /api/analytics/dashboard` supports query params: `fromUtc`, `toUtc`, `wasteType` where `wasteType` is `all`, `rawWaste`, or `recycledProduct`.
Default date behavior: missing both -> last 30 days; missing one bound -> inferred 30-day window.
```

- [ ] **Step 2: Run targeted regression tests**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~DashboardAnalyticsServiceTests"`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests|FullyQualifiedName~SalesEndpointsTests|FullyQualifiedName~InventoryEndpointsTests|FullyQualifiedName~AuthEndpointsTests|FullyQualifiedName~HealthEndpointTests"`
Expected: PASS.

- [ ] **Step 3: Commit documentation and verification completion**

```bash
git add README.md
git commit -m "docs: add dashboard analytics api documentation"
```

## Self-Review

### 1. Spec Coverage Check

- Endpoint and consolidated payload: covered by Task 1, Task 2, Task 5.
- Auth and role visibility rules (admin vs collector): covered by Task 1 tests and Task 4 logic.
- Query filters (`fromUtc`, `toUtc`, `wasteType`) and default range behavior: covered by Task 1 tests and Task 4 range/filter implementation.
- Formula rules (total waste, revenue, recycling efficiency with denominator zero handling, CO2 factors): covered by Task 4 implementation and Task 6 unit tests.
- Waste/category chart + table shared grouped dataset: covered by Task 4 (`chartRows` used for both arrays).
- Pending approvals card count and fixed availability/message: covered by Task 1 and Task 4.
- Error behavior for invalid range and wasteType: covered by Task 1 and Task 4.
- No schema/migration changes: upheld (no migration tasks present).
- README update: covered by Task 8.

No spec gaps found.

### 2. Placeholder Scan

Checked for placeholders (`TBD`, `TODO`, vague “handle edge cases”, “similar to task N”). None present.

### 3. Type/Name Consistency Check

Consistent names used across tasks:
- `GetDashboardAnalyticsQueryRequest`
- `DashboardAnalyticsResponse`
- `DashboardAnalyticsService.GetDashboardAsync(...)`
- `DashboardAnalyticsOptions`
- `GetDashboard` action in analytics controller

No naming conflicts found.
# Dashboard Analytics API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a single authenticated dashboard analytics endpoint that returns KPI cards, category breakdowns, and pending approvals for the current dashboard page.

**Architecture:** Add a dedicated analytics application service that owns query normalization, role-aware filtering, and aggregation logic, then expose it through a new analytics controller endpoint. Keep analytics contracts and options strongly typed so formula inputs (especially CO2 factors) are validated at startup and reusable. Use integration tests for end-to-end behavior and service-level unit tests for formula correctness and edge cases.

**Tech Stack:** ASP.NET Core Web API (.NET 10), EF Core 10, options pattern (`IOptions<T>`), xUnit, FluentAssertions, Testcontainers PostgreSQL.

---

## File Structure

- Create: `src/EcoTrack.Api/Controllers/AnalyticsController.cs`
  - Adds `GET /api/analytics/dashboard` endpoint restricted to admin and collector roles.
- Create: `src/EcoTrack.Application/Analytics/DashboardAnalyticsService.cs`
  - Implements range normalization, filter validation, role visibility, aggregations, and response mapping.
- Create: `src/EcoTrack.Application/Analytics/Contracts/GetDashboardAnalyticsQueryRequest.cs`
  - Query contract for `fromUtc`, `toUtc`, and `wasteType`.
- Create: `src/EcoTrack.Application/Analytics/Contracts/DashboardAnalyticsResponse.cs`
  - Response payload contract for range, KPIs, category collections, and pending approvals.
- Create: `src/EcoTrack.Application/Analytics/Contracts/DashboardAnalyticsWasteCategoryBreakdownResponse.cs`
  - Shared category row contract (`category`, `weightKg`, `sharePercent`).
- Create: `src/EcoTrack.Infrastructure/Analytics/Co2ReductionOptions.cs`
  - Typed options for CO2 reduction factors by inventory category.
- Modify: `src/EcoTrack.Infrastructure/DependencyInjection.cs`
  - Register `DashboardAnalyticsService` and bind `Co2ReductionOptions`.
- Modify: `src/EcoTrack.Api/appsettings.json`
  - Add default `Analytics:Co2FactorsKgPerKg` configuration section.
- Modify: `tests/EcoTrack.IntegrationTests/Inventory/SalesEndpointsTests.cs`
  - Keep existing tests; no changes needed here.
- Create: `tests/EcoTrack.IntegrationTests/Analytics/DashboardAnalyticsEndpointsTests.cs`
  - Covers endpoint authorization, default range, range validation, wasteType filter behavior, empty windows, role visibility, and pending approvals count.
- Modify: `tests/EcoTrack.UnitTests/EcoTrack.UnitTests.csproj`
  - Add project/package references needed for application service unit tests.
- Create: `tests/EcoTrack.UnitTests/Analytics/DashboardAnalyticsServiceTests.cs`
  - Covers denominator zero efficiency, CO2 factor application, and share percent calculations.
- Modify: `README.md`
  - Add dashboard endpoint docs and query parameter details.

### Task 1: Add Failing Integration Tests for Dashboard Endpoint Contract

**Files:**
- Create: `tests/EcoTrack.IntegrationTests/Analytics/DashboardAnalyticsEndpointsTests.cs`

- [ ] **Step 1: Write failing integration tests for endpoint shape, role visibility, filters, and error behavior**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace EcoTrack.IntegrationTests.Analytics;

public class DashboardAnalyticsEndpointsTests : IClassFixture<IntegrationTestWebAppFactory>
{
    private readonly HttpClient _client;

    public DashboardAnalyticsEndpointsTests(IntegrationTestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDashboard_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/analytics/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDashboard_WithAdminToken_ReturnsPayloadShape()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<DashboardAnalyticsContract>();
        payload.Should().NotBeNull();
        payload!.Range.Should().NotBeNull();
        payload.Kpis.Should().NotBeNull();
        payload.WasteByCategory.Should().NotBeNull();
        payload.CategoryDistribution.Should().NotBeNull();
        payload.PendingSalesApprovals.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDashboard_DefaultRange_UsesLast30DaysWindow()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<DashboardAnalyticsContract>();
        payload.Should().NotBeNull();

        var spanDays = (payload!.Range.ToUtc - payload.Range.FromUtc).TotalDays;
        spanDays.Should().BeApproximately(30, 0.2);
        payload.Range.Label.Should().Be("Last 30 days");
    }

    [Fact]
    public async Task GetDashboard_WithInvalidRange_ReturnsBadRequest()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard?fromUtc=2026-06-15T00:00:00Z&toUtc=2026-06-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("rawWaste")]
    [InlineData("recycledProduct")]
    public async Task GetDashboard_WithSupportedWasteType_ReturnsOk(string wasteType)
    {
        await AuthenticateAsCollectorAsync();

        var response = await _client.GetAsync($"/api/analytics/dashboard?wasteType={wasteType}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDashboard_WithUnsupportedWasteType_ReturnsBadRequest()
    {
        await AuthenticateAsCollectorAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard?wasteType=glass");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDashboard_Collector_OnlySeesOwnSalesInAggregates()
    {
        await AuthenticateAsCollectorAsync();
        var collectorResponse = await _client.GetAsync("/api/analytics/dashboard");
        collectorResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var collectorPayload = await collectorResponse.Content.ReadFromJsonAsync<DashboardAnalyticsContract>();

        await AuthenticateAsAdminAsync();
        var adminResponse = await _client.GetAsync("/api/analytics/dashboard");
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminPayload = await adminResponse.Content.ReadFromJsonAsync<DashboardAnalyticsContract>();

        adminPayload!.Kpis.TotalWasteProcessedKg.Should().BeGreaterThanOrEqualTo(collectorPayload!.Kpis.TotalWasteProcessedKg);
    }

    [Fact]
    public async Task GetDashboard_EmptyWindow_ReturnsZerosAndEmptyCollections()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard?fromUtc=1990-01-01T00:00:00Z&toUtc=1990-01-02T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<DashboardAnalyticsContract>();
        payload.Should().NotBeNull();
        payload!.Kpis.TotalWasteProcessedKg.Should().Be(0);
        payload.Kpis.RevenueInr.Should().Be(0);
        payload.Kpis.RecyclingEfficiencyPercent.Should().Be(0);
        payload.Kpis.Co2ReductionKg.Should().Be(0);
        payload.WasteByCategory.Should().BeEmpty();
        payload.CategoryDistribution.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDashboard_PendingApprovalsCount_IsReturned()
    {
        await AuthenticateAsAdminAsync();

        var response = await _client.GetAsync("/api/analytics/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<DashboardAnalyticsContract>();
        payload.Should().NotBeNull();
        payload!.PendingSalesApprovals.Count.Should().BeGreaterThanOrEqualTo(0);
        payload.PendingSalesApprovals.IsDataAvailable.Should().BeTrue();
        payload.PendingSalesApprovals.Message.Should().BeNull();
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

    private sealed record DashboardAnalyticsContract(
        DashboardRangeContract Range,
        DashboardKpisContract Kpis,
        List<DashboardCategoryBreakdownContract> WasteByCategory,
        List<DashboardCategoryBreakdownContract> CategoryDistribution,
        PendingSalesApprovalsContract PendingSalesApprovals);

    private sealed record DashboardRangeContract(DateTime FromUtc, DateTime ToUtc, string Label);

    private sealed record DashboardKpisContract(
        decimal TotalWasteProcessedKg,
        decimal RevenueInr,
        decimal RecyclingEfficiencyPercent,
        decimal Co2ReductionKg);

    private sealed record DashboardCategoryBreakdownContract(string Category, decimal WeightKg, decimal SharePercent);

    private sealed record PendingSalesApprovalsContract(int Count, bool IsDataAvailable, string? Message);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests"`
Expected: FAIL with 404 for `/api/analytics/dashboard` and/or deserialization failures.

- [ ] **Step 3: Commit failing integration tests**

```bash
git add tests/EcoTrack.IntegrationTests/Analytics/DashboardAnalyticsEndpointsTests.cs
git commit -m "test: add failing dashboard analytics integration tests"
```

### Task 2: Add Analytics Contracts and CO2 Options Wiring

**Files:**
- Create: `src/EcoTrack.Application/Analytics/Contracts/GetDashboardAnalyticsQueryRequest.cs`
- Create: `src/EcoTrack.Application/Analytics/Contracts/DashboardAnalyticsResponse.cs`
- Create: `src/EcoTrack.Application/Analytics/Contracts/DashboardAnalyticsWasteCategoryBreakdownResponse.cs`
- Create: `src/EcoTrack.Infrastructure/Analytics/Co2ReductionOptions.cs`
- Modify: `src/EcoTrack.Infrastructure/DependencyInjection.cs`
- Modify: `src/EcoTrack.Api/appsettings.json`

- [ ] **Step 1: Add analytics query contract**

```csharp
namespace EcoTrack.Application.Analytics.Contracts;

public sealed record GetDashboardAnalyticsQueryRequest(
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? WasteType);
```

- [ ] **Step 2: Add analytics response contracts**

```csharp
namespace EcoTrack.Application.Analytics.Contracts;

public sealed record DashboardAnalyticsResponse(
    DashboardAnalyticsRangeResponse Range,
    DashboardAnalyticsKpisResponse Kpis,
    IReadOnlyList<DashboardAnalyticsWasteCategoryBreakdownResponse> WasteByCategory,
    IReadOnlyList<DashboardAnalyticsWasteCategoryBreakdownResponse> CategoryDistribution,
    DashboardPendingSalesApprovalsResponse PendingSalesApprovals);

public sealed record DashboardAnalyticsRangeResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    string Label);

public sealed record DashboardAnalyticsKpisResponse(
    decimal TotalWasteProcessedKg,
    decimal RevenueInr,
    decimal RecyclingEfficiencyPercent,
    decimal Co2ReductionKg);

public sealed record DashboardPendingSalesApprovalsResponse(
    int Count,
    bool IsDataAvailable,
    string? Message);
```

```csharp
namespace EcoTrack.Application.Analytics.Contracts;

public sealed record DashboardAnalyticsWasteCategoryBreakdownResponse(
    string Category,
    decimal WeightKg,
    decimal SharePercent);
```

- [ ] **Step 3: Add typed options and register in DI/config**

```csharp
namespace EcoTrack.Infrastructure.Analytics;

public sealed class Co2ReductionOptions
{
    public const string SectionName = "Analytics:Co2FactorsKgPerKg";

    public decimal RawWaste { get; init; }
    public decimal RecycledProduct { get; init; }
}
```

```csharp
using EcoTrack.Application.Analytics;
using EcoTrack.Infrastructure.Analytics;

// inside AddInfrastructure(...)
services.Configure<Co2ReductionOptions>(configuration.GetSection(Co2ReductionOptions.SectionName));
services.AddScoped<DashboardAnalyticsService>();
```

```json
{
  "Analytics": {
    "Co2FactorsKgPerKg": {
      "RawWaste": 0.4,
      "RecycledProduct": 0.7
    }
  }
}
```

- [ ] **Step 4: Run build to verify contracts/options compile**

Run: `dotnet build EcoTrack-Backend.slnx`
Expected: SUCCESS or next expected failures from unimplemented analytics service/controller.

- [ ] **Step 5: Commit contracts and options wiring**

```bash
git add src/EcoTrack.Application/Analytics/Contracts src/EcoTrack.Infrastructure/Analytics/Co2ReductionOptions.cs src/EcoTrack.Infrastructure/DependencyInjection.cs src/EcoTrack.Api/appsettings.json
git commit -m "feat: add dashboard analytics contracts and co2 options wiring"
```

### Task 3: Enable and Add Failing Service-Level Unit Tests

**Files:**
- Modify: `tests/EcoTrack.UnitTests/EcoTrack.UnitTests.csproj`
- Create: `tests/EcoTrack.UnitTests/Analytics/DashboardAnalyticsServiceTests.cs`

- [ ] **Step 1: Add unit-test project references needed for application service tests**

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.8" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\..\src\EcoTrack.Application\EcoTrack.Application.csproj" />
  <ProjectReference Include="..\..\src\EcoTrack.Infrastructure\EcoTrack.Infrastructure.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Add failing analytics formula tests**

```csharp
using EcoTrack.Application.Analytics;
using EcoTrack.Application.Analytics.Contracts;
using EcoTrack.Domain.Inventory;
using EcoTrack.Infrastructure.Analytics;
using EcoTrack.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrack.UnitTests.Analytics;

public class DashboardAnalyticsServiceTests
{
    [Fact]
    public async Task GetDashboard_WhenCollectedInventoryIsZero_ReturnsZeroRecyclingEfficiency()
    {
        await using var db = CreateDbContext();
        SeedSale(db, InventoryCategory.RawWaste, quantitySold: 10, revenueInr: 120m, SaleApprovalStatus.PendingApproval);

        var service = CreateService(db);

        var result = await service.GetDashboardAsync(new GetDashboardAnalyticsQueryRequest(null, null, "all"), Guid.NewGuid(), "admin", CancellationToken.None);

        result.Kpis.RecyclingEfficiencyPercent.Should().Be(0);
    }

    [Fact]
    public async Task GetDashboard_AppliesCo2FactorPerCategory()
    {
        await using var db = CreateDbContext();
        SeedInventory(db, InventoryCategory.RawWaste, 100m);
        SeedInventory(db, InventoryCategory.RecycledProduct, 100m);
        SeedSale(db, InventoryCategory.RawWaste, quantitySold: 10, revenueInr: 100m, SaleApprovalStatus.Approved);
        SeedSale(db, InventoryCategory.RecycledProduct, quantitySold: 10, revenueInr: 100m, SaleApprovalStatus.PendingApproval);

        var service = CreateService(db, rawWasteFactor: 0.5m, recycledProductFactor: 0.8m);

        var result = await service.GetDashboardAsync(new GetDashboardAnalyticsQueryRequest(null, null, "all"), Guid.NewGuid(), "admin", CancellationToken.None);

        result.Kpis.Co2ReductionKg.Should().Be(13.0m);
    }

    [Fact]
    public async Task GetDashboard_ComputesSharePercentFromGroupedWeights()
    {
        await using var db = CreateDbContext();
        SeedInventory(db, InventoryCategory.RawWaste, 100m);
        SeedInventory(db, InventoryCategory.RecycledProduct, 100m);
        SeedSale(db, InventoryCategory.RawWaste, quantitySold: 30, revenueInr: 300m, SaleApprovalStatus.Approved);
        SeedSale(db, InventoryCategory.RecycledProduct, quantitySold: 70, revenueInr: 700m, SaleApprovalStatus.PendingApproval);

        var service = CreateService(db);

        var result = await service.GetDashboardAsync(new GetDashboardAnalyticsQueryRequest(null, null, "all"), Guid.NewGuid(), "admin", CancellationToken.None);

        result.WasteByCategory.Should().HaveCount(2);
        result.WasteByCategory.Single(x => x.Category == "RawWaste").SharePercent.Should().Be(30.0m);
        result.WasteByCategory.Single(x => x.Category == "RecycledProduct").SharePercent.Should().Be(70.0m);
    }

    private static DashboardAnalyticsService CreateService(
        AppDbContext db,
        decimal rawWasteFactor = 0.4m,
        decimal recycledProductFactor = 0.7m)
    {
        var options = Options.Create(new Co2ReductionOptions
        {
            RawWaste = rawWasteFactor,
            RecycledProduct = recycledProductFactor,
        });

        return new DashboardAnalyticsService(db, options);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    private static void SeedInventory(AppDbContext db, InventoryCategory category, decimal quantityKg)
    {
        db.InventoryItems.Add(InventoryItem.Create(
            name: $"item-{Guid.NewGuid():N}",
            category: category,
            quantityKg: quantityKg,
            unit: "kg",
            standardPriceInr: 10m,
            createdAtUtc: DateTime.UtcNow));

        db.SaveChanges();
    }

    private static void SeedSale(AppDbContext db, InventoryCategory category, int quantitySold, decimal revenueInr, SaleApprovalStatus status)
    {
        var item = InventoryItem.Create($"sale-item-{Guid.NewGuid():N}", category, 100m, "kg", 10m, DateTime.UtcNow);
        db.InventoryItems.Add(item);

        var userId = Guid.NewGuid();
        var sale = SaleRecord.CreateDraft(item.Id, userId, quantitySold, revenueInr, DateTime.UtcNow, DateTime.UtcNow);
        if (status is SaleApprovalStatus.PendingApproval or SaleApprovalStatus.Approved)
        {
            sale.SubmitForApproval(userId, Domain.Auth.UserRole.Admin, DateTime.UtcNow);
        }

        if (status is SaleApprovalStatus.Approved)
        {
            sale.Approve(Guid.NewGuid(), Domain.Auth.UserRole.Admin, DateTime.UtcNow);
        }

        db.SaleRecords.Add(sale);
        db.SaveChanges();
    }
}
```

- [ ] **Step 3: Run unit tests to verify they fail**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~DashboardAnalyticsServiceTests"`
Expected: FAIL with missing `DashboardAnalyticsService`/contracts until implementation is added.

- [ ] **Step 4: Commit failing unit test setup and tests**

```bash
git add tests/EcoTrack.UnitTests/EcoTrack.UnitTests.csproj tests/EcoTrack.UnitTests/Analytics/DashboardAnalyticsServiceTests.cs
git commit -m "test: add failing dashboard analytics service unit tests"
```

### Task 4: Implement DashboardAnalyticsService Aggregation Logic

**Files:**
- Create: `src/EcoTrack.Application/Analytics/DashboardAnalyticsService.cs`

- [ ] **Step 1: Implement minimal service to make unit tests compile and pass**

```csharp
using EcoTrack.Application.Common.Exceptions;
using EcoTrack.Application.Common.Interfaces;
using EcoTrack.Application.Analytics.Contracts;
using EcoTrack.Domain.Auth;
using EcoTrack.Domain.Inventory;
using EcoTrack.Infrastructure.Analytics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrack.Application.Analytics;

public class DashboardAnalyticsService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly Co2ReductionOptions _co2Options;

    public DashboardAnalyticsService(
        IApplicationDbContext dbContext,
        IOptions<Co2ReductionOptions> co2Options)
    {
        _dbContext = dbContext;
        _co2Options = co2Options.Value;
    }

    public async Task<DashboardAnalyticsResponse> GetDashboardAsync(
        GetDashboardAnalyticsQueryRequest request,
        Guid actorUserId,
        string actorRole,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc, label) = NormalizeRange(request.FromUtc, request.ToUtc);
        var wasteTypeFilter = ParseWasteType(request.WasteType);

        var salesQuery = _dbContext.SaleRecords.AsNoTracking()
            .Where(x => x.SoldAtUtc >= fromUtc && x.SoldAtUtc <= toUtc)
            .Where(x => x.ApprovalStatus == SaleApprovalStatus.Approved || x.ApprovalStatus == SaleApprovalStatus.PendingApproval);

        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            salesQuery = salesQuery.Where(x => x.RequestedByUserId == actorUserId);
        }

        var joinedSales = from sale in salesQuery
                          join item in _dbContext.InventoryItems.AsNoTracking() on sale.InventoryItemId equals item.Id
                          select new
                          {
                              item.Category,
                              sale.QuantitySold,
                              sale.RevenueInr,
                          };

        if (wasteTypeFilter.HasValue)
        {
            joinedSales = joinedSales.Where(x => x.Category == wasteTypeFilter.Value);
        }

        var grouped = await joinedSales
            .GroupBy(x => x.Category)
            .Select(g => new
            {
                Category = g.Key,
                WeightKg = g.Sum(x => (decimal)x.QuantitySold),
                RevenueInr = g.Sum(x => x.RevenueInr)
            })
            .ToListAsync(cancellationToken);

        var totalWasteProcessedKg = grouped.Sum(x => x.WeightKg);
        var revenueInr = grouped.Sum(x => x.RevenueInr);

        var inventoryQuery = _dbContext.InventoryItems.AsNoTracking();
        if (wasteTypeFilter.HasValue)
        {
            inventoryQuery = inventoryQuery.Where(x => x.Category == wasteTypeFilter.Value);
        }

        var totalCollectedKg = await inventoryQuery.SumAsync(x => x.QuantityKg, cancellationToken);
        var recyclingEfficiencyPercent = totalCollectedKg <= 0
            ? 0
            : Math.Round((totalWasteProcessedKg / totalCollectedKg) * 100m, 2, MidpointRounding.AwayFromZero);

        var co2ReductionKg = grouped.Sum(x => x.WeightKg * GetCo2Factor(x.Category));

        var breakdown = BuildCategoryBreakdown(grouped, totalWasteProcessedKg);

        var pendingApprovalsQuery = _dbContext.SaleRecords.AsNoTracking()
            .Where(x => x.ApprovalStatus == SaleApprovalStatus.PendingApproval);

        if (!string.Equals(actorRole, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            pendingApprovalsQuery = pendingApprovalsQuery.Where(x => x.RequestedByUserId == actorUserId);
        }

        var pendingCount = await pendingApprovalsQuery.CountAsync(cancellationToken);

        return new DashboardAnalyticsResponse(
            new DashboardAnalyticsRangeResponse(fromUtc, toUtc, label),
            new DashboardAnalyticsKpisResponse(totalWasteProcessedKg, revenueInr, recyclingEfficiencyPercent, co2ReductionKg),
            breakdown,
            breakdown,
            new DashboardPendingSalesApprovalsResponse(pendingCount, true, null));
    }

    private static (DateTime FromUtc, DateTime ToUtc, string Label) NormalizeRange(DateTime? fromUtc, DateTime? toUtc)
    {
        if (!fromUtc.HasValue && !toUtc.HasValue)
        {
            var end = DateTime.UtcNow;
            var start = end.AddDays(-30);
            return (start, end, "Last 30 days");
        }

        if (fromUtc.HasValue && !toUtc.HasValue)
        {
            return (fromUtc.Value, fromUtc.Value.AddDays(30), "Custom");
        }

        if (!fromUtc.HasValue && toUtc.HasValue)
        {
            return (toUtc.Value.AddDays(-30), toUtc.Value, "Custom");
        }

        if (fromUtc > toUtc)
        {
            throw new BadRequestException("fromUtc must be less than or equal to toUtc.");
        }

        return (fromUtc!.Value, toUtc!.Value, "Custom");
    }

    private static InventoryCategory? ParseWasteType(string? wasteType)
    {
        if (string.IsNullOrWhiteSpace(wasteType) || wasteType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return wasteType.ToLowerInvariant() switch
        {
            "rawwaste" => InventoryCategory.RawWaste,
            "recycledproduct" => InventoryCategory.RecycledProduct,
            _ => throw new BadRequestException("wasteType must be one of: all, rawWaste, recycledProduct."),
        };
    }

    private decimal GetCo2Factor(InventoryCategory category) =>
        category switch
        {
            InventoryCategory.RawWaste => _co2Options.RawWaste,
            InventoryCategory.RecycledProduct => _co2Options.RecycledProduct,
            _ => 0,
        };

    private static IReadOnlyList<DashboardAnalyticsWasteCategoryBreakdownResponse> BuildCategoryBreakdown(
        IEnumerable<dynamic> grouped,
        decimal totalWeightKg)
    {
        if (totalWeightKg <= 0)
        {
            return Array.Empty<DashboardAnalyticsWasteCategoryBreakdownResponse>();
        }

        return grouped
            .Select(x => new DashboardAnalyticsWasteCategoryBreakdownResponse(
                x.Category.ToString(),
                x.WeightKg,
                Math.Round((x.WeightKg / totalWeightKg) * 100m, 2, MidpointRounding.AwayFromZero)))
            .OrderByDescending(x => x.WeightKg)
            .ToList();
    }
}
```

- [ ] **Step 2: Run unit tests to verify service formulas pass**

Run: `dotnet test tests/EcoTrack.UnitTests --filter "FullyQualifiedName~DashboardAnalyticsServiceTests"`
Expected: PASS.

- [ ] **Step 3: Commit service implementation**

```bash
git add src/EcoTrack.Application/Analytics/DashboardAnalyticsService.cs
git commit -m "feat: implement dashboard analytics service aggregations"
```

### Task 5: Add Analytics Controller Endpoint and Role Authorization

**Files:**
- Create: `src/EcoTrack.Api/Controllers/AnalyticsController.cs`

- [ ] **Step 1: Implement endpoint action using new service and query contract**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EcoTrack.Application.Analytics;
using EcoTrack.Application.Analytics.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrack.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize(Roles = "admin,collector")]
public class AnalyticsController : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardAnalyticsResponse>> GetDashboard(
        [FromServices] DashboardAnalyticsService service,
        [FromQuery] GetDashboardAnalyticsQueryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;

        var result = await service.GetDashboardAsync(request, userId, role, cancellationToken);
        return Ok(result);
    }
}
```

- [ ] **Step 2: Run integration tests to verify endpoint exists and basic auth behavior passes**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests.GetDashboard_WithoutToken_ReturnsUnauthorized|FullyQualifiedName~DashboardAnalyticsEndpointsTests.GetDashboard_WithAdminToken_ReturnsPayloadShape"`
Expected: PASS.

- [ ] **Step 3: Commit controller endpoint**

```bash
git add src/EcoTrack.Api/Controllers/AnalyticsController.cs
git commit -m "feat: add dashboard analytics endpoint"
```

### Task 6: Finish Range and Filter Behavior Until Integration Tests Pass

**Files:**
- Modify: `src/EcoTrack.Application/Analytics/DashboardAnalyticsService.cs`
- Modify: `tests/EcoTrack.IntegrationTests/Analytics/DashboardAnalyticsEndpointsTests.cs` (only if assertions need minor alignment to seeded data behavior)

- [ ] **Step 1: Add missing behavior required by integration tests (if any fail)**

```csharp
// Keep this behavior in GetDashboardAsync:
// 1) fromUtc/toUtc default + inference with 30-day window
// 2) reject fromUtc > toUtc with BadRequestException
// 3) wasteType accepts only all/rawWaste/recycledProduct
// 4) include only Approved and PendingApproval in KPI and category aggregations
// 5) pending approvals card counts PendingApproval under role visibility
```

- [ ] **Step 2: Run integration suite for dashboard endpoint**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests"`
Expected: PASS.

- [ ] **Step 3: Commit behavior fixes from test feedback**

```bash
git add src/EcoTrack.Application/Analytics/DashboardAnalyticsService.cs tests/EcoTrack.IntegrationTests/Analytics/DashboardAnalyticsEndpointsTests.cs
git commit -m "fix: align dashboard analytics behavior with endpoint contract"
```

### Task 7: Update README and Run Full Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Document new dashboard endpoint and query params**

```markdown
| `GET` | `/api/analytics/dashboard` | Admin, Collector | Dashboard KPIs, category charts/table, pending approvals |
```

```markdown
`GET /api/analytics/dashboard` supports query params: `fromUtc`, `toUtc`, `wasteType`.

- `wasteType`: `all` (default), `rawWaste`, `recycledProduct`
- Default range: last 30 days when both bounds are omitted
- If one bound is omitted, the other is inferred using a 30-day window
```

- [ ] **Step 2: Run full tests for regression safety**

Run: `dotnet test tests/EcoTrack.UnitTests`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~HealthEndpointTests"`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~DashboardAnalyticsEndpointsTests"`
Expected: PASS (Docker Desktop required).

- [ ] **Step 3: Commit docs and final verification state**

```bash
git add README.md
git commit -m "docs: add dashboard analytics endpoint documentation"
```

## Self-Review

- Spec coverage check:
  - Single consolidated endpoint (`GET /api/analytics/dashboard`) is covered by Tasks 1, 5, and 6.
  - Roles, visibility, and supported query filters are covered by Tasks 1 and 6.
  - Default/inferred range behavior and invalid range handling are covered by Tasks 1 and 6.
  - KPI formulas and category share logic are covered by Tasks 3 and 4.
  - Pending approvals summary behavior is covered by Tasks 1 and 4.
  - Integration and unit testing requirements are covered by Tasks 1, 3, 5, 6, and 7.
  - README update requirement is covered by Task 7.
- Placeholder scan:
  - Removed all TODO/TBD placeholders; each task has explicit files, code snippets, commands, and commit messages.
- Type consistency check:
  - Contract and method names are consistent across tasks: `GetDashboardAnalyticsQueryRequest`, `DashboardAnalyticsResponse`, `DashboardAnalyticsService.GetDashboardAsync`, `AnalyticsController.GetDashboard`.

