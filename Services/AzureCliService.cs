using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Azure.ResourceManager;
using Azure.ResourceManager.KeyVault;
using Azure.Security.KeyVault.Secrets;
using LazyKeyVault.Models;

namespace LazyKeyVault.Services;

public class AzureCliService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string? _azCliPath;
    private AzureCliCredential? _credential;
    private ArmClient? _armClient;
    private readonly ConcurrentDictionary<string, SecretClient> _secretClients = new();
    private readonly CacheService _cache = new();
    
    public string? LastError { get; private set; }

    /// <summary>Clears all cached data. Call when user requests refresh.</summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>Invalidates secrets cache for a vault. Call after create/delete.</summary>
    public void InvalidateSecrets(string vaultName) => _cache.Invalidate($"secrets:{vaultName}");

    public async Task<(bool IsInstalled, string? Error)> IsAzureCliInstalledAsync()
    {
        try
        {
            // Try to find the Azure CLI executable
            _azCliPath = FindAzureCli();
            
            if (_azCliPath == null)
            {
                LastError = "Azure CLI not found. Searched for 'az.cmd' and 'az' in PATH.\n" +
                           "Please install Azure CLI from: https://aka.ms/installazurecliwindows";
                return (false, LastError);
            }
            
            var result = await RunAzCommandAsync("--version");
            if (result.ExitCode == 0)
            {
                // Initialize credential for SDK usage
                _credential = new AzureCliCredential(_azCliPath);
                _armClient = new ArmClient(_credential);
                return (true, null);
            }
            
            LastError = $"Azure CLI found but returned error: {result.Error}";
            return (false, LastError);
        }
        catch (Exception ex)
        {
            LastError = $"Failed to run Azure CLI: {ex.Message}";
            return (false, LastError);
        }
    }

    private static string? FindAzureCli()
    {
        // On Windows, Azure CLI is typically installed as az.cmd
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);
        
        // Try common executable names
        string[] azNames = OperatingSystem.IsWindows() 
            ? ["az.cmd", "az.exe", "az.bat"] 
            : ["az"];
        
        foreach (var path in paths)
        {
            foreach (var azName in azNames)
            {
                var fullPath = Path.Combine(path, azName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        
        // Fallback: try common installation directories on Windows
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var commonPaths = new[]
            {
                Path.Combine(programFiles, "Microsoft SDKs", "Azure", "CLI2", "wbin", "az.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft SDKs", "Azure", "CLI2", "wbin", "az.cmd")
            };
            
            foreach (var p in commonPaths)
            {
                if (File.Exists(p))
                {
                    return p;
                }
            }
        }
        
        return null;
    }

    public async Task<bool> IsLoggedInAsync()
    {
        var result = await RunAzCommandAsync("account show");
        return result.ExitCode == 0;
    }

    public async Task<List<AzureAccount>> GetAccountsAsync()
    {
        const string cacheKey = "accounts";
        if (_cache.TryGet<List<AzureAccount>>(cacheKey, out var cached) && cached != null)
            return cached;

        var result = await RunAzCommandAsync("account list --all --output json");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            return [];

        try
        {
            var accounts = JsonSerializer.Deserialize<List<AzureAccount>>(result.Output, JsonOptions) ?? [];
            _cache.Set(cacheKey, accounts);
            return accounts;
        }
        catch
        {
            return [];
        }
    }

    public async Task<AzureAccount?> GetCurrentAccountAsync()
    {
        var result = await RunAzCommandAsync("account show --output json");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AzureAccount>(result.Output, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SetSubscriptionAsync(string subscriptionId)
    {
        var result = await RunAzCommandAsync($"account set --subscription \"{subscriptionId}\"");
        if (result.ExitCode == 0)
        {
            // Clear token cache since subscription context changed
            _credential?.ClearCache();
            return true;
        }
        return false;
    }

    /// <summary>Checks if vaults for a subscription are cached.</summary>
    public bool AreVaultsCached(string subscriptionId) => 
        _cache.TryGet<List<KeyVault>>($"vaults:{subscriptionId}", out _);

    /// <summary>Checks if secrets for a vault are cached.</summary>
    public bool AreSecretsCached(string vaultName) =>
        _cache.TryGet<List<Models.KeyVaultSecret>>($"secrets:{vaultName}", out _);

    /// <summary>Checks if a secret value is cached and returns it.</summary>
    public string? GetCachedSecretValue(string vaultName, string secretName)
    {
        var cacheKey = $"secretvalue:{vaultName}:{secretName}";
        return _cache.TryGet<Models.KeyVaultSecret>(cacheKey, out var cached) ? cached?.Value : null;
    }

    public async Task<List<KeyVault>> GetKeyVaultsAsync(string? subscriptionId = null)
    {
        if (_armClient == null || string.IsNullOrEmpty(subscriptionId)) return [];

        var cacheKey = $"vaults:{subscriptionId}";
        if (_cache.TryGet<List<KeyVault>>(cacheKey, out var cached) && cached != null)
            return cached;

        try
        {
            var subscription = _armClient.GetSubscriptionResource(new Azure.Core.ResourceIdentifier($"/subscriptions/{subscriptionId}"));
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

    public async Task<List<Models.KeyVaultSecret>> GetSecretsAsync(string vaultName)
    {
        if (_credential == null) return [];

        var cacheKey = $"secrets:{vaultName}";
        if (_cache.TryGet<List<Models.KeyVaultSecret>>(cacheKey, out var cached) && cached != null)
            return cached;

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
            return secrets;
        }
        catch
        {
            return [];
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

    private async Task<(int ExitCode, string Output, string Error)> RunAzCommandAsync(string arguments)
    {
        // Use the discovered path, or fallback to 'az' for initial detection
        var azPath = _azCliPath ?? (OperatingSystem.IsWindows() ? "az.cmd" : "az");
        
        var psi = new ProcessStartInfo
        {
            FileName = azPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        
        try
        {
            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            var output = await outputTask;
            var error = await errorTask;
            
            return (process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
