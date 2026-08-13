using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TCGCardShopSimModManager.App;

/// <summary>
/// A tiny modal yes/no confirmation. Avalonia has no built-in MessageBox, so
/// this stands in for the "are you sure?" prompts (e.g. Reset application).
/// </summary>
public static class ConfirmDialog
{
    public static async Task<bool> Show(Window owner, string title, string message)
    {
        var result = false;
        var yes = new Button { Content = "Yes", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var no = new Button { Content = "No", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

        var dlg = new Window
        {
            Title = title,
            Width = 400,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { no, yes }
                    }
                }
            }
        };

        yes.Click += (_, _) => { result = true; dlg.Close(); };
        no.Click += (_, _) => { result = false; dlg.Close(); };

        await dlg.ShowDialog(owner);
        return result;
    }
}
