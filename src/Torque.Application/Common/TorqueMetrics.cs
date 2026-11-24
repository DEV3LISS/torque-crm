using System.Diagnostics.Metrics;

namespace Torque.Application.Common;

/// <summary>
/// Business metrics — what the owner and on-call engineer care about, not just CPU and HTTP codes:
/// "are bookings happening?", "how much money came in?", "are reminders failing?".
/// Uses the built-in System.Diagnostics.Metrics API; exporting (Prometheus/OTLP) is configured in Api,
/// so this layer does not depend on any monitoring vendor.
/// </summary>
public sealed class TorqueMetrics
{
    public const string MeterName = "Torque";

    private readonly Counter<long> _appointmentsBooked;
    private readonly Counter<long> _workOrdersClosed;
    private readonly Counter<double> _money;
    private readonly Counter<long> _paymentReplays;
    private readonly Counter<long> _outboxMessages;

    public TorqueMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _appointmentsBooked = meter.CreateCounter<long>("torque.appointments.booked", unit: "{appointment}");
        _workOrdersClosed = meter.CreateCounter<long>("torque.work_orders.closed", unit: "{order}");
        _money = meter.CreateCounter<double>("torque.payments.amount", unit: "RUB",
            description: "Money received (type=Payment) or returned (type=Refund)");
        _paymentReplays = meter.CreateCounter<long>("torque.payments.replayed", unit: "{request}",
            description: "Retried payment requests answered from the idempotency store");
        _outboxMessages = meter.CreateCounter<long>("torque.outbox.processed", unit: "{message}");
    }

    public void AppointmentBooked() => _appointmentsBooked.Add(1);

    public void WorkOrderClosed() => _workOrdersClosed.Add(1);

    public void MoneyMoved(string type, string method, decimal amount) =>
        _money.Add((double)amount, new KeyValuePair<string, object?>("type", type), new KeyValuePair<string, object?>("method", method));

    public void PaymentReplayed() => _paymentReplays.Add(1);

    public void OutboxProcessed(string messageType, string outcome) =>
        _outboxMessages.Add(1, new KeyValuePair<string, object?>("message_type", messageType), new KeyValuePair<string, object?>("outcome", outcome));
}
