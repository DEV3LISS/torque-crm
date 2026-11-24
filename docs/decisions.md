# Architecture Decision Records

Each record: **Problem → Options → Decision → Arguments → Drawbacks → When to choose an alternative.**

| # | Decision | Area |
|---|---|---|
| 001 | Modular monolith with layers | Architecture |
| 002 | No repository layer over EF Core | Architecture |
| 003 | Feature service classes instead of MediatR | Architecture |
| 004 | Roles as an enum column | Data / Security |
| 005 | Exceptions mapped to ProblemDetails in one place | Errors |
| 006 | JWT + rotating hashed refresh tokens | Security |
| 007 | Real PostgreSQL in tests (Testcontainers) | Testing |
| 008 | .NET 10 LTS | Platform |
| 009 | Double-booking prevented by a PostgreSQL exclusion constraint | Concurrency |
| 010 | Store UTC, compute opening hours in the business time zone | Data |
| 011 | Optimistic concurrency via `xmin` | Concurrency |
| 012 | Customer search: normalised phone + trigram index | Performance |
| 013 | Idempotency keys for payments | Reliability |
| 014 | Pessimistic row lock for payments, optimistic for editing | Concurrency |
| 015 | Audit log via EF Core interceptor, same transaction | Observability / Security |
| 016 | Work order as aggregate: stored totals, price snapshots, append-only payments | Data |
| 017 | Application-generated keys: `ValueGeneratedNever` | Data |
| 018 | Reports as a SQL read model | Performance / CQRS |
| 019 | Transactional outbox with `FOR UPDATE SKIP LOCKED` for reminders | Reliability |
| 020 | OpenTelemetry: Prometheus metrics + OTLP traces, business metrics | Observability |
| 021 | Liveness vs readiness; database outage → 503 | Resilience |

---

## ADR-001 Modular monolith with layers

**Problem.** How to structure the backend so it stays easy to change and test, for a small team and one business.

**Options.** (a) Single project, controllers talk to the DB; (b) layered monolith (Domain / Application / Infrastructure / Api); (c) microservices.

**Decision.** (b).

**Arguments.** Business rules live in Domain with no framework references, so they are unit-tested in milliseconds. One deployable unit means real ACID transactions and trivial debugging. Layer rules are enforced by architecture tests, not by goodwill.

**Drawbacks.** More projects and mapping code than (a). Cross-layer changes touch several files.

**Alternatives.** (a) for a throw-away prototype or a tiny CRUD API. (c) only when separate teams need independent deployments or parts have very different scaling needs — it brings network failures, distributed transactions, and operational cost.

## ADR-002 No repository layer over EF Core

**Problem.** Should use cases access data through custom repositories or through EF Core directly?

**Options.** (a) Generic `IRepository<T>`; (b) specific repositories per aggregate; (c) `IAppDbContext` exposing `DbSet`s.

**Decision.** (c).

**Arguments.** `DbContext` already implements Unit of Work and `DbSet` a repository. Direct access keeps projections, `AsNoTracking`, joins and `ExecuteUpdate` available; no extra layer to maintain. Integration tests on a real DB cover the queries.

**Drawbacks.** Application references the EF Core package (not the PostgreSQL provider). Unit-testing a use case in isolation needs a real/in-memory DB rather than a mock.

**Alternatives.** (b) when the persistence technology may really change, or when complex queries should be hidden behind intention-revealing methods for a large team.

## ADR-003 Feature service classes instead of MediatR

**Problem.** How do controllers invoke use cases?

**Options.** (a) MediatR commands/handlers with pipeline behaviours; (b) one handler class per use case; (c) one service class per feature.

**Decision.** (c): `AuthService`, `UserService`, `CustomerService`, `CatalogService`, `AppointmentService`.

**Arguments.** "Go to definition" lands on the code; fewer files; easy to explain. MediatR became commercially licensed in 2025.

**Drawbacks.** Service classes grow; cross-cutting concerns (validation) are called explicitly in each method rather than in a pipeline.

**Alternatives.** (b) when a service exceeds ~10 use cases or many teams work on one feature; (a) when many cross-cutting behaviours (transactions, caching, metrics) must wrap every use case uniformly.

## ADR-004 Roles as an enum column

**Problem.** How to model staff permissions.

**Options.** (a) Roles + UserRoles tables; (b) roles + permissions tables (RBAC); (c) enum column + code policies.

**Decision.** (c): `users.role` with a CHECK constraint; policies `AdminOnly`, `FrontDesk`; fallback policy requires authentication.

**Arguments.** Three fixed roles that the business never edits. No joins, no admin screens for roles. YAGNI.

