namespace Octans.Tests.Helpers;

public static class TestClock
{
    public static DateTimeOffset UtcNow => new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
}
