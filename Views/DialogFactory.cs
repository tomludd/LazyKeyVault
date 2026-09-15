using System.Globalization;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;

namespace LazyKeyVault.Views;

/// <summary>
/// Factory for creating common dialogs used in the application.
/// Provides consistent dialog creation patterns for editing, creating, and confirming actions.
/// </summary>
public static class DialogFactory
{
    /// <summary>Format used to display and parse the Expires/Not-Before fields.</summary>
    private const string DateTimeEditFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Creates a Cancel button wired to close the given dialog without saving.</summary>
    private static Button CreateCancelButton(Dialog dialog)
    {
        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Accepting += (_, _) => dialog.RequestStop();
        return cancelBtn;
    }

    /// <summary>Creates a dialog for editing an existing secret value.</summary>
    public static Dialog CreateEditSecretDialog(
        string resourceType,
        string secretName, 
        string? currentValue,
        Action<string> onSave)
    {
        var dialog = new Dialog 
        { 
            Title = $"Edit {resourceType} Secret", 
            Width = 60, 
            Height = 10 
        };
        
        var nameLabel = new Label 
        { 
            Text = $"Name: {secretName}", 
            X = 1, 
            Y = 1 
        };
        
        var valueLabel = new Label 
        { 
            Text = "New Value:", 
            X = 1, 
            Y = 2 
        };
        
        var valueField = new TextField 
        { 
            Text = currentValue ?? "", 
            X = 1, 
            Y = 3, 
            Width = Dim.Fill(1) 
        };

        var saveBtn = new Button { Text = "Save" };
        saveBtn.Accepting += (_, _) =>
        {
            var newValue = valueField.Text?.ToString() ?? "";
            if (string.IsNullOrEmpty(newValue))
            {
                ShowError("Error", "Value cannot be empty");
                return;
            }
            dialog.RequestStop();
            onSave(newValue);
        };

        dialog.Add(nameLabel, valueLabel, valueField);
        dialog.AddButton(saveBtn);
        dialog.AddButton(CreateCancelButton(dialog));

        return dialog;
    }

    /// <summary>Creates a dialog for creating a new secret.</summary>
    public static Dialog CreateNewSecretDialog(
        string resourceType,
        Action<string, string> onCreate)
    {
        var dialog = new Dialog 
        { 
            Title = $"Create {resourceType} Secret", 
            Width = 60, 
            Height = 12 
        };
        
        var nameLabel = new Label 
        { 
            Text = "Name:", 
            X = 1, 
            Y = 1 
        };
        
        var nameField = new TextField 
        { 
            X = 1, 
            Y = 2, 
            Width = Dim.Fill(1) 
        };
        
        var valueLabel = new Label 
        { 
            Text = "Value:", 
            X = 1, 
            Y = 4 
        };
        
        var valueField = new TextField 
        { 
            X = 1, 
            Y = 5, 
            Width = Dim.Fill(1) 
        };

        var createBtn = new Button { Text = "Create" };
        createBtn.Accepting += (_, _) =>
        {
            var name = nameField.Text?.ToString() ?? "";
            var value = valueField.Text?.ToString() ?? "";
            
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
            {
                ShowError("Error", "Name and value are required");
                return;
            }

            dialog.RequestStop();
            onCreate(name, value);
        };

        dialog.Add(nameLabel, nameField, valueLabel, valueField);
        dialog.AddButton(createBtn);
        dialog.AddButton(CreateCancelButton(dialog));

        return dialog;
    }

    /// <summary>Creates a confirmation dialog for deletion.</summary>
    public static bool ConfirmDelete(string resourceType, string secretName)
    {
        return MessageBox.Query(
            Application.Instance,
            "Delete",
            $"Delete {resourceType} secret '{secretName}'?",
            "Delete",
            "Cancel") == 0;
    }

    /// <summary>Shows an error dialog with detailed error message.</summary>
    public static void ShowError(string title, string message)
    {
        MessageBox.ErrorQuery(Application.Instance, title, message, "OK");
    }

