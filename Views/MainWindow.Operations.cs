using Terminal.Gui;
using LazyKeyVault.Models;
using LazyKeyVault.Services;

namespace LazyKeyVault.Views;

/// <summary>
/// Partial class containing CRUD operations, filtering, and UI update methods.
/// Includes refresh, reveal, copy, edit, create, and delete operations.
/// </summary>
public partial class MainWindow
{
    private async Task RefreshDataAsync(bool force = false)
    {
        // Save current selections
        var savedAccountIndex = _accountsList.SelectedItem;
        var savedSubscriptionIndex = _subscriptionsList.SelectedItem;
        var savedVaultIndex = _vaultsList.SelectedItem;
        var savedSecretIndex = _secretsList.SelectedItem;

        if (force)
        {
            ClearAllCache();
        }

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

        _accounts = await _cliClient.GetAccountsAsync();

        // Group by user (tenant) - unique users
        var uniqueUsers = _accounts.GroupBy(a => a.User?.Name ?? a.TenantId).Select(g => g.First()).ToList();

        Application.Invoke(() =>
        {
            _accountsLoading.Visible = false;
            _accountsSource.Clear();
            
            foreach (var acc in uniqueUsers)
            {
                _accountsSource.Add(EscapeHotkey(acc.User?.Name ?? acc.TenantId));
            }
            
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
        if (accountIndex < 0 || accountIndex >= uniqueUsers.Count)
        {
            return;
        }

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
        var userSubscriptions = LoadSubscriptionsForAccount(_accounts, userName);

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
        _ = LoadVaultsInBackgroundAsync(userSubscriptions, (completed, total, currentSub) =>
        {
            var subName = userSubscriptions.FirstOrDefault(s => s.Id == currentSub)?.Name ?? currentSub;
            Application.Invoke(() => SetStatus($"Loading vaults ({completed}/{total}): {subName.Substring(0, Math.Min(30, subName.Length))}..."));
        });

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
        if (_resourcesClient.AreSecretsCached(vault.Name))
        {
            _secrets = (await _resourcesClient.GetSecretsAsync(vault.Name))
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

        Application.Invoke(() =>
        {
            _secretsLoading.Visible = true;
            _secretsSource.Clear();
            ClearSecretDetails();
        });
        
        SetStatus($"Loading secrets from {vault.Name}...");

        _secrets = (await _resourcesClient.GetSecretsAsync(vault.Name))
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

    private void FilterSecrets()
    {
        if (_selectedVault != null)
        {
            var filter = _searchField.Text?.ToString()?.ToLowerInvariant() ?? "";
            var previousSelection = _selectedSecret;
            _filteredSecrets = string.IsNullOrWhiteSpace(filter)
                ? [.. _secrets]
                : _secrets.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            
            _secretsSource.Clear();
            foreach (var s in _filteredSecrets)
            {
                _secretsSource.Add(EscapeHotkey(s.Name));
            }
            
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
                _secretsList.SelectedItem = 0;
            }
            else
            {
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
        _filteredContainerAppSecrets = string.IsNullOrWhiteSpace(filter)
            ? [.. _containerAppSecrets]
            : _containerAppSecrets.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        
        _secretsSource.Clear();
        foreach (var s in _filteredContainerAppSecrets)
        {
            _secretsSource.Add(EscapeHotkey(s.Name));
        }
        
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
            _secretsList.SelectedItem = 0;
        }
        else
        {
            _selectedContainerAppSecret = null;
            ClearSecretDetails();
        }
    }

    private void UpdateSecretDetails()
    {
        if (_selectedSecret == null)
        {
            ClearSecretDetails();
            return;
        }

        _secretNameLabel.Text = $"Name: {_selectedSecret.Name}";
        _secretValueText.Text = _currentSecretValue ?? "[Loading...]";

        var attrs = _selectedSecret.Attributes;
        _createdLabel.Text = !string.IsNullOrEmpty(attrs?.Created) ? $"Created: {FormatDate(attrs.Created)}" : "Created: -";
        _updatedLabel.Text = !string.IsNullOrEmpty(attrs?.Updated) ? $"Updated: {FormatDate(attrs.Updated)}" : "Updated: -";
        _expiresLabel.Text = !string.IsNullOrEmpty(attrs?.Expires) ? $"Expires: {FormatDate(attrs.Expires)}" : "Expires: Never";
        _enabledLabel.Text = $"Enabled: {(attrs?.Enabled == true ? "Yes" : "No")}";
    }

    private void UpdateSecretDetailsContainerApp()
    {
        if (_selectedContainerAppSecret == null)
        {
            ClearSecretDetails();
            return;
        }

        _secretNameLabel.Text = $"Name: {_selectedContainerAppSecret.Name}";
        _secretValueText.Text = _selectedContainerAppSecret.Value ?? "";
        _createdLabel.Text = "Created: -";
        _updatedLabel.Text = "Updated: -";
        _expiresLabel.Text = "Expires: -";
        _enabledLabel.Text = "Enabled: -";
    }

    private async Task RevealSecretAsync()
    {
        if (_selectedVault != null && _selectedSecret != null)
        {
            var vault = _selectedVault;
            var secret = _selectedSecret;
            
            // Check cache first
            var cachedValue = _resourcesClient.GetCachedSecretValue(vault.Name, secret.Name);
            if (cachedValue != null)
            {
                _currentSecretValue = cachedValue;
                UpdateSecretDetails();
                SetStatus("Secret loaded (cached)");
                return;
            }

            SetStatus("Fetching secret value...");
            var secretValue = await _resourcesClient.GetSecretValueAsync(vault.Name, secret.Name);
            
            Application.Invoke(() =>
            {
                if (_selectedVault?.Name == vault.Name && _selectedSecret?.Name == secret.Name)
                {
                    if (secretValue?.Value != null)
                    {
                        _currentSecretValue = secretValue.Value;
                        UpdateSecretDetails();
                        SetStatus("Secret loaded");
                    }
                    else
                    {
                        SetStatus("Failed to fetch secret - check permissions");
                    }
                }
            });
        }
        else if (_selectedContainerApp != null && _selectedContainerAppSecret != null)
        {
            var app = _selectedContainerApp;
            var secret = _selectedContainerAppSecret;
            
            // Check cache first
            var cachedValue = _resourcesClient.GetCachedContainerAppSecretValue(app.Name, secret.Name);
            if (cachedValue != null)
            {
                _selectedContainerAppSecret = _selectedContainerAppSecret with { Value = cachedValue };
                UpdateSecretDetailsContainerApp();
                SetStatus("Secret loaded (cached)");
                return;
            }

            SetStatus("Fetching secret value via Azure CLI...");
            var secretValue = await _resourcesClient.GetContainerAppSecretValueAsync(app.Name, app.ResourceGroup, app.SubscriptionId, secret.Name);
            
            Application.Invoke(() =>
            {
                if (_selectedContainerApp?.Name == app.Name && _selectedContainerAppSecret?.Name == secret.Name)
                {
                    if (secretValue?.Value != null)
                    {
                        _selectedContainerAppSecret = _selectedContainerAppSecret with { Value = secretValue.Value };
                        UpdateSecretDetailsContainerApp();
                        
                        if (secretValue.Value.StartsWith("ERROR:"))
                        {
                            SetStatus("Failed to fetch secret - see Value field for details");
                        }
                        else
                        {
                            SetStatus("Secret loaded");
                        }
                    }
                    else
                    {
                        SetStatus("Failed to fetch secret - no response from Azure CLI");
                    }
                }
            });
        }
    }

    private async void CopySecretToClipboard()
    {
        if (_selectedVault != null && _selectedSecret != null)
        {
            var secretValue = _currentSecretValue ?? _resourcesClient.GetCachedSecretValue(_selectedVault.Name, _selectedSecret.Name);
            
            if (secretValue == null)
            {
                SetStatus("Fetching secret...");
                var secret = await _resourcesClient.GetSecretValueAsync(_selectedVault.Name, _selectedSecret.Name);
                secretValue = secret?.Value;
            }
            
            if (secretValue != null)
            {
                try
                {
                    await TextCopy.ClipboardService.SetTextAsync(secretValue);
                    Application.Invoke(() => SetStatus("Copied to clipboard"));
                }
                catch (Exception ex)
                {
                    Application.Invoke(() => SetStatus($"Copy failed: {ex.Message}"));
                }
            }
            else
            {
                Application.Invoke(() => SetStatus("Failed to get value"));
            }
        }
        else if (_selectedContainerApp != null && _selectedContainerAppSecret != null)
        {
            var secretValue = _selectedContainerAppSecret.Value ?? _resourcesClient.GetCachedContainerAppSecretValue(_selectedContainerApp.Name, _selectedContainerAppSecret.Name);
            
            if (secretValue == null)
            {
                SetStatus("Fetching secret via Azure CLI...");
                var secretResult = await _resourcesClient.GetContainerAppSecretValueAsync(_selectedContainerApp.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId, _selectedContainerAppSecret.Name);
                secretValue = secretResult?.Value;
                
                Application.Invoke(() =>
                {
                    if (secretValue != null && _selectedContainerApp != null && _selectedContainerAppSecret != null)
                    {
                        _selectedContainerAppSecret = _selectedContainerAppSecret with { Value = secretValue };
                        UpdateSecretDetailsContainerApp();
                    }
                });
            }
            
            // Don't copy if it's an error message
            if (secretValue != null && secretValue.StartsWith("ERROR:"))
            {
                Application.Invoke(() => SetStatus("Cannot copy - error fetching secret"));
                return;
            }
            
            if (secretValue != null)
            {
                try
                {
                    await TextCopy.ClipboardService.SetTextAsync(secretValue);
                    Application.Invoke(() => SetStatus("Copied to clipboard"));
                }
                catch (Exception ex)
                {
                    Application.Invoke(() => SetStatus($"Copy failed: {ex.Message}"));
                }
            }
            else
            {
                Application.Invoke(() => SetStatus("Failed to get value"));
            }
        }
    }

    private void EditSecret()
    {
        if (_selectedVault != null && _selectedSecret != null)
        {
            var dialog = DialogFactory.CreateEditSecretDialog("Key Vault", _selectedSecret.Name, _currentSecretValue, async newValue =>
            {
                SetStatus("Updating...");
                var (success, error) = await _resourcesClient.SetSecretAsync(_selectedVault!.Name, _selectedSecret!.Name, newValue);
                
                Application.Invoke(() =>
                {
                    if (success)
                    {
                        _currentSecretValue = newValue;
                        UpdateSecretDetails();
                        SetStatus("Updated");
                    }
                    else
                    {
                        SetStatus($"Failed: {error}");
                    }
                });
            });
            
            Application.Run(dialog);
        }
        else if (_selectedContainerApp != null && _selectedContainerAppSecret != null)
        {
            var currentValue = _selectedContainerAppSecret.Value ?? _resourcesClient.GetCachedContainerAppSecretValue(_selectedContainerApp.Name, _selectedContainerAppSecret.Name);
            
            var dialog = DialogFactory.CreateEditSecretDialog("Container App", _selectedContainerAppSecret.Name, currentValue, async newValue =>
            {
                SetStatus("Updating via Azure CLI...");
                var (success, error) = await _resourcesClient.SetContainerAppSecretAsync(_selectedContainerApp!.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId, _selectedContainerAppSecret!.Name, newValue);
                
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
                        DialogFactory.ShowError("Update Secret Failed", error ?? "Unknown error occurred");
                    }
                });
            });
            
            Application.Run(dialog);
        }
        else
        {
            SetStatus("No secret selected");
        }
    }

    private void CreateNewSecret()
    {
        if (_selectedVault != null)
        {
            var dialog = DialogFactory.CreateNewSecretDialog("Key Vault", async (name, value) =>
            {
                SetStatus("Creating...");
                var (success, error) = await _resourcesClient.SetSecretAsync(_selectedVault!.Name, name, value);
                
                Application.Invoke(async () =>
                {
                    if (success)
                    {
                        _resourcesClient.InvalidateSecrets(_selectedVault!.Name);
                        _secrets = (await _resourcesClient.GetSecretsAsync(_selectedVault!.Name)).OrderBy(s => s.Name).ToList();
                        _filteredSecrets = [.. _secrets];
                        FilterSecrets();
                        SetStatus("Created");
                    }
                    else
                    {
                        SetStatus($"Failed: {error}");
                    }
                });
            });
            
            Application.Run(dialog);
        }
        else if (_selectedContainerApp != null)
        {
            var dialog = DialogFactory.CreateNewSecretDialog("Container App", async (name, value) =>
            {
                SetStatus("Creating via Azure CLI...");
                var (success, error) = await _resourcesClient.CreateContainerAppSecretAsync(_selectedContainerApp!.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId, name, value);
                
                Application.Invoke(async () =>
                {
                    if (success)
                    {
                        _resourcesClient.InvalidateContainerAppSecrets(_selectedContainerApp!.Name);
                        _containerAppSecrets = (await _resourcesClient.GetContainerAppSecretsAsync(_selectedContainerApp!.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId)).OrderBy(s => s.Name).ToList();
                        _filteredContainerAppSecrets = [.. _containerAppSecrets];
                        FilterSecretsContainerApp();
                        SetStatus("Created");
                    }
                    else
                    {
                        SetStatus($"Failed: {error}");
                        DialogFactory.ShowError("Create Secret Failed", error ?? "Unknown error occurred");
                    }
                });
            });
            
            Application.Run(dialog);
        }
        else
        {
            SetStatus("Select a vault or container app first");
        }
    }

    private void DeleteSecret()
    {
        if (_selectedVault != null && _selectedSecret != null)
        {
            if (DialogFactory.ConfirmDelete("Key Vault", _selectedSecret.Name))
            {
                _ = DeleteKeyVaultSecretAsync();
            }
        }
        else if (_selectedContainerApp != null && _selectedContainerAppSecret != null)
        {
            if (DialogFactory.ConfirmDelete("Container App", _selectedContainerAppSecret.Name))
            {
                _ = DeleteContainerAppSecretAsync();
            }
        }
        else
        {
            SetStatus("No secret selected");
        }
    }

    private async Task DeleteKeyVaultSecretAsync()
    {
        if (_selectedSecret == null || _selectedVault == null)
        {
            return;
        }
        
        SetStatus("Deleting...");
        var (success, error) = await _resourcesClient.DeleteSecretAsync(_selectedVault.Name, _selectedSecret.Name);
        
        Application.Invoke(async () =>
        {
            if (success)
            {
                _resourcesClient.InvalidateSecrets(_selectedVault!.Name);
                _secrets = (await _resourcesClient.GetSecretsAsync(_selectedVault!.Name)).OrderBy(s => s.Name).ToList();
                _filteredSecrets = [.. _secrets];
                FilterSecrets();
                ClearSecretDetails();
                SetStatus("Deleted");
            }
            else
            {
                SetStatus($"Failed: {error}");
            }
        });
    }

    private async Task DeleteContainerAppSecretAsync()
    {
        if (_selectedContainerAppSecret == null || _selectedContainerApp == null)
        {
            return;
        }
        
        SetStatus("Deleting via Azure CLI...");
        var (success, error) = await _resourcesClient.DeleteContainerAppSecretAsync(_selectedContainerApp.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId, _selectedContainerAppSecret.Name);
        
        Application.Invoke(async () =>
        {
            if (success)
            {
                _resourcesClient.InvalidateContainerAppSecrets(_selectedContainerApp!.Name);
                _containerAppSecrets = (await _resourcesClient.GetContainerAppSecretsAsync(_selectedContainerApp!.Name, _selectedContainerApp.ResourceGroup, _selectedContainerApp.SubscriptionId)).OrderBy(s => s.Name).ToList();
                _filteredContainerAppSecrets = [.. _containerAppSecrets];
                FilterSecretsContainerApp();
                ClearSecretDetails();
                SetStatus("Deleted");
            }
            else
            {
                SetStatus($"Failed: {error}");
                DialogFactory.ShowError("Delete Secret Failed", error ?? "Unknown error occurred");
            }
        });
    }
}
