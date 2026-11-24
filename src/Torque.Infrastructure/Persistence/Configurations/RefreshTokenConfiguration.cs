using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Torque.Domain.Auth;
using Torque.Domain.Users;

namespace Torque.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        // SHA-256 in hex = 64 characters.
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.UserId).HasFilter("revoked_at IS NULL");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency via PostgreSQL's system column xmin (changes on every row update).
        // Two parallel refreshes of the same token: the second UPDATE finds a different xmin,
        // EF throws DbUpdateConcurrencyException and the API answers 409 — no double rotation.
        builder.Property<uint>("Version").IsRowVersion();
    }
}
