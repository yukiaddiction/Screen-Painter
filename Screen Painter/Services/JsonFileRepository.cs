using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Screen_Painter.Services;

public abstract class JsonFileRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    protected readonly ILogger _logger;

    protected JsonFileRepository(string fileName, ILoggerFactory loggerFactory)
    {
        _filePath = Path.Combine(FileSystem.AppDataDirectory, fileName);
        _logger = loggerFactory.CreateLogger(GetType());
    }

    protected async Task<List<T>> ReadAsync<T>()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
                return new List<T>();

            var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
            var items = JsonSerializer.Deserialize<List<T>>(json);
            if (items != null)
                return items;

            // Unrecoverable parse: quarantine the corrupt file so the app starts
            // fresh instead of silently treating "no accounts" as the truth.
            QuarantineCorruptFile();
            return new List<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {FileName}", Path.GetFileName(_filePath));
            QuarantineCorruptFile();
            return new List<T>();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void QuarantineCorruptFile()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;
            var corruptPath = _filePath + ".corrupt";
            File.Move(_filePath, corruptPath, overwrite: true);
            _logger.LogWarning("Quarantined corrupt data file {FileName} -> {CorruptName}",
                Path.GetFileName(_filePath), Path.GetFileName(corruptPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to quarantine corrupt data file {FileName}", Path.GetFileName(_filePath));
        }
    }

    protected async Task ReadModifyWriteAsync<T>(Func<List<T>, List<T>> modifier)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            List<T> items;
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
                var deserialized = JsonSerializer.Deserialize<List<T>>(json);
                if (deserialized == null)
                {
                    // Corrupt existing data would make every subsequent write fail
                    // forever. Quarantine it and start from an empty list instead.
                    QuarantineCorruptFile();
                    items = new List<T>();
                }
                else
                {
                    items = deserialized;
                }
            }
            else
            {
                items = new List<T>();
            }

            items = modifier(items);

            var resultJson = JsonSerializer.Serialize(items, JsonOptions);
            var tmpPath = _filePath + ".tmp";
            var bakPath = _filePath + ".bak";

            await File.WriteAllTextAsync(tmpPath, resultJson).ConfigureAwait(false);

            try
            {
                if (File.Exists(_filePath))
                    File.Replace(tmpPath, _filePath, bakPath);
                else
                    File.Move(tmpPath, _filePath);
            }
            catch
            {
                // File.Replace can fail on some filesystems; fall back to an
                // overwriting move so the write still lands atomically enough.
                File.Move(tmpPath, _filePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update {FileName}", Path.GetFileName(_filePath));
        }
        finally
        {
            _semaphore.Release();
        }
    }

    protected async Task<T?> ReadByIdAsync<T>(string id, Func<T, string> idSelector)
    {
        var all = await ReadAsync<T>();
        foreach (var item in all)
        {
            if (string.Equals(idSelector(item), id, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return default;
    }
}
