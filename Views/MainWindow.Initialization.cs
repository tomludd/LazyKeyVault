using Terminal.Gui;
using LazyKeyVault.Services;

namespace LazyKeyVault.Views;

/// <summary>
/// Partial class containing initialization logic for MainWindow.
/// Includes constructor, UI component setup, and startup initialization.
/// </summary>
public partial class MainWindow
{
    public MainWindow()
    {
        BorderStyle = LineStyle.None;
        _cliClient = new AzureCliClient();
        _resourcesClient = new AzureResourcesClient(_cliClient);

        // Azure Key Vault themed color scheme (blue tones)
        var azureScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.White, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.Blue),
            HotNormal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Blue)
        };
        ColorScheme = azureScheme;

        var frameScheme = new ColorScheme
        {
            Normal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black),
            Focus = new Terminal.Gui.Attribute(Color.White, Color.Blue),
            HotNormal = new Terminal.Gui.Attribute(Color.Cyan, Color.Black),
            HotFocus = new Terminal.Gui.Attribute(Color.Cyan, Color.Blue)
        };

        var loadingScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.Yellow, Color.Black) };

        // === LEFT COLUMN (35%) ===

        // Accounts Frame (top of left - compact)
        _accountsFrame = new FrameView { Title = "Accounts (^1)", X = 0, Y = 0, Width = Dim.Percent(35), Height = Dim.Percent(12), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        _accountsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = new ListWrapper<string>(_accountsSource) };
        _accountsList.SelectedItemChanged += OnAccountSelected;
        _accountsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false, ColorScheme = loadingScheme };
        _accountsFrame.Add(_accountsList, _accountsLoading);

        // Subscriptions Frame (middle of left - larger for groups)
        _subscriptionsFrame = new FrameView { Title = "Subscriptions (^2)", X = 0, Y = Pos.Bottom(_accountsFrame), Width = Dim.Percent(35), Height = Dim.Percent(40), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        _subscriptionsSource = new ColoredListDataSource();
        _subscriptionsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = _subscriptionsSource };
        _subscriptionsList.SelectedItemChanged += OnSubscriptionSelected;
        _subscriptionsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false, ColorScheme = loadingScheme };
        _subscriptionsFrame.Add(_subscriptionsList, _subscriptionsLoading);

        // Resources Frame (bottom third of left) - Shows both KeyVaults and ContainerApps
        _vaultsFrame = new FrameView { Title = "Resources (^3)", X = 0, Y = Pos.Bottom(_subscriptionsFrame), Width = Dim.Percent(35), Height = Dim.Fill(1), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        _vaultsSource = new ColoredListDataSource();
        _vaultsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = _vaultsSource };
        _vaultsList.SelectedItemChanged += OnVaultSelected;
        _vaultsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false, ColorScheme = loadingScheme };
        _vaultsFrame.Add(_vaultsList, _vaultsLoading);

        // === RIGHT COLUMN (65%) ===

        // Secrets List Frame (top half of right)
        _secretsFrame = new FrameView { Title = "Secrets (^4)", X = Pos.Right(_accountsFrame), Y = 0, Width = Dim.Fill(), Height = Dim.Percent(50), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        var searchLabel = new Label { Text = "/", X = 0, Y = 0, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black) } };
        _searchField = new TextField { X = 2, Y = 0, Width = Dim.Fill(), Height = 1 };
        _searchField.TextChanged += (_, _) => FilterSecrets();
        _secretsList = new ListView { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(), Source = new ListWrapper<string>(_secretsSource) };
        _secretsList.SelectedItemChanged += OnSecretSelected;
        _secretsList.OpenSelectedItem += OnSecretEntered;
        _secretsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false, ColorScheme = loadingScheme };
        _secretsFrame.Add(searchLabel, _searchField, _secretsList, _secretsLoading);

        // Secret Details Frame (bottom half of right)
        _detailsFrame = new FrameView { Title = "Secret Details (^5)", X = Pos.Right(_accountsFrame), Y = Pos.Bottom(_secretsFrame), Width = Dim.Fill(), Height = Dim.Fill(1), ColorScheme = frameScheme, BorderStyle = LineStyle.Rounded };
        _secretNameLabel = new Label { Text = "Name: -", X = 1, Y = 1, Width = Dim.Fill(1), ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Black) } };
        var valueLabel = new Label { Text = "Value:", X = 1, Y = 3, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black) } };
        _secretValueText = new TextView { X = 1, Y = 4, Width = Dim.Fill(1), Height = Dim.Fill(4), ReadOnly = true, WordWrap = true, ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black) } };
        // All metadata on a single line
        _createdLabel = new Label { Text = "Created: -", X = 1, Y = Pos.Bottom(_secretValueText) };
        _updatedLabel = new Label { Text = "Updated: -", X = Pos.Right(_createdLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _expiresLabel = new Label { Text = "Expires: -", X = Pos.Right(_updatedLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _enabledLabel = new Label { Text = "Enabled: -", X = Pos.Right(_expiresLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _detailsFrame.Add(_secretNameLabel, valueLabel, _secretValueText, _createdLabel, _updatedLabel, _expiresLabel, _enabledLabel);

        // Status bar
        _statusLabel = new Label { Text = "Loading...", X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), ColorScheme = new ColorScheme { Normal = new Terminal.Gui.Attribute(Color.White, Color.Blue) } };

        Add(_accountsFrame, _subscriptionsFrame, _vaultsFrame, _secretsFrame, _detailsFrame, _statusLabel);
        SetupKeyBindings();
    }

    public async Task InitializeAsync()
    {
        SetStatus("Checking Azure CLI...");
        var (isInstalled, error) = await _cliClient.IsAzureCliInstalledAsync();
        
        if (!isInstalled)
        {
            MessageBox.ErrorQuery("Azure CLI Not Found", error ?? "Unknown error", "OK");
            Application.RequestStop();
            return;
        }
        
        // Initialize resources client after CLI is verified
        if (_cliClient.CliPath != null)
        {
            _resourcesClient.Initialize(_cliClient.CliPath);
        }
        
        if (!await _cliClient.IsLoggedInAsync())
        {
            MessageBox.ErrorQuery("Not Logged In", "Please run: az login", "OK");
            Application.RequestStop();
            return;
        }
        
        await RefreshDataAsync();
    }

    private void ClearAllCache()
    {
        _cache.Clear();
        _cliClient.ClearCache();
        _resourcesClient.ClearCache();
    }
}