**Drawbacks.** One role per user; adding a role needs a deploy.

**Alternatives.** (b) when customers (tenants) must define their own roles/permissions — that is a typical SaaS requirement.

## ADR-005 Exceptions mapped to ProblemDetails in one place

**Problem.** Use cases must report "not found", "conflict", "rule violated" without knowing about HTTP, and clients need one error format.

**Options.** (a) Typed exceptions + global handler; (b) `Result<T>` return values; (c) controllers return status codes themselves.

**Decision.** (a), RFC 9457 ProblemDetails with `correlationId`. Database constraint violations (unique 23505, exclusion 23P01, FK 23503) and concurrency conflicts are mapped there too.

**Arguments.** Thin controllers, consistent responses, impossible to "forget" to handle an error path.

**Drawbacks.** Exceptions are slower than return values and make the failure path less visible in signatures.

**Alternatives.** (b) when errors are frequent (high-throughput validation) or the team prefers explicit railway-style code.

## ADR-006 JWT + rotating hashed refresh tokens

**Problem.** Stateless API authentication that still allows logout, deactivation and theft containment.

**Options.** (a) Cookie sessions; (b) long-lived JWT only; (c) short JWT + refresh token; (d) external identity provider (Keycloak, Auth0, Entra ID).

**Decision.** (c): 15-min HS256 JWT + 14-day refresh token stored as SHA-256 hash, rotated on every use; reuse of a rotated token revokes the whole family; deactivation revokes all tokens.

**Arguments.** Works for any client (SPA, mobile). Stolen DB dump does not leak usable tokens. Theft is detected on reuse.

**Drawbacks.** A deactivated user's access token lives up to 15 minutes. HS256 means the API both issues and validates tokens.

**Alternatives.** (d) when there are several services, SSO, or social logins (then RS256 / OIDC). (a) for a server-rendered web app on one domain.

## ADR-007 Real PostgreSQL in tests (Testcontainers)

**Problem.** The most important rules live in the database (unique indexes, exclusion constraint, `xmin`). How to test them honestly?

**Options.** (a) EF InMemory provider; (b) SQLite; (c) PostgreSQL in Docker via Testcontainers; (d) shared test database server.

**Decision.** (c), one container per test run, Respawn wipes data between tests.

**Arguments.** Tests exercise the exact SQL and constraints of production, including real races (10 parallel bookings).

**Drawbacks.** Docker required; suite takes seconds, not milliseconds.

**Alternatives.** (a)/(b) never for constraint behaviour — they would give false confidence. (d) when Docker is unavailable in CI.

## ADR-008 .NET 10 LTS

**Problem.** Which runtime to target. **Options.** .NET 10 (LTS, to Nov 2028) or .NET 11 (STS, Nov 2026). **Decision.** .NET 10. **Arguments.** Companies standardise on LTS. **Drawbacks.** Newest language/runtime features arrive later. **Alternatives.** STS for short-lived projects that want the latest features.

## ADR-009 Double-booking prevented by a PostgreSQL exclusion constraint

**Problem.** Two managers book the same bay for overlapping times at the same moment. A C# check "is the slot free? → insert" has a race window: both requests read "free", both insert.

**Options.**
1. Application check only — **broken under concurrency**.
2. `SELECT … FOR UPDATE` on the bay row before inserting (pessimistic lock serialises bookings per bay).
3. `SERIALIZABLE` isolation + retry on serialization failure.
4. PostgreSQL advisory lock per bay.
5. Exclusion constraint: `EXCLUDE USING gist (bay_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&) WHERE (status IN ('Scheduled','CheckedIn'))`.

**Decision.** 5, plus application checks for friendly messages. Violation (SQLSTATE 23P01) → `409 Conflict`.

**Arguments.** The invariant is declared once, in the database, and holds for every code path — API, scripts, future services, manual SQL. No locks held in application code; no retries needed. The GiST index also speeds up "appointments of bay X in period Y". Proven by a test firing 10 simultaneous requests: exactly 1 × 201, 9 × 409.

**Drawbacks.** PostgreSQL-specific (needs `btree_gist`); EF Core cannot model it, so it lives in hand-written migration SQL; the error message comes from the DB layer. Found under load: three or more *simultaneous* inserts of overlapping rows can **deadlock** each other while checking the constraint (B waits for C, C waits for B); PostgreSQL aborts one after `deadlock_timeout` (1 s). The victim always overlapped the winner, so the API maps `40P01` to `409 Conflict` (retryable), never 500. If contention per bay became high, add `pg_advisory_xact_lock(bay)` before the insert to queue bookings per bay and avoid deadlocks entirely.

