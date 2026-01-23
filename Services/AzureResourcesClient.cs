using System.Collections.Concurrent;
using Azure.ResourceManager;
using Azure.ResourceManager.KeyVault;
using Azure.ResourceManager.AppContainers;
using Azure.Security.KeyVault.Secrets;
using LazyKeyVault.Models;

namespace LazyKeyVault.Services;

/// <summary>
/// Client for Azure SDK operations with intelligent caching.
/// Handles Key Vault secrets, Container Apps, and Azure Resource Manager interactions.
/// </summary>
public class AzureResourcesClient
{
    private AzureCliCredential? _credential;
    private ArmClient? _armClient;
    private readonly ConcurrentDictionary<string, SecretClient> _secretClients = new();
    private readonly CacheService _cache = new();
    private readonly AzureCliClient _cliClient;

    public AzureResourcesClient(AzureCliClient cliClient)
    {
        _cliClient = cliClient ?? throw new ArgumentNullException(nameof(cliClient));
    }

    /// <summary>Initializes the credential and ARM client after Azure CLI is verified.</summary>
    public void Initialize(string azCliPath)
    {
        _credential = new AzureCliCredential(azCliPath);
        _armClient = new ArmClient(_credential);
    }

    /// <summary>Clears all cached data.</summary>
    public void ClearCache()
    {
        _credential?.ClearCacheAsync();
        _cache.Clear();
    }

    #region Key Vault Operations

