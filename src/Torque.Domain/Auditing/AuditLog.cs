namespace Torque.Domain.Auditing;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
}

/// <summary>
/// "Who changed what, when." Written automatically for every change of a business entity,
/// in the same transaction as the change itself.
/// </summary>
public sealed class AuditLog
{
    private AuditLog() { }

    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Null for system actions (seeding, background jobs).</summary>
    public Guid? UserId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public AuditAction Action { get; private set; }

    /// <summary>JSON: {"Status": {"old": "Open", "new": "InProgress"}, …}. Personal data is masked.</summary>
    public string Changes { get; private set; } = null!;
    public string? CorrelationId { get; private set; }

    public static AuditLog Create(DateTimeOffset occurredAt, Guid? userId, string entityType, string entityId,
        AuditAction action, string changes, string? correlationId) => new()
    {
        OccurredAt = occurredAt,
        UserId = userId,
        EntityType = entityType,
        EntityId = entityId,
        Action = action,
        Changes = changes,
        CorrelationId = correlationId,
    };
}
