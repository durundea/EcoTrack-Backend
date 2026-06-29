# Segregation API Design

Date: 2026-06-29
Topic: Waste segregation recording and recycling workflow APIs for v1 segregation page
Status: Approved design (pre-implementation)

## 1. Goal and Scope

Add CRUD and workflow endpoints for the Segregation page waste category recording system in v1:

1. List segregation batches (pending, recorded, recycled)
2. View batch details with linked pickup info
3. Record segregation data (waste category weights: Plastic, Organic, Metal, Paper, E-Waste)
4. Mark batch as recycled (status transition)
5. Auto-create batches when pickups transition to "SentToSegregation" (from Collection API)
6. Provide pending batches dropdown for UI selection

This design is focused on the current Segregation page functionality and does not include:
- Segregation worker/dispatch assignment (admin selects from dropdown in v1)
- Inventory stock movement (downstream system)
- Edit/update recorded segregation (record once, transitions forward)
- Advanced waste analytics or reporting

## 2. Access and Visibility Rules

- Authentication required for all endpoints.
- **Admin users only:**
  - Can list all segregation batches (by status filter)
  - Can view any batch details
  - Can record segregation data (enter waste weights, transition Pending → Recorded)
  - Can mark batch as recycled (transition Recorded → Recycled)
  - Batches are auto-created when pickups move to segregation
- **Non-admin users:**
  - Cannot access segregation endpoints (403 Forbidden)

**Rationale:** Segregation is specialized work in v1. Future versions can add segregation worker role if needed.

## 3. Domain Model

### 3.1 SegregationBatch Aggregate

Core fields:
- `Id` (Guid, primary key)
- `PickupTaskId` (Guid, foreign key to PickupTask)
- `BatchCode` (string, sequential display code like SB-0001, unique)
- `Status` (enum: Pending, Recorded, Recycled)
- `PlasticKg` (decimal, null if Pending)
- `OrganicKg` (decimal, null if Pending)
- `MetalKg` (decimal, null if Pending)
- `PaperKg` (decimal, null if Pending)
- `EWasteKg` (decimal, null if Pending)
- `RecordedByUserId` (nullable Guid, populated when recorded)
- `RecordedAtUtc` (nullable datetime, populated when recorded)
- `RecycledByUserId` (nullable Guid, populated when recycled)
- `RecycledAtUtc` (nullable datetime, populated when recycled)
- `CreatedAtUtc` (datetime, auto-set when batch created by system)
- `UpdatedAtUtc` (datetime, updated on status transitions)

Navigation:
- `PickupTask` (one-to-one reference to the source pickup)

### 3.2 Status Transition Rules

Allowed transitions:
- `Pending` → `Recorded` (on record segregation data call)
- `Recorded` → `Recycled` (on mark recycled call)

Guard rules:
- Cannot record if batch not in Pending state
- Cannot mark recycled if batch not in Recorded state
- All waste weights (Plastic, Organic, Metal, Paper, E-Waste) must be ≥ 0
- At least one weight must be > 0 (cannot record all zeros)

### 3.3 Integration with Collection API

When Collection API's "SendToSegregation" endpoint is called on a PickupTask:
1. PickupTask status transitions to `SentToSegregation`
2. System automatically creates a new SegregationBatch with status `Pending`
3. Batch code is auto-generated sequentially (SB-0001, SB-0002, etc.)
4. `CreatedAtUtc` is set to current time; no user attribution needed (system-created)

This one-to-one link ensures every "SentToSegregation" pickup has a corresponding batch awaiting segregation data.

## 4. API Endpoints

All endpoints require authentication and admin role authorization.

### 4.1 List Segregation Batches
```
GET /api/segregation/batches
Query params:
  - status: string (Pending|Recorded|Recycled, optional, filters by status)
  - page: int (default 1)
  - pageSize: int (default 20)
Response: PagedResponse<SegregationBatchListItemResponse>
```

**SegregationBatchListItemResponse:**
- `id` (Guid)
- `batchCode` (string, e.g., "SB-0001")
- `pickupCode` (string, e.g., "P-1003", from linked PickupTask)
- `status` (string)
- `recordedAtUtc` (datetime, null if Pending)
- `recycledAtUtc` (datetime, null if not yet recycled)

*For the UI dropdown: call this endpoint with `?status=Pending` to get pending batches available for segregation.*

### 4.2 Get Batch Details
```
GET /api/segregation/batches/{id:guid}
Response: SegregationBatchDetailResponse
```

**SegregationBatchDetailResponse:**
- `id` (Guid)
- `batchCode` (string)
- `status` (string)
- **Linked Pickup:**
  - `pickupTaskId` (Guid)
  - `pickupCode` (string)
  - `siteName` (string)
  - `siteAddressText` (string)
  - `scheduledAtUtc` (datetime)
  - `collectedWeightKg` (decimal)
