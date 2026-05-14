namespace Octans.Tests.Helpers;

public static class TestClock
{
    public static DateTime UtcNow => new(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
}
