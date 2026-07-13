using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Screen_Painter.Models;
using Screen_Painter.Services.Security;

namespace Screen_Painter.Services.Storage;

public static class StorageProviderExtensions
{
    public static IStorageProvider? Resolve(this IEnumerable<IStorageProvider> providers, StorageType type)
    {
        return providers.FirstOrDefault(p => p.SupportedType == type);
    }

    public static async Task<(string username, string password)> DecryptCredentialsAsync(
        this ISecureStorageService secureStorage,
        FolderSource folder)
    {
        var username = await secureStorage.DecryptAndGetAsync(folder.EncryptedUsername) ?? string.Empty;
        var password = await secureStorage.DecryptAndGetAsync(folder.EncryptedPasswordOrToken) ?? string.Empty;
        return (username, password);
    }
}
