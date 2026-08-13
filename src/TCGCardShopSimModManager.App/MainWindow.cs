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
    private List<InstalledModpack> _installedPacks = new();
    private ModpackSummary? _selectedPack;
    private ModListManifest? _selectedManifest;
    private int _packSelectionVersion;
    private bool _packInstallRunning;
    private readonly HttpClient _http = new();
    private readonly ModpackIndexReader _packReader = new();

    public MainWindow()
    {
        // Builds the visual tree declared in MainWindow.axaml and assigns the
        // x:Name fields (_log, _gameBox, ...). Must use InitializeComponent
        // (not AvaloniaXamlLoader.Load) so those fields are populated.
        InitializeComponent();
        Closed += (_, _) => _http.Dispose();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is not null)
            Title = $"TCG Card Shop Sim Mod Manager {version}";

        // BUG-038: an exception during startup detection must be caught and
        // logged, not left as an unobserved async-void exception that can crash
        // the app at launch. Route it through RunHandler like every button.
        Opened += async (_, _) => await RunHandler(WelcomeDetectAsync);
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
            // BUG-037: surface the full exception (type + message) on screen and
            // record the detail to the diagnostic log, so a thrown failure is
            // diagnosable instead of being silently swallowed as one line.
            Log($"Error: {ex.GetType().Name}: {ex.Message}");
            Diagnostic.Write(ex.ToString(), "error");
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

            // BUG-008: loading the installed-packs journal must not abort gallery
            // rendering. Isolate it so a corrupt/unreadable journal only suppresses
            // update badges (with a warning), never the whole gallery.
            try
            {
                _installedPacks = ReadInstalledPacks();
            }
            catch (Exception ex)
            {
                _installedPacks = new List<InstalledModpack>();
                PackLog($"Could not read installed modpacks (update badges disabled): {ex.Message}");
            }

            _packsPanel.Children.Clear();
            foreach (var pack in _packs)
                _packsPanel.Children.Add(BuildPackCard(pack, IsUpdateAvailable(pack)));
            PackLog($"Found {_packs.Count} modpack(s).");
        }
        catch (Exception ex)
        {
            PackLog($"Could not load modpacks: {ex.Message}");
        }
    }

    private List<InstalledModpack> ReadInstalledPacks()
    {
        var gameFolder = _gameBox.Text;
        if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
            return new List<InstalledModpack>();

        var installed = new ModpackJournalStore(gameFolder).Load();

        // BUG-009: a pack-id rename must not orphan the stored entry. Map a legacy
        // PackId (matching a pack's FormerIds) to its canonical id, and persist the
        // normalization so the legacy id doesn't linger and the next Record can
        // cleanly replace it.
        if (_packs is not null)
        {
            var byFormer = _packs
                .Where(p => p.FormerIds is { Count: > 0 })
                .SelectMany(p => p.FormerIds!.Select(f => (former: f, canonical: p.Id)))
                .ToDictionary(x => x.former, x => x.canonical, StringComparer.OrdinalIgnoreCase);

            if (byFormer.Count > 0)
            {
                var changed = false;
                var rewritten = installed.Select(e =>
                {
                    if (byFormer.TryGetValue(e.PackId, out var canonical))
                    {
                        changed = true;
                        return e with { PackId = canonical };
                    }
                    return e;
                }).ToList();

                if (changed)
                {
                    new ModpackJournalStore(gameFolder).Save(rewritten);
                    installed = rewritten;
                }
            }
        }

        return installed;
    }

    private bool IsUpdateAvailable(ModpackSummary pack)
    {
        // BUG-009: match by canonical id or any legacy FormerId, so a pack-id
        // rename doesn't break update detection for an already-installed pack.
        var installed = _installedPacks.FirstOrDefault(p => pack.IsId(p.PackId));
        return installed is not null && ModpackVersion.IsNewer(installed.PackVersion, pack.Version);
    }

    private Border BuildPackCard(ModpackSummary pack, bool updateAvailable)
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

        if (updateAvailable)
            stack.Children.Add(new TextBlock
            {
                Text = "Update available",
                Foreground = new SolidColorBrush(Colors.Orange),
                FontSize = 11,
                FontWeight = FontWeight.Bold
            });

        card.Child = stack;
        card.PointerPressed += async (_, _) => await RunHandler(() => SelectPack(pack));
        return card;
    }

    private async Task SelectPack(ModpackSummary pack)
    {
        var selectionVersion = ++_packSelectionVersion;
        _selectedPack = pack;
        _selectedManifest = null;
        _packInstall.IsEnabled = false;
        _packName.Text = pack.Name;
        _packDesc.Text = pack.ShortDescription;
        _packMods.ItemsSource = null;
        _packStatus.Text = "Reading manifest...";

        var logo = await LoadLogoAsync(_packReader.LogoUrl(pack));
        if (selectionVersion != _packSelectionVersion)
            return;
        if (logo is not null)
            _packLogo.Source = logo;

        try
        {
            var manifest = await _packReader.FetchManifestAsync(pack);
            if (selectionVersion != _packSelectionVersion)
                return;
            _selectedManifest = manifest;
            _packMods.ItemsSource = manifest.Mods
                .Select(m => $"  {m.Name} {m.Version ?? ""}".Trim())
                .ToList();
            _packInstall.IsEnabled = true;
            _packInstall.Content = IsUpdateAvailable(pack) ? "Update" : "Install modpack";
            _packStatus.Text = $"{manifest.Mods.Count} mod(s). Ready to install.";
        }
        catch (Exception ex)
        {
            if (selectionVersion == _packSelectionVersion)
                _packStatus.Text = $"Could not read manifest: {ex.Message}";
        }
    }

    private async Task OnPackInstallAsync()
    {
        if (_packInstallRunning)
            return;

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

        _packInstallRunning = true;
        _packInstall.IsEnabled = false;
        DeploymentReport report;
        try
        {
            PackLog($"--- Install {pack.Name} into {gameFolder}");
            var fallback = BuildFallback(pack);
            report = await RunUnderPack(() =>
                Task.Run(() => new ModpackInstaller(gameFolder, _http)
                    .InstallAsync(manifest, fallback, pack: pack)));
        }
        finally
        {
            _packInstallRunning = false;
            _packInstall.IsEnabled = _selectedManifest is not null;
        }

        foreach (var line in report.Lines)
            PackLog(line);

        _packStatus.Text = report.Success
            ? $"Installed {pack.Name}."
            : "Install did not complete — see the log above.";

        if (report.Success)
        {
            // Refresh the gallery and this pack's badge/button now that the
            // installed version is recorded.
            await LoadPacksAsync();
            if (_selectedPack is not null)
                await SelectPack(_selectedPack);
        }
    }

    private static IModSource? BuildFallback(ModpackSummary pack)
    {
        if (pack.Source is null)
            return null;

        return pack.Source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new HttpModSource(m => $"{pack.Source.TrimEnd('/')}/{Uri.EscapeDataString(m.FileName)}")
            : new LocalFileSource(pack.Source);
    }

    private async Task<Bitmap?> LoadLogoAsync(string url)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
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

        Log($"Disabled {mod.ModName} (files moved out of the game so BepInEx won't load them).");
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

        Log($"--- Uninstall {mod.ModName}");
        var result = await RunUnderProgress(() => Task.Run(() => new ModInstaller(gameFolder).Uninstall(mod.ModName)));

        if (!result.Success)
        {
            Log(result.Error ?? "Uninstall failed.");
            return;
        }

        Log($"Uninstalled {mod.ModName}.");
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
        {
            target.Text = folders[0].Path.LocalPath;
            // The game folder is now known — reload the gallery so update badges
            // can be shown for any already-installed packs.
            await LoadPacksAsync();
        }
    }

    private void Log(string line)
    {
        _log.Text += line + "\n";
        _log.CaretIndex = _log.Text.Length;
    }
}
