# Collection API Design

Date: 2026-06-18
Topic: Pickup schedule and collection workflow APIs for v1 collection page
Status: Approved design (pre-implementation)

## 1. Goal and Scope

Add CRUD and workflow endpoints for the Collection page pickup scheduling system in v1:

1. List pickups with status and date filtering
2. Create, read, update, delete (soft) pickups
3. Assign/reassign collectors with history tracking
4. Mark pickup as collected with actual weight
5. Send collected pickup to segregation (status-only transition in v1)
6. View assignment history for admin audit trail

This design is focused on the current Collection page functionality and does not include:
- Segregation task management
- Inventory stock movement on collection
- Site master table (v1 uses freeform text)
- Advanced analytics on collection metrics

## 2. Access and Visibility Rules

- Authentication required for all endpoints.
- Admin users:
  - Can create, read, update (all fields), delete, assign, mark collected, send to segregation.
  - Can perform all transitions.
  - Can view all pickups and assignment history.
- Collector users:
  - Can read their own assigned pickups only.
  - Can update notes on their own assigned pickups only.
  - Can mark their own assigned pickups as collected.
  - Cannot assign, create, delete, or send to segregation.
- Visibility rule: collectors see only pickups assigned to them; non-existent or unassigned pickups return 404.

Rationale: keeps permissions simple and operationally clear while preventing cross-user data leakage.

## 3. Domain Model

### 3.1 PickupTask Aggregate

Core fields:
- `Id` (Guid, primary key)
- `PickupCode` (string, sequential display code like P-1001, unique)
- `SiteName` (string)
- `SiteAddressText` (string)
- `ScheduledAtUtc` (datetime)
- `EstimatedWeightKg` (decimal)
- `CollectedWeightKg` (nullable decimal, set when moved to Collected state)
- `Status` (enum: Scheduled, Assigned, Collected, SentToSegregation, Cancelled)
- `AssignedCollectorUserId` (nullable Guid)
- `AssignedAtUtc` (nullable datetime)
- `Notes` (optional string)
- `CreatedByUserId` (Guid)
- `CreatedAtUtc` (datetime)
- `UpdatedAtUtc` (datetime)
- `CancelledByUserId` (nullable Guid, populated if status is Cancelled)
- `CancelledAtUtc` (nullable datetime)
- `CancelReason` (optional string)

### 3.2 PickupAssignmentEvent

Tracks every assignment and reassignment for admin history.

Fields:
- `Id` (Guid, primary key)
- `PickupTaskId` (Guid, foreign key)
- `PreviousCollectorUserId` (nullable Guid, null if first assignment)
- `NewCollectorUserId` (Guid)
- `ChangedByUserId` (Guid, admin who performed the change)
- `ChangedAtUtc` (datetime)
- `Note` (optional string, e.g., "Reassigned due to unavailability")

### 3.3 Status Transition Rules

Allowed transitions:
- `Scheduled` → `Assigned` (on assign)
- `Assigned` → `Collected` (on mark collected)
- `Collected` → `SentToSegregation` (on send to segregation)
- `Scheduled` → `Cancelled` (on cancel/delete before assignment)
- `Assigned` → `Cancelled` (on cancel/delete after assignment)

Reassignment:
- Status stays `Assigned` when reassigning; assignment event is appended.

Blocked transitions:
- No transitions out of `Cancelled` or `SentToSegregation`.
- Cannot go backward (e.g., `Collected` → `Assigned` not allowed).
- Cannot cancel after `Collected`.

## 4. Endpoint Contracts

### 4.1 List Pickups

**Request:**
```
GET /api/collection/pickups?status=&page=1&pageSize=20&sortBy=scheduledAtUtc&sortDirection=asc
```

Query parameters (all optional):
- `status` (enum string: Scheduled, Assigned, Collected, SentToSegregation, Cancelled)
- `page` (int, default 1, must be >= 1)
- `pageSize` (int, default 20, must be 1-100)
- `sortBy` (string, only `scheduledAtUtc` supported in v1)
- `sortDirection` (string, `asc` or `desc`, default `desc`)

**Response:**
```json
{
  "items": [
    {
      "id": "uuid",
      "pickupCode": "P-1001",
      "siteName": "Green Residency, Block A",
      "siteAddressText": "123 Eco Street",
      "scheduledAtUtc": "2026-06-20T08:00:00Z",
      "estimatedWeightKg": 120.5,
      "collectedWeightKg": null,
      "status": "Scheduled",
      "assignedCollectorUserId": null,
      "assignedCollectorDisplayName": null,
      "notes": "First collection"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 42,
  "totalPages": 3
}
```

### 4.2 Get Pickup Detail

**Request:**
```
GET /api/collection/pickups/{id}
```

**Response:**
Same shape as list item, plus:
- `createdByUserId` (Guid)
- `createdAtUtc` (datetime)
- `updatedAtUtc` (datetime)
- `cancelledByUserId` (Guid, nullable)
- `cancelledAtUtc` (datetime, nullable)
- `cancelReason` (string, nullable)
- `assignmentEvents` (array of all assignment history events for this pickup, in chronological order)

