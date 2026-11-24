# Failure scenarios

What happens when things break — and how each case was verified.

| # | Scenario | Behaviour | How verified |
|---|---|---|---|
| 1 | **PostgreSQL down** while the API runs | `/health/live` 200 (process fine, do not restart it), `/health/ready` 503 (load balancer stops routing), API requests **503 + `Retry-After: 5`** instead of 500 | `DatabaseDownTests` + manual: `docker compose stop db` |
| 2 | **PostgreSQL comes back** | API recovers **without restart** — Npgsql opens new connections on demand; ready = 200 ~2 s after the DB starts | Manual chaos run (below) |
| 3 | Client **retries a payment** (timeout, double click) | Same `Idempotency-Key` → stored result, `200`, `replayed: true`; no double charge | `WorkOrderTests` (sequential and 5 parallel retries), E2E |
| 4 | **Two cashiers** pay the same order at once | Row lock (`FOR UPDATE`) serialises them; second gets 422 (amount exceeds due) | `Two_cashiers_paying_the_full_amount_at_once_cannot_overpay` |
| 5 | **Simultaneous bookings** of one slot | Exactly one 201; others 409. Under contention PostgreSQL may report a **deadlock** between inserters — mapped to 409, never 500 | 10-way race test, repeated 10× |
| 6 | **SMS gateway down** | Outbox message stays Pending, back-off 1/2/4/8 min, `Failed` after 5 attempts; `/health/status` → Degraded | `Gateway_outage_is_retried_later_and_nothing_is_lost`, `Status_shows_degraded_outbox…` |
| 7 | **Crash after booking commit, before the SMS** | Reminder is in the outbox (same transaction) → sent by the next worker run | Design (ADR-019) |
| 8 | **Crash after the SMS, before marking it sent** | Sent again (at-least-once); stable `MessageId` for gateway de-duplication | Design (ADR-019), documented trade-off |
| 9 | **Several API instances** run the outbox worker | `FOR UPDATE SKIP LOCKED` → each message processed once | `Parallel_workers_deliver_every_message_exactly_once` |
| 10 | **Concurrent edits** of one work order | Optimistic concurrency (`xmin`) → 409, totals always consistent | `Concurrent_line_edits_never_leave_wrong_totals` |
| 11 | **Stolen refresh token** reused | Whole token family revoked; both parties must log in | `Reusing_a_rotated_token_revokes_the_whole_session` |
| 12 | **Misconfiguration** (missing JWT key, unknown time zone) | App refuses to start (`ValidateOnStart`) instead of failing on first request | Options validation |
| 13 | **Huge request body** | 413 before reading it | `Oversized_body_is_refused` |

## Manual chaos run (2026-11-24, docker compose)

```
1) normal:             live=200 ready=200 customers=200 (0.070s)
2) db stopped:         live=200 ready=503 customers=503 (0.238s)
   HTTP/1.1 503 Service Unavailable
   Retry-After: 5
   {"title":"Service temporarily unavailable","status":503,"detail":"The database is not reachable right now. Please retry in a few seconds.", "correlationId":"5faef4b0…"}
3) db started again (~2 s), API NOT restarted:
                       live=200 ready=200 customers=200 (0.009s)
   api container restarts: 0
```

## Known gaps

- No automatic retry of transient DB errors inside the API (EF `EnableRetryOnFailure` conflicts with the explicit
  transactions used for payments; it would need `CreateExecutionStrategy().ExecuteAsync(...)` around them).
- No circuit breaker for the SMS gateway (the outbox back-off plays that role for now).
- Backups / point-in-time recovery are an operations concern of the hosting, not covered here.
