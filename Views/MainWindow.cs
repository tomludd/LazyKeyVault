using Terminal.Gui;
using LazyKeyVault.Models;
using LazyKeyVault.Services;
using System.Collections.ObjectModel;

namespace LazyKeyVault.Views;

public class MainWindow : Window
{
    private readonly AzureCliService _azureService;

    // Left panel - Accounts
    private readonly FrameView _accountsFrame;
    private readonly ListView _accountsList;
    private readonly Label _accountsLoading;

    // Left panel - Subscriptions
    private readonly FrameView _subscriptionsFrame;
    private readonly ListView _subscriptionsList;
    private readonly Label _subscriptionsLoading;

    // Left panel - Key Vaults
    private readonly FrameView _vaultsFrame;
    private readonly ListView _vaultsList;
    private readonly Label _vaultsLoading;

    // Right panel - Secrets list
    private readonly FrameView _secretsFrame;
    private readonly ListView _secretsList;
    private readonly Label _secretsLoading;
    private readonly TextField _searchField;

    // Right panel - Secret details
    private readonly FrameView _detailsFrame;
    private readonly Label _secretNameLabel, _createdLabel, _updatedLabel, _expiresLabel, _enabledLabel;
    private readonly TextView _secretValueText;

    // Status
    private readonly Label _statusLabel;

    // Data
    private List<AzureAccount> _accounts = [];
    private List<AzureAccount?> _subscriptions = [];  // null entries are group headers
    private Dictionary<int, List<AzureAccount>> _groupSubscriptions = [];  // Maps group header index to its subscriptions
    private List<KeyVault> _keyVaults = [];
    private List<ContainerApp> _containerApps = [];
    private List<KeyVaultSecret> _secrets = [], _filteredSecrets = [];
    private List<ContainerAppSecret> _containerAppSecrets = [], _filteredContainerAppSecrets = [];
    private ObservableCollection<string> _accountsSource = [], _secretsSource = [];
    private ColoredListDataSource _subscriptionsSource = new();
    private ColoredListDataSource _vaultsSource = new();

    private AzureAccount? _selectedAccount, _selectedSubscription;
    private KeyVault? _selectedVault;
    private ContainerApp? _selectedContainerApp;
    private KeyVaultSecret? _selectedSecret;
    private ContainerAppSecret? _selectedContainerAppSecret;
    private string? _currentSecretValue;

    // UI colors
    private static readonly Color GroupColor = Color.BrightCyan;

