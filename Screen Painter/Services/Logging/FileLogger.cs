using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Screen_Painter.Services.Logging;

public class FileLogger : ILogger
{
    private readonly string _categoryName;
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private const long MaxFileSizeBytes = 1 * 1024 * 1024;
    private const int MaxBackupFiles = 3;
    private const int RetentionDays = 7;

    public FileLogger(string categoryName)
    {
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var entry = FormatLogEntry(logLevel, message, exception);

        _ = WriteAsync(entry);
    }

    private string FormatLogEntry(LogLevel logLevel, string message, Exception? exception)
    {
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.Append(" [");
        sb.Append(logLevel.ToString().ToUpperInvariant().PadRight(5));
        sb.Append("] [");
        sb.Append(_categoryName);
        sb.Append("] ");
        sb.Append(message);

        if (exception != null)
        {
            sb.Append(" | Exception: ");
            sb.Append(exception.Message);
            if (exception.InnerException != null)
            {
                sb.Append(" -> ");
                sb.Append(exception.InnerException.Message);
            }
        }

        return sb.ToString();
    }

    private static async Task WriteAsync(string line)
    {
        await WriteLock.WaitAsync();
        try
        {
            var logDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
            Directory.CreateDirectory(logDir);

            var logPath = Path.Combine(logDir, "app.log");
            RotateIfNeeded(logDir, logPath);

            await File.AppendAllTextAsync(logPath, line + Environment.NewLine);
        }
        catch
        {
        }
        finally
        {
            WriteLock.Release();
        }
    }

    private static void RotateIfNeeded(string logDir, string logPath)
    {
        try
        {
            if (!File.Exists(logPath)) return;

            var fileInfo = new FileInfo(logPath);
            if (fileInfo.Length < MaxFileSizeBytes) return;

            for (int i = MaxBackupFiles - 1; i >= 1; i--)
            {
                var oldFile = Path.Combine(logDir, $"app.{i}.log");
                var newFile = Path.Combine(logDir, $"app.{i + 1}.log");
                if (File.Exists(oldFile))
                {
                    if (File.Exists(newFile)) File.Delete(newFile);
                    File.Move(oldFile, newFile);
                }
            }

            var backupPath = Path.Combine(logDir, "app.1.log");
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(logPath, backupPath);
        }
        catch
        {
        }
    }

    public static void PurgeOldLogs()
    {
        try
        {
            var logDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
            if (!Directory.Exists(logDir)) return;

            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(logDir, "app*.log"))
            {
                var fi = new FileInfo(file);
                if (fi.LastWriteTime < cutoff)
                {
                    fi.Delete();
                }
            }
        }
        catch
        {
        }
    }
}