**Alternatives.** Option 2 when the database cannot express the rule (e.g. MySQL) or the rule depends on several tables (e.g. "max 3 bookings per mechanic per day"). Option 3 for complex multi-row invariants, accepting retries.

## ADR-010 Store UTC, compute opening hours in the business time zone

**Problem.** Opening hours are local ("09:00 in Yekaterinburg") but instants must be comparable and unambiguous. Clients may send `10:00+05:00`.

**Options.** (a) Store local time without zone; (b) store UTC (`timestamptz`) and convert with a configured IANA zone; (c) store local time + zone per row.

**Decision.** (b). The domain normalises every incoming instant with `ToUniversalTime()`; `BusinessCalendar` converts local opening hours to UTC; zone comes from `Business:TimeZone` and is validated at startup.

**Arguments.** Correct ordering and overlap checks; DST-safe; one place for conversion. Bug found during development: Npgsql refuses to write a non-UTC `DateTimeOffset` to `timestamptz` — normalising in the domain fixed it for every client.

**Drawbacks.** Every "local date" computation needs the calendar; reports must group by local date explicitly.

**Alternatives.** (c) for multi-location businesses in different time zones (then the zone belongs to the location).

## ADR-011 Optimistic concurrency via `xmin`

**Problem.** Two users change the same record (refresh token, appointment status) at the same time: lost update.

**Options.** (a) Last write wins; (b) pessimistic locks; (c) optimistic concurrency with a version column; (d) PostgreSQL `xmin` system column as the version.

**Decision.** (d), via `IsRowVersion()` on a shadow `uint` property. Conflict → `DbUpdateConcurrencyException` → `409`.

**Arguments.** No extra column or trigger; PostgreSQL changes `xmin` on every update. No locks held while the user thinks.

**Drawbacks.** The client must reload and retry on 409. `xmin` is PostgreSQL-specific.

**Alternatives.** (b) for short, high-contention operations where retries are expensive (e.g. a payment against an order — see M3).

## ADR-012 Customer search: normalised phone + trigram index

**Problem.** Front desk types a phone in any format ("8 912…", "+7(912)…", last digits) or part of a name; must be fast on 100k+ customers.

**Options.** (a) `LIKE '%…%'` without index; (b) PostgreSQL full-text search; (c) trigram (`pg_trgm`) GIN index on `lower(full_name)`; (d) Elasticsearch.

**Decision.** Phones stored normalised (E.164) and matched by digits (with "8 → +7" handling); names matched with `LIKE` on `lower(full_name)` backed by a trigram GIN index. LIKE wildcards in user input are escaped.

**Arguments.** "Contains" search on names needs trigrams (B-tree cannot help `%x%`; full-text works on words, not fragments). No extra infrastructure.

**Drawbacks.** Trigram index costs write time and disk; fuzzy/typo-tolerant search is not implemented yet.

**Alternatives.** (d) when search becomes a product feature (typos, ranking, many fields) or data grows to tens of millions of rows.

## ADR-013 Idempotency keys for payments

**Problem.** The cashier clicks "Pay" and the network times out. Did the payment go through? The client retries — and the customer may be charged twice. Double clicks and mobile reconnects cause the same.

**Options.** (a) Do nothing, rely on the UI to disable the button; (b) detect duplicates by content ("same amount for the same order within N seconds"); (c) client-generated `Idempotency-Key` header stored with a unique index.

**Decision.** (c). The key is required. A repeated key with the same content returns the stored result with `200` and `replayed: true`; the same key with different content is rejected (`422`); two identical retries racing in parallel are resolved by the unique index — the loser returns the winner's result.

**Arguments.** Industry standard (Stripe, payment APIs, IETF draft "Idempotency-Key HTTP header"). Works no matter where the failure happened. Legitimate identical payments (two 500 ₽ cash payments) are still possible with different keys — option (b) would block them.

**Drawbacks.** Clients must generate and keep the key per payment attempt. Keys are stored forever (could be expired after N days).

**Alternatives.** (b) only as an extra fraud heuristic, never as the main guarantee.

## ADR-014 Pessimistic row lock for payments, optimistic for editing

**Problem.** Two cashiers take a payment for the same order at the same time. Each reads "amount due 1000", each accepts 1000 → the customer pays 2000.

**Options.** (a) Optimistic concurrency (`xmin`) + retry; (b) `SELECT … FOR UPDATE` on the order row inside a transaction; (c) `SERIALIZABLE` isolation; (d) a DB CHECK `paid_amount <= total` alone.

**Decision.** (b) for payments, plus (d) as the last line of defence. Editing lines/discounts uses (a) — see ADR-011.

