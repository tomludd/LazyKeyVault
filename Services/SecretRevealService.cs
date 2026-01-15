namespace LazyAzureKeyVault.Services;

/// <summary>
/// Manages secret value visibility with auto-hide after timeout.
/// </summary>
public class SecretRevealService
{
    private readonly TimeSpan _autoHideTimeout;
    private System.Timers.Timer? _hideTimer;
    private string? _currentRevealedSecret;
    
    public event Action? OnSecretHidden;
    
    public bool IsRevealed => _currentRevealedSecret != null;
    public string? RevealedSecretName => _currentRevealedSecret;
    
    public SecretRevealService(TimeSpan? autoHideTimeout = null)
    {
        _autoHideTimeout = autoHideTimeout ?? TimeSpan.FromMinutes(2);
    }
    
    public void RevealSecret(string secretName)
    {
        _currentRevealedSecret = secretName;
        ResetTimer();
    }
    
    public void HideSecret()
    {
        _currentRevealedSecret = null;
        StopTimer();
        OnSecretHidden?.Invoke();
    }
    
    public bool IsSecretRevealed(string secretName)
    {
        return _currentRevealedSecret == secretName;
    }
    
    private void ResetTimer()
    {
        StopTimer();
        
        _hideTimer = new System.Timers.Timer(_autoHideTimeout.TotalMilliseconds);
        _hideTimer.Elapsed += (_, _) =>
        {
            HideSecret();
        };
        _hideTimer.AutoReset = false;
        _hideTimer.Start();
    }
    
    private void StopTimer()
    {
        _hideTimer?.Stop();
        _hideTimer?.Dispose();
        _hideTimer = null;
    }
    
    public TimeSpan GetRemainingTime()
    {
        // Note: System.Timers.Timer doesn't expose remaining time
        // This would need additional tracking if we want to show countdown
        return _autoHideTimeout;
    }
    
    public void Dispose()
    {
        StopTimer();
    }
}