    public MainWindow()
    {
        BorderStyle = LineStyle.None;
        _azureService = new AzureCliService();

        // Azure Key Vault themed color scheme (blue tones)
        var azureScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.Blue),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Blue)
        };
        ColorScheme = azureScheme;

        var frameScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.Blue),
            HotNormal = new Terminal.Gui.Attribute(Color.Cyan, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.Cyan, Color.Blue)
        };

        var loadingScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.Yellow, Color.Black) };

        // === LEFT COLUMN (35%) ===

        // Accounts Frame (top of left - compact)
        _accountsFrame = new FrameView { Title = "Accounts (^1)", X = 0, Y = 0, Width = Dim.Percent(35), Height = Dim.Percent(12), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        _accountsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = new ListWrapper<string>(_accountsSource) };
        _accountsList.SelectedItemChanged += OnAccountSelected;
        _accountsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false, ColorScheme = loadingScheme };
        _accountsFrame.Add(_accountsList, _accountsLoading);

        // Subscriptions Frame (middle of left - larger for groups)
        _subscriptionsFrame = new FrameView { Title = "Subscriptions (^2)", X = 0, Y = Pos.Bottom(_accountsFrame), Width = Dim.Percent(35), Height = Dim.Percent(40), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        _subscriptionsSource = new ColoredListDataSource();
        _subscriptionsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = _subscriptionsSource };
        _subscriptionsList.SelectedItemChanged += OnSubscriptionSelected;
        _subscriptionsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false, ColorScheme = loadingScheme };
        _subscriptionsFrame.Add(_subscriptionsList, _subscriptionsLoading);

        // Resources Frame (bottom third of left) - Shows both KeyVaults and ContainerApps
        _vaultsFrame = new FrameView { Title = "Resources (^3)", X = 0, Y = Pos.Bottom(_subscriptionsFrame), Width = Dim.Percent(35), Height = Dim.Fill(1), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        _vaultsSource = new ColoredListDataSource();
        _vaultsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = _vaultsSource };
        _vaultsList.SelectedItemChanged += OnVaultSelected;
        _vaultsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false, ColorScheme = loadingScheme };
        _vaultsFrame.Add(_vaultsList, _vaultsLoading);

        // === RIGHT COLUMN (65%) ===

        // Secrets List Frame (top half of right)
        _secretsFrame = new FrameView { Title = "Secrets (^4)", X = Pos.Right(_accountsFrame), Y = 0, Width = Dim.Fill(), Height = Dim.Percent(50), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        var searchLabel = new Label { Text = "/", X = 0, Y = 0, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black) } };
        _searchField = new TextField { X = 2, Y = 0, Width = Dim.Fill(), Height = 1 };
        _searchField.TextChanged += (_, _) => FilterSecrets();
        _secretsList = new ListView { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(), Source = new ListWrapper<string>(_secretsSource) };
        _secretsList.SelectedItemChanged += OnSecretSelected;
        _secretsList.OpenSelectedItem += OnSecretEntered;
        _secretsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false, ColorScheme = loadingScheme };
        _secretsFrame.Add(searchLabel, _searchField, _secretsList, _secretsLoading);

        // Secret Details Frame (bottom half of right)
        _detailsFrame = new FrameView { Title = "Secret Details (^5)", X = Pos.Right(_accountsFrame), Y = Pos.Bottom(_secretsFrame), Width = Dim.Fill(), Height = Dim.Fill(1), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        _secretNameLabel = new Label { Text = "Name: -", X = 1, Y = 1, Width = Dim.Fill(1), ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black) } };
        var valueLabel = new Label { Text = "Value:", X = 1, Y = 3, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black) } };
        _secretValueText = new TextView { X = 1, Y = 4, Width = Dim.Fill(1), Height = Dim.Fill(4), ReadOnly = true, WordWrap = true, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black) } };
        // All metadata on a single line
        _createdLabel = new Label { Text = "Created: -", X = 1, Y = Pos.Bottom(_secretValueText) };
        _updatedLabel = new Label { Text = "Updated: -", X = Pos.Right(_createdLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _expiresLabel = new Label { Text = "Expires: -", X = Pos.Right(_updatedLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _enabledLabel = new Label { Text = "Enabled: -", X = Pos.Right(_expiresLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        var actionsLabel = new Label { Text = "─── Actions ───", X = 1, Y = Pos.Bottom(_secretValueText) + 1, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.Cyan, Color.Black) } };
        var hintsLabel = new Label { Text = "Ctrl+C Copy  Ctrl+E Edit  Ctrl+N New  Ctrl+D Del  Ctrl+A LoadAll", X = 1, Y = Pos.Bottom(_secretValueText) + 2, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black) } };
        _detailsFrame.Add(_secretNameLabel, valueLabel, _secretValueText, _createdLabel, _updatedLabel, _expiresLabel, _enabledLabel, actionsLabel, hintsLabel);

        // Status bar
        _statusLabel = new Label { Text = "Loading...", X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.White, Color.Blue) } };

        Add(_accountsFrame, _subscriptionsFrame, _vaultsFrame, _secretsFrame, _detailsFrame, _statusLabel);
        SetupKeyBindings();
    }

    private void SetupKeyBindings()
    {
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case KeyCode.D1 | KeyCode.CtrlMask: _accountsList.SetFocus(); e.Handled = true; break;
                case KeyCode.D2 | KeyCode.CtrlMask: _subscriptionsList.SetFocus(); e.Handled = true; break;
                case KeyCode.D3 | KeyCode.CtrlMask: _vaultsList.SetFocus(); e.Handled = true; break;
                case KeyCode.D4 | KeyCode.CtrlMask: _secretsList.SetFocus(); e.Handled = true; break;
                case KeyCode.D5 | KeyCode.CtrlMask: _detailsFrame.SetFocus(); e.Handled = true; break;
                case KeyCode.R | KeyCode.CtrlMask: _ = RefreshDataAsync(true); e.Handled = true; break;
                case KeyCode.C | KeyCode.CtrlMask: CopySecretToClipboard(); e.Handled = true; break;
                case KeyCode.E | KeyCode.CtrlMask: EditSecret(); e.Handled = true; break;
                case KeyCode.N | KeyCode.CtrlMask: CreateNewSecret(); e.Handled = true; break;
                case KeyCode.D | KeyCode.CtrlMask: DeleteSecret(); e.Handled = true; break;
                case KeyCode.A | KeyCode.CtrlMask: _ = LoadAllSecretsForSelectedResourceAsync(); e.Handled = true; break;
                case (KeyCode)'/': _searchField.SetFocus(); e.Handled = true; break;
                case KeyCode.Esc when _searchField.HasFocus: _searchField.Text = ""; _secretsList.SetFocus(); e.Handled = true; break;
                case KeyCode.Q | KeyCode.CtrlMask: Application.RequestStop(); e.Handled = true; break;
                case KeyCode.Esc: Application.RequestStop(); e.Handled = true; break;
            }
        };
    }



    public async Task InitializeAsync()
    {
        SetStatus("Checking Azure CLI...");
        var (isInstalled, error) = await _azureService.IsAzureCliInstalledAsync();
        if (!isInstalled) { MessageBox.ErrorQuery("Azure CLI Not Found", error ?? "Unknown error", "OK"); Application.RequestStop(); return; }
        if (!await _azureService.IsLoggedInAsync()) { MessageBox.ErrorQuery("Not Logged In", "Please run: az login", "OK"); Application.RequestStop(); return; }
        await RefreshDataAsync();
    }

    private async Task RefreshDataAsync(bool force = false)
    {
        // Save current selections
        var savedAccountIndex = _accountsList.SelectedItem;
        var savedSubscriptionIndex = _subscriptionsList.SelectedItem;
        var savedVaultIndex = _vaultsList.SelectedItem;
        var savedSecretIndex = _secretsList.SelectedItem;

        if (force) _azureService.ClearCache();

        // Clear all UI
        Application.Invoke(() => 
        { 
            _accountsLoading.Visible = true; 
            _accountsSource.Clear();
            _subscriptionsSource.Clear();
            _vaultsSource.Clear();
            _secretsSource.Clear();
            ClearSecretDetails();
        });
        SetStatus("Refreshing all data...");

        _accounts = await _azureService.GetAccountsAsync();

        // Group by user (tenant) - unique users
        var uniqueUsers = _accounts.GroupBy(a => a.User?.Name ?? a.TenantId).Select(g => g.First()).ToList();

        Application.Invoke(() =>
        {
            _accountsLoading.Visible = false;
            _accountsSource.Clear();
            foreach (var acc in uniqueUsers) _accountsSource.Add(EscapeHotkey(acc.User?.Name ?? acc.TenantId));
            SetStatus($"Refreshed {uniqueUsers.Count} accounts");
        });

        // Restore account selection and cascade reload
        if (savedAccountIndex >= 0 && savedAccountIndex < uniqueUsers.Count)
        {
            await RestoreSelectionsAsync(savedAccountIndex, savedSubscriptionIndex, savedVaultIndex, savedSecretIndex);
        }
        else if (uniqueUsers.Count > 0)
        {
            // Default to first account if previous selection invalid
            await RestoreSelectionsAsync(0, -1, -1, -1);
        }
    }

    private async Task RestoreSelectionsAsync(int accountIndex, int subscriptionIndex, int vaultIndex, int secretIndex)
    {
        // Manually trigger account selection to load subscriptions
        var uniqueUsers = _accounts.GroupBy(a => a.User?.Name ?? a.TenantId).Select(g => g.First()).ToList();
        if (accountIndex < 0 || accountIndex >= uniqueUsers.Count) return;

        _selectedAccount = uniqueUsers[accountIndex];
        var userName = _selectedAccount.User?.Name ?? _selectedAccount.TenantId;

        Application.Invoke(() => 
        { 
            _accountsList.SelectedItem = accountIndex;
            _subscriptionsLoading.Visible = true; 
            _subscriptionsSource.Clear(); 
            ClearVaultsAndSecrets(); 
        });

        // Load subscriptions for selected account
        var userSubscriptions = _accounts
            .Where(a => (a.User?.Name ?? a.TenantId) == userName)
            .OrderBy(a => a.Name)
            .ToList();

        var grouped = userSubscriptions
            .GroupBy(s => GetSubscriptionBaseName(s.Name))
            .OrderBy(g => g.Key)
            .ToList();

        Application.Invoke(() =>
        {
            _subscriptionsLoading.Visible = false;
            _subscriptionsSource.Clear();
            var flatList = new List<AzureAccount?>();
            _groupSubscriptions = [];
            
            foreach (var group in grouped)
            {
                if (group.Count() > 1)
                {
                    var headerIndex = flatList.Count;
                    _subscriptionsSource.Add($"{EscapeHotkey(group.Key)}:", GroupColor);
                    flatList.Add(null);
                    _groupSubscriptions[headerIndex] = group.OrderBy(s => s.Name).ToList();
                    foreach (var sub in group.OrderBy(s => s.Name))
                    {
                        _subscriptionsSource.Add($"  {EscapeHotkey(sub.Name)}", GetNameColor(sub.Name));
                        flatList.Add(sub);
                    }
                }
                else
                {
                    var sub = group.First();
                    _subscriptionsSource.Add(EscapeHotkey(sub.Name), GetNameColor(sub.Name));
                    flatList.Add(sub);
                }
            }
            _subscriptions = flatList;
        });

        // Start background vault loading
        _ = LoadVaultsAsync(userSubscriptions);

        // Restore subscription selection
        if (subscriptionIndex >= 0 && subscriptionIndex < _subscriptions.Count)
        {
            Application.Invoke(() => _subscriptionsList.SelectedItem = subscriptionIndex);
            await LoadVaultsForSubscriptionAsync(subscriptionIndex);

            // Restore vault selection
            if (vaultIndex >= 0 && vaultIndex < _keyVaults.Count)
            {
                Application.Invoke(() => _vaultsList.SelectedItem = vaultIndex);
                _selectedVault = _keyVaults[vaultIndex];
                await LoadSecretsForVaultAsync(_selectedVault);

                // Restore secret selection
                if (secretIndex >= 0 && secretIndex < _filteredSecrets.Count)
                {
                    Application.Invoke(() => _secretsList.SelectedItem = secretIndex);
                }
            }
        }
        else if (_subscriptions.Count > 0)
        {
            // Default to first subscription
            var firstSubIndex = _subscriptions.FindIndex(s => s != null);
            if (firstSubIndex >= 0)
            {
                Application.Invoke(() => _subscriptionsList.SelectedItem = firstSubIndex);
                await LoadVaultsForSubscriptionAsync(firstSubIndex);
            }
        }
    }

    private async Task LoadSecretsForVaultAsync(KeyVault vault)
    {
        if (_azureService.AreSecretsCached(vault.Name))
        {
            _secrets = (await _azureService.GetSecretsAsync(vault.Name))
                .OrderBy(s => s.Name)
                .ToList();

            Application.Invoke(() =>
            {
                if (_selectedVault?.Name == vault.Name)
                {
                    _secretsSource.Clear();
                    ClearSecretDetails();
                    _filteredSecrets = [.. _secrets];
                    FilterSecrets();
                    SetStatus($"Found {_secrets.Count} secrets (cached)");
                }
            });
            return;
        }

        Application.Invoke(() => { _secretsLoading.Visible = true; _secretsSource.Clear(); ClearSecretDetails(); });
        SetStatus($"Loading secrets from {vault.Name}...");

        if (!string.IsNullOrEmpty(vault.SubscriptionId))
        {
            await _azureService.SetSubscriptionAsync(vault.SubscriptionId);
        }

        _secrets = (await _azureService.GetSecretsAsync(vault.Name))
            .OrderBy(s => s.Name)
            .ToList();

        Application.Invoke(() =>
        {
            if (_selectedVault?.Name == vault.Name)
            {
                _secretsLoading.Visible = false;
                _filteredSecrets = [.. _secrets];
                FilterSecrets();
                SetStatus($"Found {_secrets.Count} secrets");
            }
            else
            {
                _secretsLoading.Visible = false;
            }
        });
    }

    private async void OnAccountSelected(object? sender, ListViewItemEventArgs e)
    {
        var uniqueUsers = _accounts.GroupBy(a => a.User?.Name ?? a.TenantId).Select(g => g.First()).ToList();
        if (e.Item < 0 || e.Item >= uniqueUsers.Count) return;

        _selectedAccount = uniqueUsers[e.Item];
        var userName = _selectedAccount.User?.Name ?? _selectedAccount.TenantId;

        Application.Invoke(() => { _subscriptionsLoading.Visible = true; _subscriptionsSource.Clear(); ClearVaultsAndSecrets(); });
        SetStatus($"Loading subscriptions for {userName}...");

        // Get all subscriptions for this user, sorted by name
        var userSubscriptions = _accounts
            .Where(a => (a.User?.Name ?? a.TenantId) == userName)
            .OrderBy(a => a.Name)
            .ToList();

        // Group subscriptions by base name (remove dev-/tst-/stg-/prd-/sub- prefixes)
        var grouped = userSubscriptions
            .GroupBy(s => GetSubscriptionBaseName(s.Name))
            .OrderBy(g => g.Key)
            .ToList();

        Application.Invoke(() =>
        {
            _subscriptionsLoading.Visible = false;
            _subscriptionsSource.Clear();
            var flatList = new List<AzureAccount?>();
            _groupSubscriptions = [];
            
            foreach (var group in grouped)
            {
                if (group.Count() > 1)
                {
                    // Multiple subscriptions in group - show group header
                    var headerIndex = flatList.Count;
                    _subscriptionsSource.Add($"{EscapeHotkey(group.Key)}:", GroupColor);
                    flatList.Add(null);  // Header placeholder
                    _groupSubscriptions[headerIndex] = group.OrderBy(s => s.Name).ToList();
                    foreach (var sub in group.OrderBy(s => s.Name))
                    {
                        _subscriptionsSource.Add($"  {EscapeHotkey(sub.Name)}", GetNameColor(sub.Name));
                        flatList.Add(sub);
                    }
                }
                else
                {
                    // Single subscription - show directly
                    var sub = group.First();
                    _subscriptionsSource.Add(EscapeHotkey(sub.Name), GetNameColor(sub.Name));
                    flatList.Add(sub);
                }
            }
            _subscriptions = flatList;
            
            if (_subscriptionsSource.Count > 0)
            {
                // Find first actual subscription (not a header)
                var firstSubIndex = _subscriptions.FindIndex(s => s != null);
                if (firstSubIndex >= 0)
                {
                    _subscriptionsList.SelectedItem = firstSubIndex;
                    _ = LoadVaultsForSubscriptionAsync(firstSubIndex);
                }
            }
            else SetStatus($"Found 0 subscriptions");
        });

        // Load vaults for all subscriptions in the background
        _ = LoadVaultsAsync(userSubscriptions);
    }

    private async Task LoadVaultsAsync(List<AzureAccount> subscriptions)
    {
        var subIds = subscriptions.Select(s => s.Id).ToList();
        
        // Load vaults in background without updating UI - only cache them
        // The UI will be updated when user selects a subscription
        await _azureService.LoadVaultsAsync(subIds, (completed, total, currentSub) =>
        {
            // Update status only if not showing subscription-specific info
            if (_selectedSubscription == null || !_azureService.AreVaultsCached(_selectedSubscription.Id))
            {
                var subName = subscriptions.FirstOrDefault(s => s.Id == currentSub)?.Name ?? currentSub;
                Application.Invoke(() => SetStatus($"Loading vaults ({completed}/{total}): {subName.Substring(0, Math.Min(30, subName.Length))}..."));
            }
        });
    }

    private static string GetSubscriptionBaseName(string name)
    {
        // Environment patterns to remove from start, middle, or end
        var envPatterns = new[] { "dev", "tst", "test", "stg", "stage", "staging", "prd", "prod", "production", "sub", "liv", "live" };
        
        var result = name.ToLowerInvariant();
        
        // Remove environment patterns with dashes (handles start, middle, end)
        foreach (var env in envPatterns)
        {
            // Remove "env-" at start
            if (result.StartsWith($"{env}-"))
                result = result[($"{env}-").Length..];
            
            // Remove "-env" at end
            if (result.EndsWith($"-{env}"))
                result = result[..^($"-{env}").Length];
            
            // Remove "-env-" in middle (replace with single dash)
            result = result.Replace($"-{env}-", "-");
        }
        
        // Clean up any double dashes and trim dashes
        while (result.Contains("--"))
            result = result.Replace("--", "-");
        result = result.Trim('-');
        
        return result;
    }

    private static Color GetNameColor(string name)
    {
        // Generate a consistent, deterministic color based on the name
        // Use a simple FNV-1a hash which is deterministic across runs
        var hash = GetDeterministicHash(name);
        
        // Use a set of vibrant, readable colors (avoiding too dark or too similar to background)
        Color[] colors = [
            Color.BrightRed,
            Color.BrightGreen,
            Color.BrightYellow,
            Color.BrightBlue,
            Color.BrightMagenta,
            Color.BrightCyan,
            Color.Red,
            Color.Green,
            Color.Yellow,
            Color.Magenta,
            Color.Cyan,
            new Color(255, 165, 0),   // Orange
            new Color(255, 105, 180), // Hot Pink
            new Color(0, 255, 127),   // Spring Green
            new Color(138, 43, 226),  // Blue Violet
            new Color(255, 215, 0),   // Gold
        ];
        
        var index = Math.Abs(hash) % colors.Length;
        return colors[index];
    }

    // FNV-1a hash - deterministic across process runs
    private static int GetDeterministicHash(string text)
    {
        unchecked
        {
            const int fnvOffsetBasis = unchecked((int)2166136261);
            const int fnvPrime = 16777619;
            
            var hash = fnvOffsetBasis;
            foreach (var c in text)
            {
                hash ^= c;
                hash *= fnvPrime;
            }
            return hash;
        }
    }

    private async void OnSubscriptionSelected(object? sender, ListViewItemEventArgs e)
    {
        await LoadVaultsForSubscriptionAsync(e.Item);
    }

    private async Task LoadVaultsForSubscriptionAsync(int index)
    {
        if (index < 0 || index >= _subscriptions.Count) return;
        var sub = _subscriptions[index];
        
        // Check if this is a group header
        if (sub == null && _groupSubscriptions.TryGetValue(index, out var groupSubs))
        {
            await LoadVaultsForGroupAsync(groupSubs);
            return;
        }
        
        if (sub == null) return;

        // Load both KeyVaults and ContainerApps
        await LoadBothResourcesForSubscriptionAsync(index, sub);
    }

    private async Task LoadBothResourcesForSubscriptionAsync(int index, AzureAccount sub)
    {
        // Check if both are cached
        bool vaultsCached = _azureService.AreVaultsCached(sub.Id);
        bool appsCached = _azureService.AreContainerAppsCached(sub.Id);

        if (vaultsCached && appsCached)
        {
            // Load from cache in parallel
            var vaultsTask = _azureService.GetKeyVaultsAsync(sub.Id);
            var appsTask = _azureService.GetContainerAppsAsync(sub.Id);
            await Task.WhenAll(vaultsTask, appsTask);

            var vaults = vaultsTask.Result.OrderBy(v => v.Name).ToList();
            var apps = appsTask.Result.OrderBy(a => a.Name).ToList();

            Application.Invoke(() =>
            {
                if (_subscriptionsList.SelectedItem == index)
                {
                    _selectedSubscription = sub;
                    _keyVaults = vaults;
                    _containerApps = apps;
                    _vaultsSource.Clear();
                    ClearSecrets();
                    
                    // Add KeyVaults with [KV] prefix
                    foreach (var v in _keyVaults)
                        _vaultsSource.Add($"[KV] {EscapeHotkey(v.Name)}", Color.BrightCyan);
                    
                    // Add ContainerApps with [CA] prefix
                    foreach (var a in _containerApps)
                        _vaultsSource.Add($"[CA] {EscapeHotkey(a.Name)}", Color.BrightGreen);
                    
                    SetStatus($"Found {_keyVaults.Count} key vaults + {_containerApps.Count} container apps (cached)");
                }
            });
            return;
        }

        Application.Invoke(() => { _vaultsLoading.Visible = true; _vaultsSource.Clear(); ClearSecrets(); });
        SetStatus($"Loading resources for {sub.Name}...");

        await _azureService.SetSubscriptionAsync(sub.Id);
        
        // Load both in parallel
        var vaultsLoadTask = _azureService.GetKeyVaultsAsync(sub.Id);
        var appsLoadTask = _azureService.GetContainerAppsAsync(sub.Id);
        await Task.WhenAll(vaultsLoadTask, appsLoadTask);

        var loadedVaults = vaultsLoadTask.Result.OrderBy(v => v.Name).ToList();
        var loadedApps = appsLoadTask.Result.OrderBy(a => a.Name).ToList();

        Application.Invoke(() =>
        {
            if (_subscriptionsList.SelectedItem == index)
            {
                _selectedSubscription = sub;
                _keyVaults = loadedVaults;
                _containerApps = loadedApps;
                _vaultsLoading.Visible = false;
                _vaultsSource.Clear();
                
                // Add KeyVaults with [KV] prefix
                foreach (var v in _keyVaults)
                    _vaultsSource.Add($"[KV] {EscapeHotkey(v.Name)}", Color.BrightCyan);
                
                // Add ContainerApps with [CA] prefix
                foreach (var a in _containerApps)
                    _vaultsSource.Add($"[CA] {EscapeHotkey(a.Name)}", Color.BrightGreen);
                
                SetStatus($"Found {_keyVaults.Count} key vaults + {_containerApps.Count} container apps");
            }
            else
            {
                _vaultsLoading.Visible = false;
            }
        });
    }

    private async Task LoadKeyVaultsForSubscriptionAsync(int index, AzureAccount sub)
    {
        // Check if already cached - if so, skip loading indicator
        if (_azureService.AreVaultsCached(sub.Id))
        {
            var vaults = (await _azureService.GetKeyVaultsAsync(sub.Id))
                .OrderBy(v => v.Name)
                .ToList();

            Application.Invoke(() =>
            {
                // Only update UI if this subscription index is still selected
                if (_subscriptionsList.SelectedItem == index)
                {
                    _selectedSubscription = sub;
                    _keyVaults = vaults;
                    _vaultsSource.Clear();
                    ClearSecrets();
                    foreach (var v in _keyVaults) _vaultsSource.Add(EscapeHotkey(v.Name), GetNameColor(v.Name));
                    SetStatus($"Found {_keyVaults.Count} key vaults (cached)");
                }
            });
            return;
        }

        Application.Invoke(() => { _vaultsLoading.Visible = true; _vaultsSource.Clear(); ClearSecrets(); });
        SetStatus($"Loading vaults for {sub.Name}...");

        await _azureService.SetSubscriptionAsync(sub.Id);
        var loadedVaults = (await _azureService.GetKeyVaultsAsync(sub.Id))
            .OrderBy(v => v.Name)
            .ToList();

        Application.Invoke(() =>
        {
            // Only update UI if this subscription index is still selected
            if (_subscriptionsList.SelectedItem == index)
            {
                _selectedSubscription = sub;
                _keyVaults = loadedVaults;
                _vaultsLoading.Visible = false;
                _vaultsSource.Clear();
                foreach (var v in _keyVaults) _vaultsSource.Add(EscapeHotkey(v.Name), GetNameColor(v.Name));
                SetStatus($"Found {_keyVaults.Count} key vaults");
            }
            else
            {
                _vaultsLoading.Visible = false;
            }
        });
    }

    private async Task LoadContainerAppsForSubscriptionAsync(int index, AzureAccount sub)
    {
        // Check if already cached - if so, skip loading indicator
        if (_azureService.AreContainerAppsCached(sub.Id))
        {
            var apps = (await _azureService.GetContainerAppsAsync(sub.Id))
                .OrderBy(a => a.Name)
                .ToList();

            Application.Invoke(() =>
            {
                // Only update UI if this subscription index is still selected
                if (_subscriptionsList.SelectedItem == index)
                {
                    _selectedSubscription = sub;
                    _containerApps = apps;
                    _vaultsSource.Clear();
                    ClearSecrets();
                    foreach (var a in _containerApps) _vaultsSource.Add(EscapeHotkey(a.Name), GetNameColor(a.Name));
                    SetStatus($"Found {_containerApps.Count} container apps (cached)");
                }
            });
            return;
        }

        Application.Invoke(() => { _vaultsLoading.Visible = true; _vaultsSource.Clear(); ClearSecrets(); });
        SetStatus($"Loading container apps for {sub.Name}...");

        await _azureService.SetSubscriptionAsync(sub.Id);
        var loadedApps = (await _azureService.GetContainerAppsAsync(sub.Id))
            .OrderBy(a => a.Name)
            .ToList();

        Application.Invoke(() =>
        {
            // Only update UI if this subscription index is still selected
            if (_subscriptionsList.SelectedItem == index)
            {
                _selectedSubscription = sub;
                _containerApps = loadedApps;
                _vaultsLoading.Visible = false;
                _vaultsSource.Clear();
                foreach (var a in _containerApps) _vaultsSource.Add(EscapeHotkey(a.Name), GetNameColor(a.Name));
                SetStatus($"Found {_containerApps.Count} container apps");
            }
            else
            {
                _vaultsLoading.Visible = false;
            }
        });
    }

    private async Task LoadVaultsForGroupAsync(List<AzureAccount> subscriptions)
    {
        _selectedSubscription = null;  // Multiple subscriptions selected
        Application.Invoke(() => { _vaultsLoading.Visible = true; _vaultsSource.Clear(); ClearSecrets(); });
        SetStatus($"Loading vaults for {subscriptions.Count} subscriptions...");

        // Load vaults for all subscriptions in parallel
        var tasks = subscriptions.Select(async sub => await _azureService.GetKeyVaultsAsync(sub.Id));
        var results = await Task.WhenAll(tasks);
        
        var allVaults = results.SelectMany(v => v).ToList();
        _keyVaults = allVaults.OrderBy(v => v.Name).ToList();

        Application.Invoke(() =>
        {
            _vaultsLoading.Visible = false;
            _vaultsSource.Clear();
            foreach (var v in _keyVaults) _vaultsSource.Add(EscapeHotkey(v.Name), GetNameColor(v.Name));
            SetStatus($"Found {_keyVaults.Count} key vaults across {subscriptions.Count} subscriptions");
        });
    }

    private async void OnVaultSelected(object? sender, ListViewItemEventArgs e)
    {
        if (e.Item < 0) return;
        
        // Calculate which resource type was selected based on index
        // KeyVaults come first, then ContainerApps
        int totalResources = _keyVaults.Count + _containerApps.Count;
        if (e.Item >= totalResources) return;
        
        // Determine if this is a KeyVault or ContainerApp
        bool isKeyVault = e.Item < _keyVaults.Count;
        
        if (isKeyVault)
        {
            // Load KeyVault secrets
            var vault = _keyVaults[e.Item];
            _selectedVault = vault;
            _selectedContainerApp = null;

            // Check if already cached - if so, skip loading indicator
            if (_azureService.AreSecretsCached(vault.Name))
            {
                var secrets = (await _azureService.GetSecretsAsync(vault.Name))
                    .OrderBy(s => s.Name)
                    .ToList();

                Application.Invoke(() =>
                {
                    // Only update UI if this vault index is still selected
                    if (_vaultsList.SelectedItem == e.Item && e.Item < _keyVaults.Count && _keyVaults[e.Item].Name == vault.Name)
                    {
                        _secrets = secrets;
                        _secretsSource.Clear();
                        ClearSecretDetails();
                        _filteredSecrets = [.. _secrets];
                        FilterSecrets();
                        SetStatus($"Found {_secrets.Count} secrets (cached)");
                        // Auto-select first secret if any exist
                        if (_filteredSecrets.Count > 0)
                        {
                            _secretsList.SelectedItem = 0;
                        }
                    }
                });
                return;
            }

            Application.Invoke(() => { _secretsLoading.Visible = true; _secretsSource.Clear(); ClearSecretDetails(); });
            SetStatus($"Loading secrets from {vault.Name}...");

            if (!string.IsNullOrEmpty(vault.SubscriptionId))
            {
                await _azureService.SetSubscriptionAsync(vault.SubscriptionId);
            }

            var loadedSecrets = (await _azureService.GetSecretsAsync(vault.Name))
                .OrderBy(s => s.Name)
                .ToList();

            Application.Invoke(() =>
            {
                if (_vaultsList.SelectedItem == e.Item && e.Item < _keyVaults.Count && _keyVaults[e.Item].Name == vault.Name)
                {
                    _secrets = loadedSecrets;
                    _secretsLoading.Visible = false;
                    _filteredSecrets = [.. _secrets];
                    FilterSecrets();
                    SetStatus($"Found {_secrets.Count} secrets");
                    // Auto-select first secret if any exist
                    if (_filteredSecrets.Count > 0)
                    {
                        _secretsList.SelectedItem = 0;
                    }
                }
                else
                {
                    _secretsLoading.Visible = false;
                }
            });
        }
        else
        {
            // Load ContainerApp secrets
            int appIndex = e.Item - _keyVaults.Count;
            if (appIndex < 0 || appIndex >= _containerApps.Count) return;
            
            var app = _containerApps[appIndex];
            _selectedContainerApp = app;
            _selectedVault = null;

            // Check if already cached - if so, skip loading indicator
            if (_azureService.AreContainerAppSecretsCached(app.Name))
            {
                var secrets = (await _azureService.GetContainerAppSecretsAsync(app.Name, app.ResourceGroup, app.SubscriptionId))
                    .OrderBy(s => s.Name)
                    .ToList();

                Application.Invoke(() =>
                {
                    // Only update UI if this app is still selected
                    if (_vaultsList.SelectedItem == e.Item && appIndex < _containerApps.Count && _containerApps[appIndex].Name == app.Name)
                    {
                        _containerAppSecrets = secrets;
                        _secretsSource.Clear();
                        ClearSecretDetails();
                        _filteredContainerAppSecrets = [.. _containerAppSecrets];
                        FilterSecretsContainerApp();
                        SetStatus($"Found {_containerAppSecrets.Count} secrets (cached) - Note: Container Apps don't expose secret values");
                        // Auto-select first secret if any exist
                        if (_filteredContainerAppSecrets.Count > 0)
                        {
                            _secretsList.SelectedItem = 0;
                        }
                    }
                });
                return;
            }

            Application.Invoke(() => { _secretsLoading.Visible = true; _secretsSource.Clear(); ClearSecretDetails(); });
            SetStatus($"Loading secrets from {app.Name}...");

            if (!string.IsNullOrEmpty(app.SubscriptionId))
            {
                await _azureService.SetSubscriptionAsync(app.SubscriptionId);
            }

            var loadedSecrets = (await _azureService.GetContainerAppSecretsAsync(app.Name, app.ResourceGroup, app.SubscriptionId))
                .OrderBy(s => s.Name)
                .ToList();

            Application.Invoke(() =>
            {
                if (_vaultsList.SelectedItem == e.Item && appIndex < _containerApps.Count && _containerApps[appIndex].Name == app.Name)
                {
                    _containerAppSecrets = loadedSecrets;
                    _secretsLoading.Visible = false;
                    _filteredContainerAppSecrets = [.. _containerAppSecrets];
                    FilterSecretsContainerApp();
                    SetStatus($"Found {_containerAppSecrets.Count} secrets - Note: Container Apps don't expose secret values");
                    // Auto-select first secret if any exist
                    if (_filteredContainerAppSecrets.Count > 0)
                    {
                        _secretsList.SelectedItem = 0;
                    }
                }
                else
                {
                    _secretsLoading.Visible = false;
                }
            });
        }
    }

    private void OnSecretSelected(object? sender, ListViewItemEventArgs e)
    {
        // Determine which resource type we're dealing with based on what's selected
        if (_selectedVault != null)
        {
            if (e.Item < 0 || e.Item >= _filteredSecrets.Count) return;
            _selectedSecret = _filteredSecrets[e.Item];
            _selectedContainerAppSecret = null;
            
            // Check if we have cached value
            _currentSecretValue = _azureService.GetCachedSecretValue(_selectedVault.Name, _selectedSecret.Name);
            UpdateSecretDetails();
        }
        else if (_selectedContainerApp != null)
        {
            if (e.Item < 0 || e.Item >= _filteredContainerAppSecrets.Count) return;
            var secret = _filteredContainerAppSecrets[e.Item];
            
            // Check if we have cached value
            var cachedValue = _azureService.GetCachedContainerAppSecretValue(_selectedContainerApp.Name, secret.Name);
            _selectedContainerAppSecret = cachedValue != null ? secret with { Value = cachedValue } : secret;
            _selectedSecret = null;
            UpdateSecretDetailsContainerApp();
        }
    }

    private async void OnSecretEntered(object? sender, ListViewItemEventArgs e) => await RevealSecretAsync();

    private void FilterSecrets()
    {
        // Filter based on which resource type is selected
        if (_selectedVault != null)
        {
            var filter = _searchField.Text?.ToString()?.ToLowerInvariant() ?? "";
            var previousSelection = _selectedSecret;
            _filteredSecrets = string.IsNullOrWhiteSpace(filter) ? [.. _secrets] : _secrets.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            _secretsSource.Clear();
            foreach (var s in _filteredSecrets) _secretsSource.Add(EscapeHotkey(s.Name));
            
            // Maintain selection if the previously selected secret is still in the filtered list
            if (previousSelection != null && _filteredSecrets.Any(s => s.Name == previousSelection.Name))
            {
                var newIndex = _filteredSecrets.FindIndex(s => s.Name == previousSelection.Name);
                if (newIndex >= 0)
                {
                    _secretsList.SelectedItem = newIndex;
                }
            }
            else if (_filteredSecrets.Count > 0)
            {
                // Select first item if previous selection is filtered out
                _secretsList.SelectedItem = 0;
            }
            else
            {
                // No secrets in filtered list, clear selection
                _selectedSecret = null;
                ClearSecretDetails();
            }
        }
        else if (_selectedContainerApp != null)
        {
            FilterSecretsContainerApp();
        }
    }

    private void FilterSecretsContainerApp()
    {
        var filter = _searchField.Text?.ToString()?.ToLowerInvariant() ?? "";
        var previousSelection = _selectedContainerAppSecret;
        _filteredContainerAppSecrets = string.IsNullOrWhiteSpace(filter) ? [.. _containerAppSecrets] : _containerAppSecrets.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        _secretsSource.Clear();
        foreach (var s in _filteredContainerAppSecrets) _secretsSource.Add(EscapeHotkey(s.Name));
        
        // Maintain selection if the previously selected secret is still in the filtered list
        if (previousSelection != null && _filteredContainerAppSecrets.Any(s => s.Name == previousSelection.Name))
        {
            var newIndex = _filteredContainerAppSecrets.FindIndex(s => s.Name == previousSelection.Name);
            if (newIndex >= 0)
            {
                _secretsList.SelectedItem = newIndex;
            }
        }
        else if (_filteredContainerAppSecrets.Count > 0)
        {
            // Select first item if previous selection is filtered out
            _secretsList.SelectedItem = 0;
        }
        else
        {
            // No secrets in filtered list, clear selection
            _selectedContainerAppSecret = null;
            ClearSecretDetails();
        }
    }

    private void UpdateSecretDetails()
    {
        if (_selectedSecret == null) { ClearSecretDetails(); return; }

        _secretNameLabel.Text = $"Name: {_selectedSecret.Name}";
        _secretValueText.Text = _currentSecretValue ?? "[Press Enter to load]";

        var attrs = _selectedSecret.Attributes;
        _createdLabel.Text = !string.IsNullOrEmpty(attrs?.Created) ? $"Created: {FormatDate(attrs.Created)}" : "Created: -";
        _updatedLabel.Text = !string.IsNullOrEmpty(attrs?.Updated) ? $"Updated: {FormatDate(attrs.Updated)}" : "Updated: -";
        _expiresLabel.Text = !string.IsNullOrEmpty(attrs?.Expires) ? $"Expires: {FormatDate(attrs.Expires)}" : "Expires: Never";
        _enabledLabel.Text = $"Enabled: {(attrs?.Enabled == true ? "Yes" : "No")}";
    }

    private void UpdateSecretDetailsContainerApp()
    {
        if (_selectedContainerAppSecret == null) { ClearSecretDetails(); return; }

        _secretNameLabel.Text = $"Name: {_selectedContainerAppSecret.Name}";
        _secretValueText.Text = _selectedContainerAppSecret.Value ?? "[Press Enter to load via Azure CLI]";
        _createdLabel.Text = "Created: -";
        _updatedLabel.Text = "Updated: -";
        _expiresLabel.Text = "Expires: -";
        _enabledLabel.Text = "Enabled: -";
    }

    private void ClearSecretDetails()
    {
        _secretNameLabel.Text = "Name: -"; 
        _secretValueText.Text = "-";
        _createdLabel.Text = "Created: -"; _updatedLabel.Text = "Updated: -";
        _expiresLabel.Text = "Expires: -"; _enabledLabel.Text = "Enabled: -";
    }

    private void ClearVaultsAndSecrets() { _vaultsSource.Clear(); _keyVaults.Clear(); ClearSecrets(); }
    private void ClearSecrets() { _secretsSource.Clear(); _secrets.Clear(); _filteredSecrets.Clear(); ClearSecretDetails(); }

    private async Task RevealSecretAsync()
    {
        // Determine which resource type based on what's selected
        if (_selectedVault != null && _selectedSecret != null)
        {
            var vault = _selectedVault;
            var secret = _selectedSecret;
            
            // Check cache first via service
            var cachedValue = _azureService.GetCachedSecretValue(vault.Name, secret.Name);
            if (cachedValue != null)
            {
                _currentSecretValue = cachedValue;
                UpdateSecretDetails();
                SetStatus("Secret loaded (cached)");
                return;
            }

            SetStatus("Fetching secret value...");
            var secretValue = await _azureService.GetSecretValueAsync(vault.Name, secret.Name);
            Application.Invoke(() =>
            {
                // Only update if the same vault and secret are still selected
                if (_selectedVault?.Name == vault.Name && _selectedSecret?.Name == secret.Name)
                {
                    if (secretValue?.Value != null) 
                    { 
                        _currentSecretValue = secretValue.Value; 
                        UpdateSecretDetails(); 
                        SetStatus("Secret loaded"); 
                    }
                    else SetStatus("Failed to fetch secret - check permissions");
                }
            });
        }
        else if (_selectedContainerApp != null && _selectedContainerAppSecret != null)
        {
            var app = _selectedContainerApp;
            var secret = _selectedContainerAppSecret;
            
            // Check cache first
            var cachedValue = _azureService.GetCachedContainerAppSecretValue(app.Name, secret.Name);
            if (cachedValue != null)
            {
                _selectedContainerAppSecret = _selectedContainerAppSecret with { Value = cachedValue };
                UpdateSecretDetailsContainerApp();
                SetStatus("Secret loaded (cached)");
                return;
            }

            SetStatus("Fetching secret value via Azure CLI...");
            var secretValue = await _azureService.GetContainerAppSecretValueAsync(app.Name, app.ResourceGroup, app.SubscriptionId, secret.Name);
            Application.Invoke(() =>
            {
                // Only update if the same app and secret are still selected
                if (_selectedContainerApp?.Name == app.Name && _selectedContainerAppSecret?.Name == secret.Name)
                {
                    if (secretValue?.Value != null) 
                    { 
                        _selectedContainerAppSecret = _selectedContainerAppSecret with { Value = secretValue.Value };
                        UpdateSecretDetailsContainerApp();
                        
                        // Check if it's an error message
                        if (secretValue.Value.StartsWith("ERROR:"))
                        {
                            SetStatus("Failed to fetch secret - see Value field for details");
                        }
                        else
                        {
                            SetStatus("Secret loaded");
                        }
                    }
                    else SetStatus("Failed to fetch secret - no response from Azure CLI");
                }
            });
        }
    }

    private async Task LoadAllSecretsForSelectedResourceAsync()
    {
        if (_selectedVault != null)
        {
            await LoadAllSecretsAsync();
        }
        else if (_selectedContainerApp != null)
        {
            await LoadAllContainerAppSecretsAsync();
        }
        else
        {
            SetStatus("No resource selected");
        }
    }

    private async Task LoadAllSecretsAsync()
    {
        if (_selectedVault == null || _secrets.Count == 0) 
        { 
            SetStatus("No vault or secrets selected"); 
            return; 
        }

        // Filter to only secrets not already cached
        var vaultName = _selectedVault.Name;
        var secretsToLoad = _secrets
            .Where(s => _azureService.GetCachedSecretValue(vaultName, s.Name) == null)
            .ToList();

        if (secretsToLoad.Count == 0)
        {
            SetStatus("All secrets already loaded");
            return;
        }

        SetStatus($"Loading {secretsToLoad.Count} secrets (parallel)...");
        var loaded = 0;
        var failed = 0;

        // Load in parallel batches of 10 to avoid overwhelming the system
        const int batchSize = 10;
        for (var i = 0; i < secretsToLoad.Count; i += batchSize)
        {
            var batch = secretsToLoad.Skip(i).Take(batchSize).ToList();
            var tasks = batch.Select(async secret =>
            {
                var result = await _azureService.GetSecretValueAsync(vaultName, secret.Name);
                return (secret.Name, result?.Value);
            });

            var results = await Task.WhenAll(tasks);
            foreach (var (name, value) in results)
            {
                if (value != null)
                {
                    Interlocked.Increment(ref loaded);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }
            }
            
            var currentLoaded = loaded;
            var currentFailed = failed;
            Application.Invoke(() => SetStatus($"Loading secrets... {currentLoaded + (_secrets.Count - secretsToLoad.Count)}/{_secrets.Count}" + (currentFailed > 0 ? $" ({currentFailed} failed)" : "")));
        }

        // Refresh current selection to show value
        if (_selectedSecret != null)
        {
            _currentSecretValue = _azureService.GetCachedSecretValue(vaultName, _selectedSecret.Name);
            Application.Invoke(UpdateSecretDetails);
        }

        var totalLoaded = loaded + (_secrets.Count - secretsToLoad.Count);
        Application.Invoke(() => SetStatus($"Loaded {totalLoaded} secrets" + (failed > 0 ? $" ({failed} failed)" : "")));
    }

    private async Task LoadAllContainerAppSecretsAsync()
    {
        if (_selectedContainerApp == null || _containerAppSecrets.Count == 0) 
        { 
            SetStatus("No container app or secrets selected"); 
            return; 
        }

        // Filter to only secrets not already cached
        var appName = _selectedContainerApp.Name;
        var secretsToLoad = _containerAppSecrets
            .Where(s => _azureService.GetCachedContainerAppSecretValue(appName, s.Name) == null)
            .ToList();

        if (secretsToLoad.Count == 0)
        {
            SetStatus("All secrets already loaded");
            return;
        }

        SetStatus($"Loading {secretsToLoad.Count} container app secrets (parallel)...");
        var loaded = 0;
        var failed = 0;

        // Load in parallel batches of 10 to avoid overwhelming the system
        const int batchSize = 10;
        for (var i = 0; i < secretsToLoad.Count; i += batchSize)
        {
            var batch = secretsToLoad.Skip(i).Take(batchSize).ToList();
            var tasks = batch.Select(async secret =>
            {
                var result = await _azureService.GetContainerAppSecretValueAsync(
                    _selectedContainerApp.Name, 
                    _selectedContainerApp.ResourceGroup, 
                    _selectedContainerApp.SubscriptionId, 
                    secret.Name);
                return (secret.Name, result?.Value);
            });

            var results = await Task.WhenAll(tasks);
            foreach (var (name, value) in results)
            {
                if (value != null && !value.StartsWith("ERROR:"))
                {
                    Interlocked.Increment(ref loaded);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }
            }
            
            var currentLoaded = loaded;
            var currentFailed = failed;
            Application.Invoke(() => SetStatus($"Loading secrets... {currentLoaded + (_containerAppSecrets.Count - secretsToLoad.Count)}/{_containerAppSecrets.Count}" + (currentFailed > 0 ? $" ({currentFailed} failed)" : "")));
        }

        // Refresh current selection to show value
        if (_selectedContainerAppSecret != null)
        {
            var cachedValue = _azureService.GetCachedContainerAppSecretValue(appName, _selectedContainerAppSecret.Name);
            if (cachedValue != null)
            {
                _selectedContainerAppSecret = _selectedContainerAppSecret with { Value = cachedValue };
                Application.Invoke(UpdateSecretDetailsContainerApp);
            }
        }

        var totalLoaded = loaded + (_containerAppSecrets.Count - secretsToLoad.Count);
        Application.Invoke(() => SetStatus($"Loaded {totalLoaded} secrets" + (failed > 0 ? $" ({failed} failed)" : "")));
    }

    private async void CopySecretToClipboard()
    {
        // Determine which resource type based on what's selected
        if (_selectedVault != null && _selectedSecret != null)
        {
            // Use current value or fetch from service (which caches)
            var secretValue = _currentSecretValue ?? _azureService.GetCachedSecretValue(_selectedVault.Name, _selectedSecret.Name);
            
            if (secretValue == null)
            {
                SetStatus("Fetching secret...");
                var secret = await _azureService.GetSecretValueAsync(_selectedVault.Name, _selectedSecret.Name);
                secretValue = secret?.Value;
            }
            
            if (secretValue != null) 
            { 
                try { await TextCopy.ClipboardService.SetTextAsync(secretValue); Application.Invoke(() => SetStatus("Copied to clipboard")); } 
                catch (Exception ex) { Application.Invoke(() => SetStatus($"Copy failed: {ex.Message}")); } 
            }
            else Application.Invoke(() => SetStatus("Failed to get value"));
        }
        else if (_selectedContainerApp != null && _selectedContainerAppSecret != null)
        {
            var app = _selectedContainerApp;
            var secret = _selectedContainerAppSecret;
            var secretValue = secret.Value ?? _azureService.GetCachedContainerAppSecretValue(app.Name, secret.Name);
            
            if (secretValue == null)
            {
                SetStatus("Fetching secret via Azure CLI...");
                var secretResult = await _azureService.GetContainerAppSecretValueAsync(app.Name, app.ResourceGroup, app.SubscriptionId, secret.Name);
                secretValue = secretResult?.Value;
                
                Application.Invoke(() =>
                {
                    if (secretValue != null && _selectedContainerApp?.Name == app.Name && _selectedContainerAppSecret?.Name == secret.Name)
                    {
                        _selectedContainerAppSecret = _selectedContainerAppSecret with { Value = secretValue };
                        UpdateSecretDetailsContainerApp();
                    }
                });
            }
            
            // Don't copy if it's an error message
            if (secretValue != null && secretValue.StartsWith("ERROR:"))
            {
                Application.Invoke(() => SetStatus("Cannot copy - error fetching secret. See Value field."));
                return;
            }
            
            if (secretValue != null)
            { 
                try { await TextCopy.ClipboardService.SetTextAsync(secretValue); Application.Invoke(() => SetStatus("Copied to clipboard")); } 
                catch (Exception ex) { Application.Invoke(() => SetStatus($"Copy failed: {ex.Message}")); } 
            }
            else 
            {
                Application.Invoke(() => SetStatus("Failed to get value"));
            }
        }
    }

    private void EditSecret()
    {
        // Handle both KeyVault and Container App secrets
        if (_selectedVault != null && _selectedSecret != null)
        {
            EditKeyVaultSecret();
        }
        else if (_selectedContainerApp != null && _selectedContainerAppSecret != null)
        {
            EditContainerAppSecret();
        }
        else
        {
            SetStatus("No secret selected");
        }
    }

    private void EditKeyVaultSecret()
    {
        if (_selectedSecret == null || _selectedVault == null) return;

        var dialog = new Dialog { Title = "Edit Key Vault Secret", Width = 60, Height = 10 };
        var nameLabel = new Label { Text = $"Name: {_selectedSecret.Name}", X = 1, Y = 1 };
        var valueLabel = new Label { Text = "New Value:", X = 1, Y = 2 };
        var valueField = new TextField { Text = _currentSecretValue ?? "", X = 1, Y = 3, Width = Dim.Fill(1) };

        var saveBtn = new Button { Text = "Save" };
        saveBtn.Accepting += async (_, _) =>
        {
            var newValue = valueField.Text?.ToString() ?? "";
            if (string.IsNullOrEmpty(newValue)) { MessageBox.ErrorQuery("Error", "Value cannot be empty", "OK"); return; }
            dialog.RequestStop(); SetStatus("Updating...");
            var (success, error) = await _azureService.SetSecretAsync(_selectedVault!.Name, _selectedSecret!.Name, newValue);
            Application.Invoke(() => { if (success) { _currentSecretValue = newValue; UpdateSecretDetails(); SetStatus("Updated"); } else SetStatus($"Failed: {error}"); });
        };

        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Accepting += (_, _) => dialog.RequestStop();

        dialog.Add(nameLabel, valueLabel, valueField);
        dialog.AddButton(saveBtn); dialog.AddButton(cancelBtn);
        Application.Run(dialog);
    }

    private void EditContainerAppSecret()
    {
        if (_selectedContainerAppSecret == null || _selectedContainerApp == null) return;

        var currentValue = _selectedContainerAppSecret.Value ?? _azureService.GetCachedContainerAppSecretValue(_selectedContainerApp.Name, _selectedContainerAppSecret.Name);

        var dialog = new Dialog { Title = "Edit Container App Secret", Width = 60, Height = 10 };
        var nameLabel = new Label { Text = $"Name: {_selectedContainerAppSecret.Name}", X = 1, Y = 1 };
        var valueLabel = new Label { Text = "New Value:", X = 1, Y = 2 };
        var valueField = new TextField { Text = currentValue ?? "", X = 1, Y = 3, Width = Dim.Fill(1) };

        var saveBtn = new Button { Text = "Save" };
        saveBtn.Accepting += async (_, _) =>
        {
            var newValue = valueField.Text?.ToString() ?? "";
            if (string.IsNullOrEmpty(newValue)) { MessageBox.ErrorQuery("Error", "Value cannot be empty", "OK"); return; }
            dialog.RequestStop(); SetStatus("Updating via Azure CLI...");
            var (success, error) = await _azureService.SetContainerAppSecretAsync(_selectedContainerApp!.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId, _selectedContainerAppSecret!.Name, newValue);
            Application.Invoke(() => 
            { 
                if (success) 
                { 
                    _selectedContainerAppSecret = _selectedContainerAppSecret with { Value = newValue };
                    UpdateSecretDetailsContainerApp(); 
                    SetStatus("Updated"); 
                } 
                else 
                {
                    SetStatus($"Failed: {error}");
                }
            });
        };

        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Accepting += (_, _) => dialog.RequestStop();

        dialog.Add(nameLabel, valueLabel, valueField);
        dialog.AddButton(saveBtn); dialog.AddButton(cancelBtn);
        Application.Run(dialog);
    }

    private void CreateNewSecret()
    {
        // Handle both KeyVault and Container App secrets
        if (_selectedVault != null)
        {
            CreateNewKeyVaultSecret();
        }
        else if (_selectedContainerApp != null)
        {
            CreateNewContainerAppSecret();
        }
        else
        {
            SetStatus("Select a vault or container app first");
        }
    }

    private void CreateNewKeyVaultSecret()
    {
        if (_selectedVault == null) return;

        var dialog = new Dialog { Title = "Create Key Vault Secret", Width = 60, Height = 12 };
        var nameLabel = new Label { Text = "Name:", X = 1, Y = 1 };
        var nameField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1) };
        var valueLabel = new Label { Text = "Value:", X = 1, Y = 4 };
        var valueField = new TextField { X = 1, Y = 5, Width = Dim.Fill(1) };

        var createBtn = new Button { Text = "Create" };
        createBtn.Accepting += async (_, _) =>
        {
            var name = nameField.Text?.ToString() ?? ""; var value = valueField.Text?.ToString() ?? "";
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value)) { MessageBox.ErrorQuery("Error", "Name and value required", "OK"); return; }
            dialog.RequestStop(); SetStatus("Creating...");
            var (success, error) = await _azureService.SetSecretAsync(_selectedVault!.Name, name, value);
            Application.Invoke(async () =>
            {
                if (success) { _azureService.InvalidateSecrets(_selectedVault!.Name); _secrets = (await _azureService.GetSecretsAsync(_selectedVault!.Name)).OrderBy(s => s.Name).ToList(); _filteredSecrets = [.. _secrets]; FilterSecrets(); SetStatus("Created"); }
                else SetStatus($"Failed: {error}");
            });
        };

        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Accepting += (_, _) => dialog.RequestStop();

        dialog.Add(nameLabel, nameField, valueLabel, valueField);
        dialog.AddButton(createBtn); dialog.AddButton(cancelBtn);
        Application.Run(dialog);
    }

    private void CreateNewContainerAppSecret()
    {
        if (_selectedContainerApp == null) return;

        var dialog = new Dialog { Title = "Create Container App Secret", Width = 60, Height = 12 };
        var nameLabel = new Label { Text = "Name:", X = 1, Y = 1 };
        var nameField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1) };
        var valueLabel = new Label { Text = "Value:", X = 1, Y = 4 };
        var valueField = new TextField { X = 1, Y = 5, Width = Dim.Fill(1) };

        var createBtn = new Button { Text = "Create" };
        createBtn.Accepting += async (_, _) =>
        {
            var name = nameField.Text?.ToString() ?? ""; var value = valueField.Text?.ToString() ?? "";
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value)) { MessageBox.ErrorQuery("Error", "Name and value required", "OK"); return; }
            dialog.RequestStop(); SetStatus("Creating via Azure CLI...");
            var (success, error) = await _azureService.CreateContainerAppSecretAsync(_selectedContainerApp!.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId, name, value);
            Application.Invoke(async () =>
            {
                if (success) 
                { 
                    _azureService.InvalidateContainerAppSecrets(_selectedContainerApp!.Name); 
                    _containerAppSecrets = (await _azureService.GetContainerAppSecretsAsync(_selectedContainerApp!.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId)).OrderBy(s => s.Name).ToList(); 
                    _filteredContainerAppSecrets = [.. _containerAppSecrets]; 
                    FilterSecretsContainerApp(); 
                    SetStatus("Created"); 
                }
                else SetStatus($"Failed: {error}");
            });
        };

        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Accepting += (_, _) => dialog.RequestStop();

        dialog.Add(nameLabel, nameField, valueLabel, valueField);
        dialog.AddButton(createBtn); dialog.AddButton(cancelBtn);
        Application.Run(dialog);
    }

    private void DeleteSecret()
    {
        // Handle both KeyVault and Container App secrets
        if (_selectedVault != null && _selectedSecret != null)
        {
            if (MessageBox.Query("Delete", $"Delete Key Vault secret '{_selectedSecret.Name}'?", "Delete", "Cancel") == 0) 
                _ = DeleteKeyVaultSecretAsync();
        }
        else if (_selectedContainerApp != null && _selectedContainerAppSecret != null)
        {
            if (MessageBox.Query("Delete", $"Delete Container App secret '{_selectedContainerAppSecret.Name}'?", "Delete", "Cancel") == 0) 
                _ = DeleteContainerAppSecretAsync();
        }
        else
        {
            SetStatus("No secret selected");
        }
    }

    private async Task DeleteKeyVaultSecretAsync()
    {
        if (_selectedSecret == null || _selectedVault == null) return;
        SetStatus("Deleting...");
        var (success, error) = await _azureService.DeleteSecretAsync(_selectedVault.Name, _selectedSecret.Name);
        Application.Invoke(async () =>
        {
            if (success) { _azureService.InvalidateSecrets(_selectedVault!.Name); _secrets = (await _azureService.GetSecretsAsync(_selectedVault!.Name)).OrderBy(s => s.Name).ToList(); _filteredSecrets = [.. _secrets]; FilterSecrets(); ClearSecretDetails(); SetStatus("Deleted"); }
            else SetStatus($"Failed: {error}");
        });
    }

    private async Task DeleteContainerAppSecretAsync()
    {
        if (_selectedContainerAppSecret == null || _selectedContainerApp == null) return;
        SetStatus("Deleting via Azure CLI...");
        var (success, error) = await _azureService.DeleteContainerAppSecretAsync(_selectedContainerApp.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId, _selectedContainerAppSecret.Name);
        Application.Invoke(async () =>
        {
            if (success) 
            { 
                _azureService.InvalidateContainerAppSecrets(_selectedContainerApp!.Name); 
                _containerAppSecrets = (await _azureService.GetContainerAppSecretsAsync(_selectedContainerApp!.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId)).OrderBy(s => s.Name).ToList(); 
                _filteredContainerAppSecrets = [.. _containerAppSecrets]; 
                FilterSecretsContainerApp(); 
                ClearSecretDetails(); 
                SetStatus("Deleted"); 
            }
            else SetStatus($"Failed: {error}");
        });
    }

    private void SetStatus(string msg) => _statusLabel.Text = $" {msg} | ^1-5:Panels ^C:Copy ^E:Edit ^N:New ^D:Del ^A:LoadAll ^R:Refresh [/]Search [Esc]Quit";

    private static string FormatDate(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate)) return "-";
        if (DateTimeOffset.TryParse(isoDate, out var dto))
            return dto.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        return isoDate;
    }

    // Escape underscores to prevent Terminal.Gui from treating them as hotkey markers
    private static string EscapeHotkey(string text) => text.Replace("_", "__");
}
