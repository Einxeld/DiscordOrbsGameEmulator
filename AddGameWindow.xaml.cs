using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DiscordOrbsGameEmulator;

public partial class AddGameWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private const string WebDatabaseUrl = "https://cdn.discordapp.com/detectables/games.json";

    private static List<DiscordGameModel>? _cachedGamesDb;
    public GameEntry? ResultGameEntry { get; private set; }

    public AddGameWindow()
    {
        InitializeComponent();
        _ = LoadOnlineDatabaseAsync();
    }

    private async Task LoadOnlineDatabaseAsync(bool forceReload = false)
    {
        if (_cachedGamesDb is not null && !forceReload)
        {
            OnlineStatusText.Text = $"Database ready ({_cachedGamesDb.Count} games loaded).";
            return;
        }

        OnlineStatusText.Text = "Loading web games database...";
        RefreshDbButton.IsEnabled = false;

        try
        {
            var data = await Http.GetFromJsonAsync<List<DiscordGameModel>>(WebDatabaseUrl);
            _cachedGamesDb = data?.Where(g => !string.IsNullOrWhiteSpace(g.Name)).ToList() ?? [];
            OnlineStatusText.Text = $"Database loaded ({_cachedGamesDb.Count} games). Type to search.";
        }
        catch (Exception ex)
        {
            OnlineStatusText.Text = $"Failed to load web database: {ex.Message}";
        }
        finally
        {
            RefreshDbButton.IsEnabled = true;
        }
    }

    private void TabRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (OnlineSearchGrid is null || ManualAddGrid is null) return;

        bool isOnline = TabOnlineRadio.IsChecked == true;
        OnlineSearchGrid.Visibility = isOnline ? Visibility.Visible : Visibility.Collapsed;
        ManualAddGrid.Visibility = isOnline ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnlineSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = OnlineSearchBox.Text.Trim();
        if (_cachedGamesDb is null || string.IsNullOrEmpty(query))
        {
            OnlineResultsList.ItemsSource = null;
            OnlineStatusText.Visibility = Visibility.Visible;
            return;
        }

        var results = _cachedGamesDb
            .Where(g => g.Matches(query))
            .Take(40)
            .ToList();

        OnlineResultsList.ItemsSource = results;
        OnlineStatusText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Count == 0)
            OnlineStatusText.Text = "No games matched your query.";
    }

    private void OnlineResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OnlineResultsList.SelectedItem is not DiscordGameModel selected) return;

        string exeName = selected.GetPreferredExecutable();
        string? steamId = selected.GetSteamAppId();
        string sanitizedFolder = SanitizeFolderName(selected.Name);

        // Build default install path (in Steam or Program Files)
        string basePath;
        if (steamId is not null)
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", sanitizedFolder, exeName);
        }
        else
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam", "steamapps", "common", "DOGE", sanitizedFolder, exeName);
        }

        OnlineExePathBox.Text = basePath;
    }

    private static string SanitizeFolderName(string folderName)
    {
        // Keep only letters, digits, and spaces
        string sanitized = Regex.Replace(folderName, @"[^a-zA-Z0-9 ]", "").Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Game" : sanitized;
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select Game Executable"
        };
        if (dlg.ShowDialog() == true)
        {
            OnlineExePathBox.Text = dlg.FileName;
        }
    }

    private void BrowseManualExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select Game Executable"
        };
        if (dlg.ShowDialog() == true)
        {
            ManualPathBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(ManualNameBox.Text))
                ManualNameBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
    }

    private async void RefreshDbButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadOnlineDatabaseAsync(forceReload: true);
        OnlineSearchBox_TextChanged(OnlineSearchBox, null!);
    }

    private void AddConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (TabOnlineRadio.IsChecked == true)
        {
            if (OnlineResultsList.SelectedItem is not DiscordGameModel selected)
            {
                MessageBox.Show("Please select a game from the search results.", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string path = OnlineExePathBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Please provide an executable path for this game.", "Path Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultGameEntry = new GameEntry
            {
                GameName = selected.Name,
                ExePath = path,
                SteamAppId = selected.GetSteamAppId(),
                IsInstalled = File.Exists(path)
            };
        }
        else
        {
            string name = ManualNameBox.Text.Trim();
            string path = ManualPathBox.Text.Trim();
            string? appId = string.IsNullOrWhiteSpace(ManualAppIdBox.Text) ? null : ManualAppIdBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Please fill in both Game Name and Executable Path.", "Fields Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                path += ".exe";

            ResultGameEntry = new GameEntry
            {
                GameName = name,
                ExePath = path,
                SteamAppId = appId,
                IsInstalled = File.Exists(path)
            };
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}

// ─────────────────────────────────────────────────────────────────────────────
//  Web Database Data Models
// ─────────────────────────────────────────────────────────────────────────────

public class DiscordGameModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    [JsonPropertyName("executables")]
    public List<DiscordExecutableModel>? Executables { get; set; }

    [JsonPropertyName("third_party_skus")]
    public List<DiscordSkuModel>? ThirdPartySkus { get; set; }

    public string ExecutableSummary
    {
        get
        {
            var winExes = Executables?
                .Where(e => e.Os?.Equals("win32", StringComparison.OrdinalIgnoreCase) == true)
                .Select(e => Path.GetFileName(e.Name))
                .Distinct();

            return winExes is not null && winExes.Any()
                ? "Executable: " + string.Join(", ", winExes)
                : "Executable: (win32 default)";
        }
    }

    public string SteamIdDisplay
    {
        get
        {
            string? id = GetSteamAppId();
            return id is not null ? $"Steam ID: {id}" : "";
        }
    }

    public bool Matches(string query)
    {
        if (Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        if (Aliases != null && Aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public string GetPreferredExecutable()
    {
        var winExe = Executables?
            .FirstOrDefault(e => e.Os?.Equals("win32", StringComparison.OrdinalIgnoreCase) == true && !e.IsLauncher);

        winExe ??= Executables?.FirstOrDefault(e => e.Os?.Equals("win32", StringComparison.OrdinalIgnoreCase) == true);

        if (winExe?.Name is not null)
            return Path.GetFileName(winExe.Name);

        return $"{Name.ToLower().Replace(" ", "")}.exe";
    }

    public string? GetSteamAppId()
    {
        return ThirdPartySkus?
            .FirstOrDefault(s => s.Distributor?.Equals("steam", StringComparison.OrdinalIgnoreCase) == true && !string.IsNullOrWhiteSpace(s.Id))
            ?.Id;
    }
}

public class DiscordExecutableModel
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("os")]
    public string? Os { get; set; }

    [JsonPropertyName("is_launcher")]
    public bool IsLauncher { get; set; }
}

public class DiscordSkuModel
{
    [JsonPropertyName("distributor")]
    public string? Distributor { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}