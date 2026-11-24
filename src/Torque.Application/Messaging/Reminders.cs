using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Torque.Application.Abstractions;
using Torque.Domain.Messaging;
using Torque.Domain.Scheduling;

namespace Torque.Application.Messaging;

/// <summary>Result of handling one outbox message.</summary>
public enum HandleOutcome
{
    Sent,
    Skipped,
}

/// <summary>One handler per outbox message type. Throwing = temporary failure, the message will be retried.</summary>
public interface IOutboxMessageHandler
{
    string Type { get; }

    Task<HandleOutcome> HandleAsync(OutboxMessage message, CancellationToken ct);
}

public sealed record AppointmentReminderPayload(Guid AppointmentId, DateTimeOffset StartsAt);

/// <summary>Creates the "reminder 24 hours before the visit" message together with the booking.</summary>
public static class AppointmentReminder
{
    public const string Type = "appointment-reminder";
    public static readonly TimeSpan Lead = TimeSpan.FromHours(24);

    /// <summary>Booked less than 2 hours ahead → no reminder: the customer has just talked to us.</summary>
    public static readonly TimeSpan MinimumNotice = TimeSpan.FromHours(2);

    public static void Schedule(IAppDbContext db, Appointment appointment, DateTimeOffset now)
    {
        if (appointment.StartsAt - now < MinimumNotice)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new AppointmentReminderPayload(appointment.Id, appointment.StartsAt));
        db.OutboxMessages.Add(OutboxMessage.Create(Type, payload, now, appointment.StartsAt - Lead));
    }
}

/// <summary>
/// Sends the reminder — but first re-checks reality: the appointment may have been cancelled or moved
/// since the message was written. Then the message is skipped (a moved appointment has its own new reminder).
/// </summary>
public sealed class AppointmentReminderHandler(IAppDbContext db, IBusinessTimeZone timeZone, INotificationSender sender)
    : IOutboxMessageHandler
{
    public string Type => AppointmentReminder.Type;

    public async Task<HandleOutcome> HandleAsync(OutboxMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<AppointmentReminderPayload>(message.Payload)
            ?? throw new InvalidOperationException("Empty reminder payload.");

        var target = await (
            from a in db.Appointments.AsNoTracking()
            join c in db.Customers on a.CustomerId equals c.Id
            join b in db.Bays on a.BayId equals b.Id
            where a.Id == payload.AppointmentId
            select new { a.Status, a.StartsAt, c.Phone, BayName = b.Name }).SingleOrDefaultAsync(ct);

        if (target is null || target.Status != AppointmentStatus.Scheduled || target.StartsAt != payload.StartsAt)
        {
            return HandleOutcome.Skipped;
        }

        var local = TimeZoneInfo.ConvertTime(target.StartsAt, timeZone.TimeZone);
        var text = string.Create(CultureInfo.GetCultureInfo("ru-RU"),
            $"Torque: напоминаем о записи {local:dd.MM} в {local:HH:mm} ({target.BayName}). Если планы изменились — позвоните нам.");

        await sender.SendAsync(new Notification(message.Id, target.Phone, text), ct);
        return HandleOutcome.Sent;
    }
}
