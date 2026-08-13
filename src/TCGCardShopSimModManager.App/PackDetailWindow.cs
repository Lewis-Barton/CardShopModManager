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
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false };
    private readonly Button _install = new() { Content = "Install modpack", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private ModListManifest? _manifest;

    public PackDetailWindow(
        ModpackSummary pack,
        string? gameFolder,
        HttpClient http,
        ModpackIndexReader reader)
    {
        _pack = pack;
        _gameFolder = gameFolder;
        _http = http;
        _reader = reader;
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

        var mods = new ListBox { MaxHeight = 200 };
        _install.Click += async (_, _) => await InstallAsync();

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
                    new TextBlock { Text = "Includes:", FontWeight = FontWeight.Bold },
                    mods,
                    _progress,
                    _install,
                    _status
                }
            }
        };

        _ = LoadManifestAsync(mods);
    }

    private async Task LoadManifestAsync(ListBox mods)
    {
        try
        {
            _manifest = await _reader.FetchManifestAsync(_pack);
            mods.ItemsSource = _manifest.Mods.ConvertAll(m => $"{m.Name} {m.Version ?? ""}".Trim());
            _install.IsEnabled = !string.IsNullOrWhiteSpace(_gameFolder);
            if (string.IsNullOrWhiteSpace(_gameFolder))
                _status.Text = "Set the game folder on the Manage tab first.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not read manifest: {ex.Message}";
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
            var report = await Task.Run(() => new ModpackInstaller(_gameFolder, _http)
                .InstallAsync(_manifest, fallback, pack: _pack));
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
            _install.IsEnabled = true;
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
