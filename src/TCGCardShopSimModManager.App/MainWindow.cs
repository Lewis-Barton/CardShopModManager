using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.App;

/// <summary>
/// The desktop shell over <see cref="DeploymentService"/> and the rest of the
/// engine. The layout lives in MainWindow.axaml; this file is the behaviour.
/// Compute happens on a background task; controls are only touched on the UI
/// thread (after the await resumes there).
/// </summary>
public sealed partial class MainWindow : Window
{
    // The controls declared with x:Name in MainWindow.axaml (_gameBox, _log, ...)
    // are generated for this partial class by Avalonia's name generator, so we
    // don't declare them here.

    private List<DiscoveredMod> _discovered = new();
    private readonly DeploymentService _service = new();

    public MainWindow()
    {
        // Builds the visual tree declared in MainWindow.axaml and assigns the
        // x:Name fields (_log, _gameBox, ...). Must use InitializeComponent
        // (not AvaloniaXamlLoader.Load) so those fields are populated.
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is not null)
            Title = $"TCG Card Shop Sim Mod Manager {version}";

        Opened += async (_, _) => await WelcomeDetectAsync();
    }

    // --- click handlers -----------------------------------------------------
    // XAML wires each Button.Click to one of these. They forward to the real
    // async work and swallow exceptions into the log (mirrors the old helper).

    private async void OnInstallClick(object? sender, RoutedEventArgs e) => await RunHandler(OnInstallAsync);
    private async void OnUninstallClick(object? sender, RoutedEventArgs e) => await RunHandler(OnUninstallAsync);
    private async void OnListModsClick(object? sender, RoutedEventArgs e) => await RunHandler(OnListModsAsync);
    private async void OnEnableClick(object? sender, RoutedEventArgs e) => await RunHandler(OnEnableAsync);
    private async void OnDisableClick(object? sender, RoutedEventArgs e) => await RunHandler(OnDisableAsync);
    private async void OnUpdateCheckClick(object? sender, RoutedEventArgs e) => await RunHandler(OnUpdateCheckAsync);
    private async void OnExportBundleClick(object? sender, RoutedEventArgs e) => await RunHandler(OnExportBundleAsync);
    private async void OnPickGameFolder(object? sender, RoutedEventArgs e) => await RunHandler(() => PickFolderAsync(_gameBox));
    private async void OnPickManifestFile(object? sender, RoutedEventArgs e) => await RunHandler(() => PickFileAsync(_manifestBox));
    private async void OnPickSourceFolder(object? sender, RoutedEventArgs e) => await RunHandler(() => PickFolderAsync(_sourceBox));

    private async Task RunHandler(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
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
            new UpdateChecker("Lewis-Barton/TCGCardShopSimModManager", local).CheckAsync(CancellationToken.None));

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

    private async Task OnInstallAsync()
    {
        var manifestPath = _manifestBox.Text;
        var source = _sourceBox.Text;
        var gameFolder = _gameBox.Text;
        if (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(gameFolder))
        {
            Log($"Enter the modpack manifest, the archives folder and the game folder first.");
            return;
        }

        // DeploymentService.Install already validates the manifest, plans every
        // archive and refuses conflicts before copying anything — so one click
        // covers what used to be three buttons.
        Log($"--- Install modpack into {gameFolder}");
        Log("Validating manifest and planning the install...");
        var report = await RunUnderProgress(() => Task.Run(() => _service.Install(manifestPath, source, gameFolder)));

        foreach (var line in report.Lines)
            Log(line);

        if (!report.Success)
        {
            Log("Install did not complete — see the errors above.");
            return;
        }

        Log("Modpack installed.");
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