**Arguments.** Payment is a short, high-value operation where "wait a few ms" is better than "fail and retry": the second transaction waits for the lock, then sees the updated amount due and gets a clear business error. Proven by a test: 5 parallel full payments → exactly one succeeds, paid amount = total. Editing is long-lived (a person looks at the screen), where holding locks is wrong — optimistic fits.

**Drawbacks.** Locks serialise payments per order (fine — contention per order is tiny). Requires an explicit transaction and raw SQL (`FOR UPDATE`) in Infrastructure. Bug found: `SELECT *` does not return the `xmin` system column, which EF needs — the query must select it explicitly.

**Alternatives.** (c) when the rule spans many rows/tables and cannot be expressed as one lock; (a) when conflicts are rare and retries are cheap.

## ADR-015 Audit log via EF Core interceptor, same transaction

**Problem.** "Who gave this discount? Who changed the price?" Every change of business data must be traceable, and the trail must not be forgettable or lossy.

**Options.** (a) Explicit audit calls in each use case; (b) EF Core `SaveChangesInterceptor`; (c) database triggers; (d) event sourcing; (e) CDC (Debezium) to a separate store.

**Decision.** (b): the interceptor inspects the change tracker and adds `audit_logs` rows (user, correlation id, entity, action, changed fields as JSONB) inside the same `SaveChanges` → same transaction. Password hashes are never written; phones/emails are masked.

**Arguments.** Cannot be forgotten; new entities are covered automatically; atomic with the change; knows the application user (a trigger does not).

**Drawbacks.** Records *what* changed, not *why*. Bypassed by raw SQL / `ExecuteUpdate`. Grows quickly — needs partitioning/retention eventually.

**Alternatives.** (c) when other systems also write to the DB; (d) when history *is* the domain (ledgers); (e) for very high volume or analytics.

## ADR-016 Work order as aggregate: stored totals, price snapshots, append-only payments

**Problem.** Totals must always match lines; price list changes must not rewrite history; money records must be trustworthy.

**Decision.** `WorkOrder` is an aggregate root: lines change only through it, every change first *computes and validates* new totals and only then mutates (a rejected change leaves the order untouched). Totals are stored (fast reports) and guarded by DB CHECK constraints (`total = subtotal - discount_amount`, `paid_amount <= total`). Lines copy name and price from the catalog (snapshot). Payments are append-only, amounts always positive, direction in `type`; corrections are refunds.

**Arguments.** Mirrors accounting practice; consistent under concurrency together with ADR-011/014; reports don't re-aggregate millions of lines.

**Drawbacks.** Stored totals are denormalised data — must only ever change through the aggregate.

**Alternatives.** Compute totals on read (simpler, slower reports) for small datasets.

## ADR-017 Application-generated keys: `ValueGeneratedNever`

**Problem.** Entities get UUIDv7 ids in the constructor. EF Core by default treats Guid keys as *database-generated*: a new child entity added to a tracked aggregate already has a non-empty key, so EF assumes it exists and issues `UPDATE` instead of `INSERT` — 0 rows affected → a false `DbUpdateConcurrencyException`.

**Decision.** All `Entity.Id` properties are configured `ValueGeneratedNever()` in one loop in `AppDbContext`.

**Arguments.** Ids known before saving (useful for links, events, idempotency); no DB round-trip; tells EF the truth.

**Drawbacks.** A convention every new entity relies on — covered by tests adding children to aggregates.

**Alternatives.** Database-generated keys (sequence/identity) — then ids are unknown until `SaveChanges`.

## ADR-018 Reports as a SQL read model

**Problem.** Reports aggregate payments and work orders by *local* day/week/month, clip appointments to a period, and must stay fast as data grows.

**Options.** (a) LINQ over EF entities; (b) load rows and aggregate in C#; (c) hand-written SQL behind an `IReportQueries` interface (a small CQRS read side); (d) a separate reporting DB / materialised views.

**Decision.** (c). Application defines `IReportQueries`; Infrastructure implements it with `Database.SqlQuery<T>` and fully parameterised interpolated SQL (`date_trunc`, `AT TIME ZONE`, `FILTER`, range clipping). Open time for utilisation is computed in C# from the opening hours.

**Arguments.** The database does the aggregation (no millions of rows over the wire); time-zone-correct grouping is one expression in SQL; the use case stays testable and unaware of SQL. Covered by integration tests against real PostgreSQL.

**Drawbacks.** SQL is not checked by the compiler — only by tests. Bug found by a manual check: `LEAST`/`GREATEST` ignore NULLs in PostgreSQL, so a bay with no appointments (LEFT JOIN) showed 218 % utilisation; fixed with `FILTER (WHERE a.id IS NOT NULL)` + regression test.

