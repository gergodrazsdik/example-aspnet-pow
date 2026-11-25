namespace PowForm.Tests;

/// <summary>A clock the tests move by hand.</summary>
public sealed class TestTime(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public TestTime() : this(new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
