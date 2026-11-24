using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Torque.Domain.Customers;

namespace Torque.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FullName).HasMaxLength(Customer.MaxNameLength).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(16).IsRequired(); // E.164: '+' and up to 15 digits
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.Notes).HasMaxLength(Customer.MaxNotesLength);

        builder.HasIndex(c => c.Phone).IsUnique();
        // + trigram GIN index on lower(full_name) for "contains" search — created in the migration with raw SQL,
        // because EF Core cannot describe an index on an expression.
    }
}

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles", t => t.HasCheckConstraint("ck_vehicles_year", "year IS NULL OR year BETWEEN 1950 AND 2100"));
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Make).HasMaxLength(Vehicle.MaxMakeModelLength).IsRequired();
        builder.Property(v => v.Model).HasMaxLength(Vehicle.MaxMakeModelLength).IsRequired();
        builder.Property(v => v.Plate).HasMaxLength(12).IsRequired();
        builder.Property(v => v.Vin).HasMaxLength(17).IsFixedLength();

        builder.HasIndex(v => v.Plate).IsUnique();
        builder.HasIndex(v => v.Vin).IsUnique().HasFilter("vin IS NOT NULL");
        builder.HasIndex(v => v.CustomerId);

        builder.HasOne<Customer>().WithMany().HasForeignKey(v => v.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
