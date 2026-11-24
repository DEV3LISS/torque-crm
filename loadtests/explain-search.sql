-- 1) Customer name search exactly as the API runs it. A selective term (real names are diverse).
\echo '=== Search WITH trigram index ==='
EXPLAIN (ANALYZE, COSTS OFF)
SELECT count(*) FROM customers c WHERE NOT c.is_archived AND lower(c.full_name) LIKE '%kozlova 4242%' ESCAPE '\';

\echo '=== Search WITHOUT trigram index (dropped in a transaction, rolled back) ==='
BEGIN;
DROP INDEX ix_customers_full_name_trgm;
EXPLAIN (ANALYZE, COSTS OFF)
SELECT count(*) FROM customers c WHERE NOT c.is_archived AND lower(c.full_name) LIKE '%kozlova 4242%' ESCAPE '\';
ROLLBACK;

-- 2) Schedule board for one day. Overlap condition only (before the fix):
\echo '=== Board BEFORE: starts_at < to AND ends_at > from ==='
EXPLAIN (ANALYZE, COSTS OFF)
SELECT * FROM appointments a
WHERE a.starts_at < now() - interval '300 days' AND a.ends_at > now() - interval '301 days';

-- After the fix: an appointment never lasts longer than a day (domain guarantees it), so starts_at > from - 1 day
-- gives the index a lower bound as well.
\echo '=== Board AFTER: + starts_at > from - 1 day ==='
EXPLAIN (ANALYZE, COSTS OFF)
SELECT * FROM appointments a
WHERE a.starts_at < now() - interval '300 days' AND a.ends_at > now() - interval '301 days'
  AND a.starts_at > now() - interval '302 days';
