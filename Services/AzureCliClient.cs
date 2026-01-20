using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using LazyKeyVault.Models;

namespace LazyKeyVault.Services;

/// <summary>
/// Client for Azure CLI process execution and account management.
/// Handles CLI installation checks, authentication, and account/subscription operations.
/// </summary>
public class AzureCliClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private string? _azCliPath;
    private readonly CacheService _cache = new();
    private string? _currentTenantId;
    
    public string? LastError { get; private set; }

    /// <summary>Gets the discovered Azure CLI executable path.</summary>
    public string? CliPath => _azCliPath;

    /// <summary>Checks if Azure CLI is installed and initializes the CLI path.</summary>
    public async Task<(bool IsInstalled, string? Error)> IsAzureCliInstalledAsync()
    {
        try
        {
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

    /// <summary>Checks if the user is logged in to Azure CLI.</summary>
    public async Task<bool> IsLoggedInAsync()
    {
        var result = await RunAzCommandAsync("account show");
        return result.ExitCode == 0;
    }

    /// <summary>Retrieves all Azure accounts/subscriptions with caching.</summary>
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

            if(accounts.FirstOrDefault(a => a.IsDefault) is AzureAccount defaultAccount)
            {
                _currentTenantId = defaultAccount.TenantId;
            }

            return accounts;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Sets the active Azure subscription if switching to a different tenant.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID to switch to</param>
    /// <param name="tenantId">The tenant ID of the subscription</param>
    /// <returns>True if successful or no switch needed</returns>
    public async Task<bool> SetSubscriptionIfNeededAsync(string subscriptionId, string tenantId)
    {
        // If same tenant, no need to switch - SDK handles subscription via resource ID
        if (_currentTenantId == tenantId)
        {
            return true;
        }

        // Switching to different tenant - need to set subscription
        var result = await RunAzCommandAsync($"account set --subscription \"{subscriptionId}\"");
        if (result.ExitCode == 0)
        {
            _currentTenantId = tenantId;
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Executes an Azure CLI command for Container App secret operations.
    /// Used by AzureResourcesClient for operations not available via SDK.
    /// </summary>
    public async Task<(int ExitCode, string Output, string Error)> RunAzCommandAsync(string arguments)
    {
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

    /// <summary>Clears all cached data.</summary>
    public void ClearCache() => _cache.Clear();

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
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    "Programs", "Microsoft SDKs", "Azure", "CLI2", "wbin", "az.cmd")
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
}
