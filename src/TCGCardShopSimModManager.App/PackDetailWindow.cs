using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.App;

/// <summary>
/// Modal opened when a card in Browse Lists is clicked. Shows the pack's logo,
/// description and mod list, and runs the existing one-click install pipeline.
/// Kept as code-built UI (no extra .axaml) to stay in step with the rest of the
/// app's code-behind style.
/// </summary>
public sealed class PackDetailWindow : Window
{
    private readonly ModpackSummary _pack;
    private readonly string? _gameFolder;
    private readonly ModpackIndexReader _reader;
    private readonly HttpClient _http;
    private readonly InstalledModpack? _installedPack;
    private readonly string? _installedGameBuildId;
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private readonly Button _install = new() { Content = "Install modpack", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _compatibility = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _acknowledgeCompatibility = new()
    {
        Content = "Install even though this game build may not be supported",
        IsVisible = false
    };
    private readonly Dictionary<string, CheckBox> _modChoices = new(StringComparer.OrdinalIgnoreCase);
    private bool _updatingChoices;
    private ModListManifest? _manifest;

    public PackDetailWindow(
        ModpackSummary pack,
        string? gameFolder,
        HttpClient http,
        ModpackIndexReader reader,
        InstalledModpack? installedPack = null,
        string? installedGameBuildId = null)
    {
        _pack = pack;
        _gameFolder = gameFolder;
        _http = http;
        _reader = reader;
        _installedPack = installedPack;
        _installedGameBuildId = installedGameBuildId;
        Title = pack.Name;
        Width = 560;
        Height = 500;
        MinWidth = 460;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var img = new Image { Width = 96, Height = 96, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        _ = LoadLogoAsync(_reader.LogoUrl(pack)).ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion && t.Result is Bitmap bmp)
                Dispatcher.UIThread.Post(() => img.Source = bmp);
        });

