using System.Threading.Tasks;

namespace Screen_Painter.Services.Security;

public interface ISecureStorageService
{
    Task<string> EncryptAndSaveAsync(string key, string plainText);
    Task<string?> DecryptAndGetAsync(string key);
    Task RemoveAsync(string key);
}
