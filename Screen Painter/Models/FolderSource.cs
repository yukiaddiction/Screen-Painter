using System;
using System.Text.Json.Serialization;

namespace Screen_Painter.Models;

public class FolderSource : CredentialedEntity, IEquatable<FolderSource>
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

    [JsonPropertyName("pathOrUrl")]
    public string PathOrUrl { get; set; } = string.Empty;

    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    public bool Equals(FolderSource? other) => other is not null && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is FolderSource source && Equals(source);

    public override int GetHashCode() => Id.GetHashCode();
}
