# Sales Read APIs Design

Date: 2026-06-02
Topic: Add read endpoints for sales records
Status: Approved design (pre-implementation)

## 1. Goal and Scope

Add two authenticated read endpoints:

1. `GET /api/inventory/sales`
2. `GET /api/inventory/sales/{id}`

This design is additive and does not alter existing create/submit/approve/update sales workflow behavior.

## 2. Access and Visibility Rules

- Authentication required for both endpoints.
- Admin users can read all sales.
- Collector users can read only their own sales (`RequestedByUserId == caller user id`).
- For `GET /api/inventory/sales/{id}`:
  - Return `404 Not Found` when the record does not exist.
  - Return `404 Not Found` when the record exists but is outside caller visibility.

Rationale: prevents cross-user data leakage while keeping behavior simple.

## 3. Endpoint Contracts

### 3.1 GET /api/inventory/sales

Query parameters for v1:

- `status` (Draft, PendingApproval, Approved, Rejected)
- `requestedByUserId` (Guid)
- `fromSoldAtUtc` (DateTime)
- `toSoldAtUtc` (DateTime)
- `inventoryItemId` (Guid)
- `sortBy` (`soldAtUtc` only in v1)
- `sortDirection` (`asc` or `desc`)
- `page` (default `1`)
- `pageSize` (default `20`, max `100`)

Response:

- `200 OK` with paged envelope:
  - `items`: array of existing `SaleRecordResponse`
  - `page`
  - `pageSize`
  - `totalCount`
  - `totalPages`

Empty result returns `200 OK` with empty `items`.

### 3.2 GET /api/inventory/sales/{id}

Response:

- `200 OK` with `SaleRecordResponse` when visible.
- `404 Not Found` when not found or not visible.

## 4. Architecture and File-Level Changes

Follow existing layering and patterns:

- API layer:
  - Add GET actions in `src/EcoTrack.Api/Controllers/SalesController.cs`.
- Application contracts:
  - Add query request contract for list filters/paging/sort.
  - Add paged response contract for sales list result.
- Application service:
  - Add read methods in `src/EcoTrack.Application/Inventory/SalesService.cs`.
  - Keep controller thin; place query rules in service.

No domain model changes are required.
No persistence schema changes are required.

## 5. Data Flow

### 5.1 List endpoint flow

1. Controller binds query string to list query contract.
2. Controller extracts caller user id and role from claims.
3. Service starts from `SaleRecords` queryable.
4. Service applies visibility filter first (admin all, collector own).
5. Service applies optional filters (`status`, `requestedByUserId`, dates, `inventoryItemId`).
6. Service applies sorting (`soldAtUtc` only, default direction `desc`).
7. Service computes `totalCount` before paging.
8. Service applies paging (`Skip/Take`) and maps to `SaleRecordResponse`.
9. Service returns paged envelope.

### 5.2 Get-by-id flow

1. Controller passes `id` and caller context to service.
2. Service queries by `id` and visibility guard.
3. If not found after guard, throw not-found for API 404 mapping.
4. If found, map to `SaleRecordResponse` and return.

## 6. Validation and Error Rules

Request validation rules:

- `page >= 1`
- `1 <= pageSize <= 100`
- `fromSoldAtUtc <= toSoldAtUtc` when both present
- `sortBy` must be `soldAtUtc` in v1
- `sortDirection` must be `asc` or `desc`
- `status` must map to allowed enum values

Error behavior:

- `401 Unauthorized` for missing/invalid token.
- `400 Bad Request` for invalid query parameters.
- `404 Not Found` for non-visible or non-existent id lookup.
- `200 OK` for successful list, including empty page.

## 7. Testing Strategy

Extend integration tests in `tests/EcoTrack.IntegrationTests/Inventory/SalesEndpointsTests.cs`.

Required test scenarios:

1. Admin can list all sales.
2. Collector list returns only collector-owned sales.
3. Admin can fetch any sale by id.
4. Collector fetch of another user sale returns 404.
5. Filter by status.
6. Filter by sold date range.
7. Filter by inventory item id.
8. Filter by requestedByUserId with collector visibility still enforced.
9. Sorting by soldAtUtc asc and desc.
10. Paging returns correct `totalCount`, `totalPages`, and slice.
11. Invalid filter/sort/paging inputs return 400.

Regression expectation:

- Existing sales workflow tests remain green.
- Existing inventory and auth endpoint tests remain green.

## 8. Rollout and Documentation

- Additive change, backward compatible for existing consumers.
- No migration required.
- Swagger should automatically expose new controller actions.
- Update API endpoint table in `README.md` with new GET routes and list query params summary.

## 9. Definition of Done

1. `GET /api/inventory/sales` implemented with agreed filters/paging/sort.
2. `GET /api/inventory/sales/{id}` implemented with role-aware visibility.
3. Integration tests for new read behavior and validation are added and passing.
4. Existing tests remain passing.
5. README is updated for new read endpoints.
