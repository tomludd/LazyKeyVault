# LazyKeyVault

A terminal UI for Azure Key Vault and Container Apps secrets management, inspired by [LazyDocker](https://github.com/jesseduffield/lazydocker) and [LazyGit](https://github.com/jesseduffield/lazygit).

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Terminal.Gui](https://img.shields.io/badge/Terminal.Gui-2.5-blue)
[![NuGet](https://img.shields.io/nuget/v/LazyKeyVault.svg)](https://www.nuget.org/packages/LazyKeyVault/)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- 🔐 **Browse Azure Key Vaults and Container Apps** across multiple accounts and subscriptions
- 🎨 **Colorful UI** with unique colors per subscription/vault/app name
- ✏️ **Create, edit, and delete** secrets directly from the terminal
- ⏳ **Expiry at a glance** - expired/soon-to-expire Key Vault secrets are highlighted in the list, with a countdown in the details panel
- ⚙️ **Edit secret settings** - toggle enabled, set content type, expiry, and "not before" per Key Vault secret
- 📋 **Copy to clipboard** with a single keystroke
- 🔍 **Filter secrets** by name
- ⚡ **Fast** - uses Azure SDK with intelligent caching
- ⌨️ **Keyboard-driven** interface
- 🐳 **Container Apps support** - manage secrets for Azure Container Apps alongside Key Vaults

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) installed and logged in


## Usage

    lazykeyvault

## Installation

    dotnet tool install --global LazyKeyVault

### Login with Azure CLI
Make sure you are logged in with Azure CLI

    az login


## Run without Installing

```bash
git clone https://github.com/tomludd/LazyKeyVault.git
cd LazyKeyVault
dotnet run
```

## Install tool from Local Build

```bash
# Clone the repository
git clone https://github.com/tomludd/LazyKeyVault.git
cd LazyKeyVault

# Pack and install globally
dotnet pack -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg LazyKeyVault
```

### Update Local tool

```bash
dotnet tool uninstall -g LazyKeyVault
dotnet pack -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg LazyKeyVault
```

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `Ctrl+1` | Focus Accounts panel |
| `Ctrl+2` | Focus Subscriptions panel |
| `Ctrl+3` | Focus Resources panel (Key Vaults / Container Apps) |
| `Ctrl+4` | Focus Secrets panel |
| `Ctrl+5` | Focus Details panel |
| `↑/↓` | Navigate lists |
| `Enter` | Reveal secret value |
| `Ctrl+C` | Copy secret to clipboard (works for both Key Vaults and Container Apps) |
| `Ctrl+E` | Edit selected secret (works for both Key Vaults and Container Apps) |
| `Ctrl+P` | Edit secret settings: enabled, content type, expiry, not-before (Key Vault secrets only) |
| `Ctrl+N` | Create new secret (works for both Key Vaults and Container Apps) |
| `Ctrl+D` | Delete selected secret (works for both Key Vaults and Container Apps) |
| `Ctrl+A` | Load all secret values (works for both Key Vaults and Container Apps) |
| `Ctrl+R` | Refresh data (clear cache) |
| `/` | Focus search/filter field |
| `Esc` | Clear search / Quit |

## Layout

```
╭─Accounts (^1)─────────────────╮╭─Secrets (^4)─────────────────────────────╮
│ user@contoso.com              ││ / [filter...]                            │
╰───────────────────────────────╯│ > database-connection                    │
╭─Subscriptions (^2)────────────╮│   api-key                                │
│ myapp:                        ││   storage-key                            │
│   dev-myapp                   │╰──────────────────────────────────────────╯
│   prd-myapp                   │╭─Secret Details (^5)──────────────────────╮
│   tst-myapp                   ││ Name:    my-secret                       │
│ other-subscription            ││ Value:   [Press Enter to load]           │
╰───────────────────────────────╯│ Created: 2025-01-10 14:30:22             │
╭─Resources (^3)────────────────╮│ Updated: 2025-01-14 09:15:33             │
│ kv-dev-myapp                  ││ Expires: Never                           │
│ kv-prd-myapp                  ││ Enabled: Yes                             │
│ ca-myapp                      ││ ─── Actions ───                          │
╰───────────────────────────────╯╰──────────────────────────────────────────╯
 ^1-5:Panels ^C:Copy ^E:Edit ^N:New ^D:Del ^A:LoadAll ^R:Refresh [/]Search
```

## Supported Resources

### Azure Key Vault
- Full CRUD operations on secrets
- Secret metadata (created, updated, expires, enabled status)
- Fast SDK-based operations with intelligent caching

### Azure Container Apps
- Full CRUD operations on secrets
- Uses Azure CLI for secret management
- Parallel loading of secret values and Container Apps resource listing
- **Azure CLI** - Authentication via `az login` and Container Apps secret operationsdata like Key Vaults do

## Security

- **Secret values are hidden by default** - Press Enter to reveal
- Uses Azure CLI authentication - no credentials stored in the app
- Intelligent caching for performance (use Ctrl+R to refresh)

## Tech Stack

- **.NET 10.0** - Cross-platform runtime
- **[Terminal.Gui v2](https://github.com/gui-cs/Terminal.Gui)** - TUI framework
- **Azure SDK** - Key Vault operations (secrets, vault listing)
- **Azure CLI** - Authentication via `az login`
- **TextCopy** - Cross-platform clipboard support

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
