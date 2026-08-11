using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CardShopModManager.Core;

namespace CardShopModManager.App;

/// <summary>
/// The desktop shell: thin UI over <see cref="DeploymentService"/> — the same
/// engine the CLI uses. Heavy work runs on a background task so the window
/// stays responsive, then results are written to the log panel.
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
    private readonly ProgressBar _progress = new()
    {
        IsIndeterminate = true,
        IsVisible = false,
        Height = 6
    };

    private readonly DeploymentService _service = new();

    public MainWindow()
    {
        Title = "Card Shop Mod Manager";
        Width = 940;
        Height = 640;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var grid = new Grid { Margin = new Thickness(12) };
        for (var i = 0; i < 5; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star)); // log fills the rest

        AddPathRow(grid, 0, _gameBox, () => PickFolderAsync(_gameBox));
        AddPathRow(grid, 1, _manifestBox, () => PickFileAsync(_manifestBox));
        AddPathRow(grid, 2, _sourceBox, () => PickFolderAsync(_sourceBox));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Button("Validate", OnValidateAsync));
        buttons.Children.Add(Button("Plan", OnPlanAsync));
        buttons.Children.Add(Button("Install", OnInstallAsync));
        buttons.Children.Add(Button("Uninstall", OnUninstallAsync));
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);

        Grid.SetRow(_progress, 4);
        grid.Children.Add(_progress);

        Grid.SetRow(_log, 5);
        grid.Children.Add(_log);

        return grid;
    }

    private void AddPathRow(Grid grid, int row, TextBox box, Func<Task> browseAction)
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        Grid.SetColumn(box, 0);
        panel.Children.Add(box);

        var browse = Button("Browse…", browseAction);
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

    private async Task OnValidateAsync()
    {
        var manifestPath = _manifestBox.Text;
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            Log($"Enter a manifest path first.");
            return;
        }

        Log($"--- Validate {manifestPath}");
        await RunUnderProgress(() => Task.Run(() =>
        {
            var gameFolder = string.IsNullOrWhiteSpace(_gameBox.Text) ? null : _gameBox.Text;
            var report = _service.Validate(manifestPath, gameFolder);
            foreach (var line in report.Lines)
                Log(line);
        }));
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
        await RunUnderProgress(() => Task.Run(() =>
        {
            var previews = _service.Preview(manifestPath, source);
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
        }));
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
        await RunUnderProgress(() => Task.Run(() =>
        {
            var report = _service.Install(manifestPath, source, gameFolder);
            foreach (var line in report.Lines)
                Log(line);
        }));
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
        await RunUnderProgress(() => Task.Run(() =>
        {
            var result = new ModInstaller(gameFolder).Uninstall(modName);
            if (!result.Success)
            {
                Log(result.Error ?? "Uninstall failed.");
                return;
            }

            Log($"Uninstalled {modName}.");
            foreach (var warning in result.Warnings)
                Log($"  Warning: {warning}");
        }));
    }

    private async Task RunUnderProgress(Func<Task> work)
    {
        _progress.IsVisible = true;
        try
        {
            await work();
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