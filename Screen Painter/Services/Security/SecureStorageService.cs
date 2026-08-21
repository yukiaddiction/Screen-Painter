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
    private const string EnvelopePrefix = "SP1:";
    private readonly ILogger<SecureStorageService> _logger;
    private readonly SemaphoreSlim _keyLock = new(1, 1);
    private byte[]? _cachedKey;

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
            RandomNumberGenerator.Fill(newKey);

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
            var encKey = await GetEncryptionKeyAsync();
            return EnvelopePrefix + EncryptEnvelope(encKey, plainText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt credential");
            return string.Empty;
        }
    }

    public async Task<string?> DecryptAndGetAsync(string keyOrCipher)
    {
        if (string.IsNullOrEmpty(keyOrCipher))
            return null;

        // New format: the value is a self-describing ciphertext envelope.
        if (keyOrCipher.StartsWith(EnvelopePrefix, StringComparison.Ordinal))
        {
            try
            {
                var encKey = await GetEncryptionKeyAsync();
                return DecryptEnvelope(encKey, keyOrCipher.Substring(EnvelopePrefix.Length));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt credential envelope");
                return null;
            }
        }

        // Legacy format: a GUID key that points to a plaintext value already
        // stored in platform SecureStorage by older app versions. Return it so
        // existing accounts keep working; new writes use the envelope format.
        try
        {
            var legacy = await SecureStorage.Default.GetAsync(keyOrCipher);
            if (!string.IsNullOrEmpty(legacy))
                return legacy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read legacy credential from SecureStorage");
        }

        return null;
    }

    public Task RemoveAsync(string key)
    {
        try
        {
            // Only legacy GUID entries live in SecureStorage; envelopes are stored
            // in the account JSON itself and need no platform cleanup.
            if (!string.IsNullOrEmpty(key) && !key.StartsWith(EnvelopePrefix, StringComparison.Ordinal))
                SecureStorage.Default.Remove(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove credential from SecureStorage for key {Key}", key);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// AES-256-CBC with Encrypt-then-MAC (HMAC-SHA256). Layout:
    /// iv(16) || mac(32) || ciphertext, base64-encoded. Universally supported
    /// on all .NET platforms and Android API levels.
    /// </summary>
    private static string EncryptEnvelope(byte[] key, string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var iv = new byte[16];
        RandomNumberGenerator.Fill(iv);

        byte[] cipherBytes;
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
            }
            cipherBytes = ms.ToArray();
        }

        using var hmac = new HMACSHA256(key);
        var mac = hmac.ComputeHash(cipherBytes);

        using var envelope = new MemoryStream();
        envelope.Write(iv, 0, iv.Length);
        envelope.Write(mac, 0, mac.Length);
        envelope.Write(cipherBytes, 0, cipherBytes.Length);
        return Convert.ToBase64String(envelope.ToArray());
    }

    private static string? DecryptEnvelope(byte[] key, string envelopeBase64)
    {
        var combined = Convert.FromBase64String(envelopeBase64);
        if (combined.Length <= 48)
            return null;

        var iv = new byte[16];
        var mac = new byte[32];
        var cipherBytes = new byte[combined.Length - 48];
        Array.Copy(combined, 0, iv, 0, 16);
        Array.Copy(combined, 16, mac, 0, 32);
        Array.Copy(combined, 48, cipherBytes, 0, cipherBytes.Length);

        using var hmac = new HMACSHA256(key);
        var expectedMac = hmac.ComputeHash(cipherBytes);
        if (!CryptographicOperations.FixedTimeEquals(expectedMac, mac))
            return null;

        byte[] plainBytes;
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(cipherBytes);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var outMs = new MemoryStream();
            cs.CopyTo(outMs);
            plainBytes = outMs.ToArray();
        }

        return Encoding.UTF8.GetString(plainBytes);
    }
}
