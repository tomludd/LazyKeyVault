using System.Text.Json.Serialization;

namespace LazyAzureKeyVault.Models;

public record KeyVaultSecret(
    string Id = "",
    string Name = "",
    string? Value = null,
    string? ContentType = null,
    SecretAttributes? Attributes = null
)
{
    public override string ToString() => Name;
}

public record SecretAttributes(
    bool? Enabled = null,
    [property: JsonPropertyName("created")] string? Created = null,
    [property: JsonPropertyName("updated")] string? Updated = null,
    [property: JsonPropertyName("expires")] string? Expires = null,
    [property: JsonPropertyName("notBefore")] string? NotBefore = null
);
