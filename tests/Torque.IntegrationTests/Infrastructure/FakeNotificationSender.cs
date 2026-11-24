using System.Collections.Concurrent;
using Torque.Application.Abstractions;

namespace Torque.IntegrationTests.Infrastructure;

/// <summary>Records messages; can be told to fail to simulate a gateway outage.</summary>
public sealed class FakeNotificationSender : INotificationSender
{
    private readonly ConcurrentQueue<Notification> _sent = new();

    public bool FailAll { get; set; }

    public IReadOnlyCollection<Notification> Sent => _sent;

    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (FailAll)
        {
            throw new HttpRequestException("SMS gateway unavailable (simulated)");
        }

        _sent.Enqueue(notification);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        _sent.Clear();
        FailAll = false;
    }
}
