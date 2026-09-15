using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Input;
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
        var azureScheme = new Scheme
        {
            Normal = new Terminal.Gui.Drawing.Attribute(ColorName16.White, ColorName16.Black),
            Focus = new Terminal.Gui.Drawing.Attribute(ColorName16.White, ColorName16.Blue),
            HotNormal = new Terminal.Gui.Drawing.Attribute(ColorName16.BrightCyan, ColorName16.Black),
            HotFocus = new Terminal.Gui.Drawing.Attribute(ColorName16.BrightCyan, ColorName16.Blue)
        };
        SetScheme(azureScheme);

        var frameScheme = new Scheme
        {
            Normal = new Terminal.Gui.Drawing.Attribute(ColorName16.BrightCyan, ColorName16.Black),
            Focus = new Terminal.Gui.Drawing.Attribute(ColorName16.White, ColorName16.Blue),
            HotNormal = new Terminal.Gui.Drawing.Attribute(ColorName16.Cyan, ColorName16.Black),
            HotFocus = new Terminal.Gui.Drawing.Attribute(ColorName16.Cyan, ColorName16.Blue)
        };

        var loadingScheme = new Scheme { Normal = new Terminal.Gui.Drawing.Attribute(ColorName16.Yellow, ColorName16.Black) };

        // === LEFT COLUMN (35%) ===

        // Accounts Frame (top of left - compact)
        _accountsFrame = new FrameView { Title = "Accounts (^1)", X = 0, Y = 0, Width = Dim.Percent(35), Height = Dim.Percent(12), BorderStyle = LineStyle.Rounded };
        _accountsFrame.SetScheme(frameScheme);
        _accountsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = new ListWrapper<string>(_accountsSource) };
        RemoveConflictingDefaultBindings(_accountsList);
        _accountsList.ValueChanged += OnAccountSelected;
        _accountsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false };
        _accountsLoading.SetScheme(loadingScheme);
        _accountsFrame.Add(_accountsList, _accountsLoading);

        // Subscriptions Frame (middle of left - larger for groups)
        _subscriptionsFrame = new FrameView { Title = "Subscriptions (^2)", X = 0, Y = Pos.Bottom(_accountsFrame), Width = Dim.Percent(35), Height = Dim.Percent(40), BorderStyle = LineStyle.Rounded };
        _subscriptionsFrame.SetScheme(frameScheme);
        _subscriptionsSource = new ColoredListDataSource();
        _subscriptionsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = _subscriptionsSource };
        RemoveConflictingDefaultBindings(_subscriptionsList);
        _subscriptionsList.ValueChanged += OnSubscriptionSelected;
        _subscriptionsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false };
        _subscriptionsLoading.SetScheme(loadingScheme);
        _subscriptionsFrame.Add(_subscriptionsList, _subscriptionsLoading);

        // Resources Frame (bottom third of left) - Shows both KeyVaults and ContainerApps
        _vaultsFrame = new FrameView { Title = "Resources (^3)", X = 0, Y = Pos.Bottom(_subscriptionsFrame), Width = Dim.Percent(35), Height = Dim.Fill(1), BorderStyle = LineStyle.Rounded };
        _vaultsFrame.SetScheme(frameScheme);
        _vaultsSource = new ColoredListDataSource();
        _vaultsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), Source = _vaultsSource };
        RemoveConflictingDefaultBindings(_vaultsList);
        _vaultsList.ValueChanged += OnVaultSelected;
        _vaultsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false };
        _vaultsLoading.SetScheme(loadingScheme);
        _vaultsFrame.Add(_vaultsList, _vaultsLoading);

        // === RIGHT COLUMN (65%) ===

        // Secrets List Frame (top half of right)
        _secretsFrame = new FrameView { Title = "Secrets (^4)", X = Pos.Right(_accountsFrame), Y = 0, Width = Dim.Fill(), Height = Dim.Percent(50), BorderStyle = LineStyle.Rounded };
        _secretsFrame.SetScheme(frameScheme);
        var searchLabel = new Label { Text = "/", X = 0, Y = 0 };
        searchLabel.SetScheme(new Scheme { Normal = new Terminal.Gui.Drawing.Attribute(ColorName16.BrightYellow, ColorName16.Black) });
        _searchField = new TextField { X = 2, Y = 0, Width = Dim.Fill(), Height = 1 };
        _searchField.TextChanged += (_, _) => FilterSecrets();
        _secretsSource = new ColoredListDataSource();
        _secretsList = new ListView { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(), Source = _secretsSource };
        RemoveConflictingDefaultBindings(_secretsList);
        _secretsList.ValueChanged += OnSecretSelected;
        _secretsList.Accepting += OnSecretEntered;
        _secretsLoading = new Label { Text = "⏳ Loading...", X = Pos.Center(), Y = Pos.Center(), Visible = false };
        _secretsLoading.SetScheme(loadingScheme);
        _secretsFrame.Add(searchLabel, _searchField, _secretsList, _secretsLoading);

        // Secret Details Frame (bottom half of right)
        _detailsFrame = new FrameView { Title = "Secret Details (^5)", X = Pos.Right(_accountsFrame), Y = Pos.Bottom(_secretsFrame), Width = Dim.Fill(), Height = Dim.Fill(1), BorderStyle = LineStyle.Rounded };
        _detailsFrame.SetScheme(frameScheme);
        _secretNameLabel = new Label { Text = "Name: -", X = 1, Y = 1, Width = Dim.Fill(1) };
        _secretNameLabel.SetScheme(new Scheme { Normal = new Terminal.Gui.Drawing.Attribute(ColorName16.BrightCyan, ColorName16.Black) });
        var valueLabel = new Label { Text = "Value:", X = 1, Y = 3 };
        valueLabel.SetScheme(new Scheme { Normal = new Terminal.Gui.Drawing.Attribute(ColorName16.BrightYellow, ColorName16.Black) });
        _secretValueText = new TextView { X = 1, Y = 4, Width = Dim.Fill(1), Height = Dim.Fill(4), ReadOnly = true, WordWrap = true };
        _secretValueText.SetScheme(new Scheme
        {
            // TextView renders its content using the ReadOnly role (since ReadOnly = true), not Normal -
            // set both explicitly so it doesn't derive a tinted background from Normal's foreground.
            Normal = new Terminal.Gui.Drawing.Attribute(ColorName16.BrightYellow, ColorName16.Black),
            ReadOnly = new Terminal.Gui.Drawing.Attribute(ColorName16.BrightYellow, ColorName16.Black)
        });
        RemoveConflictingDefaultBindings(_secretValueText);
        // All metadata on a single line
        _createdLabel = new Label { Text = "Created: -", X = 1, Y = Pos.Bottom(_secretValueText) };
        _updatedLabel = new Label { Text = "Updated: -", X = Pos.Right(_createdLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _expiresLabel = new Label { Text = "Expires: -", X = Pos.Right(_updatedLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _enabledLabel = new Label { Text = "Enabled: -", X = Pos.Right(_expiresLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _notBeforeLabel = new Label { Text = "Not Before: -", X = Pos.Right(_enabledLabel) + 2, Y = Pos.Bottom(_secretValueText) };
        _detailsFrame.Add(_secretNameLabel, valueLabel, _secretValueText, _createdLabel, _updatedLabel, _expiresLabel, _enabledLabel, _notBeforeLabel);

        // Status bar
        _statusLabel = new Label { Text = "Loading...", X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };
        _statusLabel.SetScheme(new Scheme { Normal = new Terminal.Gui.Drawing.Attribute(ColorName16.White, ColorName16.Blue) });

        Add(_accountsFrame, _subscriptionsFrame, _vaultsFrame, _secretsFrame, _detailsFrame, _statusLabel);
        SetupKeyBindings();
    }

    /// <summary>
    /// ListView and TextView both bind Ctrl+N/Ctrl+P to Down/Up navigation by default (emacs-style), which would
    /// otherwise shadow this app's global Ctrl+N (new secret) and Ctrl+P (secret settings) shortcuts whenever
    /// one of them has focus.
    /// </summary>
    private static void RemoveConflictingDefaultBindings(View view)
    {
        view.KeyBindings.Remove(Key.N.WithCtrl);
        view.KeyBindings.Remove(Key.P.WithCtrl);
    }

    public async Task InitializeAsync()
    {
        SetStatus("Checking Azure CLI...");
        var (isInstalled, error) = await _cliClient.IsAzureCliInstalledAsync();

        if (!isInstalled)
        {
            MessageBox.ErrorQuery(Application.Instance, "Azure CLI Not Found", error ?? "Unknown error", "OK");
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
            MessageBox.ErrorQuery(Application.Instance, "Not Logged In", "Please run: az login", "OK");
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
