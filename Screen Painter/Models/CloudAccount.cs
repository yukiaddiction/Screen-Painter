using System;
using System.Text.Json.Serialization;

namespace Screen_Painter.Models;

public class CloudAccount : CredentialedEntity, IEquatable<CloudAccount>
{
    [JsonPropertyName("id")]
    public new string Id { get => base.Id; set => base.Id = value; }

    [JsonPropertyName("name")]
    public new string Name { get => base.Name; set => base.Name = value; }

    [JsonPropertyName("type")]
    public new StorageType Type { get => base.Type; set => base.Type = value; }

    [JsonPropertyName("encryptedUsername")]
    public new string EncryptedUsername { get => base.EncryptedUsername; set => base.EncryptedUsername = value; }

    [JsonPropertyName("encryptedPasswordOrToken")]
    public new string EncryptedPasswordOrToken { get => base.EncryptedPasswordOrToken; set => base.EncryptedPasswordOrToken = value; }

    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = string.Empty;

    public bool Equals(CloudAccount? other) => other is not null && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is CloudAccount account && Equals(account);

    public override int GetHashCode() => Id.GetHashCode();
}
