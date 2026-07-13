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
            return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {FileName}", Path.GetFileName(_filePath));
            return new List<T>();
        }
        finally
        {
            _semaphore.Release();
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
                items = JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
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

            if (File.Exists(_filePath))
                File.Replace(tmpPath, _filePath, bakPath);
            else
                File.Move(tmpPath, _filePath);
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
