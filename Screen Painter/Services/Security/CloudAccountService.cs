using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Security;

public class CloudAccountService : JsonFileRepository, ICloudAccountService
{
    public CloudAccountService(ILoggerFactory loggerFactory) : base("cloud_accounts.json", loggerFactory)
    {
    }

    public Task<List<CloudAccount>> GetAllAccountsAsync() => ReadAsync<CloudAccount>();

    public Task SaveAccountAsync(CloudAccount account)
    {
        return ReadModifyWriteAsync<CloudAccount>(accounts =>
        {
            var index = accounts.FindIndex(a => string.Equals(a.Id, account.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                accounts[index] = account;
            else
                accounts.Add(account);
            return accounts;
        });
    }

    public Task DeleteAccountAsync(string accountId)
    {
        return ReadModifyWriteAsync<CloudAccount>(accounts =>
        {
            accounts.RemoveAll(a => string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));
            return accounts;
        });
    }
}
