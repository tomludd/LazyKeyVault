using LazyKeyVault.Models;

namespace LazyKeyVault.Views;

/// <summary>
/// Partial class containing resource loading methods.
/// Handles loading of subscriptions, vaults, and apps with caching support.
/// </summary>
public partial class MainWindow
{
    /// <summary>Loads subscriptions for a specific account/user.</summary>
    private List<AzureAccount> LoadSubscriptionsForAccount(List<AzureAccount> allAccounts, string userName)
    {
        return allAccounts
            .Where(a => (a.User?.Name ?? a.TenantId) == userName)
            .OrderBy(a => a.Name)
            .ToList();
    }

    /// <summary>Loads both Key Vaults and Container Apps for a subscription in parallel.</summary>
    private async Task<(List<KeyVault> Vaults, List<ContainerApp> Apps)> LoadBothResourcesForSubscriptionAsync(
        string subscriptionId, string tenantId)
    {
        // Switch subscription only if changing tenants (handles guest account scenarios)
        await _cliClient.SetSubscriptionIfNeededAsync(subscriptionId, tenantId);
        
        var vaultsTask = _resourcesClient.GetKeyVaultsAsync(subscriptionId);
        var appsTask = _resourcesClient.GetContainerAppsAsync(subscriptionId);
        
        await Task.WhenAll(vaultsTask, appsTask);
        
        var vaults = vaultsTask.Result.OrderBy(v => v.Name).ToList();
        var apps = appsTask.Result.OrderBy(a => a.Name).ToList();
        
        return (vaults, apps);
    }

    /// <summary>Loads both vaults and container apps for all subscriptions in background with progress reporting.</summary>
    private async Task LoadVaultsInBackgroundAsync(
        List<AzureAccount> subscriptions, 
        Action<int, int, string> progressCallback)
    {
        var subIds = subscriptions.Select(s => s.Id).ToList();
        
        // Load both vaults and container apps in parallel
        var vaultsTask = _resourcesClient.LoadVaultsAsync(subIds, progressCallback);
        var appsTask = _resourcesClient.LoadContainerAppsAsync(subIds, progressCallback);
        
        await Task.WhenAll(vaultsTask, appsTask);
    }
}
