using Terminal.Gui;
using LazyKeyVault.Models;
using LazyKeyVault.Services;

namespace LazyKeyVault.Views;

/// <summary>
/// Partial class containing event handlers for MainWindow.
/// Includes handlers for account, subscription, vault, and secret selection events.
/// </summary>
public partial class MainWindow
{
    private async void OnAccountSelected(object? sender, ListViewItemEventArgs e)
    {
        var uniqueUsers = _accounts.GroupBy(a => a.User?.Name ?? a.TenantId).Select(g => g.First()).ToList();
        if (e.Item < 0 || e.Item >= uniqueUsers.Count)
        {
            return;
        }

        _selectedAccount = uniqueUsers[e.Item];
        var userName = _selectedAccount.User?.Name ?? _selectedAccount.TenantId;

        Application.Invoke(() =>
        {
            _subscriptionsLoading.Visible = true;
            _subscriptionsSource.Clear();
            ClearVaultsAndSecrets();
        });
        
        SetStatus($"Loading subscriptions for {userName}...");

        // Get all subscriptions for this user, sorted by name
        var userSubscriptions = LoadSubscriptionsForAccount(_accounts, userName);

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
            else
            {
                SetStatus($"Found 0 subscriptions");
            }
        });

        // Load vaults for all subscriptions in the background
        _ = LoadVaultsInBackgroundAsync(userSubscriptions, (completed, total, currentSub) =>
        {
            // Update status only if not showing subscription-specific info
            if (_selectedSubscription == null || !_resourcesClient.AreVaultsCached(_selectedSubscription.Id))
            {
                var subName = userSubscriptions.FirstOrDefault(s => s.Id == currentSub)?.Name ?? currentSub;
                Application.Invoke(() => SetStatus($"Loading vaults ({completed}/{total}): {subName.Substring(0, Math.Min(30, subName.Length))}..."));
            }
        });
    }

    private async void OnSubscriptionSelected(object? sender, ListViewItemEventArgs e)
    {
        await LoadVaultsForSubscriptionAsync(e.Item);
    }

    private async Task LoadVaultsForSubscriptionAsync(int index)
    {
        if (index < 0 || index >= _subscriptions.Count)
        {
            return;
        }
        
        var sub = _subscriptions[index];
        
        // Check if this is a group header
        if (sub == null && _groupSubscriptions.TryGetValue(index, out var groupSubs))
        {
            await LoadVaultsForGroupAsync(groupSubs);
            return;
        }
        
        if (sub == null)
        {
            return;
        }

        // Load both KeyVaults and ContainerApps
        await LoadBothResourcesForSubscriptionAsync(index, sub);
    }

    private async Task LoadBothResourcesForSubscriptionAsync(int index, AzureAccount sub)
    {
        // Check if both are cached
        if (_resourcesClient.AreVaultsCached(sub.Id) && _resourcesClient.AreContainerAppsCached(sub.Id))
        {
            var (vaults, apps) = await LoadBothResourcesForSubscriptionAsync(sub.Id, sub.TenantId);

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
                    {
                        _vaultsSource.Add($"[KV] {EscapeHotkey(v.Name)}", Color.BrightCyan);
                    }
                    
                    // Add ContainerApps with [CA] prefix
                    foreach (var a in _containerApps)
                    {
                        _vaultsSource.Add($"[CA] {EscapeHotkey(a.Name)}", Color.BrightGreen);
                    }
                    
                    SetStatus($"Found {_keyVaults.Count} key vaults + {_containerApps.Count} container apps (cached)");
                }
            });
            return;
        }

        Application.Invoke(() =>
        {
            _vaultsLoading.Visible = true;
            _vaultsSource.Clear();
            ClearSecrets();
        });
        
        SetStatus($"Loading resources for {sub.Name}...");

        var (loadedVaults, loadedApps) = await LoadBothResourcesForSubscriptionAsync(sub.Id, sub.TenantId);

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
                {
                    _vaultsSource.Add($"[KV] {EscapeHotkey(v.Name)}", Color.BrightCyan);
                }
                
                // Add ContainerApps with [CA] prefix
                foreach (var a in _containerApps)
                {
                    _vaultsSource.Add($"[CA] {EscapeHotkey(a.Name)}", Color.BrightGreen);
                }
                
                SetStatus($"Found {_keyVaults.Count} key vaults + {_containerApps.Count} container apps");
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
        
        Application.Invoke(() =>
        {
            _vaultsLoading.Visible = true;
            _vaultsSource.Clear();
            ClearSecrets();
        });
        
        SetStatus($"Loading resources for {subscriptions.Count} subscriptions...");

        // Load both Key Vaults and Container Apps in parallel
        var vaultTasks = subscriptions.Select(sub => _resourcesClient.GetKeyVaultsAsync(sub.Id));
        var appTasks = subscriptions.Select(sub => _resourcesClient.GetContainerAppsAsync(sub.Id));
        
        await Task.WhenAll(Task.WhenAll(vaultTasks), Task.WhenAll(appTasks));
        
        var vaultResults = await Task.WhenAll(vaultTasks);
        var appResults = await Task.WhenAll(appTasks);
        
        var allVaults = vaultResults.SelectMany(v => v).OrderBy(v => v.Name).ToList();
        var allApps = appResults.SelectMany(a => a).OrderBy(a => a.Name).ToList();
        
        _keyVaults = allVaults;
        _containerApps = allApps;

        Application.Invoke(() =>
        {
            _vaultsLoading.Visible = false;
            _vaultsSource.Clear();
            
            // Add KeyVaults with [KV] prefix
            foreach (var v in _keyVaults)
            {
                _vaultsSource.Add($"[KV] {EscapeHotkey(v.Name)}", Color.BrightCyan);
            }
            
            // Add ContainerApps with [CA] prefix
            foreach (var a in _containerApps)
            {
                _vaultsSource.Add($"[CA] {EscapeHotkey(a.Name)}", Color.BrightGreen);
            }
            
            SetStatus($"Found {_keyVaults.Count} key vaults + {_containerApps.Count} container apps across {subscriptions.Count} subscriptions");
        });
    }

    private async void OnVaultSelected(object? sender, ListViewItemEventArgs e)
    {
        if (e.Item < 0)
        {
            return;
        }
        
        // Calculate which resource type was selected based on index
        int totalResources = _keyVaults.Count + _containerApps.Count;
        if (e.Item >= totalResources)
        {
            return;
        }
        
        // Determine if this is a KeyVault or ContainerApp
        bool isKeyVault = e.Item < _keyVaults.Count;
        
        if (isKeyVault)
        {
            await LoadKeyVaultSecretsAsync(e.Item);
        }
        else
        {
            await LoadContainerAppSecretsAsync(e.Item);
        }
    }

    private async Task LoadKeyVaultSecretsAsync(int itemIndex)
    {
        var vault = _keyVaults[itemIndex];
        _selectedVault = vault;
        _selectedContainerApp = null;

        // Check if already cached
        if (_resourcesClient.AreSecretsCached(vault.Name))
        {
            var (cachedSecrets, _) = await _resourcesClient.GetSecretsAsync(vault.Name);

            Application.Invoke(() =>
            {
                if (_vaultsList.SelectedItem == itemIndex && itemIndex < _keyVaults.Count && _keyVaults[itemIndex].Name == vault.Name)
                {
                    _secrets = cachedSecrets.OrderBy(s => s.Name).ToList();
                    _secretsSource.Clear();
                    ClearSecretDetails();
                    _filteredSecrets = [.. _secrets];
                    FilterSecrets();
                    SetStatus($"Found {_secrets.Count} secrets (cached)");
                    
                    if (_filteredSecrets.Count > 0)
                    {
                        _secretsList.SelectedItem = 0;
                    }
                }
            });
            return;
        }

        Application.Invoke(() =>
        {
            _secretsLoading.Visible = true;
            _secretsSource.Clear();
            ClearSecretDetails();
        });
        
        SetStatus($"Loading secrets from {vault.Name}...");

        if (!string.IsNullOrEmpty(vault.SubscriptionId) && _selectedSubscription != null)
        {
            await _cliClient.SetSubscriptionIfNeededAsync(vault.SubscriptionId, _selectedSubscription.TenantId);
        }

        var (loadedSecrets, error) = await _resourcesClient.GetSecretsAsync(vault.Name);

        Application.Invoke(() =>
        {
            if (_vaultsList.SelectedItem == itemIndex && itemIndex < _keyVaults.Count && _keyVaults[itemIndex].Name == vault.Name)
            {
                _secrets = loadedSecrets.OrderBy(s => s.Name).ToList();
                _secretsLoading.Visible = false;
                _filteredSecrets = [.. _secrets];
                FilterSecrets();
                
                if (error != null)
                {
                    SetStatus($"⚠ Failed to load secrets");
                    _secretsSource.Clear();
                    foreach (var line in SplitErrorMessage(error))
                    {
                        _secretsSource.Add(line);
                    }
                }
                else
                {
                    SetStatus($"Found {_secrets.Count} secrets");
                    if (_filteredSecrets.Count > 0)
                    {
                        _secretsList.SelectedItem = 0;
                    }
                }
            }
            else
            {
                _secretsLoading.Visible = false;
            }
        });
    }

    private async Task LoadContainerAppSecretsAsync(int itemIndex)
    {
        int appIndex = itemIndex - _keyVaults.Count;
        if (appIndex < 0 || appIndex >= _containerApps.Count)
        {
            return;
        }
        
        var app = _containerApps[appIndex];
        _selectedContainerApp = app;
        _selectedVault = null;

        // Check if already cached
        if (_resourcesClient.AreContainerAppSecretsCached(app.Name))
        {
            var (cachedSecrets, _) = await _resourcesClient.GetContainerAppSecretsAsync(app.Name, app.ResourceGroup, app.SubscriptionId);

            Application.Invoke(() =>
            {
                if (_vaultsList.SelectedItem == itemIndex && appIndex < _containerApps.Count && _containerApps[appIndex].Name == app.Name)
                {
                    _containerAppSecrets = cachedSecrets.OrderBy(s => s.Name).ToList();
                    _secretsSource.Clear();
                    ClearSecretDetails();
                    _filteredContainerAppSecrets = [.. _containerAppSecrets];
                    FilterSecretsContainerApp();
                    SetStatus($"Found {_containerAppSecrets.Count} secrets (cached) - Note: Container Apps don't expose secret values");
                    
                    if (_filteredContainerAppSecrets.Count > 0)
                    {
                        _secretsList.SelectedItem = 0;
                    }
                }
            });
            return;
        }

        Application.Invoke(() =>
        {
            _secretsLoading.Visible = true;
            _secretsSource.Clear();
            ClearSecretDetails();
        });
        
        SetStatus($"Loading secrets from {app.Name}...");

        if (!string.IsNullOrEmpty(app.SubscriptionId) && _selectedSubscription != null)
        {
            await _cliClient.SetSubscriptionIfNeededAsync(app.SubscriptionId, _selectedSubscription.TenantId);
        }

        var (loadedSecrets, error) = await _resourcesClient.GetContainerAppSecretsAsync(app.Name, app.ResourceGroup, app.SubscriptionId);

        Application.Invoke(() =>
        {
            if (_vaultsList.SelectedItem == itemIndex && appIndex < _containerApps.Count && _containerApps[appIndex].Name == app.Name)
            {
                _containerAppSecrets = loadedSecrets.OrderBy(s => s.Name).ToList();
                _secretsLoading.Visible = false;
                _filteredContainerAppSecrets = [.. _containerAppSecrets];
                FilterSecretsContainerApp();
                
                if (error != null)
                {
                    SetStatus($"⚠ Failed to load secrets");
                    _secretsSource.Clear();
                    foreach (var line in SplitErrorMessage(error))
                    {
                        _secretsSource.Add(line);
                    }
                }
                else
                {
                    SetStatus($"Found {_containerAppSecrets.Count} secrets - Note: Container Apps don't expose secret values");
                    if (_filteredContainerAppSecrets.Count > 0)
                    {
                        _secretsList.SelectedItem = 0;
                    }
                }
            }
            else
            {
                _secretsLoading.Visible = false;
            }
        });
    }

    private async void OnSecretSelected(object? sender, ListViewItemEventArgs e)
    {
        // Determine which resource type we're dealing with based on what's selected
        if (_selectedVault != null)
        {
            if (e.Item < 0 || e.Item >= _filteredSecrets.Count)
            {
                return;
            }
            
            _selectedSecret = _filteredSecrets[e.Item];
            _selectedContainerAppSecret = null;
            
            // Check if we have cached value
            _currentSecretValue = _resourcesClient.GetCachedSecretValue(_selectedVault.Name, _selectedSecret.Name);
            UpdateSecretDetails();
            
            // Auto-load value if not cached
            if (_currentSecretValue == null)
            {
                await RevealSecretAsync();
            }
        }
        else if (_selectedContainerApp != null)
        {
            if (e.Item < 0 || e.Item >= _filteredContainerAppSecrets.Count)
            {
                return;
            }
            
            var secret = _filteredContainerAppSecrets[e.Item];
            
            // Check if we have cached value
            var cachedValue = _resourcesClient.GetCachedContainerAppSecretValue(_selectedContainerApp.Name, secret.Name);
            _selectedContainerAppSecret = cachedValue != null ? secret with { Value = cachedValue } : secret;
            _selectedSecret = null;
            UpdateSecretDetailsContainerApp();
        }
    }

    private async void OnSecretEntered(object? sender, ListViewItemEventArgs e)
    {
        await RevealSecretAsync();
    }
}
