using Torque.Domain.Common;

namespace Torque.Domain.Orders;

public enum PaymentType
{
    Payment,
    Refund,
}

public enum PaymentMethod
{
    Cash,
    Card,
    Transfer,
}

/// <summary>
/// One money movement. Append-only: never updated or deleted — a mistake is corrected with a refund,
/// exactly like in accounting. The amount is always positive; the direction is <see cref="Type"/>
/// (mixing signs into amounts is a classic source of bugs in sums).
/// </summary>
public sealed class Payment : Entity
{
    public const int MaxIdempotencyKeyLength = 100;

    private Payment() { }

    public Guid WorkOrderId { get; private set; }
    public PaymentType Type { get; private set; }
    public PaymentMethod Method { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Client-generated key; a retried request with the same key returns this payment instead of charging again.</summary>
    public string IdempotencyKey { get; private set; } = null!;
    public Guid ReceivedById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Payment Create(Guid workOrderId, PaymentType type, PaymentMethod method, decimal amount,
        string idempotencyKey, Guid receivedById, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            throw new DomainException("A valid idempotency key is required.");
        }

        return new Payment
        {
            WorkOrderId = workOrderId,
            Type = type,
            Method = method,
            Amount = amount,
            IdempotencyKey = idempotencyKey,
            ReceivedById = receivedById,
            CreatedAt = now,
        };
    }

    /// <summary>Was this payment created by the very same request (same key and same content)?</summary>
    public bool Matches(Guid workOrderId, PaymentType type, PaymentMethod method, decimal amount) =>
        WorkOrderId == workOrderId && Type == type && Method == method && Amount == amount;
}