**Alternatives.** (d) materialised views or a separate analytics store when reports get heavy or must not load the transactional DB.

## ADR-019 Transactional outbox with `FOR UPDATE SKIP LOCKED` for reminders

**Problem.** "Send an SMS the day before the appointment." Sending directly from the booking request is wrong twice: the booking may roll back after the SMS was sent, or the app may crash after commit and the reminder is lost. Also, several API instances must not send the same reminder twice.

**Options.** (a) Send inside the request; (b) Hangfire/Quartz scheduled job; (c) message broker (RabbitMQ/Kafka); (d) transactional outbox table + background worker.

**Decision.** (d). The booking writes an `outbox_messages` row (due 24 h before, skipped if booked < 2 h ahead) in the same transaction. A `BackgroundService` polls due rows with `SELECT … FOR UPDATE SKIP LOCKED` (multiple instances never take the same row), re-checks reality (cancelled or moved → `Skipped`), sends, and marks `Sent`. Failures back off exponentially (1, 2, 4, 8 min) and become `Failed` after 5 attempts. Delivery is **at-least-once**; the stable message id lets a gateway de-duplicate.

**Arguments.** No new infrastructure; atomic with the business change; survives crashes and gateway outages; horizontally safe. Proven by tests: sent once, cancelled → skipped, rescheduled → only new time, outage → retried, 3 parallel workers → each message exactly once.

**Drawbacks.** Polling latency (seconds); the outbox table needs cleanup of old rows; a send that succeeded right before a crash is repeated.

**Alternatives.** (c) when many services must react to events or volume is high (the outbox would then *publish* to the broker); (b) when a dashboard, cron expressions and job history are needed out of the box.

## ADR-020 OpenTelemetry: Prometheus metrics + OTLP traces, business metrics

**Problem.** Logs answer "what happened in this request?" but not "is the system healthy right now?", "which endpoint got slower?", "are bookings and payments still flowing?".

**Options.** (a) Logs only; (b) vendor agent (Datadog, New Relic); (c) `prometheus-net` for metrics; (d) OpenTelemetry — vendor-neutral standard for metrics and traces.

**Decision.** (d). Metrics: ASP.NET Core (request duration by route/status = RED), runtime (GC, thread pool), Npgsql (pool, command duration) and **business metrics** (`torque.appointments.booked`, `torque.payments.amount{type,method}`, `torque.payments.replayed`, `torque.outbox.processed{outcome}`) via the BCL `System.Diagnostics.Metrics` API — so Application has no monitoring dependency. Exported at `/metrics` for Prometheus. Traces: ASP.NET Core + Npgsql spans, exported via OTLP when `OpenTelemetry:OtlpEndpoint` is set (Jaeger in the `observability` compose profile).

**Arguments.** One standard, any backend (Prometheus/Grafana, Jaeger/Tempo, cloud vendors) without code changes. Business metrics make alerts meaningful ("no bookings for 2 hours on a weekday").

**Drawbacks.** The Prometheus exporter package is still beta (`1.21.0-beta.1`) — accepted, it is widely used and the OTLP path is stable. `/metrics` is anonymous for the demo; in production it must be reachable only from the internal network.

**Alternatives.** (b) when a company already pays for a vendor; (c) for a metrics-only setup with a stable package.

## ADR-021 Liveness vs readiness; database outage → 503

**Problem.** When PostgreSQL is down, what should the API and the orchestrator do?

**Options.** (a) One health endpoint that fails when the DB fails; (b) separate liveness (process OK) and readiness (dependencies OK); (c) also map DB connectivity errors to 503.

**Decision.** (b) + (c). `/health/live` checks nothing external (restarting would not fix the DB); `/health/ready` checks the DB (stop routing traffic). Connection errors (`NpgsqlException` transient, socket, timeout) found anywhere in the exception chain → `503` with `Retry-After: 5`. `/health/status` (Admin) adds the outbox state (Degraded when reminders are stuck).

**Arguments.** Orchestrators restart on failed liveness — with (a) a DB outage would cause a restart storm. 503 tells clients and proxies "retry later", 500 says "bug". Verified by a manual chaos run: DB stopped → 503; DB started → recovered in ~2 s with 0 API restarts.

**Drawbacks.** No automatic retry of transient errors inside the request (would need execution strategies around the explicit transactions).

**Alternatives.** EF `EnableRetryOnFailure` + `CreateExecutionStrategy` wrappers when brief network blips are common (cloud databases).
