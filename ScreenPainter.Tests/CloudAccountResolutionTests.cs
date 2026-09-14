using System;
using System.Collections.Generic;
using System.Linq;
using Screen_Painter.Models;
using Screen_Painter.Services.Security;

namespace ScreenPainter.Tests;

/// <summary>
/// Guards the folder-picker credential plumbing. The picker used to rely on the credential
/// envelopes that were captured when the navigation route was built, so fixing an account in
/// Settings had no effect on the folder browser and listing kept failing with HTTP 401.
/// </summary>
public class CloudAccountResolutionTests
{
    private static CloudAccount Account(string id, string name, string url, string userKey, string passKey) => new()
    {
        Id = id,
        Name = name,
        Type = StorageType.WebDav,
        ServerUrl = url,
        EncryptedUsername = userKey,
        EncryptedPasswordOrToken = passKey
    };

    private static Dictionary<string, object> Route(params (string Key, string Value)[] pairs)
    {
        var query = new Dictionary<string, object>();
        foreach (var (key, value) in pairs)
            query[key] = value;
        return query;
    }

    [Fact]
    public void Resolve_UsesCredentialsStoredOnTheAccount_NotTheRouteSnapshot()
    {
        // The account was corrected AFTER this route was built with the old envelope.
        var account = Account("acc-1", "My Nextcloud", "https://dav.example.com/photos", "SP1:newuser", "SP1:newpass");
        var query = Route(
            ("accountId", "acc-1"),
            ("serverUrl", Uri.EscapeDataString("https://dav.example.com/photos")),
            ("type", "WebDav"),
            ("userKey", Uri.EscapeDataString("SP1:olduser")),
            ("passKey", Uri.EscapeDataString("SP1:oldpass")));

        var resolved = CloudAccountResolver.Resolve(query, new[] { account }, out _, out var error);

        Assert.Null(error);
        Assert.NotNull(resolved);
        Assert.Equal("SP1:newuser", resolved!.EncryptedUsername);
        Assert.Equal("SP1:newpass", resolved.EncryptedPasswordOrToken);
    }

    [Fact]
    public void Resolve_PrefersTheAccountServerUrl()
    {
        // Moving the account to a new base URL must also move the folder browser.
        var account = Account("acc-1", "My Nextcloud", "https://dav.example.com/new-root", "SP1:user", "SP1:pass");
        var query = Route(
            ("accountId", "acc-1"),
            ("serverUrl", Uri.EscapeDataString("https://dav.example.com/old-root")),
            ("type", "WebDav"));

        var resolved = CloudAccountResolver.Resolve(query, new[] { account }, out _, out var error);

        Assert.Null(error);
        Assert.Equal("https://dav.example.com/new-root", resolved!.PathOrUrl);
    }

    [Fact]
    public void Resolve_MatchesTheAccountIdCaseInsensitively()
    {
        var account = Account("ACC-1", "My Nextcloud", "https://dav.example.com", "SP1:user", "SP1:pass");
        var query = Route(("accountId", "acc-1"));

        var resolved = CloudAccountResolver.Resolve(query, new[] { account }, out _, out var error);

        Assert.Null(error);
        Assert.Equal("SP1:user", resolved!.EncryptedUsername);
    }

    [Fact]
    public void Resolve_FallsBackToLegacyRouteCredentials_WhenNoAccountIdIsPresent()
    {
        // Routes built by older app versions carried the envelopes directly. Shell hands these
        // over already percent-decoded, so the resolver must pass them through untouched.
        var query = Route(
            ("serverUrl", "https://dav.example.com/photos"),
            ("type", "WebDav"),
            ("userKey", "SP1:legacyuser"),
            ("passKey", "SP1:legacypass"));

        var resolved = CloudAccountResolver.Resolve(query, Enumerable.Empty<CloudAccount>(), out _, out var error);

        Assert.Null(error);
        Assert.Equal("SP1:legacyuser", resolved!.EncryptedUsername);
        Assert.Equal("SP1:legacypass", resolved.EncryptedPasswordOrToken);
        Assert.Equal("https://dav.example.com/photos", resolved.PathOrUrl);
        Assert.Equal(StorageType.WebDav, resolved.Type);
    }

