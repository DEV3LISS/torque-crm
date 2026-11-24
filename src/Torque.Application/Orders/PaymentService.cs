using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Application.Common;
using Torque.Domain.Common;
using Torque.Domain.Orders;

namespace Torque.Application.Orders;

/// <summary>
/// Taking and refunding money. Two independent protections:
/// <list type="number">
/// <item><b>Idempotency</b> — the client sends an Idempotency-Key; a retried request (double click,
/// network timeout, mobile reconnect) returns the original payment instead of charging twice.</item>
/// <item><b>Row lock</b> — the work order row is locked (SELECT … FOR UPDATE) for the duration of the
/// transaction, so two cashiers paying the same order at once are serialised: the second one sees the
/// updated amount due and cannot overpay.</item>
/// </list>
/// </summary>
public sealed class PaymentService(
    IAppDbContext db,
    ICurrentUser currentUser,
    TimeProvider clock,
    WorkOrderService workOrders,
    TorqueMetrics metrics,
    IValidator<PaymentRequest> validator)
{
    public Task<PaymentResult> PayAsync(Guid workOrderId, PaymentRequest request, string? idempotencyKey, CancellationToken ct) =>
        RecordAsync(workOrderId, PaymentType.Payment, request, idempotencyKey, ct);

    public Task<PaymentResult> RefundAsync(Guid workOrderId, PaymentRequest request, string? idempotencyKey, CancellationToken ct) =>
        RecordAsync(workOrderId, PaymentType.Refund, request, idempotencyKey, ct);

    private async Task<PaymentResult> RecordAsync(Guid workOrderId, PaymentType type, PaymentRequest request,
        string? idempotencyKey, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length is < 8 or > Payment.MaxIdempotencyKeyLength)
        {
            throw new DomainException("Header Idempotency-Key is required (8–100 characters, e.g. a UUID).");
        }

        // 1. Seen this key before? Return the stored result — do not charge again.
        var replay = await FindReplayAsync(idempotencyKey, workOrderId, type, request, ct);
        if (replay is not null)
        {
            return replay;
        }

        // 2. New request: lock the order row, apply the domain rule, write the payment, commit — atomically.
        await using var transaction = await db.BeginTransactionAsync(ct);
        var order = await db.LockWorkOrderAsync(workOrderId, ct) ?? throw new NotFoundException("WorkOrder", workOrderId);

        var now = clock.GetUtcNow();
        if (type == PaymentType.Payment)
        {
            order.RegisterPayment(request.Amount, now);
        }
        else
        {
            order.RegisterRefund(request.Amount);
        }

        var payment = Payment.Create(order.Id, type, request.Method, request.Amount, idempotencyKey,
            currentUser.Id ?? throw new UnauthorizedException("Not authenticated."), now);
        db.Payments.Add(payment);

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two identical retries raced past step 1 at the same time: the unique index on
            // idempotency_key let only one insert win. The loser returns the winner's result.
            await transaction.RollbackAsync(ct);
            return await FindReplayAsync(idempotencyKey, workOrderId, type, request, ct) ?? throw new ConflictException(
                "The payment could not be recorded because of a concurrent change. Please retry.");
        }

        metrics.MoneyMoved(type.ToString(), request.Method.ToString(), request.Amount);
        if (order.Status == WorkOrderStatus.Closed)
        {
            metrics.WorkOrderClosed();
        }

        return new PaymentResult(PaymentDto.From(payment), await workOrders.GetAsync(workOrderId, ct), Replayed: false);
    }

    private async Task<PaymentResult?> FindReplayAsync(string key, Guid workOrderId, PaymentType type,
        PaymentRequest request, CancellationToken ct)
    {
        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.IdempotencyKey == key, ct);
        if (existing is null)
        {
            return null;
        }

        if (!existing.Matches(workOrderId, type, request.Method, request.Amount))
        {
            // Same key, different content = a client bug. Refuse rather than guess.
            throw new DomainException("This Idempotency-Key was already used for a different payment request.");
        }

        metrics.PaymentReplayed();
        return new PaymentResult(PaymentDto.From(existing), await workOrders.GetAsync(workOrderId, ct), Replayed: true);
    }
}
