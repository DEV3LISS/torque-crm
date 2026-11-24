-- Fills the demo database with a realistic volume for performance testing:
-- 100 000 customers, 100 000 vehicles, ~200 000 past appointments over ~2 years in 3 bays.
-- Run:  docker compose exec -T db psql -U torque -d torque < loadtests/seed-volume.sql
\timing on
BEGIN;

INSERT INTO customers (id, full_name, phone, email, notes, is_archived, created_at, updated_at)
SELECT gen_random_uuid(),
       (ARRAY['Ivan','Maria','Alexey','Olga','Dmitry','Anna','Sergey','Elena','Pavel','Irina'])[1 + i % 10] || ' ' ||
       (ARRAY['Petrov','Sokolova','Smirnov','Kuznetsova','Volkov','Popova','Lebedev','Kozlova','Novikov','Morozova'])[1 + (i / 10) % 10] ||
       ' ' || i,
       '+7900' || lpad(i::text, 7, '0'), NULL, NULL, false, now(), now()
FROM generate_series(1, 100000) AS i;

INSERT INTO vehicles (id, customer_id, make, model, year, plate, vin, created_at, updated_at)
SELECT gen_random_uuid(), c.id, 'Lada', 'Vesta', 2020, 'T' || lpad(row_number() OVER ()::text, 6, '0') || 'XX', NULL, now(), now()
FROM customers c WHERE c.phone LIKE '+7900%';

-- Completed appointments in the past: 30-minute slots, back to back per bay (status Completed is outside
-- the exclusion constraint, so the bulk insert does not fight it).
INSERT INTO appointments (id, customer_id, vehicle_id, bay_id, starts_at, ends_at, status, comment, cancellation_reason, created_at, updated_at)
SELECT gen_random_uuid(), v.customer_id, v.id, b.id,
       now() - interval '720 days' + (n * interval '30 minutes'),
       now() - interval '720 days' + (n * interval '30 minutes') + interval '30 minutes',
       'Completed', NULL, NULL, now(), now()
FROM generate_series(0, 66666) AS n
JOIN LATERAL (SELECT id, customer_id FROM vehicles WHERE plate LIKE 'T%' OFFSET (n * 7) % 100000 LIMIT 1) v ON true
CROSS JOIN (SELECT id FROM bays) b;

COMMIT;
ANALYZE;
SELECT (SELECT count(*) FROM customers) AS customers, (SELECT count(*) FROM vehicles) AS vehicles,
       (SELECT count(*) FROM appointments) AS appointments;
