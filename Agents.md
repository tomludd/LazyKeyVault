# Agents

This document describes the AI agents and tools used in the development of LazyKeyVault.

## Development Agents

### GitHub Copilot (Claude Opus 4.5)
- **Role**: Primary development assistant
- **Contributions**:
  - Initial project scaffolding and architecture
  - Terminal.Gui v2 TUI implementation
  - Azure CLI and SDK integration
  - Caching layer design
  - Color theming and UI enhancements
  - NuGet packaging configuration
  - GitHub Actions workflow setup

## Project Architecture

The project was developed with AI assistance following these principles:

1. **Iterative Development** - Features built incrementally with continuous feedback
2. **Best Practices** - Azure SDK patterns, async/await, proper error handling
3. **User Experience** - LazyGit-inspired keyboard-driven interface
4. **Performance** - Intelligent caching, parallel loading, SDK over CLI for operations

## Tech Stack Decisions

| Component | Choice | Reasoning |
|-----------|--------|-----------|
| UI Framework | Terminal.Gui v2 | Modern TUI with rich widget support |
| Azure Auth | Azure CLI tokens | No credential storage, leverages existing login |
| Secret Operations | Azure SDK | Better performance than CLI process spawning |
| Vault Listing | Azure Resource Manager SDK | Efficient enumeration across subscriptions |
| Clipboard | TextCopy | Cross-platform clipboard support |

## Human Oversight

All AI-generated code was reviewed and tested by human developers before integration.
