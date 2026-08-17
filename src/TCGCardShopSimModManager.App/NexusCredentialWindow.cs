using System.Diagnostics;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TCGCardShopSimModManager.Core;

namespace TCGCardShopSimModManager.App;

public sealed class NexusCredentialWindow : Window
{
    internal const string ApiAccessUrl = "https://www.nexusmods.com/users/myaccount?tab=api%20access";

    private readonly HttpClient _http;
    private readonly TextBox _key = new()
    {
        PasswordChar = '\u2022',
        Watermark = "Paste your personal API key",
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _save = new() { Content = "Validate and save" };

    public NexusCredentialWindow(HttpClient http, bool hasStoredKey)
    {
        _http = http;
        Title = "Nexus personal API key";
        Width = 500;
        Height = 290;
        MinWidth = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _save.Click += async (_, _) => await SaveAsync();
        var cancel = new Button { Content = "Cancel", Classes = { "secondary" } };
        cancel.Click += (_, _) => Close();

        var remove = new Button
        {
            Content = "Remove saved key",
            Classes = { "danger" },
            IsVisible = hasStoredKey
        };
        remove.Click += (_, _) =>
        {
            ApiKeyStore.Delete();
            Close();
        };

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Personal API key",
                    FontSize = 20,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = "Nexus should now be open at API Access. Request a personal API key there, copy it, then paste it below. The key is encrypted for your Windows account and is only sent to Nexus.",
                    TextWrapping = TextWrapping.Wrap
                },
                _key,
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { _save, cancel, remove }
                }
            }
        };

        Opened += (_, _) => OpenApiAccessPage();
    }

    private void OpenApiAccessPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ApiAccessUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not open Nexus automatically. Open {ApiAccessUrl} in your browser. {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        var key = _key.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            _status.Text = "Paste your Nexus API key first.";
            return;
        }

        _save.IsEnabled = false;
        _key.IsEnabled = false;
        _status.Text = "Checking the key with Nexus...";
        try
        {
            using var api = new NexusApi(NexusApi.ApiBaseUrl(), NexusApi.GameDomain, NexusApi.UserAgent, _http);
            await api.GetUserAsync(NexusAuth.FromApiKey(key), CancellationToken.None);
            ApiKeyStore.Save(key);
            _key.Text = string.Empty;
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = $"Nexus did not accept that key: {ex.Message}";
            Diagnostic.Write(ex.ToString(), "nexus-api-key");
        }
        finally
        {
            _save.IsEnabled = true;
            _key.IsEnabled = true;
        }
    }
}
