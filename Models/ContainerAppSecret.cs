namespace LazyKeyVault.Models;

public record ContainerAppSecret(
    string Name = "",
    string? Value = null
)
{
    public override string ToString() => Name;
}
