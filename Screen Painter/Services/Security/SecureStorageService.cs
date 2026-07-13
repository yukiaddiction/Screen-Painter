using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Screen_Painter.Services.Security;

public class SecureStorageService : ISecureStorageService
{
    private const string MasterKeyName = "ScreenPainter_MasterAesKey";
    private readonly ILogger<SecureStorageService> _logger;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private byte[]? _cachedKey;
    private static readonly System.Security.Cryptography.RandomNumberGenerator Rng
        = System.Security.Cryptography.RandomNumberGenerator.Create();

    public SecureStorageService(ILogger<SecureStorageService> logger)
    {
        _logger = logger;
    }

    private async Task<byte[]> GetEncryptionKeyAsync()
    {
        if (_cachedKey != null)
            return _cachedKey;

        await _keyLock.WaitAsync();
        try
        {
            if (_cachedKey != null)
                return _cachedKey;

            try
            {
                var stored = await SecureStorage.Default.GetAsync(MasterKeyName);
                if (!string.IsNullOrEmpty(stored))
                {
                    _cachedKey = Convert.FromBase64String(stored);
                    return _cachedKey;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read master encryption key from SecureStorage");
            }

            var newKey = new byte[32];
            Rng.GetBytes(newKey);

            try
            {
                await SecureStorage.Default.SetAsync(MasterKeyName, Convert.ToBase64String(newKey));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist master encryption key to SecureStorage");
            }

            _cachedKey = newKey;
            return newKey;
        }
        finally
        {
            _keyLock.Release();
        }
    }

    public async Task<string> EncryptAndSaveAsync(string key, string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            await SecureStorage.Default.SetAsync(key, plainText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save to SecureStorage for key {Key}", key);
        }

        var encKey = await GetEncryptionKeyAsync();
        return EncryptAesGcm(encKey, plainText);
    }

    public async Task<string?> DecryptAndGetAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        try
        {
            var value = await SecureStorage.Default.GetAsync(key);
            if (!string.IsNullOrEmpty(value))
                return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read from SecureStorage for key {Key}", key);
        }

        return DecryptAesGcm(await GetEncryptionKeyAsync(), key);
    }

    public Task RemoveAsync(string key)
    {
        try
        {
            SecureStorage.Default.Remove(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove from SecureStorage for key {Key}", key);
        }
        return Task.CompletedTask;
    }

    private static string EncryptAesGcm(byte[] key, string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[12];
        Rng.GetBytes(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        using var ms = new MemoryStream();
        ms.Write(nonce, 0, nonce.Length);
        ms.Write(tag, 0, tag.Length);
        ms.Write(cipherBytes, 0, cipherBytes.Length);
        return Convert.ToBase64String(ms.ToArray());
    }

    private static string DecryptAesGcm(byte[] key, string cipherText)
    {
        try
        {
            var combined = Convert.FromBase64String(cipherText);
            if (combined.Length < 28)
                return string.Empty;

            var nonce = new byte[12];
            var tag = new byte[16];
            var cipherBytes = new byte[combined.Length - 28];

            Array.Copy(combined, 0, nonce, 0, 12);
            Array.Copy(combined, 12, tag, 0, 16);
            Array.Copy(combined, 28, cipherBytes, 0, cipherBytes.Length);

            var plainBytes = new byte[cipherBytes.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return string.Empty;
        }
    }
}
