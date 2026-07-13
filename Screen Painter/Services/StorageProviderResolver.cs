using System.Collections.Generic;
using System.Linq;
using Screen_Painter.Models;
using Screen_Painter.Services.Storage;

namespace Screen_Painter.Services;

public interface IStorageProviderResolver
{
    IStorageProvider? Resolve(StorageType type);
    IStorageProvider? ResolveLocal();
    IStorageProvider? ResolveWebDav();
    IStorageProvider? ResolveOAuth();
}

public class StorageProviderResolver : IStorageProviderResolver
{
    private readonly Dictionary<StorageType, IStorageProvider> _providers;

    public StorageProviderResolver(IEnumerable<IStorageProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.SupportedType);
    }

    public IStorageProvider? Resolve(StorageType type)
    {
        return _providers.TryGetValue(type, out var provider) ? provider : null;
    }

    public IStorageProvider? ResolveLocal() => Resolve(StorageType.Local);
    public IStorageProvider? ResolveWebDav() => Resolve(StorageType.WebDav);
    public IStorageProvider? ResolveOAuth() => Resolve(StorageType.OAuthCloud);
}
