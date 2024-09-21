using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Octans.Tests;


public class XUnitLogger : ILogger
{
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly string _categoryName;

    public XUnitLogger(ITestOutputHelper testOutputHelper, string categoryName)
    {
        _testOutputHelper = testOutputHelper;
        _categoryName = categoryName;
    }

    public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (exception is not null)
        {
            _testOutputHelper.WriteLine(exception.Message);
            _testOutputHelper.WriteLine(exception.StackTrace);
        }
        
        var message = formatter(state, exception);
        _testOutputHelper.WriteLine($"{DateTime.UtcNow:o} {logLevel} {_categoryName} - {message}");
    }
}

public sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper;

    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(_testOutputHelper, categoryName);
    }

    public void Dispose()
    {
    }
}

public class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new NullScope();
    private NullScope() { }
    public void Dispose() { }
}