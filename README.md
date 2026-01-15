# LazyAzureKeyVault

A terminal UI for Azure Key Vault secrets management, inspired by [LazyDocker](https://github.com/jesseduffield/lazydocker) and [LazyGit](https://github.com/jesseduffield/lazygit).

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Terminal.Gui](https://img.shields.io/badge/Terminal.Gui-2.0-blue)
[![NuGet](https://img.shields.io/nuget/v/LazyAzureKeyVault.svg)](https://www.nuget.org/packages/LazyAzureKeyVault/)
![License](https://img.shields.io/badge/license-MIT-green)

## Features

- 🔐 **Browse Azure Key Vaults** across multiple accounts and subscriptions
- 🎨 **Colorful UI** with unique colors per subscription/vault name
- ✏️ **Create, edit, and delete** secrets directly from the terminal
- 📋 **Copy to clipboard** with a single keystroke
- 🔍 **Filter secrets** by name
- ⚡ **Fast** - uses Azure SDK with intelligent caching
- ⌨️ **Keyboard-driven** interface

## Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- [Azure CLI](https://docs.microsoft.com/cli/azure/install-azure-cli) installed and logged in

## Installation

### Install as .NET Tool from NuGet

```bash
dotnet tool install --global LazyAzureKeyVault
```

Then run:
```bash
lazykeyvault
```

### Install from Local Build

```bash
# Clone the repository
git clone https://github.com/yourusername/LazyAzureKeyVault.git
cd LazyAzureKeyVault

# Pack and install globally
dotnet pack -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg LazyAzureKeyVault
```

Then run:
```bash
lazykeyvault
```

### Update Local Installation

```bash
dotnet tool uninstall -g LazyAzureKeyVault
dotnet pack -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg LazyAzureKeyVault
```

### Run without Installing

```bash
cd LazyAzureKeyVault
dotnet run
```

## Usage

```bash
# Make sure you're logged in to Azure CLI
az login

# Run the application
lazykeyvault
```

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `Ctrl+1` | Focus Accounts panel |
| `Ctrl+2` | Focus Subscriptions panel |
| `Ctrl+3` | Focus Key Vaults panel |
| `Ctrl+4` | Focus Secrets panel |
| `Ctrl+5` | Focus Details panel |
| `↑/↓` | Navigate lists |
| `Enter` | Reveal secret value |
| `Ctrl+C` | Copy secret to clipboard |
| `Ctrl+E` | Edit selected secret |
| `Ctrl+N` | Create new secret |
| `Ctrl+D` | Delete selected secret |
| `Ctrl+A` | Load all secret values |
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
╭─Key Vaults (^3)───────────────╮│ Updated: 2025-01-14 09:15:33             │
│ kv-dev-myapp                  ││ Expires: Never                           │
│ kv-prd-myapp                  ││ Enabled: Yes                             │
│ kv-tst-myapp                  ││ ─── Actions ───                          │
╰───────────────────────────────╯╰──────────────────────────────────────────╯
 ^1-5:Panels ^C:Copy ^E:Edit ^N:New ^D:Del ^A:LoadAll ^R:Refresh [/]Search
```

## Security

- **Secret values are hidden by default** - Press Enter to reveal
- Uses Azure CLI authentication - no credentials stored in the app
- Intelligent caching for performance (use Ctrl+R to refresh)

## Tech Stack

- **.NET 8.0** - Cross-platform runtime
- **[Terminal.Gui v2](https://github.com/gui-cs/Terminal.Gui)** - TUI framework
- **Azure SDK** - Key Vault operations (secrets, vault listing)
- **Azure CLI** - Authentication via `az login`
- **TextCopy** - Cross-platform clipboard support

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
