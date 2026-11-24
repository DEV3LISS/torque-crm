using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Torque.Domain.Catalog;
using Torque.Domain.Customers;
using Torque.Domain.Scheduling;

namespace Torque.Infrastructure.Persistence.Configurations;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", t =>
        {
            t.HasCheckConstraint("ck_appointments_time", "ends_at > starts_at");
            t.HasCheckConstraint("ck_appointments_status",
                "status IN ('Scheduled', 'CheckedIn', 'Completed', 'Cancelled', 'NoShow')");
        });
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Comment).HasMaxLength(Appointment.MaxCommentLength);
        builder.Property(a => a.CancellationReason).HasMaxLength(500);
        builder.Ignore(a => a.Duration);
        builder.Ignore(a => a.IsActive);

        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Bay>().WithMany().HasForeignKey(a => a.BayId).OnDelete(DeleteBehavior.Restrict);

        // The list of booked services is owned by the appointment and saved together with it.
        builder.HasMany(a => a.Services).WithOne().HasForeignKey(s => s.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Services).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Indexes for the real queries: customer card, vehicle history, schedule board by period.
        // (bay_id + time range is covered by the GiST index of the exclusion constraint — see the migration.)
        builder.HasIndex(a => a.CustomerId);
        builder.HasIndex(a => a.VehicleId);
        builder.HasIndex(a => a.StartsAt);

        // Two people changing the status at the same moment: the second gets 409 instead of a lost update.
        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class AppointmentServiceItemConfiguration : IEntityTypeConfiguration<AppointmentServiceItem>
{
    public void Configure(EntityTypeBuilder<AppointmentServiceItem> builder)
    {
        builder.ToTable("appointment_services");
        builder.HasKey(s => new { s.AppointmentId, s.ServiceId });
        builder.HasOne<ServiceOffering>().WithMany().HasForeignKey(s => s.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.ServiceId);
    }
}
