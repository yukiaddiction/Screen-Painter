using System;
using System.Collections.Generic;
using System.Linq;
using Screen_Painter.Models;

namespace Screen_Painter.Services.Security;

/// <summary>
/// Resolves the credentials a cloud folder browser should use, from the parameters that were
/// carried on the navigation route.
///
/// The account record is authoritative: when the route carries an account id the credentials are
/// taken from that record on every navigation, so editing an account in Settings immediately
/// changes what the folder picker uses. Older builds put the encrypted credential envelopes
/// directly on the route, so a route that carries no account id (or an id that no longer exists)
/// falls back to those values to keep existing links working.
///
/// Kept free of MAUI and platform types so the resolution rules are unit-testable off-device.
/// </summary>
public static class CloudAccountResolver
{
    /// <summary>
    /// Builds the folder source used for listing, using the current account record when the route
    /// identifies one.
    /// </summary>
    /// <param name="query">The navigation route query, values still percent-encoded.</param>
    /// <param name="accounts">The accounts currently persisted by the app.</param>
    /// <param name="statusMessage">Message to show when resolution succeeds.</param>
    /// <param name="errorMessage">Set when the caller must stop and show an error instead of listing.</param>
    public static FolderSource? Resolve(
        IDictionary<string, object> query,
        IEnumerable<CloudAccount>? accounts,
        out string statusMessage,
        out string? errorMessage)
    {
        statusMessage = string.Empty;
        errorMessage = null;

        var folder = new FolderSource();

        if (query == null)
            return folder;

        if (TryGetString(query, "serverUrl", out var url))
        {
            folder.PathOrUrl = Uri.UnescapeDataString(url);
        }

        if (TryGetString(query, "type", out var typeStr) && Enum.TryParse<StorageType>(typeStr, out var parsedType))
        {
            folder.Type = parsedType;
        }

        // Legacy fallback: the route itself carried the credential envelopes.
        if (TryGetString(query, "userKey", out var userKey))
        {
            folder.EncryptedUsername = userKey;
        }

        if (TryGetString(query, "passKey", out var passKey))
        {
            folder.EncryptedPasswordOrToken = passKey;
        }

        var accountId = TryGetString(query, "accountId", out var id) ? id : string.Empty;

        if (!string.IsNullOrEmpty(accountId))
        {
            var account = accounts?.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));

            if (account == null)
            {
                errorMessage = "⚠ The selected cloud account no longer exists. Please pick it again.";
                return null;
            }

            folder.EncryptedUsername = account.EncryptedUsername;
            folder.EncryptedPasswordOrToken = account.EncryptedPasswordOrToken;
            folder.Type = account.Type;

            if (!string.IsNullOrEmpty(account.ServerUrl))
                folder.PathOrUrl = account.ServerUrl;
        }

        if (string.IsNullOrEmpty(folder.EncryptedUsername) || string.IsNullOrEmpty(folder.EncryptedPasswordOrToken))
        {
            errorMessage = "⚠ Credentials not provided. Please select a cloud account again.";
            return null;
        }

        // The folder name is cosmetic; reuse any name the route supplied before falling back.
        if (string.IsNullOrEmpty(folder.Name))
            folder.Name = string.IsNullOrEmpty(folder.PathOrUrl) ? "Cloud Root" : folder.PathOrUrl;

        statusMessage = "Connecting to cloud server...";
        return folder;
    }

    /// <summary>
    /// Account entries are selected by label, so two accounts created with the same name would be
    /// indistinguishable without the short id — that ambiguity is how a stale record used to win
    /// the lookup and send outdated credentials to the picker.
    /// </summary>
    public static string DescribeForSelection(CloudAccount account)
    {
        if (account == null)
            return string.Empty;

        return $"{account.Name} ({account.Type}) [{ShortId(account.Id)}]";
    }

    /// <summary>
    /// Applies an edit to an existing account while keeping its identity, so saving replaces the
    /// record instead of appending a second one with the same name.
    /// </summary>
    public static CloudAccount ApplyEdit(
        CloudAccount account,
        string name,
        string serverUrl,
        string? encryptedPasswordOrToken)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        return new CloudAccount
        {
            Id = account.Id,
            Name = name,
            Type = account.Type,
            ServerUrl = serverUrl,
            EncryptedUsername = account.EncryptedUsername,
            EncryptedPasswordOrToken = string.IsNullOrEmpty(encryptedPasswordOrToken)
                ? account.EncryptedPasswordOrToken
                : encryptedPasswordOrToken
        };
    }

    public static string ShortId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return "unknown";

        return id.Length <= 8 ? id : id.Substring(0, 8);
    }

    private static bool TryGetString(IDictionary<string, object> query, string key, out string value)
    {
        value = string.Empty;

        if (query.TryGetValue(key, out var raw) && raw is string text)
        {
            value = text;
            return true;
        }

        return false;
    }
}
