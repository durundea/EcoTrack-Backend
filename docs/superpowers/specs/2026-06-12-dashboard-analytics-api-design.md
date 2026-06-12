# Dashboard Analytics API Design

Date: 2026-06-12
Topic: Dashboard analytics API for v1 dashboard page
Status: Approved design (pre-implementation)

## 1. Goal and Scope

Build backend read APIs to support the current dashboard page only (v1), matching the shared UI:

- Top KPI cards:
  - Total Waste Processed
  - Revenue
  - Recycling Efficiency
  - CO2 Reduction
- Waste by category chart
- Category distribution chart
- Category breakdown table
- Pending sales approvals summary

Out of scope for v1:

- Drill-down widget endpoints
- Trend series and period-over-period comparisons
- New database schema changes

## 2. Approach Selection

Chosen approach: single consolidated endpoint for the whole page.

- Endpoint: GET /api/analytics/dashboard
- Reason: one request per page load, consistent metrics snapshot, fastest integration for the current frontend scope.

## 3. Access, Visibility, and Filters

- Authentication required.
- Allowed roles: admin, collector.
- Data visibility:
  - Admin can see all matching sales.
  - Collector can see only own matching sales (RequestedByUserId == caller user id).

Supported query parameters:

- fromUtc (optional, ISO datetime)
- toUtc (optional, ISO datetime)
- wasteType (optional; values: all, rawWaste, recycledProduct)

Default range rules:

- If both fromUtc and toUtc are missing: default to last 30 days.
- If only one bound is provided: infer the other bound with a 30-day window.
- Reject invalid range when fromUtc > toUtc.

## 4. Endpoint Contract

### 4.1 GET /api/analytics/dashboard

Response shape:

- range:
  - fromUtc
  - toUtc
  - label
- kpis:
  - totalWasteProcessedKg
  - revenueInr
  - recyclingEfficiencyPercent
  - co2ReductionKg
- wasteByCategory: array of
  - category
  - weightKg
  - sharePercent
- categoryDistribution: array of
  - category
  - weightKg
  - sharePercent
- pendingSalesApprovals:
  - count
  - isDataAvailable
  - message

Example payload:

```json
{
  "range": {
    "fromUtc": "2026-05-13T00:00:00Z",
    "toUtc": "2026-06-12T23:59:59Z",
    "label": "Last 30 days"
  },
  "kpis": {
    "totalWasteProcessedKg": 1840.0,
    "revenueInr": 42500.0,
    "recyclingEfficiencyPercent": 78.0,
    "co2ReductionKg": 920.0
  },
  "wasteByCategory": [
    { "category": "Plastic", "weightKg": 480.0, "sharePercent": 26.1 },
    { "category": "Organic", "weightKg": 620.0, "sharePercent": 33.7 }
  ],
  "categoryDistribution": [
    { "category": "Plastic", "weightKg": 480.0, "sharePercent": 26.1 }
  ],
  "pendingSalesApprovals": {
    "count": 7,
    "isDataAvailable": true,
    "message": null
  }
}
```

## 5. Data Rules and Formulas

### 5.1 Record inclusion for KPI/chart calculations

Use sales filtered by:

- Role visibility rule
- Time window
- wasteType filter
- Approval status in {Approved, PendingApproval}

### 5.2 KPI formulas

- Total Waste Processed (kg):
  - Sum(quantitySold) from included sales.
- Revenue (INR):
  - Sum(revenueInr) from included sales.
- Recycling Efficiency (%):
  - approved_or_pending_sold_kg / total_collected_kg_in_range * 100
  - If denominator is 0, return 0.
- CO2 Reduction (kg):
  - Sum(sold_kg * category_factor_kg_co2_per_kg)
  - Category factors are loaded from API configuration.

### 5.3 Charts and table

- wasteByCategory and categoryDistribution are derived from the same grouped dataset.
- Each category row includes:
  - weightKg
  - sharePercent relative to total included weight

### 5.4 Pending approvals card

- count = number of PendingApproval records under caller visibility.
- isDataAvailable = true in normal operation.
- message = null in normal operation.

## 6. Architecture and File-Level Changes

Follow existing layered architecture already used in the repository.

- API layer:
  - Add controller/action for GET /api/analytics/dashboard.
- Application layer:
  - Add DashboardAnalyticsService for range normalization, validation, filtering, aggregation, and response mapping.
  - Add query and response contracts for dashboard payload.
- Configuration:
  - Add typed options for CO2 factors by category in appsettings.

No domain entity changes required.
No database migration required.

## 7. Validation and Error Behavior

- 400 Bad Request:
  - fromUtc > toUtc
  - unsupported wasteType
  - malformed query values
- 401/403:
  - authentication/authorization failures
- 200 OK:
  - valid request, including empty data windows (return zeros and empty arrays)

## 8. Testing Strategy

### 8.1 Integration tests

Add endpoint tests that verify:

1. Admin receives full payload shape and non-error response.
2. Collector visibility restricts data to collector-owned records.
3. Default last-30-days range is applied when range not supplied.
4. Invalid range returns 400.
5. wasteType filter values all/rawWaste/recycledProduct behave correctly.
6. Empty data windows return 200 with zeros and empty collections.
7. Pending approvals count is accurate.

### 8.2 Unit tests

Add service-level tests for:

1. Recycling efficiency denominator-zero handling.
2. CO2 factor application by category.
3. Share percent calculations for grouped category data.

## 9. Rollout and Documentation

- Additive and backward compatible change.
- Swagger will expose the new endpoint automatically.
- Update README endpoint table with dashboard route and query params.

## 10. Definition of Done

1. GET /api/analytics/dashboard implemented with approved filter and formula rules.
2. Response contract supports all widgets on the current dashboard page.
3. Integration and unit tests for endpoint and formula behavior are added and passing.
4. Existing sales, inventory, and auth tests remain passing.
5. README updated with dashboard API details.
