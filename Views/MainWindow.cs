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
    private readonly Label _secretNameLabel, _secretValueLabel, _createdLabel, _updatedLabel, _expiresLabel, _enabledLabel;

    // Status
    private readonly Label _statusLabel;

    // Data
    private List<AzureAccount> _accounts = [];
    private List<AzureAccount?> _subscriptions = [];  // null entries are group headers
    private Dictionary<int, List<AzureAccount>> _groupSubscriptions = [];  // Maps group header index to its subscriptions
    private List<KeyVault> _keyVaults = [];
    private List<KeyVaultSecret> _secrets = [], _filteredSecrets = [];
    private ObservableCollection<string> _accountsSource = [], _secretsSource = [];
    private ColoredListDataSource _subscriptionsSource = new();
    private ColoredListDataSource _vaultsSource = new();

    private AzureAccount? _selectedAccount, _selectedSubscription;
    private KeyVault? _selectedVault;
    private KeyVaultSecret? _selectedSecret;
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

        // Key Vaults Frame (bottom third of left)
        _vaultsFrame = new FrameView { Title = "Key Vaults (^3)", X = 0, Y = Pos.Bottom(_subscriptionsFrame), Width = Dim.Percent(35), Height = Dim.Fill(1), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
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
        _secretValueLabel = new Label { Text = "Value: -", X = 1, Y = 3, Width = Dim.Fill(1), Height = 2, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black) } };
        _createdLabel = new Label { Text = "Created: -", X = 1, Y = 6 };
        _updatedLabel = new Label { Text = "Updated: -", X = 1, Y = 7 };
        _expiresLabel = new Label { Text = "Expires: -", X = 1, Y = 8 };
        _enabledLabel = new Label { Text = "Enabled: -", X = 1, Y = 9 };
        var actionsLabel = new Label { Text = "─── Actions ───", X = 1, Y = 11, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.Cyan, Color.Black) } };
        var hintsLabel = new Label { Text = "Ctrl+C Copy  Ctrl+E Edit  Ctrl+N New  Ctrl+D Del  Ctrl+A LoadAll", X = 1, Y = 12, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.Gray, Color.Black) } };
        _detailsFrame.Add(_secretNameLabel, _secretValueLabel, _createdLabel, _updatedLabel, _expiresLabel, _enabledLabel, actionsLabel, hintsLabel);

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
                case KeyCode.A | KeyCode.CtrlMask: _ = LoadAllSecretsAsync(); e.Handled = true; break;
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
        if (force) _azureService.ClearCache();

        Application.Invoke(() => { _accountsLoading.Visible = true; _accountsSource.Clear(); });
        SetStatus("Loading accounts...");

        _accounts = await _azureService.GetAccountsAsync();

        // Group by user (tenant) - unique users
        var uniqueUsers = _accounts.GroupBy(a => a.User?.Name ?? a.TenantId).Select(g => g.First()).ToList();

        Application.Invoke(() =>
        {
            _accountsLoading.Visible = false;
            _accountsSource.Clear();
            foreach (var acc in uniqueUsers) _accountsSource.Add(EscapeHotkey(acc.User?.Name ?? acc.TenantId));
            if (uniqueUsers.Count > 0) _accountsList.SelectedItem = 0;
            SetStatus($"Loaded {uniqueUsers.Count} accounts");
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
        _selectedSubscription = sub;

        // Check if already cached - if so, skip loading indicator
        if (_azureService.AreVaultsCached(sub.Id))
        {
            _keyVaults = (await _azureService.GetKeyVaultsAsync(sub.Id))
                .OrderBy(v => v.Name)
                .ToList();

            Application.Invoke(() =>
            {
                _vaultsSource.Clear();
                ClearSecrets();
                foreach (var v in _keyVaults) _vaultsSource.Add(EscapeHotkey(v.Name), GetNameColor(v.Name));
                SetStatus($"Found {_keyVaults.Count} key vaults (cached)");
            });
            return;
        }

        Application.Invoke(() => { _vaultsLoading.Visible = true; _vaultsSource.Clear(); ClearSecrets(); });
        SetStatus($"Loading vaults for {sub.Name}...");

        await _azureService.SetSubscriptionAsync(sub.Id);
        _keyVaults = (await _azureService.GetKeyVaultsAsync(sub.Id))
            .OrderBy(v => v.Name)
            .ToList();

        Application.Invoke(() =>
        {
            _vaultsLoading.Visible = false;
            _vaultsSource.Clear();
            foreach (var v in _keyVaults) _vaultsSource.Add(EscapeHotkey(v.Name), GetNameColor(v.Name));
            SetStatus($"Found {_keyVaults.Count} key vaults");
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
        if (e.Item < 0 || e.Item >= _keyVaults.Count) return;
        _selectedVault = _keyVaults[e.Item];

        // Check if already cached - if so, skip loading indicator
        if (_azureService.AreSecretsCached(_selectedVault.Name))
        {
            _secrets = (await _azureService.GetSecretsAsync(_selectedVault.Name))
                .OrderBy(s => s.Name)
                .ToList();

            Application.Invoke(() =>
            {
                _secretsSource.Clear();
                ClearSecretDetails();
                _filteredSecrets = [.. _secrets];
                FilterSecrets();
                SetStatus($"Found {_secrets.Count} secrets (cached)");
            });
            return;
        }

        Application.Invoke(() => { _secretsLoading.Visible = true; _secretsSource.Clear(); ClearSecretDetails(); });
        SetStatus($"Loading secrets from {_selectedVault.Name}...");

        // Ensure subscription context is set for this vault
        if (!string.IsNullOrEmpty(_selectedVault.SubscriptionId))
        {
            await _azureService.SetSubscriptionAsync(_selectedVault.SubscriptionId);
        }

        _secrets = (await _azureService.GetSecretsAsync(_selectedVault.Name))
            .OrderBy(s => s.Name)
            .ToList();

        Application.Invoke(() =>
        {
            _secretsLoading.Visible = false;
            _filteredSecrets = [.. _secrets];
            FilterSecrets();
            SetStatus($"Found {_secrets.Count} secrets");
        });
    }

    private void OnSecretSelected(object? sender, ListViewItemEventArgs e)
    {
        if (e.Item < 0 || e.Item >= _filteredSecrets.Count) return;
        _selectedSecret = _filteredSecrets[e.Item];
        
        // Check if we have cached value
        if (_selectedVault != null)
        {
            _currentSecretValue = _azureService.GetCachedSecretValue(_selectedVault.Name, _selectedSecret.Name);
        }
        else
        {
            _currentSecretValue = null;
        }
        UpdateSecretDetails();
    }

    private async void OnSecretEntered(object? sender, ListViewItemEventArgs e) => await RevealSecretAsync();

    private void FilterSecrets()
    {
        var filter = _searchField.Text?.ToString()?.ToLowerInvariant() ?? "";
        _filteredSecrets = string.IsNullOrWhiteSpace(filter) ? [.. _secrets] : _secrets.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        _secretsSource.Clear();
        foreach (var s in _filteredSecrets) _secretsSource.Add(EscapeHotkey(s.Name));
    }

    private void UpdateSecretDetails()
    {
        if (_selectedSecret == null) { ClearSecretDetails(); return; }

        _secretNameLabel.Text = $"Name: {_selectedSecret.Name}";
        _secretValueLabel.Text = _currentSecretValue != null
            ? $"Value: {_currentSecretValue}" : "Value: [Press Enter to load]";

        var attrs = _selectedSecret.Attributes;
        _createdLabel.Text = !string.IsNullOrEmpty(attrs?.Created) ? $"Created: {FormatDate(attrs.Created)}" : "Created: -";
        _updatedLabel.Text = !string.IsNullOrEmpty(attrs?.Updated) ? $"Updated: {FormatDate(attrs.Updated)}" : "Updated: -";
        _expiresLabel.Text = !string.IsNullOrEmpty(attrs?.Expires) ? $"Expires: {FormatDate(attrs.Expires)}" : "Expires: Never";
        _enabledLabel.Text = $"Enabled: {(attrs?.Enabled == true ? "Yes" : "No")}";
    }

    private void ClearSecretDetails()
    {
        _secretNameLabel.Text = "Name: -"; _secretValueLabel.Text = "Value: -";
        _createdLabel.Text = "Created: -"; _updatedLabel.Text = "Updated: -";
        _expiresLabel.Text = "Expires: -"; _enabledLabel.Text = "Enabled: -";
    }

    private void ClearVaultsAndSecrets() { _vaultsSource.Clear(); _keyVaults.Clear(); ClearSecrets(); }
    private void ClearSecrets() { _secretsSource.Clear(); _secrets.Clear(); _filteredSecrets.Clear(); ClearSecretDetails(); }

    private async Task RevealSecretAsync()
    {
        if (_selectedSecret == null || _selectedVault == null) return;
        
        // Check cache first via service
        var cachedValue = _azureService.GetCachedSecretValue(_selectedVault.Name, _selectedSecret.Name);
        if (cachedValue != null)
        {
            _currentSecretValue = cachedValue;
            UpdateSecretDetails();
            SetStatus("Secret loaded (cached)");
            return;
        }

        SetStatus("Fetching secret value...");
        var secret = await _azureService.GetSecretValueAsync(_selectedVault.Name, _selectedSecret.Name);
        Application.Invoke(() =>
        {
            if (secret?.Value != null) 
            { 
                _currentSecretValue = secret.Value; 
                UpdateSecretDetails(); 
                SetStatus("Secret loaded"); 
            }
            else SetStatus("Failed to fetch secret - check permissions");
        });
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

    private async void CopySecretToClipboard()
    {
        if (_selectedSecret == null || _selectedVault == null) return;
        
        // Use current value or fetch from service (which caches)
        var value = _currentSecretValue ?? _azureService.GetCachedSecretValue(_selectedVault.Name, _selectedSecret.Name);
        
        if (value == null)
        {
            SetStatus("Fetching secret...");
            var secret = await _azureService.GetSecretValueAsync(_selectedVault.Name, _selectedSecret.Name);
            value = secret?.Value;
        }
        
        if (value != null) 
        { 
            try { await TextCopy.ClipboardService.SetTextAsync(value); Application.Invoke(() => SetStatus("Copied to clipboard")); } 
            catch (Exception ex) { Application.Invoke(() => SetStatus($"Copy failed: {ex.Message}")); } 
        }
        else Application.Invoke(() => SetStatus("Failed to get value"));
    }

    private void EditSecret()
    {
        if (_selectedSecret == null || _selectedVault == null) { SetStatus("No secret selected"); return; }

        var dialog = new Dialog { Title = "Edit Secret", Width = 60, Height = 10 };
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

    private void CreateNewSecret()
    {
        if (_selectedVault == null) { SetStatus("Select a vault first"); return; }

        var dialog = new Dialog { Title = "Create Secret", Width = 60, Height = 12 };
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

    private void DeleteSecret()
    {
        if (_selectedSecret == null || _selectedVault == null) { SetStatus("No secret selected"); return; }
        if (MessageBox.Query("Delete", $"Delete '{_selectedSecret.Name}'?", "Delete", "Cancel") == 0) _ = DeleteSecretAsync();
    }

    private async Task DeleteSecretAsync()
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
