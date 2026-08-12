using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CardShopModManager.Core;

namespace CardShopModManager.App;

/// <summary>
/// The desktop shell over <see cref="DeploymentService"/> and the rest of the
/// engine. Compute happens on a background task; controls are only touched on
/// the UI thread (after the await resumes there).
/// </summary>
public sealed class MainWindow : Window
{
    private readonly TextBox _gameBox = new()
    {
        Watermark = "Game folder (where Card Shop Simulator is installed)"
    };
    private readonly TextBox _manifestBox = new() { Watermark = "path to manifest.json" };
    private readonly TextBox _sourceBox = new() { Watermark = "folder containing the mod archives" };
    private readonly TextBox _uninstallBox = new() { Watermark = "mod name to uninstall" };
    private readonly TextBox _log = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Consolas"),
        Text = "Card Shop Mod Manager\n"
    };
    private readonly ListBox _modsList = new() { };
    private List<DiscoveredMod> _discovered = new();
    private readonly ProgressBar _progress = new()
    {
        IsIndeterminate = true,
        IsVisible = false,
        Height = 6
    };

    private readonly DeploymentService _service = new();

    public MainWindow()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Title = version is null ? "Card Shop Mod Manager" : $"Card Shop Mod Manager {version}";
        Width = 980;
        Height = 640;
        Content = BuildLayout();

        Opened += async (_, _) => await WelcomeDetectAsync();
    }

    private Control BuildLayout()
    {
        var grid = new Grid { Margin = new Thickness(12) };
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star)); // bottom split fills the rest

        AddPathRow(grid, 0, _gameBox, "Browse…", () => PickFolderAsync(_gameBox));
        AddPathRow(grid, 1, _manifestBox, "Browse…", () => PickFileAsync(_manifestBox));
        AddPathRow(grid, 2, _sourceBox, "Browse…", () => PickFolderAsync(_sourceBox));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button("Validate", OnValidateAsync));
        actions.Children.Add(Button("Plan", OnPlanAsync));
        actions.Children.Add(Button("Install", OnInstallAsync));
        actions.Children.Add(Button("Uninstall", OnUninstallAsync));
        Grid.SetRow(actions, 3);
        grid.Children.Add(actions);

        var utilities = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        utilities.Children.Add(Button("List mods", OnListModsAsync));
        utilities.Children.Add(Button("Enable", OnEnableAsync));
        utilities.Children.Add(Button("Disable", OnDisableAsync));
        utilities.Children.Add(Button("Update check", OnUpdateCheckAsync));
        utilities.Children.Add(Button("Export bundle", OnExportBundleAsync));
        Grid.SetRow(utilities, 4);
        grid.Children.Add(utilities);

        Grid.SetRow(_progress, 5);
        grid.Children.Add(_progress);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        split.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(260)));

        Grid.SetColumn(_log, 0);
        split.Children.Add(_log);

        var modsPanel = new StackPanel { Spacing = 4 };
        modsPanel.Children.Add(new TextBlock { Text = "Installed mods", FontWeight = FontWeight.Bold });
        modsPanel.Children.Add(_modsList);
        Grid.SetColumn(modsPanel, 1);
        split.Children.Add(modsPanel);

        Grid.SetRow(split, 6);
        grid.Children.Add(split);

        return grid;
    }

    private void AddPathRow(Grid grid, int row, TextBox box, string browseText, Func<Task> browseAction)
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        Grid.SetColumn(box, 0);
        panel.Children.Add(box);

        var browse = Button(browseText, browseAction);
        Grid.SetColumn(browse, 1);
        panel.Children.Add(browse);

        Grid.SetRow(panel, row);
        grid.Children.Add(panel);
    }

    private Button Button(string content, Func<Task> onClick)
    {
        var button = new Button { Content = content };
        button.Click += async (_, _) =>
        {
            try
            {
                await onClick();
            }
            catch (Exception ex)
            {
                Log($"Error: {ex.Message}");
            }
        };
        return button;
    }

    // --- actions -----------------------------------------------------------

    /// <summary>Runs once, when the window opens: fill the game folder from Steam.</summary>
    private async Task WelcomeDetectAsync()
    {
        Log("Looking for TCG Card Shop Simulator through Steam...");

        var path = await Task.Run(() =>
            new SteamLocator().FindGameInstallPath(SteamLocator.GameAppId));

        if (path is null)
            Log("Not found. Pick the game folder manually with Browse, then List mods.");
        else
        {
            _gameBox.Text = path;
            Log($"Detected: {path}");
            await OnListModsAsync();
        }
    }

    private async Task OnListModsAsync()
    {
        var gameFolder = _gameBox.Text;
        if (string.IsNullOrWhiteSpace(gameFolder))
        {
            Log($"Enter a game folder first.");
            return;
        }

        _discovered = await Task.Run(() => ModDiscovery.Discover(gameFolder));

        _modsList.ItemsSource = _discovered
            .Select(m => $"  {m.ModName}   [{m.State}]  ({m.FileCount})")
            .ToList();

        Log($"Mods found on disk ({_discovered.Count}):");
        foreach (var mod in _discovered)
            Log($"  {mod.ModName,-35} {mod.State} ({mod.FileCount} file(s))");
    }

    private async Task OnEnableAsync()
    {
        var gameFolder = _gameBox.Text;
        var mod = SelectedMod();
        if (string.IsNullOrWhiteSpace(gameFolder))
        {
            Log($"Enter a game folder first.");
            return;
        }
        if (mod is null)
        {
            Log($"Select a mod in the list first.");
            return;
        }

        Log($"--- Enable {mod.ModName}");
        var result = await Task.Run(() => new ModInstaller(gameFolder).Enable(mod.ModName));

        if (!result.Success)
        {
            Log(result.Error ?? "Enable failed.");
            return;
        }

        Log($"Enabled {mod.ModName}.");
        foreach (var warning in result.Warnings)
            Log($"  Warning: {warning}");

        await OnListModsAsync();
    }

    private async Task OnDisableAsync()
    {
        var gameFolder = _gameBox.Text;
        var mod = SelectedMod();
        if (string.IsNullOrWhiteSpace(gameFolder))
        {
            Log($"Enter a game folder first.");
            return;
        }
        if (mod is null)
        {
            Log($"Select a mod in the list first.");
            return;
        }

        Log($"--- Disable {mod.ModName}");
        var result = await Task.Run(() => new ModInstaller(gameFolder).Disable(mod.ModName));

        if (!result.Success)
        {
            Log(result.Error ?? "Disable failed.");
            return;
        }

        Log($"Disabled {mod.ModName} (files moved to BepInEx/disabled).");
        foreach (var warning in result.Warnings)
            Log($"  Warning: {warning}");

        await OnListModsAsync();
    }

    private DiscoveredMod? SelectedMod()
    {
        if (_modsList.SelectedIndex < 0 || _modsList.SelectedIndex >= _discovered.Count)
            return null;
        return _discovered[_modsList.SelectedIndex];
    }

    private async Task OnUpdateCheckAsync()
    {
        Log("--- Update check");
        var local = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        var result = await Task.Run(() =>
            new UpdateChecker("Lewis-Barton/CardShopModManager", local).CheckAsync(CancellationToken.None));

        if (result.Error is not null)
        {
            Log(result.Error);
            return;
        }

        if (!result.HasRelease)
            Log($"Local version: {local}. No GitHub releases published yet.");
        else
            Log(result.IsUpToDate
                ? $"Local {local} — up to date (latest {result.LatestVersion})."
                : $"Update available: {result.LatestVersion} ({result.ReleaseUrl})");
    }

    private async Task OnExportBundleAsync()
    {
        Log("--- Export support bundle");
        var bundlePath = await Task.Run(() => SupportBundle.Create(gameFolder: null, outputDirectory: null));
        Log($"Support bundle written to: {bundlePath}");
    }

    private async Task OnValidateAsync()
    {
        var manifestPath = _manifestBox.Text;
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            Log($"Enter a manifest path first.");
            return;
        }

        Log($"--- Validate {manifestPath}");
        var report = await RunUnderProgress(() => Task.Run(() =>
        {
            var gameFolder = string.IsNullOrWhiteSpace(_gameBox.Text) ? null : _gameBox.Text;
            return _service.Validate(manifestPath, gameFolder);
        }));

        foreach (var line in report.Lines)
            Log(line);
    }

    private async Task OnPlanAsync()
    {
        var manifestPath = _manifestBox.Text;
        var source = _sourceBox.Text;
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(source))
        {
            Log($"Enter a manifest path and an archives folder first.");
            return;
        }

        Log($"--- Plan {manifestPath}");
        var previews = await RunUnderProgress(() => Task.Run(() => _service.Preview(manifestPath, source)));

        foreach (var preview in previews)
        {
            Log($"\n[{preview.ModName}]");
            Log($"  layout: {preview.LayoutName}");
            foreach (var file in preview.Files)
                Log(file);
            foreach (var skip in preview.Skipped)
                Log($"  skip: {skip}");
            foreach (var rejected in preview.Rejected)
                Log($"  rejected: {rejected}");
        }
    }

    private async Task OnInstallAsync()
    {
        var manifestPath = _manifestBox.Text;
        var source = _sourceBox.Text;
        var gameFolder = _gameBox.Text;
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(gameFolder))
        {
            Log($"Enter the manifest, archives folder and game folder first.");
            return;
        }

        Log($"--- Install into {gameFolder}");
        var report = await RunUnderProgress(() => Task.Run(() => _service.Install(manifestPath, source, gameFolder)));

        foreach (var line in report.Lines)
            Log(line);

        await OnListModsAsync();
    }

    private async Task OnUninstallAsync()
    {
        var gameFolder = _gameBox.Text;
        var modName = _uninstallBox.Text;
        if (string.IsNullOrWhiteSpace(gameFolder) || string.IsNullOrWhiteSpace(modName))
        {
            Log($"Enter the game folder and a mod name to uninstall.");
            return;
        }

        Log($"--- Uninstall {modName}");
        var result = await RunUnderProgress(() => Task.Run(() => new ModInstaller(gameFolder).Uninstall(modName)));

        if (!result.Success)
        {
            Log(result.Error ?? "Uninstall failed.");
            return;
        }

        Log($"Uninstalled {modName}.");
        foreach (var warning in result.Warnings)
            Log($"  Warning: {warning}");

        await OnListModsAsync();
    }

    private async Task<T> RunUnderProgress<T>(Func<Task<T>> work)
    {
        _progress.IsVisible = true;
        try
        {
            return await work();
        }
        finally
        {
            _progress.IsVisible = false;
        }
    }

    // --- dialogs & helpers -------------------------------------------------

    private async Task PickFolderAsync(TextBox target)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Choose folder", AllowMultiple = false });
        if (folders.Count > 0)
            target.Text = folders[0].Path.LocalPath;
    }

    private async Task PickFileAsync(TextBox target)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Choose manifest",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All files") { Patterns = new[] { "*" } }
                }
            });
        if (files.Count > 0)
            target.Text = files[0].Path.LocalPath;
    }

    private void Log(string line)
    {
        _log.Text += line + "\n";
        _log.CaretIndex = _log.Text.Length;
    }
}