### 4.3 Create Pickup

**Request:**
```
POST /api/collection/pickups
```

Body:
```json
{
  "siteName": "Green Residency, Block A",
  "siteAddressText": "123 Eco Street",
  "scheduledAtUtc": "2026-06-20T08:00:00Z",
  "estimatedWeightKg": 120.5,
  "notes": "First collection"
}
```

Validation:
- `siteName` required, non-empty
- `siteAddressText` required, non-empty
- `scheduledAtUtc` required, must be valid datetime
- `estimatedWeightKg` required, must be > 0
- `notes` optional

**Response:**
201 Created with full pickup detail (status = Scheduled, id auto-generated).

### 4.4 Update Pickup

**Request:**
```
PUT /api/collection/pickups/{id}
```

Body (admin can update all):
```json
{
  "siteName": "Green Residency, Block A",
  "siteAddressText": "123 Eco Street, Apt 5",
  "scheduledAtUtc": "2026-06-20T08:00:00Z",
  "estimatedWeightKg": 125.0,
  "notes": "Updated notes"
}
```

Body (collector can update only notes):
```json
{
  "notes": "Unable to access apartment, will retry tomorrow"
}
```

Validation:
- Admin can only edit non-terminal pickups (not Cancelled or SentToSegregation).
- Collector can only edit notes and only for pickups assigned to them.
- Same field validation as create for admin updates.

**Response:**
200 OK with updated pickup detail.

### 4.5 Assign Collector

**Request:**
```
POST /api/collection/pickups/{id}/assign
```

Body:
```json
{
  "assignedCollectorUserId": "uuid-of-collector",
  "note": "Assigned to new collector due to schedule change"
}
```

Validation:
- `assignedCollectorUserId` required, must be valid collector user
- `note` optional
- Pickup must be in Scheduled or Assigned state (allows reassignment)

Side effect:
- Status changes to Assigned (if was Scheduled)
- Assignment event appended with previous/new collector user ids
- `updatedAtUtc` timestamp updated

**Response:**
200 OK with updated pickup detail including new assignment event.

### 4.6 Mark Collected

**Request:**
```
POST /api/collection/pickups/{id}/mark-collected
```

Body:
```json
{
  "collectedWeightKg": 115.25
}
```

Validation:
- `collectedWeightKg` required, must be > 0
- Pickup status must be Assigned
- For collectors: pickup must be assigned to the caller
- For admin: any Assigned pickup allowed

Side effect:
- Status changes to Collected
- `collectedWeightKg` stored
- `updatedAtUtc` timestamp updated
- No inventory movement in v1

**Response:**
200 OK with updated pickup detail.

### 4.7 Send to Segregation

**Request:**
```
POST /api/collection/pickups/{id}/send-to-segregation
```

Body: empty (no required fields in v1)

Validation:
- Pickup status must be Collected
- Admin-only

Side effect:
- Status changes to SentToSegregation
- `updatedAtUtc` timestamp updated
- No inventory or segregation task creation in v1

**Response:**
200 OK with updated pickup detail.

### 4.8 Delete (Soft Delete / Cancel)

**Request:**
```
DELETE /api/collection/pickups/{id}
```

Optional body (for v1, not required):
```json
{
  "reason": "Schedule conflict"
}
```

Validation:
- Pickup must not be in terminal state (Cancelled, SentToSegregation already)
- Pickup must not be Collected (can only cancel before collection)
- Must be in Scheduled or Assigned state

Side effect:
- Status changes to Cancelled
- `cancelledByUserId` set to caller id
- `cancelledAtUtc` set to now
- `cancelReason` set if provided, else null
- `updatedAtUtc` updated
- Soft delete behavior: not removed from DB, hidden in default list (future includeDeleted param can show them)

**Response:**
200 OK with updated pickup detail (with Cancelled status).

### 4.9 Assignment History

**Request:**
```
GET /api/collection/pickups/{id}/assignment-history
```

**Response:**
```json
{
  "events": [
    {
      "id": "uuid",
      "pickupTaskId": "uuid",
      "previousCollectorUserId": null,
      "newCollectorUserId": "uuid-of-collector-1",
      "changedByUserId": "uuid-of-admin",
      "changedAtUtc": "2026-06-18T10:00:00Z",
      "note": "Initial assignment"
    },
    {
      "id": "uuid",
      "pickupTaskId": "uuid",
      "previousCollectorUserId": "uuid-of-collector-1",
      "newCollectorUserId": "uuid-of-collector-2",
      "changedByUserId": "uuid-of-admin",
      "changedAtUtc": "2026-06-18T14:30:00Z",
      "note": "Reassigned - first collector unavailable"
    }
  ]
}
```

Visibility:
- Admin: can view any pickup's history
- Collector: can view only if the pickup is assigned to them (return 404 otherwise)

## 5. Error Handling

HTTP status codes:

