using Microsoft.EntityFrameworkCore;
using Torque.Application.Reports;
using Torque.Infrastructure.Persistence;

namespace Torque.Infrastructure.Reports;

/// <summary>
/// Report SQL. Every value is passed as a parameter ({…} in an interpolated FormattableString becomes @p0, @p1…),
/// so there is no SQL injection even though the SQL is hand-written. The grouping unit is not user text:
/// it comes from an enum and is mapped to a fixed whitelist.
/// </summary>
public sealed class ReportQueries(AppDbContext db) : IReportQueries
{
    public async Task<IReadOnlyList<RevenueRow>> RevenueAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc,
        RevenueGrouping groupBy, string timeZone, CancellationToken ct)
    {
        var unit = groupBy switch
        {
            RevenueGrouping.Week => "week",
            RevenueGrouping.Month => "month",
            _ => "day",
        };

        // created_at AT TIME ZONE 'Asia/Yekaterinburg' → local wall-clock time, so a payment at 23:30 local
        // lands on the right local day (in UTC it would be the next/previous day).
        var rows = await db.Database.SqlQuery<RevenueSqlRow>($"""
            SELECT (date_trunc({unit}, p.created_at AT TIME ZONE {timeZone}))::date              AS period,
                   COALESCE(SUM(p.amount) FILTER (WHERE p.type = 'Payment'), 0)                AS payments,
                   COALESCE(SUM(p.amount) FILTER (WHERE p.type = 'Refund'), 0)                 AS refunds,
                   COALESCE(SUM(CASE WHEN p.type = 'Payment' THEN p.amount ELSE -p.amount END), 0) AS net,
                   (COUNT(*) FILTER (WHERE p.type = 'Payment'))::int                           AS payment_count
            FROM payments p
            WHERE p.created_at >= {fromUtc} AND p.created_at < {toUtc}
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(ct);

        return rows.Select(r => new RevenueRow(DateOnly.FromDateTime(r.Period), r.Payments, r.Refunds, r.Net, r.PaymentCount)).ToList();
    }

    public async Task<IReadOnlyList<RevenueByMethodRow>> RevenueByMethodAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<MethodSqlRow>($"""
            SELECT p.method AS method,
                   SUM(CASE WHEN p.type = 'Payment' THEN p.amount ELSE -p.amount END) AS net
            FROM payments p
            WHERE p.created_at >= {fromUtc} AND p.created_at < {toUtc}
            GROUP BY p.method
            ORDER BY net DESC
            """).ToListAsync(ct);
        return rows.Select(r => new RevenueByMethodRow(r.Method, r.Net)).ToList();
    }

    /// <summary>Revenue per service line, before order-level discounts (discounts belong to the order, not a line).</summary>
    public async Task<IReadOnlyList<TopServiceRow>> TopServicesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<TopServiceSqlRow>($"""
            SELECT l.service_id AS service_id, l.name AS name,
                   COUNT(*)::int AS times_sold, SUM(l.line_total) AS revenue
            FROM work_order_lines l
            JOIN work_orders w ON w.id = l.work_order_id
            WHERE l.kind = 'Service'
              AND w.status IN ('Completed', 'Closed')
              AND w.completed_at >= {fromUtc} AND w.completed_at < {toUtc}
            GROUP BY l.service_id, l.name
            ORDER BY revenue DESC, times_sold DESC
            LIMIT {limit}
            """).ToListAsync(ct);
        return rows.Select(r => new TopServiceRow(r.ServiceId, r.Name, r.TimesSold, r.Revenue)).ToList();
    }

    /// <summary>Minutes booked per active bay, counting only the part of each appointment inside the period.</summary>
    public async Task<IReadOnlyList<BookedMinutesRow>> BookedMinutesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<BookedSqlRow>($"""
            SELECT b.id AS bay_id, b.name AS bay_name,
                   -- FILTER is essential: PostgreSQL's LEAST/GREATEST ignore NULLs, so for a bay with no
                   -- appointments (LEFT JOIN → NULLs) the expression would yield the whole period, not 0.
                   COALESCE(SUM(EXTRACT(EPOCH FROM (LEAST(a.ends_at, {toUtc}) - GREATEST(a.starts_at, {fromUtc}))) / 60)
                            FILTER (WHERE a.id IS NOT NULL), 0)::int AS booked_minutes
            FROM bays b
            LEFT JOIN appointments a
                   ON a.bay_id = b.id
                  AND a.status IN ('Scheduled', 'CheckedIn', 'Completed')
                  AND a.starts_at < {toUtc} AND a.ends_at > {fromUtc}
                  AND a.starts_at > {fromUtc - Torque.Domain.Scheduling.Appointment.MaxDuration} -- index lower bound
            WHERE b.is_active
            GROUP BY b.id, b.name
            ORDER BY b.name
            """).ToListAsync(ct);
        return rows.Select(r => new BookedMinutesRow(r.BayId, r.BayName, r.BookedMinutes)).ToList();
    }

    public async Task<IReadOnlyList<MechanicWorkloadRow>> MechanicWorkloadAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<MechanicSqlRow>($"""
            SELECT u.id AS mechanic_id, u.full_name AS mechanic_name,
                   COUNT(w.id)::int AS completed_orders, COALESCE(SUM(w.total), 0) AS revenue
            FROM users u
            JOIN work_orders w ON w.mechanic_id = u.id
            WHERE w.status IN ('Completed', 'Closed')
              AND w.completed_at >= {fromUtc} AND w.completed_at < {toUtc}
            GROUP BY u.id, u.full_name
            ORDER BY revenue DESC
            """).ToListAsync(ct);
        return rows.Select(r => new MechanicWorkloadRow(r.MechanicId, r.MechanicName, r.CompletedOrders, r.Revenue)).ToList();
    }

    // Row shapes for SqlQuery (property names map to snake_case column aliases via the naming convention).
    private sealed class RevenueSqlRow
    {
        public DateTime Period { get; init; }
        public decimal Payments { get; init; }
        public decimal Refunds { get; init; }
        public decimal Net { get; init; }
        public int PaymentCount { get; init; }
    }

    private sealed class MethodSqlRow
    {
        public string Method { get; init; } = null!;
        public decimal Net { get; init; }
    }

    private sealed class TopServiceSqlRow
    {
        public Guid? ServiceId { get; init; }
        public string Name { get; init; } = null!;
        public int TimesSold { get; init; }
        public decimal Revenue { get; init; }
    }

    private sealed class BookedSqlRow
    {
        public Guid BayId { get; init; }
        public string BayName { get; init; } = null!;
        public int BookedMinutes { get; init; }
    }

    private sealed class MechanicSqlRow
    {
        public Guid MechanicId { get; init; }
        public string MechanicName { get; init; } = null!;
        public int CompletedOrders { get; init; }
        public decimal Revenue { get; init; }
    }
}