- **Waste Breakdown:**
  - `plasticKg` (decimal, null if Pending)
  - `organicKg` (decimal, null if Pending)
  - `metalKg` (decimal, null if Pending)
  - `paperKg` (decimal, null if Pending)
  - `ewasteKg` (decimal, null if Pending)
- **Audit Trail:**
  - `recordedByUserId` (Guid, null if not recorded)
  - `recordedAtUtc` (datetime, null if not recorded)
  - `recycledByUserId` (Guid, null if not recycled)
  - `recycledAtUtc` (datetime, null if not recycled)
  - `createdAtUtc` (datetime)
  - `updatedAtUtc` (datetime)

### 4.3 Record Segregation Data
```
POST /api/segregation/batches/{id:guid}/record
Request body: RecordSegregationDataRequest
{
  "plasticKg": 50.0,
  "organicKg": 30.0,
  "metalKg": 20.0,
  "paperKg": 15.0,
  "ewasteKg": 5.0
}
Response: SegregationBatchDetailResponse (updated batch)
```

**Validation:**
- Batch must exist and be in Pending status
- All weights must be ≥ 0
- At least one weight must be > 0

**Side Effects:**
- Transitions batch from Pending → Recorded
- Sets `RecordedByUserId` to current admin user
- Sets `RecordedAtUtc` to current UTC time
- Updates `UpdatedAtUtc`

**Errors:**
- 404: Batch not found
- 400: Invalid status transition (not Pending)
- 400: Validation error (negative weights, all zeros)

### 4.4 Mark as Recycled
```
POST /api/segregation/batches/{id:guid}/mark-recycled
Request body: empty
Response: SegregationBatchDetailResponse (updated batch)
```

**Validation:**
- Batch must exist and be in Recorded status

**Side Effects:**
- Transitions batch from Recorded → Recycled
- Sets `RecycledByUserId` to current admin user
- Sets `RecycledAtUtc` to current UTC time
- Updates `UpdatedAtUtc`

**Errors:**
- 404: Batch not found
- 400: Invalid status transition (not Recorded)

## 5. Error Handling

All errors follow the existing `ApiExceptionMiddleware` pattern:

**SegregationBatchNotFound (404)**
- When batch ID doesn't exist
- Response: `{ "message": "Segregation batch not found" }`

**InvalidStatusTransition (400)**
- When attempting invalid state transition
- Examples:
  - Recording data on a batch not in Pending state
  - Marking recycled on a batch not in Recorded state
- Response: `{ "message": "Cannot record segregation data on a batch in Recycled status" }`

**ValidationError (400)**
- When waste weights are invalid
- Response: includes field-level error messages
- Example: `{ "errors": { "plasticKg": "must be >= 0" } }`

**UnauthorizedAccess (403)**
- When non-admin attempts to access any segregation endpoint
- Response: `{ "message": "Access denied" }`

## 6. Testing Strategy

### 6.1 Unit Tests
**File:** `tests/EcoTrack.UnitTests/Segregation/SegregationBatchTests.cs`

Test coverage:
- Domain transition rules: Pending → Recorded, Recorded → Recycled
- Guard validations: negative weights rejected, all-zeros rejected, invalid transitions rejected
- Aggregate state consistency after transitions

### 6.2 Integration Tests
**File:** `tests/EcoTrack.IntegrationTests/Segregation/SegregationEndpointsTests.cs`

Test coverage:
- `GET /batches` returns paginated results, filters by status correctly
- `GET /batches/{id}` returns complete details with linked pickup info
- `POST /batches/{id}/record` successfully transitions Pending → Recorded, stores waste data
- `POST /batches/{id}/mark-recycled` successfully transitions Recorded → Recycled
- `GET /batches/pending` returns only Pending batches in creation order
- Authorization: non-admin requests return 403 Forbidden
- Error cases:
  - Invalid transitions (recording on already-recorded batch, etc.)
  - Negative weight validation
  - Missing batch (404)
  - All-zero weights validation

**Tech Stack (consistent with Collection API):**
- xUnit, FluentAssertions
- Testcontainers PostgreSQL
- Integration tests use `IntegrationTestWebAppFactory`

## 7. Implementation Notes

### Auto-Batch Creation
When Collection API transitions a PickupTask to "SentToSegregation", the system must create a SegregationBatch. This can be implemented as:
- A domain event raised by PickupTask, consumed by SegregationService
- Or direct creation in CollectionService before persisting the pickup status change
- Recommend event-driven approach for loose coupling

### Sequential Batch Codes
Batch codes (SB-0001, SB-0002, etc.) should be generated via a database sequence or by querying max existing code on creation. Avoid client-side generation to prevent collisions.

### Data Persistence
- Create EF Core entity configuration for SegregationBatch with all field mappings
- Add DbSet<SegregationBatch> to IApplicationDbContext
- Create migration to add segregation_batches table
- Foreign key constraint to pickup_tasks table

---

**End of Design Document**