- `200 OK` — successful operation
- `201 Created` — resource created
- `400 Bad Request` — validation failure (e.g., invalid page number, invalid status in filter, missing required field, invalid transition)
- `401 Unauthorized` — missing/invalid token
- `403 Forbidden` — authenticated but disallowed (role insufficient for operation, e.g., collector attempts admin action)
- `404 Not Found` — resource not found or not visible to caller
- `409 Conflict` — business rule violation (e.g., attempted invalid state transition, trying to collect from non-Assigned state)

Error response body:
```json
{
  "status": 400,
  "message": "PageSize must be between 1 and 100."
}
```

## 6. Architecture and File-Level Changes

### API Layer
- Add `src/EcoTrack.Api/Controllers/CollectionController.cs`
  - Resource-based routes: GET list, GET detail, POST create, PUT update, DELETE soft-delete
  - Workflow action routes: POST assign, POST mark-collected, POST send-to-segregation
  - History route: GET assignment-history

### Application Layer
- Create `src/EcoTrack.Application/Collection/` folder
- Add `CollectionService.cs` — business logic, transitions, role checks, query filtering
- Add request/response contracts under `src/EcoTrack.Application/Collection/Contracts/`
  - `GetPickupsQueryRequest.cs`
  - `CreatePickupRequest.cs`
  - `UpdatePickupRequest.cs`
  - `AssignPickupRequest.cs`
  - `MarkCollectedRequest.cs`
  - `PickupResponse.cs` (list item shape)
  - `PickupDetailResponse.cs` (includes audit fields and history)
  - `AssignmentEventResponse.cs`
  - `PagedResponse<T>` (already exists, reuse)

### Domain Layer
- Add `src/EcoTrack.Domain/Inventory/PickupTask.cs` — aggregate root with transition methods
- Add `src/EcoTrack.Domain/Inventory/PickupStatus.cs` — enum
- Add `src/EcoTrack.Domain/Inventory/PickupAssignmentEvent.cs` — value object or entity

### Infrastructure Layer
- Add EF Core mappings in `src/EcoTrack.Infrastructure/Persistence/`
- Add migration creating `PickupTasks` and `PickupAssignmentEvents` tables
- Register `CollectionService` in `src/EcoTrack.Infrastructure/DependencyInjection.cs`

### Testing
- Create `tests/EcoTrack.IntegrationTests/Collection/CollectionEndpointsTests.cs`
  - CRUD operations
  - Role enforcement (admin vs collector)
  - Transition validation
  - List filtering, sorting, paging
  - Assignment history visibility
  - Error cases (400, 403, 404, 409)

## 7. Validation and Business Rules

### Request validation
- Page: >= 1
- PageSize: 1 to 100
- SortBy: `scheduledAtUtc` only in v1
- SortDirection: `asc` or `desc`
- Status filter: must be valid enum value
- EstimatedWeightKg and CollectedWeightKg: > 0
- ScheduledAtUtc: valid datetime
- AssignedCollectorUserId: valid user (when assigning)

### State machine validation
All transitions validated via domain model methods and enforced in service layer before DB update.

### Soft-delete visibility
- Default list query excludes Cancelled pickups
- Admin can view with includeDeleted flag (deferred to v2 if needed)
- Collector visibility: cannot see any cancelled pickups

## 8. Testing Strategy

### Integration Tests
1. CRUD operations by admin
   - Create, read, list, update all fields
   - Delete (soft) allowed before Collected
   - Delete blocked after Collected

2. Role-based access control
   - Admin can perform all actions
   - Collector can read own assigned pickups only (returns 404 for others)
   - Collector can edit notes on own assigned pickups
   - Collector cannot assign, create, send-to-segregation

3. Workflow transitions
   - Scheduled → Assigned → Collected → SentToSegregation
   - Cancel allowed from Scheduled/Assigned only
   - Invalid transitions return 409

4. List filtering and paging
   - Status filter returns only matching pickups
   - Page/pageSize validated (400 on invalid)
   - Sorting by scheduled date (asc/desc)
   - Default excludes Cancelled

5. Assignment history
   - First assignment event created
   - Reassignment events appended
   - History endpoint returns chronological events
   - Collector visibility enforced

### Regression
- Existing sales, inventory, analytics, auth tests remain passing
- Health endpoint tests pass

## 9. Rollout and Documentation

### Backward Compatibility
- Additive feature, does not affect existing endpoints
- No migration of existing data required

### API Documentation
- Update `README.md` with Collection endpoint table
- List roles and permissions
- Provide example requests/responses for key operations

### Swagger Integration
- Swagger automatically exposes new controller and actions
- Response codes and schemas visible in UI

## 10. Definition of Done

1. PickupTask domain model and PickupStatus enum created
2. PickupAssignmentEvent entity created
3. EF Core migrations run, tables created
4. CollectionService implements full CRUD and workflow
5. CollectionController endpoints all implemented
6. Request/response contracts all defined
7. Role-based access control enforced per section 2
8. Transition rules enforced per domain model
9. Soft-delete behavior working (Cancelled status)
10. Assignment history logged and retrievable
11. Integration tests cover all CRUD, transitions, permissions, validation, and error paths
12. Existing tests remain passing
13. README updated with Collection API endpoints and query params
14. No placeholders or TODOs in code or docs
