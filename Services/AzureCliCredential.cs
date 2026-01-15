using System.Diagnostics;
using System.Text.Json;
using Azure.Core;

namespace LazyKeyVault.Services;

/// <summary>
/// A TokenCredential that gets access tokens from Azure CLI.
/// This allows using the Azure SDK with az cli authentication.
/// </summary>
public class AzureCliCredential : TokenCredential
{
    private readonly string _azCliPath;
    private AccessToken? _cachedToken;
    private readonly object _lock = new();

    public AzureCliCredential(string azCliPath)
    {
        _azCliPath = azCliPath;
    }

    /// <summary>
    /// Clears the cached token, forcing a refresh on next request.
    /// Call this when the subscription context changes.
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _cachedToken = null;
        }
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Check cached token
        lock (_lock)
        {
            if (_cachedToken.HasValue && _cachedToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return _cachedToken.Value;
            }
        }

        // Get the resource/scope - for Key Vault it's https://vault.azure.net
        var resource = "https://vault.azure.net";
        if (requestContext.Scopes.Length > 0)
        {
            var scope = requestContext.Scopes[0];
            // Convert scope to resource (remove /.default suffix if present)
            resource = scope.EndsWith("/.default") ? scope[..^9] : scope;
        }

        var token = await GetAccessTokenFromCliAsync(resource, cancellationToken);
        
        lock (_lock)
        {
            _cachedToken = token;
        }
        
        return token;
    }

    private async Task<AccessToken> GetAccessTokenFromCliAsync(string resource, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _azCliPath,
            Arguments = $"account get-access-token --resource \"{resource}\" --output json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to get access token from Azure CLI. Exit code: {process.ExitCode}");
        }

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        
        var accessToken = root.GetProperty("accessToken").GetString() 
            ?? throw new InvalidOperationException("Access token not found in CLI response");
        
        var expiresOn = root.GetProperty("expiresOn").GetString();
        var expiresOnDate = DateTimeOffset.Parse(expiresOn ?? DateTimeOffset.UtcNow.AddHours(1).ToString());

        return new AccessToken(accessToken, expiresOnDate);
    }
}
