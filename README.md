# EcoTrack Backend

A production-shaped .NET 10 Web API providing authentication and inventory management for the EcoTrack recycling platform.

## Tech Stack

- **.NET 10** — ASP.NET Core Web API
- **Entity Framework Core 10** + **Npgsql** — PostgreSQL persistence, code-first migrations
- **JWT Bearer** — stateless authentication via `Microsoft.AspNetCore.Authentication.JwtBearer`
- **xUnit** + **FluentAssertions** — unit and integration tests
- **Testcontainers.PostgreSql** — real PostgreSQL container for integration tests (requires Docker Desktop)

## Project Structure

```
src/
  EcoTrack.Domain/         Domain entities, enums, and business rules
  EcoTrack.Application/    Services, contracts, and interfaces
  EcoTrack.Infrastructure/ EF Core DbContext, JWT generation, password hashing
  EcoTrack.Api/            ASP.NET Core controllers, middleware, Swagger

tests/
  EcoTrack.UnitTests/       Domain rule tests (no DB required)
  EcoTrack.IntegrationTests/ HTTP endpoint tests (Docker required for most)
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL](https://www.postgresql.org/) running locally (or Docker)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for integration tests)

## Getting Started

### 1. Clone and restore

```bash
git clone <repo-url>
cd EcoTrack-Backend
dotnet restore
```

### 2. Configure the database

Edit `src/EcoTrack.Api/appsettings.json` to point to your PostgreSQL instance:

```json
{
  "ConnectionStrings": {
    "EcoTrackDb": "Host=localhost;Database=ecotrack_dev;Username=postgres;Password=postgres"
  }
}
```

### 3. Apply migrations

```bash
dotnet ef database update --project src/EcoTrack.Infrastructure --startup-project src/EcoTrack.Api
```

This creates the schema and seeds development data:
- **Admin**: `admin@ecotrack.local` / `admin123`
- **Collector**: `collector@ecotrack.local` / `collector123`
- **Inventory**: Compost, Eco-bricks, Raw Scrap Metal

### 4. Run the API

```bash
dotnet run --project src/EcoTrack.Api
```

Swagger UI is available at `https://localhost:<port>/swagger`.

## API Endpoints

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `GET` | `/api/health` | None | Health check |
| `POST` | `/api/auth/login` | None | Login → JWT |
| `GET` | `/api/auth/me` | Bearer | Current user |
| `GET` | `/api/inventory/items` | Bearer | List items |
| `POST` | `/api/inventory/items` | Admin | Create item |
| `PATCH` | `/api/inventory/items/{id}/price` | Admin | Update price |
| `POST` | `/api/inventory/sales` | Bearer | Create draft sale |
| `GET` | `/api/inventory/sales` | Bearer | List sales (filters, sorting, paging) |
| `GET` | `/api/inventory/sales/{id}` | Bearer | Get sale by id (role-aware visibility) |
| `POST` | `/api/inventory/sales/{id}/submit` | Bearer | Submit for approval |
| `POST` | `/api/inventory/sales/{id}/approve` | Admin | Approve sale |
| `PUT` | `/api/inventory/sales/{id}` | Bearer | Update draft |
| `GET` | `/api/analytics/dashboard` | Admin, Collector | Dashboard KPIs, category charts/table, pending approvals |

`GET /api/inventory/sales` supports query params: `status`, `requestedByUserId`, `fromSoldAtUtc`, `toSoldAtUtc`, `inventoryItemId`, `sortBy`, `sortDirection`, `page`, `pageSize`.

`GET /api/analytics/dashboard` supports query params: `fromUtc`, `toUtc`, `wasteType`.

- `wasteType`: `all` (default), `rawWaste`, `recycledProduct`
- default range: last 30 days when both bounds are omitted
- if one bound is omitted, the other bound is inferred using a 30-day window

## Running Tests

### Unit tests (no Docker required)

```bash
dotnet test tests/EcoTrack.UnitTests
```

### Health test (no Docker required)

```bash
dotnet test tests/EcoTrack.IntegrationTests --filter "FullyQualifiedName~HealthEndpointTests"
```

### Integration tests (Docker Desktop required)

```bash
dotnet test tests/EcoTrack.IntegrationTests
```

## Migrations

Create a new migration after model changes:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/EcoTrack.Infrastructure \
  --startup-project src/EcoTrack.Api
```

Apply all pending migrations:

```bash
dotnet ef database update \
  --project src/EcoTrack.Infrastructure \
  --startup-project src/EcoTrack.Api
```

## Configuration Reference

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:EcoTrackDb` | localhost postgres | PostgreSQL connection string |
| `Jwt:SecretKey` | (set in appsettings) | HMAC-SHA256 signing key (≥32 chars) |
| `Jwt:Issuer` | `EcoTrack` | JWT issuer claim |
| `Jwt:Audience` | `EcoTrack` | JWT audience claim |
| `Jwt:ExpiryMinutes` | `60` | Token lifetime in minutes |

> **Security**: Never commit real secrets. Use environment variables or secrets management in production.
