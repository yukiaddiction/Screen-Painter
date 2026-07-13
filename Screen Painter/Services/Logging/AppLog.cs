using Microsoft.Extensions.Logging;
using System;

namespace Screen_Painter.Services.Logging;

public static class AppLog
{
    public static ILogger<T> Get<T>() where T : class
    {
        var factory = ServiceAccessor.GetService<ILoggerFactory>();
        if (factory != null)
            return factory.CreateLogger<T>();
        return NullLogger<T>.Instance;
    }
}

public class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

public class NullLogger<T> : ILogger<T>
{
    public static readonly NullLogger<T> Instance = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
