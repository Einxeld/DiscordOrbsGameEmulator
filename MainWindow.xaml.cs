using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace DiscordOrbsGameEmulator;

// ─────────────────────────────────────────────────────────────────────────────
//  Model
// ─────────────────────────────────────────────────────────────────────────────

public class GameEntry : INotifyPropertyChanged
{
    private bool _isInstalled;

    public string GameName { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string Directory => Path.GetDirectoryName(ExePath) ?? "";
    public string ExeName => Path.GetFileName(ExePath) ?? "";

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value) return;
            _isInstalled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstall));
        }
    }

    public bool CanInstall => !_isInstalled;

    /// <summary>True when this entry has a Steam App ID configured (ACF button enabled).</summary>
    public bool HasAcf => SteamAppId is not null;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string? SteamAppId { get; init; }  // null = no ACF needed

    // Derived: where the ACF lives
    public string? AcfPath => SteamAppId is null ? null : FindSteamAppsDir(ExePath) is string s
    ? Path.Combine(s, $"appmanifest_{SteamAppId}.acf")
    : null;

    private static string? FindSteamAppsDir(string exePath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(exePath)!);
        while (dir.Parent is not null)
        {
            if (dir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Window code-behind
// ─────────────────────────────────────────────────────────────────────────────

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ObservableCollection<GameEntry> _games = [];

    // Emulation session state
    private DispatcherTimer? _emulationTimer;
    private DateTime _emulationStart;
    private const double EmulationTotalSeconds = 15 * 60; // 15 minutes
    private const double ProgressBarMaxWidth = 320.0;

    /// <summary>Path to the original launcher exe, passed via --launcher-path when in emulation mode.</summary>
    private string? _launcherExePath;

    private const string GitHubUrl = "https://github.com/Einxeld/DiscordOrbsGameEmulator";

    // ── Stats ───────────────────────────────────────────────────────────

    private const string PostHogApiKey = "phc_mrKjD2E57ifLgAKMZVDVjuyCBT7UFMY53gwqZFF8Sr4Q";
    private const string PostHogUrl = "https://eu.i.posthog.com/capture/";

    private static readonly string AnalyticsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiscordOrbsGameEmulator");

    private static readonly string UserIdFile =
        Path.Combine(AnalyticsDir, "analytics.id");

    private static string GetOrCreateUserId()
    {
        Directory.CreateDirectory(AnalyticsDir);

        if (File.Exists(UserIdFile))
            return File.ReadAllText(UserIdFile);

        string id = Guid.NewGuid().ToString();
        File.WriteAllText(UserIdFile, id);
        return id;
    }

    private async Task SendDailyActiveUserAsync()
    {
        var payload = new
        {
            api_key = PostHogApiKey,
            @event = "app_launch",
            properties = new
            {
                distinct_id = GetOrCreateUserId(),
                app_version =
                    System.Reflection.Assembly.GetExecutingAssembly()
                        .GetName().Version?.ToString(),
                os = Environment.OSVersion.VersionString
            }
        };

        try
        {
            await Http.PostAsJsonAsync(PostHogUrl, payload);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        _ = SendDailyActiveUserAsync();

        GamesList.ItemsSource = _games;

        // Hide placeholder when text is typed
        SearchBox.TextChanged += (_, _) =>
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;

        // Check if launched in emulation mode: --emulate "Game Name" --launcher-path "C:\...\Launcher.exe"
        var args = Environment.GetCommandLineArgs();
        int idx = Array.IndexOf(args, "--emulate");
        if (idx >= 0 && idx + 1 < args.Length)
        {
            // Capture the original launcher path so "Закончить" can reopen it
            int lpIdx = Array.IndexOf(args, "--launcher-path");
            string? launcherPath = (lpIdx >= 0 && lpIdx + 1 < args.Length) ? args[lpIdx + 1] : null;

            ShowEmulationMode(args[idx + 1], launcherPath);
            return; // Don't auto-load in emulation mode
        }

        // Auto-load the list if a URL is already configured
        Loaded += async (_, _) => await LoadListAsync();
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySearch(SearchBox.Text);
    }

    private void ApplySearch(string query)
    {
        string trimmed = query.Trim();

        if (_games.Count == 0) return;

        if (string.IsNullOrEmpty(trimmed))
        {
            // Show all
            GamesList.ItemsSource = _games;
            GamesList.Visibility = Visibility.Visible;
            NoResultsState.Visibility = Visibility.Collapsed;
            return;
        }

        var filtered = _games
            .Where(g => g.GameName.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        GamesList.ItemsSource = filtered;

        if (filtered.Count == 0)
        {
            GamesList.Visibility = Visibility.Collapsed;
            NoResultsState.Visibility = Visibility.Visible;
        }
        else
        {
            GamesList.Visibility = Visibility.Visible;
            NoResultsState.Visibility = Visibility.Collapsed;
        }
    }

    // ── Emulation mode ────────────────────────────────────────────────────────

    private void ShowEmulationMode(string gameName, string? launcherPath)
    {
        Title = gameName;

        // Remember the original launcher exe so "Закончить" can reopen it
        _launcherExePath = (launcherPath is not null && File.Exists(launcherPath))
            ? launcherPath
            : FindTargetExe(); // fallback: best-effort

        // Hide the launcher UI, show only the emulation overlay
        LauncherRoot.Visibility = Visibility.Collapsed;
        EmulationOverlay.Visibility = Visibility.Visible;
        EmulationTitle.Text = gameName;

        // Reset progress and time display
        EmulationProgressFill.Width = 0;
        EmulationTimeText.Text = "00:00";

        // Start the session timer
        _emulationStart = DateTime.Now;
        _emulationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _emulationTimer.Tick += EmulationTimer_Tick;
        _emulationTimer.Start();
    }

    private void EmulationTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _emulationStart;
        double totalSec = elapsed.TotalSeconds;

        // Update time label (MM:SS)
        int minutes = (int)(totalSec / 60);
        int seconds = (int)(totalSec % 60);
        EmulationTimeText.Text = $"{minutes:D2}:{seconds:D2}";

        // Update progress bar width (0 → ProgressBarMaxWidth over 15 minutes)
        double progress = Math.Min(1.0, totalSec / EmulationTotalSeconds);
        EmulationProgressFill.Width = progress * ProgressBarMaxWidth;

        // Once full, stop ticking and update hint text
        if (progress >= 1.0)
        {
            _emulationTimer!.Stop();
            EmulationSessionHint.Text = "✓ 15 minutes passed!";
        }
    }

    /// <summary>User clicks "Закончить" — stops emulation, relaunches the launcher, closes this window.</summary>
    private void FinishEmulation_Click(object sender, RoutedEventArgs e)
    {
        _emulationTimer?.Stop();

        // Reopen the original launcher (path was passed via --launcher-path)
        if (_launcherExePath is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _launcherExePath,
                    UseShellExecute = false
                });
            }
            catch
            {
                // If relaunch fails, still close this window — user can open the launcher manually.
            }
        }

        Application.Current.Shutdown();
    }

    // ── GitHub button ─────────────────────────────────────────────────────────

    private void GitHubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GitHubUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open the browser ({GitHubUrl}):\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns the path of the currently running executable.</summary>
    private static string? FindTargetExe()
    {
        string? path = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName;
        return File.Exists(path) ? path : null;
    }

    private void SetStatus(string msg) => StatusBar.Text = msg;

    /// <summary>Re-checks every entry against the filesystem and re-applies the current search filter.</summary>
    private void RefreshAll(string? statusMessage = null)
    {
        foreach (var g in _games)
            g.IsInstalled = File.Exists(g.ExePath);
        ApplySearch(SearchBox.Text);
        SetStatus(statusMessage ?? "Status updated.");
    }

    // ── Load list from GitHub ─────────────────────────────────────────────────

    private void PasteList_Click(object sender, RoutedEventArgs e) => LoadListFromClipboard();
    
    private void LoadListFromClipboard()
    {
        string raw;
        try
        {
            if (!Clipboard.ContainsText())
            {
                SetStatus("Clipboard does not contain any text.");
                return;
            }
            raw = Clipboard.GetText();
        }
        catch (Exception ex)
        {
            SetStatus($"Clipboard error: {ex.Message}");
            MessageBox.Show($"Could not read the clipboard:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
    
        if (string.IsNullOrWhiteSpace(raw))
        {
            SetStatus("Clipboard is empty.");
            return;
        }
    
        EmptyState.Visibility = Visibility.Collapsed;
        NoResultsState.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Visible;
        GamesList.Visibility = Visibility.Collapsed;
        SearchBox.Text = "";
        SetStatus("Loading list from clipboard…");
    
        try
        {
            ParseAndLoad(raw);
        }
        catch (Exception ex)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show($"Could not parse the clipboard content:\n{ex.Message}",
                "Parsing error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LoadList_Click(object sender, RoutedEventArgs e) => await LoadListAsync();

    private async Task LoadListAsync()
    {
        string url = UrlTextBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
            return;

        EmptyState.Visibility = Visibility.Collapsed;
        NoResultsState.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Visible;
        GamesList.Visibility = Visibility.Collapsed;
        SearchBox.Text = "";
        SetStatus("Loading the list…");

        try
        {
            string raw = await Http.GetStringAsync(url);
            ParseAndLoad(raw);
        }
        catch (Exception ex)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show($"Could not load the list:\n{ex.Message}",
                "Loading error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ParseAndLoad(string raw)
    {
        _games.Clear();

        var lines = raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#'));

        foreach (string line in lines)
        {
            var parts = line.Split('|', 4, StringSplitOptions.TrimEntries);

            string gameName, exePath;
            string? appId = null;

            if (parts.Length >= 2) { gameName = parts[0]; exePath = parts[1]; }
            else { exePath = parts[0]; gameName = Path.GetFileNameWithoutExtension(exePath); }

            if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                appId = parts[2];

            if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                exePath += ".exe";

            var entry = new GameEntry { GameName = gameName, ExePath = exePath, SteamAppId = appId };
            entry.IsInstalled = File.Exists(exePath);
            _games.Add(entry);
        }

        var sorted = _games
            .OrderBy(g => g.GameName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _games.Clear();

        foreach (var game in sorted)
            _games.Add(game);

        LoadingState.Visibility = Visibility.Collapsed;

        if (_games.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            SetStatus("List is empty or incorrect.");
        }
        else
        {
            GamesList.ItemsSource = _games;
            GamesList.Visibility = Visibility.Visible;
            SetStatus($"Loaded {_games.Count} games.");
        }
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void RefreshStatus_Click(object sender, RoutedEventArgs e) => RefreshAll();

    // ── INSTALL ───────────────────────────────────────────────────────────────

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        string? sourceExe = FindTargetExe();
        if (sourceExe is null)
        {
            MessageBox.Show("Could not determine game path.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        string sourceDir = Path.GetDirectoryName(sourceExe)!;

        try
        {
            CopyDirectory(sourceDir, entry.Directory, overwrite: false,
                originalPrefix: "DiscordOrbsGameEmulator",
                newPrefix: Path.GetFileNameWithoutExtension(entry.ExeName));

            // Drop marker so we know this folder is safe to fully delete later.
            File.WriteAllText(Path.Combine(entry.Directory, ".orb_emulation"), "");

            if (entry.AcfPath is not null)
            {
                string? installDir = FindInstallDir(entry.ExePath);
                string acfContent =
                    $"\"AppState\"\n{{\n" +
                    $"\t\"appid\"\t\t\"{entry.SteamAppId}\"\n" +
                    $"\t\"name\"\t\t\"{entry.GameName}\"\n" +
                    $"\t\"installdir\"\t\t\"{installDir ?? entry.GameName}\"\n" +
                    $"}}\n";
                File.WriteAllText(entry.AcfPath, acfContent);
            }

            RefreshAll($"✓  {entry.GameName} installed → {entry.Directory}");
        }
        catch (Exception ex)
        {
            SetStatus($"Install error: {ex.Message}");
            MessageBox.Show($"Could not install the game:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? FindInstallDir(string exePath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(exePath)!);
        while (dir.Parent is not null)
        {
            if (dir.Parent.Name.Equals("common", StringComparison.OrdinalIgnoreCase))
                return dir.Name;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Recursively copies <paramref name="source"/> into <paramref name="destination"/>,
    /// renaming files whose name starts with <paramref name="originalPrefix"/> to start with
    /// <paramref name="newPrefix"/> instead.</summary>
    private static void CopyDirectory(string source, string destination, bool overwrite,
        string originalPrefix, string newPrefix)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.GetFiles(source))
        {
            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith(originalPrefix, StringComparison.OrdinalIgnoreCase))
                fileName = newPrefix + fileName[originalPrefix.Length..];
            File.Copy(file, Path.Combine(destination, fileName), overwrite);
        }

        foreach (string subDir in Directory.GetDirectories(source))
        {
            string dest = Path.Combine(destination, Path.GetFileName(subDir));
            CopyDirectory(subDir, dest, overwrite, originalPrefix, newPrefix);
        }
    }

    // ── RUN ───────────────────────────────────────────────────────────────────

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        if (!File.Exists(entry.ExePath))
        {
            RefreshAll("File not found. Status updated.");
            return;
        }

        try
        {
            string? launcherExe = FindTargetExe();

            Process.Start(new ProcessStartInfo
            {
                FileName = entry.ExePath,
                Arguments = $"--emulate \"{entry.GameName}\" --launcher-path \"{launcherExe}\"",
                UseShellExecute = false
            });

            // Close the launcher now that the emulated window is starting
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetStatus($"Launch error: {ex.Message}");
            MessageBox.Show($"Could not launch:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        if (!File.Exists(entry.ExePath))
        {
            RefreshAll("File already exists. Status updated.");
            return;
        }

        string markerPath = Path.Combine(entry.Directory, ".orb_emulation");
        bool isEmulatorCopy = File.Exists(markerPath);

        if (isEmulatorCopy)
        {
            var result = MessageBox.Show(
                $"Delete emulator folder «{entry.GameName}»?\n{entry.Directory}",
                "Confirm deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                Directory.Delete(entry.Directory, recursive: true);

                // Remove ACF marker if we created one
                if (entry.AcfPath is not null && File.Exists(entry.AcfPath))
                {
                    try { File.Delete(entry.AcfPath); } catch { /* best effort */ }
                }

                RefreshAll($"✗  {entry.GameName} deleted (folder deleted completely).");
            }
            catch (Exception ex)
            {
                SetStatus($"Deletion error: {ex.Message}");
                MessageBox.Show($"Could not delete the folder:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            var result = MessageBox.Show(
                $"There is no emulator marker in the folder \"{entry.Directory}\".\n\n" +
                $"This might be a real game, not an emulator copy.\n\n" +
                $"Delete only the file \"{Path.GetFileName(entry.ExePath)}\"?",
                "Warning: .orb_emulation marker not found",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                SetStatus("Deletion cancelled.");
                return;
            }

            try
            {
                File.Delete(entry.ExePath);

                // Remove ACF marker if we created one
                if (entry.AcfPath is not null && File.Exists(entry.AcfPath))
                {
                    try { File.Delete(entry.AcfPath); } catch { /* best effort */ }
                }

                RefreshAll($"✗  {entry.GameName} deleted (only .exe).");
            }
            catch (Exception ex)
            {
                SetStatus($"Deletion error: {ex.Message}");
                MessageBox.Show($"Could not delete the file:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ── OPEN FOLDER ───────────────────────────────────────────────────────────

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        try
        {
            if (!System.IO.Directory.Exists(entry.Directory))
                System.IO.Directory.CreateDirectory(entry.Directory);

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = entry.Directory,
                UseShellExecute = true
            });
            SetStatus($"📂 Folder opened: {entry.Directory}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error opening the folder: {ex.Message}");
            MessageBox.Show($"Could not open the folder:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── OPEN ACF ─────────────────────────────────────────────────────────────

    private void OpenAcfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        string? acfPath = entry.AcfPath;
        if (acfPath is null)
        {
            SetStatus("This game has no ACF file.");
            return;
        }

        try
        {
            if (File.Exists(acfPath))
            {
                // Open Explorer with the ACF file selected
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{acfPath}\"",
                    UseShellExecute = true
                });
                SetStatus($"📄 ACF: {acfPath}");
            }
            else
            {
                // ACF not yet created — open the steamapps folder instead
                string? steamappsDir = Path.GetDirectoryName(acfPath);
                if (steamappsDir is not null && System.IO.Directory.Exists(steamappsDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = steamappsDir,
                        UseShellExecute = true
                    });
                    SetStatus($"📂 ACF was not yet created, opened the folder: {steamappsDir}");
                }
                else
                {
                    MessageBox.Show(
                        $"ACF file not found:\n{acfPath}\n\nsteamapps folder also does not exist.",
                        "ACF not found", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus($"ACF opening error: {ex.Message}");
            MessageBox.Show($"Could not open the ACF file:\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType,
        object parameter, CultureInfo culture)
    {
        return value is bool b && !b
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType,
        object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}