    /// <summary>Creates a dialog for editing a Key Vault secret's settings (enabled, content type, expiry, not-before).</summary>
    public static Dialog CreateSecretSettingsDialog(
        string secretName,
        bool enabled,
        string? contentType,
        DateTimeOffset? expiresOn,
        DateTimeOffset? notBefore,
        Action<bool, string?, DateTimeOffset?, DateTimeOffset?> onSave)
    {
        var dialog = new Dialog
        {
            Title = $"Secret Settings: {secretName}",
            Width = 64,
            Height = 16
        };

        var enabledCheckbox = new CheckBox
        {
            Text = "Enabled",
            X = 1,
            Y = 1,
            Value = enabled ? CheckState.Checked : CheckState.UnChecked
        };

        var contentTypeLabel = new Label { Text = "Content Type:", X = 1, Y = 3 };
        var contentTypeField = new TextField { Text = contentType ?? "", X = 1, Y = 4, Width = Dim.Fill(1) };

        var expiresLabel = new Label { Text = "Expires (yyyy-MM-dd HH:mm, blank = never):", X = 1, Y = 6 };
        var originalExpiresText = expiresOn?.LocalDateTime.ToString(DateTimeEditFormat, CultureInfo.InvariantCulture) ?? "";
        var expiresField = new TextField { Text = originalExpiresText, X = 1, Y = 7, Width = Dim.Fill(1) };

        var notBeforeLabel = new Label { Text = "Not Before (yyyy-MM-dd HH:mm, blank = none):", X = 1, Y = 9 };
        var originalNotBeforeText = notBefore?.LocalDateTime.ToString(DateTimeEditFormat, CultureInfo.InvariantCulture) ?? "";
        var notBeforeField = new TextField { Text = originalNotBeforeText, X = 1, Y = 10, Width = Dim.Fill(1) };

        var saveBtn = new Button { Text = "Save" };
        saveBtn.Accepting += (_, _) =>
        {
            var newContentType = contentTypeField.Text?.ToString();
            newContentType = string.IsNullOrWhiteSpace(newContentType) ? null : newContentType;

            if (!TryResolveDateField(expiresField.Text?.ToString(), originalExpiresText, expiresOn, "expiry", out var newExpires, out var expiresError))
            {
                ShowError("Error", expiresError!);
                return;
            }

            if (!TryResolveDateField(notBeforeField.Text?.ToString(), originalNotBeforeText, notBefore, "'Not Before'", out var newNotBefore, out var notBeforeError))
            {
                ShowError("Error", notBeforeError!);
                return;
            }

            if (newExpires != null && newNotBefore != null && newExpires <= newNotBefore)
            {
                ShowError("Error", "Expiry must be after 'Not Before'");
                return;
            }

            dialog.RequestStop();
            onSave(enabledCheckbox.Value == CheckState.Checked, newContentType, newExpires, newNotBefore);
        };

        dialog.Add(enabledCheckbox, contentTypeLabel, contentTypeField, expiresLabel, expiresField, notBeforeLabel, notBeforeField);
        dialog.AddButton(saveBtn);
        dialog.AddButton(CreateCancelButton(dialog));

        return dialog;
    }

    /// <summary>
    /// Resolves a date/time field's text back to a value for saving. If the text wasn't changed from what
    /// the field was pre-filled with, returns the original value unchanged (preserving any sub-minute
    /// precision that <see cref="DateTimeEditFormat"/> would otherwise silently truncate on a round-trip).
    /// Parses with an explicit invariant-culture format so the outcome doesn't depend on the OS locale's
    /// date order.
    /// </summary>
    private static bool TryResolveDateField(string? text, string originalText, DateTimeOffset? originalValue, string fieldLabel, out DateTimeOffset? result, out string? error)
    {
        text ??= "";
        error = null;

        if (text == originalText)
        {
            result = originalValue;
            return true;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            result = null;
            return true;
        }

        if (!DateTimeOffset.TryParseExact(text, DateTimeEditFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            result = null;
            error = $"Invalid {fieldLabel} date/time format - expected {DateTimeEditFormat}";
            return false;
        }

        result = parsed;
        return true;
    }
}
