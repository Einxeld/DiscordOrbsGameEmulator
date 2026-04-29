using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace GameEmulator;

// ─────────────────────────────────────────────────────────────────────────────
//  Model
// ─────────────────────────────────────────────────────────────────────────────

public class GameEntry : INotifyPropertyChanged
{
    private bool _isInstalled;

    /// <summary>Имя exe файла без расширения (отображается как название игры).</summary>
    public string GameName { get; init; } = "";

    /// <summary>Полный путь к exe, включая имя файла.</summary>
    public string ExePath { get; init; } = "";

    /// <summary>Папка, в которой должен лежать exe.</summary>
    public string Directory => Path.GetDirectoryName(ExePath) ?? "";

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

    /// <summary>Кнопка «Установить» активна только когда файла ещё нет.</summary>
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

    // Путь к calc.exe: сначала ищем в System32, потом в SysWOW64
    private static readonly string[] CalcCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "calc.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "calc.exe"),
        @"C:\Windows\System32\calc.exe"
    ];

    public MainWindow()
    {
        InitializeComponent();
        GamesList.ItemsSource = _games;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string? FindCalcExe()
        => CalcCandidates.FirstOrDefault(File.Exists);

    private void SetStatus(string msg) => StatusBar.Text = msg;

    private void UpdateCounters()
    {
        int total     = _games.Count;
        int installed = _games.Count(g => g.IsInstalled);
        TotalCount.Text        = $"Всего: {total}";
        InstalledCount.Text    = $"Установлено: {installed}";
        NotInstalledCount.Text = $"Не установлено: {total - installed}";
    }

    // ── Load list from GitHub ────────────────────────────────────────────────

    private async void LoadList_Click(object sender, RoutedEventArgs e)
    {
        string url = UrlTextBox.Text.Trim();
        if (string.IsNullOrEmpty(url))
        {
            MessageBox.Show("Введите URL txt-файла.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        EmptyState.Visibility  = Visibility.Collapsed;
        LoadingState.Visibility = Visibility.Visible;
        GamesList.Visibility   = Visibility.Collapsed;
        SetStatus("Загружаю список…");

        try
        {
            string raw = await Http.GetStringAsync(url);
            ParseAndLoad(raw);
        }
        catch (Exception ex)
        {
            LoadingState.Visibility = Visibility.Collapsed;
            EmptyState.Visibility   = Visibility.Visible;
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
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')); // # — комментарии

        foreach (string line in lines)
        {
            // Ожидаемый формат строки: C:\Games\Steam\steamapps\common\GameName\GameName.exe
            // или просто путь к exe
            string path = line;

            // Нормализуем расширение
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                path += ".exe";

            string gameName = Path.GetFileNameWithoutExtension(path);

            var entry = new GameEntry
            {
                GameName = gameName,
                ExePath  = path,
            };

            entry.IsInstalled = File.Exists(path);
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

    // ── Refresh installed status ─────────────────────────────────────────────

    private void RefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        foreach (var g in _games)
            g.IsInstalled = File.Exists(g.ExePath);
        UpdateCounters();
        SetStatus("Статус обновлён.");
    }

    // ── INSTALL ──────────────────────────────────────────────────────────────

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        string? calcPath = FindCalcExe();
        if (calcPath is null)
        {
            MessageBox.Show("Не удалось найти calc.exe в системных папках.",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            string dir = entry.Directory;

            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            File.Copy(calcPath, entry.ExePath, overwrite: false);

            entry.IsInstalled = true;
            UpdateCounters();
            SetStatus($"✓  {entry.GameName} установлена → {entry.ExePath}");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка установки: {ex.Message}");
            MessageBox.Show($"Не удалось установить игру:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── RUN ──────────────────────────────────────────────────────────────────

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        if (!File.Exists(entry.ExePath))
        {
            entry.IsInstalled = false;
            UpdateCounters();
            SetStatus("Файл не найден. Обновите статус.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = entry.ExePath,
                UseShellExecute = true
            });
            SetStatus($"▶  Запущена: {entry.GameName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка запуска: {ex.Message}");
            MessageBox.Show($"Не удалось запустить:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── DELETE ───────────────────────────────────────────────────────────────

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameEntry entry) return;

        if (!File.Exists(entry.ExePath))
        {
            entry.IsInstalled = false;
            UpdateCounters();
            SetStatus("Файл уже не существует.");
            return;
        }

        // Проверяем, не одинок ли файл в папке
        bool singleInDir = IsSingleFileInDirectory(entry.Directory, entry.ExePath);

        if (!singleInDir)
        {
            var result = MessageBox.Show(
                $"В папке \"{entry.Directory}\" есть другие файлы.\n\n" +
                $"Возможно, это настоящая игра, а не эмулятор.\n\n" +
                $"Всё равно удалить \"{Path.GetFileName(entry.ExePath)}\"?",
                "Внимание: возможно, настоящая игра",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                SetStatus("Удаление отменено пользователем.");
                return;
            }
        }
        else
        {
            var result = MessageBox.Show(
                $"Удалить «{entry.GameName}»?\n{entry.ExePath}",
                "Подтверждение удаления",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;
        }

        try
        {
            File.Delete(entry.ExePath);
            entry.IsInstalled = false;
            UpdateCounters();
            SetStatus($"✗  {entry.GameName} удалена.");
        }
        catch (Exception ex)
        {
            SetStatus($"Ошибка удаления: {ex.Message}");
            MessageBox.Show($"Не удалось удалить файл:\n{ex.Message}",
                "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Возвращает true, если в папке только один файл (тот самый exe),
    /// без вложенных папок.
    /// </summary>
    private static bool IsSingleFileInDirectory(string dir, string targetFile)
    {
        if (!System.IO.Directory.Exists(dir)) return true;

        var files = System.IO.Directory.GetFiles(dir);
        var dirs  = System.IO.Directory.GetDirectories(dir);

        return dirs.Length == 0
            && files.Length == 1
            && string.Equals(files[0], targetFile, StringComparison.OrdinalIgnoreCase);
    }
}
