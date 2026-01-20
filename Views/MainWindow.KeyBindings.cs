using Terminal.Gui;

namespace LazyKeyVault.Views;

/// <summary>
/// Partial class containing keyboard binding configuration for MainWindow.
/// Defines all keyboard shortcuts and their associated actions.
/// </summary>
public partial class MainWindow
{
    private void SetupKeyBindings()
    {
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case KeyCode.D1 | KeyCode.CtrlMask:
                    _accountsList.SetFocus();
                    e.Handled = true;
                    break;
                    
                case KeyCode.D2 | KeyCode.CtrlMask:
                    _subscriptionsList.SetFocus();
                    e.Handled = true;
                    break;
                    
                case KeyCode.D3 | KeyCode.CtrlMask:
                    _vaultsList.SetFocus();
                    e.Handled = true;
                    break;
                    
                case KeyCode.D4 | KeyCode.CtrlMask:
                    _secretsList.SetFocus();
                    e.Handled = true;
                    break;
                    
                case KeyCode.D5 | KeyCode.CtrlMask:
                    _detailsFrame.SetFocus();
                    e.Handled = true;
                    break;
                    
                case KeyCode.R | KeyCode.CtrlMask:
                    _ = RefreshDataAsync(true);
                    e.Handled = true;
                    break;
                    
                case KeyCode.C | KeyCode.CtrlMask:
                    CopySecretToClipboard();
                    e.Handled = true;
                    break;
                    
                case KeyCode.E | KeyCode.CtrlMask:
                    EditSecret();
                    e.Handled = true;
                    break;
                    
                case KeyCode.N | KeyCode.CtrlMask:
                    CreateNewSecret();
                    e.Handled = true;
                    break;
                    
                case KeyCode.D | KeyCode.CtrlMask:
                    DeleteSecret();
                    e.Handled = true;
                    break;
                    
                case KeyCode.Esc when _searchField.HasFocus:
                    _searchField.Text = "";
                    _secretsList.SetFocus();
                    e.Handled = true;
                    break;
                    
                case KeyCode.Q | KeyCode.CtrlMask:
                    Application.RequestStop();
                    e.Handled = true;
                    break;
                    
                case KeyCode.Esc:
                    Application.RequestStop();
                    e.Handled = true;
                    break;
            }
        };
    }
}