    [Fact]
    public void Resolve_ReportsAMissingAccount_InsteadOfSilentlyListingWithStaleCredentials()
    {
        var query = Route(("accountId", "deleted-account"), ("userKey", "SP1:old"), ("passKey", "SP1:old"));

        var resolved = CloudAccountResolver.Resolve(query, Enumerable.Empty<CloudAccount>(), out _, out var error);

        Assert.Null(resolved);
        Assert.NotNull(error);
        Assert.Contains("no longer exists", error);
    }

    [Fact]
    public void Resolve_ReportsMissingCredentials_WhenNeitherRouteNorAccountSuppliesThem()
    {
        var query = Route(("serverUrl", Uri.EscapeDataString("https://dav.example.com/photos")));

        var resolved = CloudAccountResolver.Resolve(query, Enumerable.Empty<CloudAccount>(), out _, out var error);

        Assert.Null(resolved);
        Assert.NotNull(error);
        Assert.Contains("Credentials not provided", error);
    }

    [Fact]
    public void Resolve_TreatsAMissingAccountListAsNoAccount()
    {
        var query = Route(("accountId", "acc-1"));

        var resolved = CloudAccountResolver.Resolve(query, null, out _, out var error);

        Assert.Null(resolved);
        Assert.NotNull(error);
    }

    [Fact]
    public void DescribeForSelection_DistinguishesAccountsThatShareAName()
    {
        // Identical labels are what let an old record win the first-match lookup.
        var stale = Account("11111111-1111-1111-1111-111111111111", "My Nextcloud", "https://dav.example.com", "SP1:old", "SP1:old");
        var fixedAccount = Account("22222222-2222-2222-2222-222222222222", "My Nextcloud", "https://dav.example.com", "SP1:new", "SP1:new");

        var staleLabel = CloudAccountResolver.DescribeForSelection(stale);
        var fixedLabel = CloudAccountResolver.DescribeForSelection(fixedAccount);

        Assert.NotEqual(staleLabel, fixedLabel);
        Assert.Contains("My Nextcloud (WebDav)", staleLabel);
        Assert.Contains("11111111", staleLabel);
        Assert.Contains("22222222", fixedLabel);

        // Selecting the corrected entry must resolve to that exact record.
        var accounts = new List<CloudAccount> { stale, fixedAccount };
        var selected = accounts.FirstOrDefault(a => CloudAccountResolver.DescribeForSelection(a) == fixedLabel);

        Assert.Same(fixedAccount, selected);
    }

    [Fact]
    public void ShortId_HandlesEmptyAndShortIdentifiers()
    {
        Assert.Equal("unknown", CloudAccountResolver.ShortId(null));
        Assert.Equal("unknown", CloudAccountResolver.ShortId(string.Empty));
        Assert.Equal("abc", CloudAccountResolver.ShortId("abc"));
        Assert.Equal("abcdefgh", CloudAccountResolver.ShortId("abcdefgh"));
        Assert.Equal("abcdefgh", CloudAccountResolver.ShortId("abcdefgh-ijkl"));
    }

    [Fact]
    public void ApplyEdit_KeepsTheAccountIdentity_SoSavingReplacesInsteadOfDuplicating()
    {
        var account = Account("acc-1", "My Nextcloud", "https://dav.example.com", "SP1:user", "SP1:oldpass");

        var updated = CloudAccountResolver.ApplyEdit(account, "Renamed", "https://dav.example.com/new", "SP1:newpass");

        Assert.Equal("acc-1", updated.Id);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal("https://dav.example.com/new", updated.ServerUrl);
        Assert.Equal("SP1:newpass", updated.EncryptedPasswordOrToken);
        Assert.Equal("SP1:user", updated.EncryptedUsername);
        Assert.Equal(account.Type, updated.Type);

        // The original instance is left untouched — the list holds the replacement.
        Assert.Equal("My Nextcloud", account.Name);
        Assert.Equal("SP1:oldpass", account.EncryptedPasswordOrToken);
    }

    [Fact]
    public void ApplyEdit_KeepsTheStoredCredential_WhenNoNewOneIsSupplied()
    {
        var account = Account("acc-1", "My Nextcloud", "https://dav.example.com", "SP1:user", "SP1:oldpass");

        var updated = CloudAccountResolver.ApplyEdit(account, "My Nextcloud", "https://dav.example.com", null);

        Assert.Equal("SP1:oldpass", updated.EncryptedPasswordOrToken);
        Assert.Equal("acc-1", updated.Id);
    }
}
