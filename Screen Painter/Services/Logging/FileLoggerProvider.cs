using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Screen_Painter.Services.Logging;

public class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name));
    }

    public void Dispose()
    {
        _loggers.Clear();
    }
}
