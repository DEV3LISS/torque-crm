# Security

Mapped to the **OWASP API Security Top 10 (2023)**. "Test" = an automated test that performs the attack.

| OWASP API risk | Where it could hit this API | Protection | Test |
|---|---|---|---|
| API1 Broken Object Level Authorization (IDOR) | Mechanic opens/edits another mechanic's work order by id | Ownership check in `WorkOrderService.EnsureMechanicMayTouch`; mechanic list filtered to own + unassigned | `Mechanic_cannot_touch_another_mechanics_order_even_knowing_its_id` |
| API2 Broken Authentication | Password guessing, token theft, forged tokens | PBKDF2 hashing, rate-limited login, same error + timing for unknown email/wrong password, 15-min JWT, rotating hashed refresh tokens with reuse detection, `ValidAlgorithms = HS256` | `Wrong_credentials_give_the_same_401`, `Reusing_a_rotated_token…`, `Token_signed_with_another_key_is_rejected`, `Unsigned_alg_none_token_is_rejected` |
| API3 Broken Object Property Level Authorization (mass assignment, data exposure) | Client sets `isArchived`, `id`, `role`; responses leak hashes | Request DTOs contain only allowed fields; entities have private setters; DTOs never include `PasswordHash`; audit log masks phone/email and never stores hashes | `Mass_assignment_cannot_escalate_role_on_create`, `Password_hashes_never_reach_the_audit_log` |
| API4 Unrestricted Resource Consumption | Huge bodies, unbounded pages/periods, login floods | 1 MB body limit (middleware + Kestrel), `pageSize ≤ 100`, board ≤ 62 days, reports ≤ 1 year, rate limiter on auth | `Oversized_body_is_refused`, validation tests |
| API5 Broken Function Level Authorization | Manager calls admin endpoints | Role policies (`AdminOnly`, `FrontDesk`) + fallback "authenticated" policy on every endpoint; discount > 15 % needs Admin (domain rule) | `Manager_cannot_manage_users`, `Manager_needs_admin_for_a_big_discount`, `Refund_is_admin_only…` |
| API6 Unrestricted Access to Sensitive Business Flows | Scripted mass booking, double payments | Booking needs staff auth; idempotent payments; DB-level overlap guarantee | Booking/payment race tests |
| API7 SSRF | — | The API makes no outbound calls based on user input | — |
| API8 Security Misconfiguration | Verbose errors, missing headers, default secrets | ProblemDetails without stack traces, `nosniff`, `X-Frame-Options: DENY`, CSP, `no-store`, no `Server` header; options validated at startup; secrets only from environment; Swagger can be disabled (`ApiDocs:Enabled`) | `Responses_carry_defensive_headers` |
| API9 Improper Inventory Management | Forgotten old endpoints | Single versioned surface `/api/v1`, OpenAPI document generated from code | — |
| API10 Unsafe Consumption of APIs | SMS gateway responses | Gateway behind `INotificationSender`; failures isolated in the outbox | Outbox tests |

## Injection

- **SQL injection**: EF Core parameterises LINQ; hand-written SQL uses interpolated `FormattableString`
  (`FromSql`/`SqlQuery`) → every `{value}` becomes a parameter. No string concatenation into SQL anywhere.
  Test: `Sql_injection_in_search_is_just_text`.
- **LIKE wildcards** in search input are escaped (`%`, `_`) so they cannot widen a search.
- **Log injection**: the `X-Correlation-Id` header is accepted only if short and alphanumeric.

## Secrets management

- No secret in git: `appsettings.json` has empty `Jwt:SigningKey`; Development values are dev-only; Docker Compose
  takes real values from `.env` (git-ignored, see `.env.example`).
- Production: environment variables / a secret store (e.g. Docker/Kubernetes secrets, Azure Key Vault). The app
  fails to start without a ≥32-byte signing key.
- Refresh tokens and passwords are stored only as hashes.

## Supply chain

- CI job `dependencies` fails on any known-vulnerable NuGet package (direct or transitive).
- Images: official Microsoft base images, multi-stage build (no SDK in the runtime image), non-root user.

## Not covered (would be next)

HTTPS termination and HSTS belong to the reverse proxy in front of the container; no WAF; no MFA for Admin;
no account lockout beyond rate limiting; personal-data retention policy (GDPR/152-FZ) not implemented.
