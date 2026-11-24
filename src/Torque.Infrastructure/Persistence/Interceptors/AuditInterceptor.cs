using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Torque.Application.Abstractions;
using Torque.Domain.Auditing;
using Torque.Domain.Auth;
using Torque.Domain.Messaging;

namespace Torque.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every created / changed / deleted business entity,
/// inside the same SaveChanges — therefore inside the same database transaction. Either the change
/// and its audit record are both stored, or neither is.
/// </summary>
/// <remarks>
/// Why an interceptor and not explicit calls in each use case: nobody can forget it, and new entities
/// are audited automatically. Trade-off: it records data changes, not business intent ("why").
/// </remarks>
public sealed class AuditInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    // Never stored, not even masked.
    private static readonly HashSet<string> Secret = ["PasswordHash"];

    // Personal data: stored masked, so the log shows THAT it changed without leaking the value.
    private static readonly HashSet<string> Personal = ["Phone", "Email"];

    // Technical noise.
    private static readonly HashSet<string> Ignored = ["CreatedAt", "UpdatedAt", "Version"];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            AddAuditEntries(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            AddAuditEntries(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void AddAuditEntries(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();

        var logs = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog and not RefreshToken and not OutboxMessage
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => Describe(e, now))
            .Where(log => log is not null)
            .ToList();

        context.Set<AuditLog>().AddRange(logs!);
    }

    private AuditLog? Describe(EntityEntry entry, DateTimeOffset now)
    {
        var action = entry.State switch
        {
            EntityState.Added => AuditAction.Created,
            EntityState.Deleted => AuditAction.Deleted,
            _ => AuditAction.Updated,
        };

        var changes = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (Ignored.Contains(name) || property.Metadata.IsPrimaryKey())
            {
                continue;
            }

            if (action == AuditAction.Updated && !property.IsModified)
            {
                continue;
            }

            var (oldValue, newValue) = action switch
            {
                AuditAction.Created => (null, property.CurrentValue),
                AuditAction.Deleted => (property.OriginalValue, null),
                _ => (property.OriginalValue, property.CurrentValue),
            };

            if (action == AuditAction.Updated && Equals(oldValue, newValue))
            {
                continue;
            }

            changes[name] = Secret.Contains(name)
                ? new { changed = true }
                : new { old = Present(name, oldValue), @new = Present(name, newValue) };
        }

        if (action == AuditAction.Updated && changes.Count == 0)
        {
            return null;
        }

        var key = string.Join(",", entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue));
        return AuditLog.Create(now, currentUser.Id, entry.Metadata.ClrType.Name, key, action,
            JsonSerializer.Serialize(changes), currentUser.CorrelationId);
    }

    private static object? Present(string property, object? value) =>
        value is not null && Personal.Contains(property) ? Mask(value.ToString()!) : value is Enum e ? e.ToString() : value;

    /// <summary>"+79123456789" → "*********89".</summary>
    private static string Mask(string value) =>
        value.Length <= 2 ? "**" : new string('*', value.Length - 2) + value[^2..];
}