    /// <summary>Retrieves Key Vaults for a subscription with caching.</summary>
    public async Task<List<KeyVault>> GetKeyVaultsAsync(string subscriptionId)
    {
        if (_armClient == null || string.IsNullOrEmpty(subscriptionId)) return [];

        var cacheKey = $"vaults:{subscriptionId}";
        if (_cache.TryGet<List<KeyVault>>(cacheKey, out var cached) && cached != null)
            return cached;

        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));
            var vaults = new List<KeyVault>();
            
            await foreach (var vault in subscription.GetKeyVaultsAsync())
            {
                vaults.Add(new KeyVault(
                    Id: vault.Id.ToString(),
                    Name: vault.Data.Name,
                    Location: vault.Data.Location.ToString(),
                    ResourceGroup: vault.Id.ResourceGroupName ?? "",
                    SubscriptionId: subscriptionId
                ));
            }
            
            _cache.Set(cacheKey, vaults);
            return vaults;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Checks if vaults for a subscription are cached.</summary>
    public bool AreVaultsCached(string subscriptionId) => 
        _cache.TryGet<List<KeyVault>>($"vaults:{subscriptionId}", out _);

    /// <summary>
    /// Loads vaults for multiple subscriptions in parallel for better performance.
    /// Reports progress via callback (completed, total, currentSubscription).
    /// </summary>
    public async Task LoadVaultsAsync(
        List<string> subscriptionIds, 
        Action<int, int, string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (_armClient == null || subscriptionIds.Count == 0) return;

        var uncachedSubIds = subscriptionIds.Where(id => !AreVaultsCached(id)).ToList();
        if (uncachedSubIds.Count == 0) return;

        var completed = 0;
        var total = uncachedSubIds.Count;

        await Parallel.ForEachAsync(
            uncachedSubIds,
            new ParallelOptions 
            { 
                MaxDegreeOfParallelism = 5,
                CancellationToken = cancellationToken 
            },
            async (subId, ct) =>
            {
                await GetKeyVaultsAsync(subId);
                var currentCompleted = Interlocked.Increment(ref completed);
                progressCallback?.Invoke(currentCompleted, total, subId);
            });
    }

    /// <summary>Retrieves secrets for a Key Vault with caching.</summary>
    public async Task<(List<Models.KeyVaultSecret> Secrets, string? Error)> GetSecretsAsync(string vaultName)
    {
        if (_credential == null) return ([], "Not authenticated");

        var cacheKey = $"secrets:{vaultName}";
        if (_cache.TryGet<List<Models.KeyVaultSecret>>(cacheKey, out var cached) && cached != null)
            return (cached, null);

        try
        {
            var client = GetSecretClient(vaultName);
            var secrets = new List<Models.KeyVaultSecret>();
            
            await foreach (var secretProperties in client.GetPropertiesOfSecretsAsync())
            {
                secrets.Add(new Models.KeyVaultSecret(
                    Id: secretProperties.Id.ToString(),
                    Name: secretProperties.Name,
                    Value: null,
                    ContentType: secretProperties.ContentType,
                    Attributes: new SecretAttributes(
                        Enabled: secretProperties.Enabled,
                        Created: secretProperties.CreatedOn?.ToString("o"),
                        Updated: secretProperties.UpdatedOn?.ToString("o"),
                        Expires: secretProperties.ExpiresOn?.ToString("o"),
                        NotBefore: secretProperties.NotBefore?.ToString("o")
                    )
                ));
            }
            
            _cache.Set(cacheKey, secrets);
            return (secrets, null);
        }
        catch (Azure.RequestFailedException ex)
        {
            var error = ex.Status switch
            {
                403 => $"Access denied: You don't have permission to list secrets in '{vaultName}'. Check your Key Vault access policies or RBAC roles.",
                _ when ex.Message.Contains("IP") || ex.Message.Contains("network") => $"Network error: '{vaultName}' may have IP restrictions. Error: {ex.Message}",
                _ => $"Failed to load secrets: {ex.Message}"
            };
            return ([], error);
        }
        catch (Exception ex)
        {
            return ([], $"Error loading secrets from '{vaultName}': {ex.Message}");
        }
    }

    /// <summary>Checks if secrets for a vault are cached.</summary>
    public bool AreSecretsCached(string vaultName) =>
        _cache.TryGet<List<Models.KeyVaultSecret>>($"secrets:{vaultName}", out _);

    /// <summary>Retrieves a secret value with caching.</summary>
    public async Task<Models.KeyVaultSecret?> GetSecretValueAsync(string vaultName, string secretName)
    {
        if (_credential == null) return null;

        var cacheKey = $"secretvalue:{vaultName}:{secretName}";
        if (_cache.TryGet<Models.KeyVaultSecret>(cacheKey, out var cached) && cached != null)
            return cached;

        try
        {
            var client = GetSecretClient(vaultName);
            var response = await client.GetSecretAsync(secretName);
            var secret = response.Value;
            
            var result = new Models.KeyVaultSecret(
                Id: secret.Id.ToString(),
                Name: secret.Name,
                Value: secret.Value,
                ContentType: secret.Properties.ContentType,
                Attributes: new SecretAttributes(
                    Enabled: secret.Properties.Enabled,
                    Created: secret.Properties.CreatedOn?.ToString("o"),
                    Updated: secret.Properties.UpdatedOn?.ToString("o"),
                    Expires: secret.Properties.ExpiresOn?.ToString("o"),
                    NotBefore: secret.Properties.NotBefore?.ToString("o")
                )
            );
            
            _cache.Set(cacheKey, result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Checks if a secret value is cached.</summary>
    public string? GetCachedSecretValue(string vaultName, string secretName)
    {
        var cacheKey = $"secretvalue:{vaultName}:{secretName}";
        return _cache.TryGet<Models.KeyVaultSecret>(cacheKey, out var cached) ? cached?.Value : null;
    }

    /// <summary>Sets or updates a secret in Key Vault.</summary>
    public async Task<(bool Success, string? Error)> SetSecretAsync(string vaultName, string secretName, string value)
    {
        if (_credential == null) return (false, "Not authenticated");

        try
        {
            var client = GetSecretClient(vaultName);
            await client.SetSecretAsync(secretName, value);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Deletes a secret from Key Vault.</summary>
    public async Task<(bool Success, string? Error)> DeleteSecretAsync(string vaultName, string secretName)
    {
        if (_credential == null) return (false, "Not authenticated");

        try
        {
            var client = GetSecretClient(vaultName);
            await client.StartDeleteSecretAsync(secretName);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private SecretClient GetSecretClient(string vaultName)
    {
        return _secretClients.GetOrAdd(vaultName, name =>
        {
            var vaultUri = new Uri($"https://{name}.vault.azure.net/");
            return new SecretClient(vaultUri, _credential);
        });
    }

    #endregion

    #region Container Apps Operations

    /// <summary>Retrieves Container Apps for a subscription with caching.</summary>
    public async Task<List<ContainerApp>> GetContainerAppsAsync(string subscriptionId)
    {
        if (_armClient == null || string.IsNullOrEmpty(subscriptionId)) return [];

        var cacheKey = $"containerapps:{subscriptionId}";
        if (_cache.TryGet<List<ContainerApp>>(cacheKey, out var cached) && cached != null)
            return cached;

        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));
            var apps = new List<ContainerApp>();
            
            await foreach (var app in subscription.GetContainerAppsAsync())
            {
                apps.Add(new ContainerApp(
                    Id: app.Id.ToString(),
                    Name: app.Data.Name,
                    Location: app.Data.Location.ToString(),
                    ResourceGroup: app.Id.ResourceGroupName ?? "",
                    SubscriptionId: subscriptionId
                ));
            }
            
            _cache.Set(cacheKey, apps);
            return apps;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Checks if container apps for a subscription are cached.</summary>
    public bool AreContainerAppsCached(string subscriptionId) => 
        _cache.TryGet<List<ContainerApp>>($"containerapps:{subscriptionId}", out _);

    /// <summary>
    /// Loads container apps for multiple subscriptions in parallel for better performance.
    /// Reports progress via callback (completed, total, currentSubscription).
    /// </summary>
    public async Task LoadContainerAppsAsync(
        List<string> subscriptionIds, 
        Action<int, int, string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (_armClient == null || subscriptionIds.Count == 0) return;

        var uncachedSubIds = subscriptionIds.Where(id => !AreContainerAppsCached(id)).ToList();
        if (uncachedSubIds.Count == 0) return;

        var completed = 0;
        var total = uncachedSubIds.Count;

        await Parallel.ForEachAsync(
            uncachedSubIds,
            new ParallelOptions 
            { 
                MaxDegreeOfParallelism = 5,
                CancellationToken = cancellationToken 
            },
            async (subId, ct) =>
            {
                await GetContainerAppsAsync(subId);
                var currentCompleted = Interlocked.Increment(ref completed);
                progressCallback?.Invoke(currentCompleted, total, subId);
            });
    }

    /// <summary>Retrieves secrets for a Container App with caching.</summary>
    public async Task<(List<ContainerAppSecret> Secrets, string? Error)> GetContainerAppSecretsAsync(
        string appName, string resourceGroup, string subscriptionId)
    {
        if (_armClient == null) return ([], "Not authenticated");

        var cacheKey = $"caappsecrets:{appName}";
        if (_cache.TryGet<List<ContainerAppSecret>>(cacheKey, out var cached) && cached != null)
            return (cached, null);

        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));
            var resourceGroupResource = await subscription.GetResourceGroupAsync(resourceGroup);
            var containerApp = await resourceGroupResource.Value.GetContainerAppAsync(appName);

            var secrets = new List<ContainerAppSecret>();
            
            await foreach (var secret in containerApp.Value.GetSecretsAsync())
            {
                var containerAppSecret = new ContainerAppSecret(
                    Name: secret.Name ?? "",
                    Value: secret.Value
                );
                
                secrets.Add(containerAppSecret);
                
                // Also cache individual secret values for faster lookup
                var secretValueCacheKey = $"caappsecretvalue:{appName}:{secret.Name}";
                _cache.Set(secretValueCacheKey, containerAppSecret);
            }
            
            _cache.Set(cacheKey, secrets);
            return (secrets, null);
        }
        catch (Azure.RequestFailedException ex)
        {
            var error = ex.Status switch
            {
                403 => $"Access denied: You don't have permission to list secrets for Container App '{appName}'. Check your RBAC roles.",
                404 => $"Container App '{appName}' not found or has been deleted.",
                _ => $"Failed to load secrets: {ex.Message}"
            };
            return ([], error);
        }
        catch (Exception ex)
        {
            return ([], $"Error loading secrets from Container App '{appName}': {ex.Message}");
        }
    }

    /// <summary>Checks if container app secrets are cached.</summary>
    public bool AreContainerAppSecretsCached(string appName) =>
        _cache.TryGet<List<ContainerAppSecret>>($"caappsecrets:{appName}", out _);

    /// <summary>Retrieves a specific Container App secret value with caching.</summary>
    public async Task<ContainerAppSecret?> GetContainerAppSecretValueAsync(
        string appName, string resourceGroup, string subscriptionId, string secretName)
    {
        var cacheKey = $"caappsecretvalue:{appName}:{secretName}";
        if (_cache.TryGet<ContainerAppSecret>(cacheKey, out var cached) && cached != null)
            return cached;

        // Retrieve all secrets (this will populate the cache)
        var (allSecrets, _) = await GetContainerAppSecretsAsync(appName, resourceGroup, subscriptionId);
        
        var secret = allSecrets.FirstOrDefault(s => s.Name == secretName);
        
        if (secret == null)
        {
            return new ContainerAppSecret(
                Name: secretName,
                Value: "ERROR: Secret not found in Container App configuration"
            );
        }

        return secret;
    }

    /// <summary>Checks if a container app secret value is cached.</summary>
    public string? GetCachedContainerAppSecretValue(string appName, string secretName)
    {
        var cacheKey = $"caappsecretvalue:{appName}:{secretName}";
        return _cache.TryGet<ContainerAppSecret>(cacheKey, out var cached) ? cached?.Value : null;
    }

    /// <summary>Sets or updates a Container App secret using Azure SDK with PATCH.</summary>
    public async Task<(bool Success, string? Error)> SetContainerAppSecretAsync(
        string appName, string resourceGroup, string subscriptionId, string secretName, string value)
    {
        if (_armClient == null) return (false, "Not authenticated");

        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));
            var resourceGroupResource = await subscription.GetResourceGroupAsync(resourceGroup);
            var containerAppResource = await resourceGroupResource.Value.GetContainerAppAsync(appName);
            
            var containerApp = containerAppResource.Value;
            var currentData = containerApp.Data;
            
            // Ensure configuration exists
            if (currentData.Configuration == null)
            {
                return (false, "Container App configuration is null");
            }
            
            // Reload current secrets using listSecrets to get the live state
            var patchData = new ContainerAppData(currentData.Location)
            {
                Configuration = new Azure.ResourceManager.AppContainers.Models.ContainerAppConfiguration()
            };
            var currentSecrets = patchData.Configuration.Secrets;
            
            await foreach (var secret in containerApp.GetSecretsAsync())
            {
                // Skip the secret we're updating - we'll add it back with new value
                if (secret.Name == secretName)
                    continue;
                    
                // Preserve all existing secrets as-is
                currentSecrets.Add(new Azure.ResourceManager.AppContainers.Models.ContainerAppWritableSecret
                {
                    Name = secret.Name,
                    Value = secret.Value
                });
            }
            
            // Add the new/updated secret
            currentSecrets.Add(new Azure.ResourceManager.AppContainers.Models.ContainerAppWritableSecret
            {
                Name = secretName,
                Value = value
            });
            
            // Use Update which performs JSON Merge Patch
            await containerApp.UpdateAsync(Azure.WaitUntil.Completed, patchData);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Creates a Container App secret (alias for Set).</summary>
    public Task<(bool Success, string? Error)> CreateContainerAppSecretAsync(
        string appName, string resourceGroup, string subscriptionId, string secretName, string value)
    {
        return SetContainerAppSecretAsync(appName, resourceGroup, subscriptionId, secretName, value);
    }

    /// <summary>Deletes a Container App secret using Azure SDK with PATCH.</summary>
    public async Task<(bool Success, string? Error)> DeleteContainerAppSecretAsync(
        string appName, string resourceGroup, string subscriptionId, string secretName)
    {
        if (_armClient == null) return (false, "Not authenticated");

        try
        {
            var subscription = _armClient.GetSubscriptionResource(
                new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));
            var resourceGroupResource = await subscription.GetResourceGroupAsync(resourceGroup);
            var containerAppResource = await resourceGroupResource.Value.GetContainerAppAsync(appName);
            
            var containerApp = containerAppResource.Value;
            var currentData = containerApp.Data;
            
            // Ensure configuration exists
            if (currentData.Configuration == null)
            {
                return (false, "Container App configuration is null");
            }
            
            // Reload current secrets using listSecrets to get the live state
            
            // Create a new patch data object with only the configuration.secrets
            var patchData = new Azure.ResourceManager.AppContainers.ContainerAppData(currentData.Location)
            {
                Configuration = new Azure.ResourceManager.AppContainers.Models.ContainerAppConfiguration()
            };
            var currentSecrets = patchData.Configuration.Secrets;
            var secretFound = false;
            
            await foreach (var secret in containerApp.GetSecretsAsync())
            {
                // Skip the secret we're deleting
                if (secret.Name == secretName)
                {
                    secretFound = true;
                    continue;
                }
                    
                // Preserve all other secrets
                currentSecrets.Add(new Azure.ResourceManager.AppContainers.Models.ContainerAppWritableSecret
                {
                    Name = secret.Name,
                    Value = secret.Value
                });
            }
            
            if (!secretFound)
            {
                return (false, $"Secret '{secretName}' not found");
            }
            
            // Use Update which performs JSON Merge Patch
            await containerApp.UpdateAsync(Azure.WaitUntil.Completed, patchData);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    #endregion

    #region Cache Invalidation

    /// <summary>Invalidates cached secrets for a Key Vault.</summary>
    public void InvalidateSecrets(string vaultName)
    {
        _cache.Invalidate($"secrets:{vaultName}");
        _cache.InvalidatePrefix($"secretvalue:{vaultName}:");
    }

    /// <summary>Invalidates cached secrets for a Container App.</summary>
    public void InvalidateContainerAppSecrets(string appName)
    {
        _cache.Invalidate($"caappsecrets:{appName}");
        _cache.InvalidatePrefix($"caappsecretvalue:{appName}:");
    }

    #endregion
}
