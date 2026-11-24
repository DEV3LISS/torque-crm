# Torque CRM — Architecture

## 1. Shape: a layered modular monolith

One deployable ASP.NET Core Web API, split into four projects with dependencies pointing **inward**:

```
            ┌──────────────────────────┐
            │      Torque.Api          │  HTTP: endpoints, auth, ProblemDetails, OpenAPI
            └────────────┬─────────────┘
                         │ uses
            ┌────────────▼─────────────┐
            │   Torque.Application     │  use cases: BookAppointment, AddPayment, reports
            └────────────┬─────────────┘
                         │ uses
            ┌────────────▼─────────────┐
            │     Torque.Domain        │  entities, rules, invariants — no frameworks
            └──────────────────────────┘
                         ▲ implements interfaces from Application
            ┌────────────┴─────────────┐
            │  Torque.Infrastructure   │  EF Core + PostgreSQL, JWT, clock, audit, outbox
            └──────────────────────────┘
```

**Why a monolith, not microservices:** one team, one database, one business. Microservices would add network failures, distributed transactions and deployment overhead with zero benefit at this size. Splitting into layers gives the testability and clarity without the cost. (ADR-001)

**Why these layers:**
- `Domain` holds the rules that make the business correct ("an order line cannot change after completion", "payments cannot exceed the amount due"). It has no reference to EF Core or ASP.NET, so it is tested with plain fast unit tests.
- `Application` orchestrates use cases, grouped in one service class per feature (`AuthService`, `UserService`, …): validate input, load data, call domain, save, return a DTO. It depends on abstractions (`IAppDbContext`, `ITokenService`, `ICurrentUser`, `TimeProvider`), not on concrete infrastructure.
- `Infrastructure` is replaceable detail: database, token issuing, system clock.
- `Api` is a thin adapter from HTTP to use cases.

We deliberately **do not** add a repository layer on top of EF Core: `DbContext` already is a unit of work + repository. A generic repository would hide EF features (projections, `AsNoTracking`, `ExecuteUpdate`) and add code to maintain. (ADR-002)

We deliberately **do not** use MediatR: plain injected service classes are simpler to read, debug (F12 goes straight to the code) and explain; the library also became commercial in 2025. (ADR-003)

## 2. Request flow (example: book an appointment)

```
POST /api/v1/appointments
  → JWT auth + role policy "Front desk"          (Api)
  → FluentValidation: required fields, 15-min grid (Api)
  → AppointmentService.BookAsync()              (Application)
       load customer, vehicle, services, bay
       Appointment.Schedule(...)  — domain checks: working hours, not in past, vehicle belongs to customer
       db.SaveChangesAsync()
          → AuditInterceptor writes AuditLog rows in the same transaction
          → PostgreSQL EXCLUDE constraint rejects overlap → mapped to 409 Conflict
  → 201 Created + Location header
```

## 3. Cross-cutting concerns

| Concern | How |
|---------|-----|
| Errors | Domain throws `DomainException` (422) / `InvalidStateException` (409); Application throws `NotFoundException`, `ConflictException`, `UnauthorizedException`, `ForbiddenException`; FluentValidation throws `ValidationException` (400). One `GlobalExceptionHandler` maps all of them (and PostgreSQL constraint violations) to `ProblemDetails`. Unknown errors → 500 with a correlation id, details only in logs. (ADR-005) |
| Validation | FluentValidation for request shape; domain methods for business rules. Two layers on purpose: shape errors are user typos, business errors are rule violations. |
| Auth | JWT bearer; policies `AdminOnly`, `FrontDesk` (Admin+Manager), `Staff` (all). Refresh tokens in DB, hashed, rotated. |
| Concurrency | Bay overlap — DB exclusion constraint. Order editing — optimistic concurrency via PostgreSQL `xmin` system column. Payments — idempotency key with unique index. |
| Time | `TimeProvider` injected everywhere (built into .NET) → tests control "now". Stored UTC, converted to business zone only for working-hours rules and reports. |
| Audit | EF Core `SaveChangesInterceptor` collects changed entries and writes `AuditLogs` in the same `SaveChanges`. |
| Logging | Serilog, request logging, correlation id middleware, enrichers (user id, not name/phone). |
| Config | `appsettings.json` + environment variables; secrets never committed; options validated on startup (`ValidateOnStart`). |
| Background work | Phase 2: `BackgroundService` processing an `OutboxMessages` table for reminders. |

## 4. Testing strategy

| Level | Tool | What |
|-------|------|------|
| Unit | xUnit + Shouldly | Domain rules: status transitions, totals, discount limits, phone normalisation |
| Integration | `WebApplicationFactory` + Testcontainers (real PostgreSQL) + Respawn | Full HTTP → DB: auth, double-booking race (parallel requests), idempotent payments, audit rows written |
| Architecture | NetArchTest | Domain does not reference EF Core / ASP.NET |

Why real PostgreSQL and not the EF InMemory provider: the most important rules (exclusion constraint, unique indexes, `xmin` concurrency) exist only in a real database. InMemory would make those tests lie.

## 5. Solution layout

```
torque-crm/
├── src/
│   ├── Torque.Api/
│   ├── Torque.Application/
│   ├── Torque.Domain/
│   └── Torque.Infrastructure/
├── tests/
│   ├── Torque.UnitTests/
│   ├── Torque.IntegrationTests/
│   └── Torque.ArchitectureTests/
├── docs/            requirements, architecture, database, decisions
├── docker-compose.yml
├── Dockerfile
├── .github/workflows/ci.yml
└── README.md
```

## 6. Possible extensions (not built, but designed for)

- **Multi-tenancy**: add `TenantId` to aggregates + EF global query filter + PostgreSQL row-level security.
- **Frontend**: the API is UI-agnostic; a small React/Blazor admin can be added.
- **Read replicas / caching** for reports if data grows — reports are isolated in their own query handlers.
