namespace LazyKeyVault.Models;

public record AzureAccount(
    string Id = "",
    string Name = "",
    bool IsDefault = false,
    string State = "",
    string TenantId = "",
    AzureUser? User = null
)
{
    public override string ToString() => User?.Name ?? Name;
}

public record AzureUser(
    string Name = "",
    string Type = ""
);
