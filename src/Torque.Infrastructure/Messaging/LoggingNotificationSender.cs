using Microsoft.Extensions.Logging;
using Torque.Application.Abstractions;

namespace Torque.Infrastructure.Messaging;

/// <summary>
/// Development / demo sender: writes the message to the log instead of sending an SMS.
/// A real gateway (SMS provider, Telegram bot) is a new class implementing <see cref="INotificationSender"/> —
/// nothing else changes (Open/Closed principle, Dependency Inversion).
/// </summary>
public sealed partial class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        // Only the last 4 digits: the log is not the place for personal data.
        LogSent(logger, notification.MessageId, notification.Phone[^4..], notification.Text);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {MessageId} to ***{PhoneTail}: {Text}")]
    private static partial void LogSent(ILogger logger, Guid messageId, string phoneTail, string text);
}
