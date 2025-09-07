using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Octans.Tests;


public class XUnitLogger(ITestOutputHelper testOutputHelper, string categoryName) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (exception is not null)
        {
            testOutputHelper.WriteLine(exception.Message);
            testOutputHelper.WriteLine(exception.StackTrace);
        }

        var message = formatter(state, exception);
        testOutputHelper.WriteLine($"{DateTime.UtcNow:o} {logLevel} {categoryName} - {message}");
    }
}

public sealed class XUnitLoggerProvider(ITestOutputHelper testOutputHelper) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(testOutputHelper, categoryName);
    }

    public void Dispose()
    {
    }
}

public sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();
    private NullScope() { }
    public void Dispose() { }
}