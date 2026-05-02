using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GameEmulator;

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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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

    // ── Entry point ───────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        GamesList.ItemsSource = _games;

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
            EmulationSessionHint.Text = "✓ 15 минут прошло, нажмите Закончить!";
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
            MessageBox.Show($"Не удалось открыть браузер:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void UpdateCounters()
    {
        int total = _games.Count;
        int installed = _games.Count(g => g.IsInstalled);
        TotalCount.Text = $"Всего: {total}";
        InstalledCount.Text = $"Установлено: {installed}";
        NotInstalledCount.Text = $"Не установлено: {total - installed}";
    }

    /// <summary>Re-checks every entry against the filesystem and updates counters.</summary>
    private void RefreshAll(string? statusMessage = null)
    {
        foreach (var g in _games)
            g.IsInstalled = File.Exists(g.ExePath);
        UpdateCounters();
        SetStatus(statusMessage ?? "Статус обновлён.");
    }

    // ── Load list from GitHub ─────────────────────────────────────────────────

    private async void LoadList_Click(object sender, RoutedEventArgs e) => await LoadListAsync();

    private async Task LoadListAsync()
    {
        string url = UrlTextBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
            return;

        EmptyState.Visibility = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Visible;
        GamesList.Visibility = Visibility.Collapsed;
        SetStatus("Загружаю список…");

        try
        {
            string raw = await Http.GetStringAsync(url);
            ParseAndLoad(raw);
        }
        catch (Exception ex)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            SetStatus($"Ошибка: {ex.Message}");
            MessageBox.Show($"Не удалось загрузить список:\n{ex.Message}",
                "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);

            string gameName, exePath;

            if (parts.Length == 2)
            {
                gameName = parts[0];
                exePath = parts[1];
            }
            else
            {
                exePath = parts[0];
                gameName = Path.GetFileNameWithoutExtension(exePath);
            }

            if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                exePath += ".exe";

            var entry = new GameEntry { GameName = gameName, ExePath = exePath };
            entry.IsInstalled = File.Exists(exePath);
            _games.Add(entry);
        }

        LoadingState.Visibility = Visibility.Collapsed;

        if (_games.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            SetStatus("Список пуст или не содержит корректных путей.");
        }
        else
        {
            GamesList.Visibility = Visibility.Visible;
            SetStatus($"Загружено {_games.Count} записей.");
        }

        UpdateCounters();
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
            MessageBox.Show("Не удалось определить путь к текущему exe-файлу.",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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

            RefreshAll($"✓  {entry.GameName} установлена → {entry.Directory}");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка установки: {ex.Message}");
            MessageBox.Show($"Не удалось установить игру:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            RefreshAll("Файл не найден. Статус обновлён.");
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
            SetStatus($"Ошибка запуска: {ex.Message}");
            MessageBox.Show($"Не удалось запустить:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── DELETE ────────────────────────────────────────────────────────────────

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        if (!File.Exists(entry.ExePath))
        {
            RefreshAll("Файл уже не существует. Статус обновлён.");
            return;
        }

        string markerPath = Path.Combine(entry.Directory, ".orb_emulation");
        bool isEmulatorCopy = File.Exists(markerPath);

        if (isEmulatorCopy)
        {
            var result = MessageBox.Show(
                $"Удалить папку эмулятора «{entry.GameName}»?\n{entry.Directory}",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                Directory.Delete(entry.Directory, recursive: true);
                RefreshAll($"✗  {entry.GameName} удалена (папка удалена целиком).");
            }
            catch (Exception ex)
            {
                SetStatus($"Ошибка удаления: {ex.Message}");
                MessageBox.Show($"Не удалось удалить папку:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            var result = MessageBox.Show(
                $"В папке \"{entry.Directory}\" нет метки эмулятора.\n\n" +
                $"Возможно, это настоящая игра, а не копия эмулятора.\n\n" +
                $"Удалить только файл \"{Path.GetFileName(entry.ExePath)}\"?",
                "Внимание: маркер .orb_emulation не найден",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                SetStatus("Удаление отменено пользователем.");
                return;
            }

            try
            {
                File.Delete(entry.ExePath);
                RefreshAll($"✗  {entry.GameName} удалена (только exe).");
            }
            catch (Exception ex)
            {
                SetStatus($"Ошибка удаления: {ex.Message}");
                MessageBox.Show($"Не удалось удалить файл:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
            SetStatus($"📂 Открыта папка: {entry.Directory}");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка открытия папки: {ex.Message}");
            MessageBox.Show($"Не удалось открыть папку:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}