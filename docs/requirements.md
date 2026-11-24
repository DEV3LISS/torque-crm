# Torque CRM — Requirements

Backend for a small car service / tyre shop: customers and their vehicles, booking into service bays without double-booking, work orders, payments, reporting and a full audit trail.

## 1. Context

A typical client: a tyre shop or car service with 2–6 service bays, 3–10 staff, 20–80 visits a day, peak load in spring and autumn (seasonal tyre change). Today they book by phone into a paper notebook or a spreadsheet. Pain points:

- double-booking the same bay at the same time;
- no history: "what did we do to this car last season?";
- the owner cannot see revenue and staff load without counting by hand;
- money disputes: "who gave this discount?", "was this order paid?".

## 2. Roles

| Role | Who | Can |
|------|-----|-----|
| `Admin` | Owner | Everything, including users, prices, reports, audit log |
| `Manager` | Front desk | Customers, vehicles, bookings, work orders, payments |
| `Mechanic` | Bay worker | Sees own schedule, starts/completes work, adds items to a work order |

Roles are fixed — the business does not invent new roles, so they are an enum, not a table (see ADR-004).

## 3. Functional requirements

### FR-1 Authentication
- Login with email + password → short-lived JWT access token (15 min) + refresh token (14 days, rotated on every use, stored hashed).
- Logout revokes the refresh token. Reuse of an already-rotated refresh token revokes the whole token family (theft detection).
- Login is rate-limited (5 attempts / minute / IP).
- First Admin is created by a seed/CLI command, not by an open endpoint.

### FR-2 Users
- Admin creates, deactivates and changes the role of users. Users are never hard-deleted (history references them).

### FR-3 Customers & vehicles
- Customer: name, phone (required, unique, normalised to E.164: `+79123456789`), optional email, notes.
- Search by any phone format (`8 912 345-67-89`, `+7(912)3456789`) and by name fragment; paginated.
- A customer has many vehicles: make, model, year, licence plate (unique, normalised: uppercase, Cyrillic look-alikes mapped to Latin), VIN optional.
- Customers are archived, not deleted.
- Customer card: vehicles + visit history + total spent.

### FR-4 Service catalog
- Service: name, category, default duration (minutes, multiple of 15), price.
- Price change does **not** change past orders (price is copied into the order line).
- Services can be deactivated (hidden from new bookings, kept in history).

### FR-5 Service bays & working hours
- Bays (e.g. "Bay 1 — tyres", "Bay 2 — lift").
- Business working hours per weekday, business time zone (default `Asia/Yekaterinburg`).

### FR-6 Booking (core business rule)
- `GET /availability?date&serviceIds` → free slots per bay, step 15 min, length = sum of service durations.
- Create appointment: customer, vehicle, bay, start, services; end = start + total duration.
- **Invariant: a bay never has two active appointments that overlap in time.** Enforced in the database (PostgreSQL exclusion constraint), not only in C#, so two managers clicking at the same second cannot both succeed. The loser gets `409 Conflict`.
- Cannot book in the past or outside working hours.
- Lifecycle: `Scheduled → CheckedIn → Completed`; `Scheduled → Cancelled | NoShow`. Invalid transitions → `409`.
- Reschedule = change start/bay with the same overlap guarantee.

### FR-7 Work orders
- Created from an appointment at check-in, or as a walk-in without an appointment.
- Lines: services (from catalog, price snapshot) and parts/consumables (free-text name, quantity, unit price).
- Discount per order (percent or amount); discount above 15% requires `Admin`.
- Lifecycle: `Open → InProgress → Completed → Closed` and `Cancelled`. Lines are editable only before `Completed`.
- Optimistic concurrency: two people editing one order — the second save gets `409` instead of silently overwriting.

### FR-8 Payments
- Several payments per order (part cash, part card). Methods: `Cash`, `Card`, `Transfer`.
- Sum of payments cannot exceed the amount due; order becomes `Closed` when fully paid.
- Refund = separate payment record with type `Refund` (Admin only); payments are never edited or deleted.
- **Idempotency:** `POST /payments` requires an `Idempotency-Key` header; repeating the request (double click, network retry) returns the first result and does not charge twice.

### FR-9 Reports (Admin)
- Revenue by day/week/month, by payment method.
- Top services by revenue and count.
- Bay utilisation (% of working time booked).
- Mechanic workload (completed orders, hours).

### FR-10 Audit log
- Every create/update/delete of business entities is recorded automatically: who, when, entity, id, action, changed fields (old → new) in JSONB.
- Admin can filter the log by entity, user and date.
- Audit write happens in the same transaction as the change (no change without audit, no audit without change).

### FR-11 Reminders (phase 2)
- Background job sends a reminder the day before the appointment (Telegram or SMS gateway behind an interface; a fake sender in dev/tests).
- Delivery via outbox table so a crash never loses or duplicates a reminder.

## 4. Non-functional requirements

| Area | Requirement |
|------|-------------|
| API | REST, JSON, versioned under `/api/v1`, OpenAPI document + Swagger UI |
| Errors | RFC 9457 `ProblemDetails` everywhere; validation errors list fields; no stack traces outside Development |
| Validation | All input validated at the edge (FluentValidation); business rules in the domain |
| Security | Passwords hashed (PBKDF2 via ASP.NET Core `PasswordHasher`), JWT, role policies, rate limiting, secrets only via env vars, CORS allow-list |
| Data | PostgreSQL; all timestamps `timestamptz` in UTC; money `numeric(12,2)`; migrations via EF Core |
| Logging | Structured logs (Serilog, JSON in prod), correlation id per request, no personal data in logs |
| Health | `/health/live`, `/health/ready` (checks DB) |
| Tests | Unit tests for domain rules; integration tests against real PostgreSQL in Testcontainers; CI runs all on every push |
| Run | `docker compose up` starts API + DB with seed data; README explains it in under 5 steps |
| Performance | p95 < 200 ms for typical reads on 100k customers / 1M appointments (indexes documented) |

## 5. Out of scope (deliberately)

- Frontend (API + Swagger is the product for the portfolio; a thin UI may come later).
- Parts inventory and suppliers — stock management is the subject of the e-commerce project.
- Multi-tenancy (one business per deployment) — noted as a possible extension in the architecture doc.
- Online payment acquiring — payments are recorded, not processed.
