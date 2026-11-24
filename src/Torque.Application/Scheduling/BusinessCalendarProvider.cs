using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Domain.Scheduling;

namespace Torque.Application.Scheduling;

/// <summary>Builds the domain <see cref="BusinessCalendar"/> from the stored opening hours and the configured time zone.</summary>
public sealed class BusinessCalendarProvider(IAppDbContext db, IBusinessTimeZone timeZone)
{
    public async Task<BusinessCalendar> GetAsync(CancellationToken ct) =>
        new(timeZone.TimeZone, await db.WorkingHours.AsNoTracking().ToListAsync(ct));
}