        var mods = new StackPanel { Spacing = 4 };
        _install.Click += async (_, _) => await InstallAsync();
        _acknowledgeCompatibility.IsCheckedChanged += (_, _) => RefreshInstallAvailability();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 8,
                Children =
                {
                    img,
                    new TextBlock { Text = pack.Name, FontWeight = FontWeight.Bold, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = pack.ShortDescription, TextWrapping = TextWrapping.Wrap },
                    _compatibility,
                    _acknowledgeCompatibility,
                    new TextBlock { Text = "Includes:", FontWeight = FontWeight.Bold },
                    new ScrollViewer { MaxHeight = 220, Content = mods },
                    _progress,
                    _install,
                    _status
                }
            }
        };

        _ = LoadManifestAsync(mods);
    }

    private async Task LoadManifestAsync(StackPanel mods)
    {
        try
        {
            _manifest = await _reader.FetchManifestAsync(_pack);
            var validation = new ManifestValidator().Validate(_manifest);
            if (!validation.IsValid)
            {
                _status.Text = "This modpack is invalid: " + string.Join(" ", validation.Errors);
                return;
            }
            ShowCompatibility(_manifest.CompatibleGameBuildIds);
            foreach (var mod in _manifest.Mods)
            {
                var version = string.IsNullOrWhiteSpace(mod.Version) ? "" : $" {mod.Version}";
                var choice = new CheckBox
                {
                    Content = $"{mod.Name}{version} — {(mod.Required ? "Required" : "Optional")}",
                    IsChecked = mod.Required || IsPreviouslySelected(mod),
                    IsEnabled = !mod.Required
                };
                choice.IsCheckedChanged += (_, _) => OnModChoiceChanged(mod);
                _modChoices[mod.Id] = choice;
                mods.Children.Add(choice);
            }
            RefreshInstallAvailability();
            if (string.IsNullOrWhiteSpace(_gameFolder))
                _status.Text = "Set the game folder on the Manage tab first.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not read manifest: {ex.Message}";
        }
    }

    private void ShowCompatibility(IEnumerable<string>? compatibleBuildIds)
    {
        var result = GameCompatibility.Evaluate(compatibleBuildIds, _installedGameBuildId);
        _compatibility.Foreground = new SolidColorBrush(
            result.Status == GameCompatibilityStatus.Compatible ? Colors.LightGreen : Colors.Orange);
        _compatibility.Text = result.Status switch
        {
            GameCompatibilityStatus.Compatible =>
                $"Compatible with installed Steam build {result.InstalledBuildId}.",
            GameCompatibilityStatus.Incompatible =>
                $"May not be supported: installed Steam build {result.InstalledBuildId} is not listed by this modpack. " +
                $"Declared builds: {string.Join(", ", result.CompatibleBuildIds)}.",
            GameCompatibilityStatus.InstalledBuildUnknown =>
                "May not be supported: the installed Steam build could not be determined. " +
                $"Declared builds: {string.Join(", ", result.CompatibleBuildIds)}.",
            _ => "May not be supported: this modpack does not declare compatible game builds."
        };
        _acknowledgeCompatibility.IsVisible = result.MayBeUnsupported;
    }

    private void RefreshInstallAvailability()
    {
        _install.IsEnabled = !string.IsNullOrWhiteSpace(_gameFolder) &&
            (!_acknowledgeCompatibility.IsVisible || _acknowledgeCompatibility.IsChecked == true);
    }

    private bool IsPreviouslySelected(ModEntry mod)
    {
        if (mod.Required || _installedPack is null)
            return false;

        return _installedPack.SelectedOptionalModIds is null ||
               _installedPack.SelectedOptionalModIds.Contains(
                   mod.Id, StringComparer.OrdinalIgnoreCase);
    }

    private void OnModChoiceChanged(ModEntry changed)
    {
        if (_manifest is null || _updatingChoices || changed.Required)
            return;

        _updatingChoices = true;
        try
        {
            if (_modChoices[changed.Id].IsChecked == true)
                SelectDependencies(changed, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            else
                ClearOptionalDependants(changed.Id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            _updatingChoices = false;
        }
    }

    private void SelectDependencies(ModEntry mod, HashSet<string> visited)
    {
        if (_manifest is null || !visited.Add(mod.Id))
            return;

        foreach (var dependencyId in mod.Dependencies)
        {
            var dependency = _manifest.Mods.FirstOrDefault(candidate =>
                candidate.Id.Equals(dependencyId, StringComparison.OrdinalIgnoreCase));
            if (dependency is null)
                continue;
            _modChoices[dependency.Id].IsChecked = true;
            SelectDependencies(dependency, visited);
        }
    }

    private void ClearOptionalDependants(string dependencyId, HashSet<string> visited)
    {
        if (_manifest is null || !visited.Add(dependencyId))
            return;

        foreach (var dependant in _manifest.Mods.Where(candidate =>
                     !candidate.Required &&
                     candidate.Dependencies.Contains(dependencyId, StringComparer.OrdinalIgnoreCase)))
        {
            _modChoices[dependant.Id].IsChecked = false;
            ClearOptionalDependants(dependant.Id, visited);
        }
    }

    private async Task InstallAsync()
    {
        if (_manifest is null)
            return;
        if (string.IsNullOrWhiteSpace(_gameFolder))
        {
            _status.Text = "Set the game folder on the Manage tab first.";
            return;
        }

        _progress.IsVisible = true;
        _install.IsEnabled = false;
        try
        {
            var fallback = BuildFallback(_pack);
            var selectedOptionalIds = _manifest.Mods
                .Where(mod => !mod.Required && _modChoices[mod.Id].IsChecked == true)
                .Select(mod => mod.Id)
                .ToArray();
            var report = await Task.Run(() => new ModpackInstaller(_gameFolder, _http)
                .InstallAsync(_manifest, fallback, pack: _pack,
                    selectedOptionalIds: selectedOptionalIds));
            _status.Text = report.Success
                ? $"Installed {_pack.Name}."
                : "Install did not complete — see the logs / run a support bundle.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Install failed: {ex.Message}";
        }
        finally
        {
            _progress.IsVisible = false;
            RefreshInstallAvailability();
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
}
