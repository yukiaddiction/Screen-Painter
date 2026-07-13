using System.Collections.Generic;
using System.Threading.Tasks;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Security;

public interface ICloudAccountService
{
    Task<List<CloudAccount>> GetAllAccountsAsync();
    Task SaveAccountAsync(CloudAccount account);
    Task DeleteAccountAsync(string accountId);
}
