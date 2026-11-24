using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Torque.Domain.Catalog;
using Torque.Domain.Scheduling;

namespace Torque.Infrastructure.Persistence.Configurations;

internal sealed class ServiceOfferingConfiguration : IEntityTypeConfiguration<ServiceOffering>
{
    public void Configure(EntityTypeBuilder<ServiceOffering> builder)
    {
        builder.ToTable("services", t =>
        {
            t.HasCheckConstraint("ck_services_duration", "duration_minutes > 0 AND duration_minutes % 15 = 0");
            t.HasCheckConstraint("ck_services_price", "price >= 0");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(ServiceOffering.MaxNameLength).IsRequired();
        builder.Property(s => s.Category).HasMaxLength(100).IsRequired();

        // numeric(12,2): exact decimal arithmetic. Never float/double for money — 0.1 + 0.2 != 0.3 in binary floating point.
        builder.Property(s => s.Price).HasPrecision(12, 2);
        builder.HasIndex(s => s.Name).IsUnique();
    }
}

internal sealed class BayConfiguration : IEntityTypeConfiguration<Bay>
{
    public void Configure(EntityTypeBuilder<Bay> builder)
    {
        builder.ToTable("bays");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Name).HasMaxLength(Bay.MaxNameLength).IsRequired();
        builder.HasIndex(b => b.Name).IsUnique();
    }
}

internal sealed class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHours>
{
    public void Configure(EntityTypeBuilder<WorkingHours> builder)
    {
        builder.ToTable("working_hours", t =>
            t.HasCheckConstraint("ck_working_hours_range", "is_closed OR closes > opens"));
        builder.HasKey(w => w.Day);

        // Default week (Mon–Sat 09:00–20:00, Sunday closed), created by the migration. Admin can change it via the API.
        var open = new TimeOnly(9, 0);
        var close = new TimeOnly(20, 0);
        builder.HasData(
            Enum.GetValues<DayOfWeek>().Select(day => new
            {
                Day = day,
                Opens = day == DayOfWeek.Sunday ? TimeOnly.MinValue : open,
                Closes = day == DayOfWeek.Sunday ? TimeOnly.MinValue : close,
                IsClosed = day == DayOfWeek.Sunday,
            }));
    }
}
