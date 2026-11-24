using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Torque.Domain.Auditing;
using Torque.Domain.Catalog;
using Torque.Domain.Customers;
using Torque.Domain.Messaging;
using Torque.Domain.Orders;
using Torque.Domain.Scheduling;
using Torque.Domain.Users;

namespace Torque.Infrastructure.Persistence.Configurations;

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders", t =>
        {
            t.HasCheckConstraint("ck_work_orders_status", "status IN ('Open', 'InProgress', 'Completed', 'Closed', 'Cancelled')");
            // Money invariants also live in the database — the last line of defence against a bug in C#.
            t.HasCheckConstraint("ck_work_orders_totals", "subtotal >= 0 AND discount_amount >= 0 AND total = subtotal - discount_amount");
            t.HasCheckConstraint("ck_work_orders_paid", "paid_amount >= 0 AND paid_amount <= total");
        });
        builder.HasKey(w => w.Id);

        // Sequential human-friendly number from a PostgreSQL identity column, starting at 1001.
        builder.Property(w => w.Number).UseIdentityAlwaysColumn().HasIdentityOptions(startValue: 1001);
        builder.HasIndex(w => w.Number).IsUnique();

        foreach (var money in new[] { nameof(WorkOrder.Subtotal), nameof(WorkOrder.DiscountAmount), nameof(WorkOrder.Total),
                     nameof(WorkOrder.PaidAmount), nameof(WorkOrder.DiscountValue) })
        {
            builder.Property(money).HasPrecision(12, 2);
        }

        builder.Ignore(w => w.AmountDue);

        // One appointment → at most one work order (walk-ins have no appointment).
        builder.HasIndex(w => w.AppointmentId).IsUnique().HasFilter("appointment_id IS NOT NULL");
        builder.HasIndex(w => w.CustomerId);
        builder.HasIndex(w => w.MechanicId);
        builder.HasIndex(w => new { w.Status, w.CreatedAt });

        builder.HasOne<Appointment>().WithMany().HasForeignKey(w => w.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(w => w.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(w => w.VehicleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.MechanicId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(w => w.Lines).WithOne().HasForeignKey(l => l.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class WorkOrderLineConfiguration : IEntityTypeConfiguration<WorkOrderLine>
{
    public void Configure(EntityTypeBuilder<WorkOrderLine> builder)
    {
        builder.ToTable("work_order_lines", t =>
        {
            t.HasCheckConstraint("ck_work_order_lines_kind", "kind IN ('Service', 'Part')");
            t.HasCheckConstraint("ck_work_order_lines_amounts", "quantity > 0 AND unit_price >= 0 AND line_total >= 0");
        });
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(WorkOrderLine.MaxNameLength).IsRequired();
        builder.Property(l => l.Quantity).HasPrecision(10, 2);
        builder.Property(l => l.UnitPrice).HasPrecision(12, 2);
        builder.Property(l => l.LineTotal).HasPrecision(12, 2);
        builder.HasOne<ServiceOffering>().WithMany().HasForeignKey(l => l.ServiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.WorkOrderId);
        builder.HasIndex(l => l.ServiceId);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t =>
        {
            t.HasCheckConstraint("ck_payments_amount", "amount > 0");
            t.HasCheckConstraint("ck_payments_type", "type IN ('Payment', 'Refund')");
            t.HasCheckConstraint("ck_payments_method", "method IN ('Cash', 'Card', 'Transfer')");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.IdempotencyKey).HasMaxLength(Payment.MaxIdempotencyKeyLength).IsRequired();

        // The heart of idempotency: the database refuses a second payment with the same key.
        builder.HasIndex(p => p.IdempotencyKey).IsUnique();
        builder.HasIndex(p => new { p.WorkOrderId, p.CreatedAt });
        builder.HasIndex(p => p.CreatedAt); // revenue reports by period

        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(p => p.WorkOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ReceivedById).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).UseIdentityAlwaysColumn();
        builder.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Changes).HasColumnType("jsonb").IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(64);

        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => a.UserId);
        // No FK to users on purpose: the audit log must survive anything that happens to other tables.
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", t =>
            t.HasCheckConstraint("ck_outbox_messages_status", "status IN ('Pending', 'Sent', 'Skipped', 'Failed')"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(1000);

        // The worker only ever looks for pending, due messages: a small partial index stays tiny
        // even when millions of processed messages accumulate.
        builder.HasIndex(m => m.DueAt).HasFilter("status = 'Pending'");
    }
}
