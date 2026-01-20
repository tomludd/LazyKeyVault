using Terminal.Gui;

namespace LazyKeyVault.Views;

/// <summary>
/// Factory for creating common dialogs used in the application.
/// Provides consistent dialog creation patterns for editing, creating, and confirming actions.
/// </summary>
public static class DialogFactory
{
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
                MessageBox.ErrorQuery("Error", "Value cannot be empty", "OK");
                return;
            }
            dialog.RequestStop();
            onSave(newValue);
        };

        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Accepting += (_, _) => dialog.RequestStop();

        dialog.Add(nameLabel, valueLabel, valueField);
        dialog.AddButton(saveBtn);
        dialog.AddButton(cancelBtn);
        
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
                MessageBox.ErrorQuery("Error", "Name and value are required", "OK");
                return;
            }
            
            dialog.RequestStop();
            onCreate(name, value);
        };

        var cancelBtn = new Button { Text = "Cancel" };
        cancelBtn.Accepting += (_, _) => dialog.RequestStop();

        dialog.Add(nameLabel, nameField, valueLabel, valueField);
        dialog.AddButton(createBtn);
        dialog.AddButton(cancelBtn);
        
        return dialog;
    }

    /// <summary>Creates a confirmation dialog for deletion.</summary>
    public static bool ConfirmDelete(string resourceType, string secretName)
    {
        return MessageBox.Query(
            "Delete", 
            $"Delete {resourceType} secret '{secretName}'?", 
            "Delete", 
            "Cancel") == 0;
    }

    /// <summary>Shows an error dialog with detailed error message.</summary>
    public static void ShowError(string title, string message)
    {
        MessageBox.ErrorQuery(title, message, "OK");
    }
}
