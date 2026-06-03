# Sales Read APIs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add authenticated sales read endpoints with role-aware visibility, full filtering, paging, sorting, tests, and docs.

**Architecture:** Keep the existing layered architecture: controller delegates to application service, service performs query composition and role visibility checks, and response contracts stay in the application contracts namespace. Validation errors that cannot be expressed through model binding will use a dedicated bad-request exception mapped by API middleware. Integration tests remain the primary safety net.

**Tech Stack:** ASP.NET Core Web API (.NET 10), Entity Framework Core 10, JWT auth, xUnit, FluentAssertions.

---

## File Structure

- Modify: `src/EcoTrack.Api/Controllers/SalesController.cs`
  - Add `GET /api/inventory/sales` and `GET /api/inventory/sales/{id}` actions.
- Modify: `src/EcoTrack.Application/Inventory/SalesService.cs`
  - Add list and by-id read methods with visibility, filtering, sorting, and paging.
- Create: `src/EcoTrack.Application/Inventory/Contracts/GetSalesQueryRequest.cs`
  - Query contract for list endpoint.
- Create: `src/EcoTrack.Application/Inventory/Contracts/PagedResponse.cs`
  - Generic paged envelope for API read lists.
- Create: `src/EcoTrack.Application/Common/Exceptions/BadRequestException.cs`
  - Application exception for query validation failures.
- Modify: `src/EcoTrack.Api/Middleware/ApiExceptionMiddleware.cs`
  - Map `BadRequestException` to 400 error payload.
- Modify: `tests/EcoTrack.IntegrationTests/Inventory/SalesEndpointsTests.cs`
  - Add list and get-by-id coverage for auth, visibility, filtering, sorting, paging, and invalid query handling.
- Modify: `README.md`
  - Add new GET sales endpoints in API table.

### Task 1: Write Failing Integration Tests for Read Endpoints

**Files:**
- Modify: `tests/EcoTrack.IntegrationTests/Inventory/SalesEndpointsTests.cs`

- [ ] **Step 1: Add failing tests for list and by-id behavior**

