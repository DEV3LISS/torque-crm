namespace Torque.IntegrationTests.Infrastructure;

/// <summary>
/// Real time plus an adjustable offset. Lets a test "jump 15 days ahead" while time still flows
/// normally, and can be reset between tests (FakeTimeProvider cannot move backwards).
/// </summary>
public sealed class TestClock : TimeProvider
{
    private TimeSpan _offset;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;

    public void Advance(TimeSpan by) => _offset += by;

    public void Reset() => _offset = TimeSpan.Zero;
}
