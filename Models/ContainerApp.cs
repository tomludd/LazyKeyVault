namespace LazyKeyVault.Models;

public record ContainerApp(
    string Id = "",
    string Name = "",
    string Location = "",
    string ResourceGroup = "",
    string SubscriptionId = ""
)
{
    public override string ToString() => Name;
}