```csharp
[Fact]
public async Task GetSales_WithAdminToken_ReturnsPagedSales()
{
    await AuthenticateAsCollectorAsync();
    var itemId = await GetFirstInventoryItemIdAsync();

    var first = await _client.PostAsJsonAsync("/api/inventory/sales", new
    {
        inventoryItemId = itemId,
        quantitySold = 2,
        soldAtUtc = DateTime.UtcNow.AddDays(-1)
    });
    first.StatusCode.Should().Be(HttpStatusCode.Created);

    var second = await _client.PostAsJsonAsync("/api/inventory/sales", new
    {
        inventoryItemId = itemId,
        quantitySold = 3,
        soldAtUtc = DateTime.UtcNow
    });
    second.StatusCode.Should().Be(HttpStatusCode.Created);

    await AuthenticateAsAdminAsync();

    var response = await _client.GetAsync("/api/inventory/sales?page=1&pageSize=10&sortBy=soldAtUtc&sortDirection=desc");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var payload = await response.Content.ReadFromJsonAsync<PagedSalesContract>();
    payload.Should().NotBeNull();
    payload!.Items.Count.Should().BeGreaterThanOrEqualTo(2);
    payload.Page.Should().Be(1);
    payload.PageSize.Should().Be(10);
    payload.TotalCount.Should().BeGreaterThanOrEqualTo(2);
}

[Fact]
public async Task GetSales_WithCollectorToken_ReturnsOnlyOwnSales()
{
    await AuthenticateAsCollectorAsync();
    var itemId = await GetFirstInventoryItemIdAsync();

    var create = await _client.PostAsJsonAsync("/api/inventory/sales", new
    {
        inventoryItemId = itemId,
        quantitySold = 1,
        soldAtUtc = DateTime.UtcNow
    });
    create.StatusCode.Should().Be(HttpStatusCode.Created);

    await AuthenticateAsAdminAsync();
    var adminCreate = await _client.PostAsJsonAsync("/api/inventory/sales", new
    {
        inventoryItemId = itemId,
        quantitySold = 7,
        soldAtUtc = DateTime.UtcNow
    });
    adminCreate.StatusCode.Should().Be(HttpStatusCode.Created);
    var adminSale = await adminCreate.Content.ReadFromJsonAsync<SaleRecordContract>();

    await AuthenticateAsCollectorAsync();

    var response = await _client.GetAsync("/api/inventory/sales");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var payload = await response.Content.ReadFromJsonAsync<PagedSalesContract>();
    payload.Should().NotBeNull();
    payload!.Items.Should().NotContain(x => x.Id == adminSale!.Id);
}

[Fact]
public async Task GetSaleById_WithCollectorToken_ForAnotherUsersSale_ReturnsNotFound()
{
    await AuthenticateAsAdminAsync();
    var itemId = await GetFirstInventoryItemIdAsync();
    var create = await _client.PostAsJsonAsync("/api/inventory/sales", new
    {
        inventoryItemId = itemId,
        quantitySold = 4,
        soldAtUtc = DateTime.UtcNow
    });
    create.StatusCode.Should().Be(HttpStatusCode.Created);
    var sale = await create.Content.ReadFromJsonAsync<SaleRecordContract>();

    await AuthenticateAsCollectorAsync();

    var response = await _client.GetAsync($"/api/inventory/sales/{sale!.Id}");

    response.StatusCode.Should().Be(HttpStatusCode.NotFound);
}

[Fact]
public async Task GetSales_WithInvalidRange_ReturnsBadRequest()
{
    await AuthenticateAsAdminAsync();

    var response = await _client.GetAsync("/api/inventory/sales?fromSoldAtUtc=2026-06-10T00:00:00Z&toSoldAtUtc=2026-06-01T00:00:00Z");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

- [ ] **Step 2: Add helper contracts for new response shape**

```csharp
private sealed record PagedSalesContract(
    List<SaleRecordDetailContract> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

private sealed record SaleRecordDetailContract(
    Guid Id,
    Guid InventoryItemId,
    int QuantitySold,
    decimal RevenueInr,
    DateTime SoldAtUtc,
    string ApprovalStatus,
    Guid RequestedByUserId,
    Guid? ApprovedByUserId,
    DateTime? ApprovedAtUtc,
    string? RejectionReason);
```

- [ ] **Step 3: Run tests to verify failure**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~SalesEndpointsTests.GetSales_"`
Expected: FAIL with 404 for GET routes and/or JSON shape mismatch.

- [ ] **Step 4: Commit failing tests**

```bash
git add tests/EcoTrack.IntegrationTests/Inventory/SalesEndpointsTests.cs
git commit -m "test: add failing integration tests for sales read endpoints"
```

### Task 2: Add Query and Paging Contracts

**Files:**
- Create: `src/EcoTrack.Application/Inventory/Contracts/GetSalesQueryRequest.cs`
- Create: `src/EcoTrack.Application/Inventory/Contracts/PagedResponse.cs`

- [ ] **Step 1: Add list query request contract**

```csharp
namespace EcoTrack.Application.Inventory.Contracts;

public sealed record GetSalesQueryRequest(
    string? Status,
    Guid? RequestedByUserId,
    DateTime? FromSoldAtUtc,
    DateTime? ToSoldAtUtc,
    Guid? InventoryItemId,
    string? SortBy,
    string? SortDirection,
    int Page = 1,
    int PageSize = 20);
```

- [ ] **Step 2: Add generic paged response contract**

```csharp
namespace EcoTrack.Application.Inventory.Contracts;

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
```

- [ ] **Step 3: Run build to verify contract compilation**

Run: `dotnet build EcoTrack-Backend.slnx`
Expected: SUCCESS (or next failing points from unimplemented service/controller references if already wired).

- [ ] **Step 4: Commit contracts**

```bash
git add src/EcoTrack.Application/Inventory/Contracts/GetSalesQueryRequest.cs src/EcoTrack.Application/Inventory/Contracts/PagedResponse.cs
git commit -m "feat: add sales list query and paging contracts"
```

### Task 3: Add Bad Request Exception Mapping

**Files:**
- Create: `src/EcoTrack.Application/Common/Exceptions/BadRequestException.cs`
- Modify: `src/EcoTrack.Api/Middleware/ApiExceptionMiddleware.cs`

- [ ] **Step 1: Add new exception type**

```csharp
namespace EcoTrack.Application.Common.Exceptions;

public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message) { }
}
```

- [ ] **Step 2: Map bad request exception in API middleware**

```csharp
catch (BadRequestException ex)
{
    await WriteErrorAsync(context, HttpStatusCode.BadRequest, ex.Message);
}
```

Place this catch block before the generic `catch (Exception ex)` block.

- [ ] **Step 3: Build to verify middleware compiles**

Run: `dotnet build EcoTrack-Backend.slnx`
Expected: SUCCESS.

- [ ] **Step 4: Commit exception handling changes**

```bash
git add src/EcoTrack.Application/Common/Exceptions/BadRequestException.cs src/EcoTrack.Api/Middleware/ApiExceptionMiddleware.cs
git commit -m "feat: map bad request exception in api middleware"
```

### Task 4: Implement Sales Read Query Logic in Service

**Files:**
- Modify: `src/EcoTrack.Application/Inventory/SalesService.cs`

- [ ] **Step 1: Add failing tests for specific filters and paging (if not already added)**

```csharp
[Fact]
public async Task GetSales_WithStatusFilter_ReturnsOnlyMatchingStatus()
{
    await AuthenticateAsCollectorAsync();

    var response = await _client.GetAsync("/api/inventory/sales?status=Draft");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var payload = await response.Content.ReadFromJsonAsync<PagedSalesContract>();
    payload!.Items.Should().OnlyContain(x => x.ApprovalStatus == "Draft");
}
```

- [ ] **Step 2: Add service methods for list and get-by-id**

```csharp
public async Task<PagedResponse<SaleRecordResponse>> GetSalesAsync(
    GetSalesQueryRequest request,
    Guid actorUserId,
    string actorRole,
    CancellationToken cancellationToken)
{
    var page = request.Page <= 0 ? throw new BadRequestException("Page must be greater than or equal to 1.") : request.Page;
    var pageSize = request.PageSize is < 1 or > 100
        ? throw new BadRequestException("PageSize must be between 1 and 100.")
        : request.PageSize;

    if (request.FromSoldAtUtc.HasValue && request.ToSoldAtUtc.HasValue && request.FromSoldAtUtc > request.ToSoldAtUtc)
        throw new BadRequestException("FromSoldAtUtc must be less than or equal to ToSoldAtUtc.");

    var query = _dbContext.SaleRecords.AsNoTracking().AsQueryable();

    if (!string.Equals(actorRole, "admin", StringComparison.OrdinalIgnoreCase))
        query = query.Where(x => x.RequestedByUserId == actorUserId);

    if (!string.IsNullOrWhiteSpace(request.Status))
    {
        if (!Enum.TryParse<SaleApprovalStatus>(request.Status, true, out var status))
            throw new BadRequestException("Invalid status value.");
        query = query.Where(x => x.ApprovalStatus == status);
    }

    if (request.RequestedByUserId.HasValue)
        query = query.Where(x => x.RequestedByUserId == request.RequestedByUserId.Value);

    if (request.FromSoldAtUtc.HasValue)
        query = query.Where(x => x.SoldAtUtc >= request.FromSoldAtUtc.Value);

    if (request.ToSoldAtUtc.HasValue)
        query = query.Where(x => x.SoldAtUtc <= request.ToSoldAtUtc.Value);

    if (request.InventoryItemId.HasValue)
        query = query.Where(x => x.InventoryItemId == request.InventoryItemId.Value);

    var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? "soldAtUtc" : request.SortBy;
    if (!string.Equals(sortBy, "soldAtUtc", StringComparison.OrdinalIgnoreCase))
        throw new BadRequestException("SortBy must be soldAtUtc.");

    var sortDirection = string.IsNullOrWhiteSpace(request.SortDirection) ? "desc" : request.SortDirection;
    query = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase)
        ? query.OrderBy(x => x.SoldAtUtc)
        : sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase)
            ? query.OrderByDescending(x => x.SoldAtUtc)
            : throw new BadRequestException("SortDirection must be asc or desc.");

    var totalCount = await query.CountAsync(cancellationToken);

    var items = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => ToResponse(x))
        .ToListAsync(cancellationToken);

    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

    return new PagedResponse<SaleRecordResponse>(items, page, pageSize, totalCount, totalPages);
}

public async Task<SaleRecordResponse> GetByIdAsync(
    Guid id,
    Guid actorUserId,
    string actorRole,
    CancellationToken cancellationToken)
{
    var query = _dbContext.SaleRecords.AsNoTracking().Where(x => x.Id == id);

    if (!string.Equals(actorRole, "admin", StringComparison.OrdinalIgnoreCase))
        query = query.Where(x => x.RequestedByUserId == actorUserId);

    var sale = await query.SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Sale record not found.");

    return ToResponse(sale);
}
```

- [ ] **Step 3: Run focused integration tests**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~SalesEndpointsTests"`
Expected: still failing only for missing controller endpoints until Task 5.

- [ ] **Step 4: Commit service query implementation**

```bash
git add src/EcoTrack.Application/Inventory/SalesService.cs
git commit -m "feat: add sales read query logic in service"
```

### Task 5: Add Sales GET Controller Endpoints

**Files:**
- Modify: `src/EcoTrack.Api/Controllers/SalesController.cs`

- [ ] **Step 1: Add GET list endpoint action**

```csharp
[HttpGet]
public async Task<ActionResult<PagedResponse<SaleRecordResponse>>> Get(
    [FromServices] SalesService service,
    [FromQuery] GetSalesQueryRequest request,
    CancellationToken cancellationToken)
{
    var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    var role = User.FindFirstValue(ClaimTypes.Role)!;
    var result = await service.GetSalesAsync(request, userId, role, cancellationToken);
    return Ok(result);
}
```

- [ ] **Step 2: Add GET by-id endpoint action**

```csharp
[HttpGet("{id:guid}")]
public async Task<ActionResult<SaleRecordResponse>> GetById(
    Guid id,
    [FromServices] SalesService service,
    CancellationToken cancellationToken)
{
    var userId = Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
    var role = User.FindFirstValue(ClaimTypes.Role)!;
    var result = await service.GetByIdAsync(id, userId, role, cancellationToken);
    return Ok(result);
}
```

- [ ] **Step 3: Run focused sales integration tests**

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~SalesEndpointsTests"`
Expected: PASS for existing and new sales endpoint tests.

- [ ] **Step 4: Commit controller changes**

```bash
git add src/EcoTrack.Api/Controllers/SalesController.cs
git commit -m "feat: add sales list and sale-by-id endpoints"
```

### Task 6: Complete Test Matrix for Filtering, Sorting, and Paging

**Files:**
- Modify: `tests/EcoTrack.IntegrationTests/Inventory/SalesEndpointsTests.cs`

- [ ] **Step 1: Add comprehensive filter/sort/paging tests**

```csharp
[Fact]
public async Task GetSales_WithSortAsc_ReturnsAscendingBySoldAtUtc()
{
    await AuthenticateAsAdminAsync();

    var response = await _client.GetAsync("/api/inventory/sales?sortBy=soldAtUtc&sortDirection=asc&page=1&pageSize=50");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var payload = await response.Content.ReadFromJsonAsync<PagedSalesContract>();
    payload.Should().NotBeNull();

    var timestamps = payload!.Items.Select(x => x.SoldAtUtc).ToList();
    timestamps.Should().BeInAscendingOrder();
}

[Fact]
public async Task GetSales_WithInvalidSortDirection_ReturnsBadRequest()
{
    await AuthenticateAsAdminAsync();

    var response = await _client.GetAsync("/api/inventory/sales?sortBy=soldAtUtc&sortDirection=sideways");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var payload = await response.Content.ReadFromJsonAsync<ApiErrorContract>();
    payload.Should().NotBeNull();
    payload!.Status.Should().Be(400);
}
```

- [ ] **Step 2: Keep helper contracts aligned with API response model**

```csharp
private sealed record PagedSalesContract(
    List<SaleRecordDetailContract> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
```

Ensure role-visibility assertions only use fields that exist in the actual response payload.

- [ ] **Step 3: Run full integration suite**

Run: `dotnet test tests/EcoTrack.IntegrationTests`
Expected: PASS (requires Docker Desktop for container-backed tests).

- [ ] **Step 4: Commit test matrix updates**

```bash
git add tests/EcoTrack.IntegrationTests/Inventory/SalesEndpointsTests.cs
git commit -m "test: add comprehensive sales read filter and paging coverage"
```

### Task 7: Update Documentation and Final Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update endpoint table with new read routes**

```markdown
| `GET` | `/api/inventory/sales` | Bearer | List sales (filters, sorting, paging) |
| `GET` | `/api/inventory/sales/{id}` | Bearer | Get sale by id (role-aware visibility) |
```

- [ ] **Step 2: Add a short query parameter note under API Endpoints**

```markdown
`GET /api/inventory/sales` supports query params:
`status`, `requestedByUserId`, `fromSoldAtUtc`, `toSoldAtUtc`, `inventoryItemId`, `sortBy`, `sortDirection`, `page`, `pageSize`.
```

- [ ] **Step 3: Run final verification commands**

Run: `dotnet test tests/EcoTrack.UnitTests`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~HealthEndpointTests"`
Expected: PASS.

Run: `dotnet test tests/EcoTrack.IntegrationTests`
Expected: PASS (Docker required).

- [ ] **Step 4: Commit docs and final changes**

```bash
git add README.md
git commit -m "docs: document sales read endpoints"
```

## Self-Review Checklist

- Spec coverage confirmed:
  - Added both GET endpoints.
  - Enforced admin/collector visibility and 404-by-visibility behavior.
  - Included full filter set, soldAtUtc sort, paging constraints.
  - Included validation and error mapping for 400.
  - Included integration testing and README updates.
- Placeholder scan:
  - No TODO/TBD placeholders left.
- Type consistency checks:
  - Uses existing `SaleRecordResponse` contract.
  - New list response uses `PagedResponse<T>` consistently.
  - Service methods and controller signatures align by type and naming.
