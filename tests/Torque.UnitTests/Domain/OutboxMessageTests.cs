using Torque.Domain.Messaging;

namespace Torque.UnitTests.Domain;

public class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Due_time_in_the_past_means_now() =>
        OutboxMessage.Create("t", "{}", Now, Now.AddHours(-3)).DueAt.ShouldBe(Now);

    [Fact]
    public void Failures_back_off_exponentially_then_give_up()
    {
        var message = OutboxMessage.Create("t", "{}", Now, Now);

        var delays = new List<TimeSpan>();
        for (var i = 0; i < OutboxMessage.MaxAttempts - 1; i++)
        {
            message.MarkFailed("boom", Now);
            delays.Add(message.DueAt - Now);
        }

        delays.ShouldBe([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8)]);
        message.Status.ShouldBe(OutboxStatus.Pending);

        message.MarkFailed("boom", Now);
        message.Status.ShouldBe(OutboxStatus.Failed);
        message.ProcessedAt.ShouldBe(Now);
    }

    [Fact]
    public void Long_errors_are_truncated()
    {
        var message = OutboxMessage.Create("t", "{}", Now, Now);

        message.MarkFailed(new string('x', 5000), Now);

        message.LastError!.Length.ShouldBe(1000);
    }
}
