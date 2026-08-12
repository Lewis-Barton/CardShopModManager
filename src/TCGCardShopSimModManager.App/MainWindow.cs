using System.Net.Http;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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

    // Modpack gallery state (the "Modpacks" tab).
    private List<ModpackSummary> _packs = new();
    private ModpackSummary? _selectedPack;
    private ModListManifest? _selectedManifest;
    private readonly HttpClient _http = new();
    private readonly ModpackIndexReader _packReader = new();

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

    private async void OnUninstallClick(object? sender, RoutedEventArgs e) => await RunHandler(OnUninstallAsync);
    private async void OnListModsClick(object? sender, RoutedEventArgs e) => await RunHandler(OnListModsAsync);
    private async void OnEnableClick(object? sender, RoutedEventArgs e) => await RunHandler(OnEnableAsync);
    private async void OnDisableClick(object? sender, RoutedEventArgs e) => await RunHandler(OnDisableAsync);
    private async void OnUpdateCheckClick(object? sender, RoutedEventArgs e) => await RunHandler(OnUpdateCheckAsync);
    private async void OnExportBundleClick(object? sender, RoutedEventArgs e) => await RunHandler(OnExportBundleAsync);
    private async void OnPackInstallClick(object? sender, RoutedEventArgs e) => await RunHandler(OnPackInstallAsync);
    private async void OnPickGameFolder(object? sender, RoutedEventArgs e) => await RunHandler(() => PickFolderAsync(_gameBox));

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

        // Best-effort: populate the Modpacks gallery too. If we're offline this
        // logs and carries on; the tab just shows "could not load".
        await LoadPacksAsync();
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

    // --- modpack gallery ----------------------------------------------------

    private async Task LoadPacksAsync()
    {
        PackLog("Loading modpacks from GitHub...");
        try
        {
            var index = await _packReader.FetchIndexAsync();
            _packs = index.Packs;
            _packsPanel.Children.Clear();
            foreach (var pack in _packs)
                _packsPanel.Children.Add(BuildPackCard(pack));
            PackLog($"Found {_packs.Count} modpack(s).");
        }
        catch (Exception ex)
        {
            PackLog($"Could not load modpacks: {ex.Message}");
        }
    }

    private Border BuildPackCard(ModpackSummary pack)
    {
        var card = new Border
        {
            Classes = { "card" },
            Width = 200,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8)
        };

        var stack = new StackPanel { Spacing = 4 };
        var img = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform };

        // Fetch the logo off the UI thread, then drop it in once it arrives.
        _ = LoadLogoAsync(_packReader.LogoUrl(pack)).ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion && t.Result is Bitmap bmp)
                Dispatcher.UIThread.Post(() => img.Source = bmp);
        });

        stack.Children.Add(img);
        stack.Children.Add(new TextBlock { Text = pack.Name, FontWeight = FontWeight.Bold });
        stack.Children.Add(new TextBlock
        {
            Text = pack.ShortDescription,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11
        });

        card.Child = stack;
        card.PointerPressed += (_, _) => _ = SelectPack(pack);
        return card;
    }

    private async Task SelectPack(ModpackSummary pack)
    {
        _selectedPack = pack;
        _selectedManifest = null;
        _packInstall.IsEnabled = false;
        _packName.Text = pack.Name;
        _packDesc.Text = pack.ShortDescription;
        _packMods.ItemsSource = null;
        _packStatus.Text = "Reading manifest...";

        var logo = await LoadLogoAsync(_packReader.LogoUrl(pack));
        if (logo is not null)
            _packLogo.Source = logo;

        try
        {
            var manifest = await _packReader.FetchManifestAsync(pack);
            _selectedManifest = manifest;
            _packMods.ItemsSource = manifest.Mods
                .Select(m => $"  {m.Name} {m.Version ?? ""}".Trim())
                .ToList();
            _packInstall.IsEnabled = true;
            _packStatus.Text = $"{manifest.Mods.Count} mod(s). Ready to install.";
        }
        catch (Exception ex)
        {
            _packStatus.Text = $"Could not read manifest: {ex.Message}";
        }
    }

    private async Task OnPackInstallAsync()
    {
        var pack = _selectedPack;
        var manifest = _selectedManifest;
        var gameFolder = _gameBox.Text;

        if (pack is null || manifest is null)
        {
            PackLog("Select a modpack first.");
            return;
        }
        if (string.IsNullOrWhiteSpace(gameFolder))
        {
            PackLog("Set the game folder first (Local / Manage tab).");
            return;
        }

        PackLog($"--- Install {pack.Name} into {gameFolder}");
        var fallback = BuildFallback(pack);
        var report = await RunUnderPack(() =>
            new ModpackInstaller(gameFolder, _http).InstallAsync(manifest, fallback));

        foreach (var line in report.Lines)
            PackLog(line);

        _packStatus.Text = report.Success
            ? $"Installed {pack.Name}."
            : "Install did not complete — see the log above.";
    }

    private static IModSource? BuildFallback(ModpackSummary pack)
    {
        if (pack.Source is null)
            return null;

        return pack.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new HttpModSource(m => $"{pack.Source.TrimEnd('/')}/{Uri.EscapeDataString(m.FileName)}")
            : new LocalFileSource(pack.Source);
    }

    private static async Task<Bitmap?> LoadLogoAsync(string url)
    {
        try
        {
            var bytes = await new HttpClient().GetByteArrayAsync(url);
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    private void PackLog(string line)
    {
        _packLog.Text += line + "\n";
        _packLog.CaretIndex = _packLog.Text.Length;
    }

    private async Task<T> RunUnderPack<T>(Func<Task<T>> work)
    {
        _packProgress.IsVisible = true;
        try
        {
            return await work();
        }
        finally
        {
            _packProgress.IsVisible = false;
        }
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

    private void Log(string line)
    {
        _log.Text += line + "\n";
        _log.CaretIndex = _log.Text.Length;
    }
}
