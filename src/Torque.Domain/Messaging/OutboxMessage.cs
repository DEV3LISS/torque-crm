namespace Torque.Domain.Messaging;

public enum OutboxStatus
{
    Pending,
    Sent,

    /// <summary>No longer relevant (e.g. the appointment was cancelled or moved).</summary>
    Skipped,

    /// <summary>Gave up after the maximum number of attempts — needs a human.</summary>
    Failed,
}

/// <summary>
/// Transactional outbox: a message that must be delivered to the outside world (SMS, Telegram, e-mail).
/// It is written in the SAME transaction as the business change that caused it, and delivered later by a
/// background worker. So we never get "appointment saved but reminder lost" (crash after commit) nor
/// "reminder sent but appointment rolled back" (crash before commit).
/// </summary>
public sealed class OutboxMessage
{
    public const int MaxAttempts = 5;

    private OutboxMessage() { }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Type { get; private set; } = null!;

    /// <summary>JSON payload, interpreted by the handler for <see cref="Type"/>.</summary>
    public string Payload { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Not before this moment (reminders are due 24 h before the visit; retries back off).</summary>
    public DateTimeOffset DueAt { get; private set; }
    public OutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static OutboxMessage Create(string type, string payload, DateTimeOffset now, DateTimeOffset dueAt) => new()
    {
        Type = type,
        Payload = payload,
        CreatedAt = now,
        DueAt = dueAt < now ? now : dueAt,
        Status = OutboxStatus.Pending,
    };

    public void MarkSent(DateTimeOffset now) => Finish(OutboxStatus.Sent, now);

    public void MarkSkipped(DateTimeOffset now) => Finish(OutboxStatus.Skipped, now);

    /// <summary>Exponential back-off: 1, 2, 4, 8 minutes… then give up.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;

        if (Attempts >= MaxAttempts)
        {
            Finish(OutboxStatus.Failed, now);
            return;
        }

        DueAt = now + TimeSpan.FromMinutes(Math.Pow(2, Attempts - 1));
    }

    private void Finish(OutboxStatus status, DateTimeOffset now)
    {
        Status = status;
        ProcessedAt = now;
    }
}
