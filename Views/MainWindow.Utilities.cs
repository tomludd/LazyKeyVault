using Terminal.Gui;

namespace LazyKeyVault.Views;

/// <summary>
/// Partial class containing utility helper methods for MainWindow.
/// Includes date formatting, color generation, hashing, and text escaping utilities.
/// </summary>
public partial class MainWindow
{
    /// <summary>Formats an ISO date string to a readable local format.</summary>
    private static string FormatDate(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate))
        {
            return "-";
        }
        
        if (DateTimeOffset.TryParse(isoDate, out var dto))
        {
            return dto.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        
        return isoDate;
    }

    /// <summary>Generates a deterministic color based on a name string.</summary>
    private static Color GetNameColor(string name)
    {
        // Generate a consistent, deterministic color based on the name
        // Use a simple FNV-1a hash which is deterministic across runs
        var hash = ComputeFNV1aHash(name);
        
        // Use a set of vibrant, readable colors (avoiding too dark or too similar to background)
        Color[] colors =
        [
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

    /// <summary>Computes a deterministic FNV-1a hash for a string.</summary>
    private static int ComputeFNV1aHash(string text)
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

    /// <summary>Removes environment prefixes from subscription names to create groupings.</summary>
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
            {
                result = result[($"{env}-").Length..];
            }
            
            // Remove "-env" at end
            if (result.EndsWith($"-{env}"))
            {
                result = result[..^($"-{env}").Length];
            }
            
            // Remove "-env-" in middle (replace with single dash)
            result = result.Replace($"-{env}-", "-");
        }
        
        // Clean up any double dashes and trim dashes
        while (result.Contains("--"))
        {
            result = result.Replace("--", "-");
        }
        
        result = result.Trim('-');
        
        return result;
    }

    /// <summary>Escapes underscores to prevent Terminal.Gui from treating them as hotkey markers.</summary>
    private static string EscapeHotkey(string text)
    {
        return text.Replace("_", "__");
    }

    /// <summary>Updates the status bar with a message and keyboard shortcuts.</summary>
    private void SetStatus(string msg)
    {
        _statusLabel.Text = $" {msg} | ^1-5:Panels ^C:Copy ^E:Edit ^N:New ^D:Del ^R:Refresh [Esc]Quit";
    }

    /// <summary>Clears all vaults and secrets from the UI.</summary>
    private void ClearVaultsAndSecrets()
    {
        _vaultsSource.Clear();
        _keyVaults.Clear();
        ClearSecrets();
    }

    /// <summary>Clears all secrets from the UI.</summary>
    private void ClearSecrets()
    {
        _secretsSource.Clear();
        _secrets.Clear();
        _filteredSecrets.Clear();
        ClearSecretDetails();
    }

    /// <summary>Clears the secret details panel.</summary>
    private void ClearSecretDetails()
    {
        _secretNameLabel.Text = "Name: -";
        _secretValueText.Text = "-";
        _createdLabel.Text = "Created: -";
        _updatedLabel.Text = "Updated: -";
        _expiresLabel.Text = "Expires: -";
        _enabledLabel.Text = "Enabled: -";
    }
}
