using System.Diagnostics;
using System.Text.Json;
using Azure.Core;

namespace LazyKeyVault.Services;

/// <summary>
/// A TokenCredential that gets access tokens from Azure CLI.
/// This allows using the Azure SDK with az cli authentication.
/// </summary>
public class AzureCliCredential(string azCliPath) : TokenCredential
{
    private readonly string _azCliPath = azCliPath;
    private readonly Dictionary<string, AccessToken> _cachedTokens = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Clears all cached tokens.
    /// Call this when forcing a full refresh.
    /// </summary>
    public void ClearCacheAsync()
    {
        _semaphore.Wait();
        try
        {
            _cachedTokens.Clear();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        // Use tenant ID from request context for cache key (empty string if not specified)
        var tenantId = requestContext.TenantId ?? string.Empty;

        // Check cached token for this tenant
        await _semaphore.WaitAsync(cancellationToken);
        AccessToken? cachedToken = null;
        bool hasValidCache = false;

        try
        {
            if (_cachedTokens.TryGetValue(tenantId, out var token))
            {
                if (token.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    cachedToken = token;
                    hasValidCache = true;
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }

        if (hasValidCache && cachedToken.HasValue)
        {
            return cachedToken.Value;
        }

        // Get the resource/scope - for Key Vault it's https://vault.azure.net
        var resource = "https://vault.azure.net";
        if (requestContext.Scopes.Length > 0)
        {
            var scope = requestContext.Scopes[0];
            // Convert scope to resource (remove /.default suffix if present)
            resource = scope.EndsWith("/.default") ? scope[..^9] : scope;
        }

        var newToken = await GetAccessTokenFromCliAsync(resource, cancellationToken);
        
        // Cache token for this tenant
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _cachedTokens[tenantId] = newToken;
        }
        finally
        {
            _semaphore.Release();
        }
        
        return newToken;
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
