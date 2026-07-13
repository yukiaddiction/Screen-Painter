using System;
using System.Text.Json.Serialization;

namespace Screen_Painter.Models;

public abstract class CredentialedEntity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public StorageType Type { get; set; }

    [JsonPropertyName("encryptedUsername")]
    public string EncryptedUsername { get; set; } = string.Empty;

    [JsonPropertyName("encryptedPasswordOrToken")]
    public string EncryptedPasswordOrToken { get; set; } = string.Empty;
}
