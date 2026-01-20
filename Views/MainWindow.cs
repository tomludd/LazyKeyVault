using Terminal.Gui;
using LazyKeyVault.Models;
using LazyKeyVault.Services;
using System.Collections.ObjectModel;

namespace LazyKeyVault.Views;

/// <summary>
/// Main window for the LazyKeyVault application.
/// This partial class contains all field declarations and view elements.
/// </summary>
public partial class MainWindow : Window
{
    // ===== SERVICES =====
    private readonly AzureCliClient _cliClient;
    private readonly AzureResourcesClient _resourcesClient;
    private readonly CacheService _cache = new();

    // ===== UI COMPONENTS - LEFT PANEL =====
    
    // Accounts
    private readonly FrameView _accountsFrame;
    private readonly ListView _accountsList;
    private readonly Label _accountsLoading;

    // Subscriptions
    private readonly FrameView _subscriptionsFrame;
    private readonly ListView _subscriptionsList;
    private readonly Label _subscriptionsLoading;

    // Resources (Key Vaults & Container Apps)
    private readonly FrameView _vaultsFrame;
    private readonly ListView _vaultsList;
    private readonly Label _vaultsLoading;

    // ===== UI COMPONENTS - RIGHT PANEL =====
    
    // Secrets list
    private readonly FrameView _secretsFrame;
    private readonly ListView _secretsList;
    private readonly Label _secretsLoading;
    private readonly TextField _searchField;

    // Secret details
    private readonly FrameView _detailsFrame;
    private readonly Label _secretNameLabel, _createdLabel, _updatedLabel, _expiresLabel, _enabledLabel;
    private readonly TextView _secretValueText;

    // Status bar
    private readonly Label _statusLabel;

    // ===== DATA COLLECTIONS =====
    private List<AzureAccount> _accounts = [];
    private List<AzureAccount?> _subscriptions = [];  // null entries are group headers
    private Dictionary<int, List<AzureAccount>> _groupSubscriptions = [];
    private List<KeyVault> _keyVaults = [];
    private List<ContainerApp> _containerApps = [];
    private List<KeyVaultSecret> _secrets = [], _filteredSecrets = [];
    private List<ContainerAppSecret> _containerAppSecrets = [], _filteredContainerAppSecrets = [];
    private ObservableCollection<string> _accountsSource = [], _secretsSource = [];
    private ColoredListDataSource _subscriptionsSource = new();
    private ColoredListDataSource _vaultsSource = new();

    // ===== SELECTION STATE =====
    private AzureAccount? _selectedAccount, _selectedSubscription;
    private KeyVault? _selectedVault;
    private ContainerApp? _selectedContainerApp;
    private KeyVaultSecret? _selectedSecret;
    private ContainerAppSecret? _selectedContainerAppSecret;
    private string? _currentSecretValue;

    // ===== UI CONSTANTS =====
    private static readonly Color GroupColor = Color.BrightCyan;
}











