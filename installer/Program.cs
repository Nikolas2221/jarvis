using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}

internal sealed class InstallerForm : Form
{
    private const string ManifestUrl = "https://github.com/Nikolas2221/jarvis/releases/latest/download/update-manifest.json";
    private const string ReleasesUrl = "https://github.com/Nikolas2221/jarvis/releases";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly Label _status = new();
    private readonly Label _version = new();
    private readonly TextBox _installPath = new();
    private readonly Button _browseButton = new();
    private readonly Button _installButton = new();
    private readonly Button _uninstallButton = new();
    private readonly Button _closeButton = new();
    private readonly CheckBox _desktopShortcut = new();
    private readonly CheckBox _startMenuShortcut = new();
    private readonly CheckBox _launchAfterInstall = new();
    private readonly ProgressBar _progress = new();
    private bool _installing;

    public InstallerForm()
    {
        Text = "Jarvis Alpha Setup";
        Width = 560;
        Height = 340;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var title = new Label
        {
            Text = "Установка Jarvis Alpha",
            Left = 24,
            Top = 22,
            Width = 480,
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold)
        };

        var pathLabel = new Label
        {
            Text = "Папка установки:",
            Left = 24,
            Top = 70,
            Width = 180
        };

        _installPath.Left = 24;
        _installPath.Top = 94;
        _installPath.Width = 390;
        _installPath.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jarvis");

        _browseButton.Text = "Обзор...";
        _browseButton.Left = 424;
        _browseButton.Top = 92;
        _browseButton.Width = 90;
        _browseButton.Click += BrowseButton_Click;

        _desktopShortcut.Text = "Создать ярлык на рабочем столе";
        _desktopShortcut.Left = 24;
        _desktopShortcut.Top = 134;
        _desktopShortcut.Width = 280;
        _desktopShortcut.Checked = true;

        _startMenuShortcut.Text = "Добавить в меню Пуск";
        _startMenuShortcut.Left = 24;
        _startMenuShortcut.Top = 162;
        _startMenuShortcut.Width = 280;
        _startMenuShortcut.Checked = true;

        _launchAfterInstall.Text = "Запустить Jarvis после установки";
        _launchAfterInstall.Left = 24;
        _launchAfterInstall.Top = 190;
        _launchAfterInstall.Width = 280;
        _launchAfterInstall.Checked = true;

        _status.Text = "Готов к установке.";
        _status.Left = 24;
        _status.Top = 214;
        _status.Width = 490;

        _version.Text = "Версия: проверка после запуска установки.";
        _version.Left = 24;
        _version.Top = 236;
        _version.Width = 490;

        _progress.Left = 24;
        _progress.Top = 258;
        _progress.Width = 490;
        _progress.Height = 20;
        _progress.Style = ProgressBarStyle.Blocks;

        _installButton.Text = "Установить";
        _installButton.Left = 194;
        _installButton.Top = 282;
        _installButton.Width = 100;
        _installButton.Click += async (_, _) => await InstallAsync();

        _uninstallButton.Text = "Удалить";
        _uninstallButton.Left = 304;
        _uninstallButton.Top = 282;
        _uninstallButton.Width = 100;
        _uninstallButton.Click += (_, _) => Uninstall();

        _closeButton.Text = "Закрыть";
        _closeButton.Left = 414;
        _closeButton.Top = 282;
        _closeButton.Width = 100;
        _closeButton.Click += (_, _) => Close();

        Controls.AddRange([
            title,
            pathLabel,
            _installPath,
            _browseButton,
            _desktopShortcut,
            _startMenuShortcut,
            _launchAfterInstall,
            _status,
            _version,
            _progress,
            _installButton,
            _uninstallButton,
            _closeButton
        ]);
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Выберите папку установки Jarvis Alpha",
            SelectedPath = _installPath.Text,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installPath.Text = EnsureJarvisFolder(dialog.SelectedPath);
        }
    }

    private async Task InstallAsync()
    {
        if (_installing) return;

        var installDir = EnsureJarvisFolder(_installPath.Text.Trim());
        _installPath.Text = installDir;
        if (string.IsNullOrWhiteSpace(installDir))
        {
            MessageBox.Show("Выберите папку установки.", "Jarvis Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetInstalling(true);
        try
        {
            Directory.CreateDirectory(installDir);
            var tempRoot = Path.Combine(Path.GetTempPath(), "JarvisInstaller", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            SetStatus("Получаю manifest обновления...", marquee: true);
            var manifest = await DownloadManifestAsync();
            var installedVersion = GetInstalledVersion(Path.Combine(installDir, "Jarvis.exe"));
            _version.Text = $"Установлена: {installedVersion}; доступна: {manifest.Version}";

            if (installedVersion == manifest.Version)
            {
                var reinstall = MessageBox.Show(
                    "Установлена актуальная версия. Переустановить Jarvis Alpha?",
                    "Jarvis Setup",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (reinstall != DialogResult.Yes)
                {
                    SetStatus("Установка отменена: версия актуальна.", marquee: false, value: 100);
                    SetInstalling(false);
                    return;
                }
            }

            var zipPath = Path.Combine(tempRoot, "Jarvis.zip");
            SetStatus($"Скачиваю Jarvis {manifest.Version}...", marquee: true);
            await DownloadFileAsync(manifest.PackageUrl, zipPath);

            var extractDir = Path.Combine(tempRoot, "app");
            SetStatus("Распаковываю файлы...", marquee: true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            SetStatus("Копирую приложение...", marquee: true);
            CopyDirectory(extractDir, installDir);

            File.WriteAllText(
                Path.Combine(installDir, "update-settings.json"),
                JsonSerializer.Serialize(new { manifestUrl = ManifestUrl }, new JsonSerializerOptions { WriteIndented = true }));

            var exePath = Path.Combine(installDir, "Jarvis.exe");
            if (_desktopShortcut.Checked)
            {
                SetStatus("Создаю ярлык на рабочем столе...", marquee: true);
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jarvis Alpha.lnk"),
                    exePath,
                    installDir);
            }

            if (_startMenuShortcut.Checked)
            {
                SetStatus("Добавляю Jarvis в меню Пуск...", marquee: true);
                CreateShortcut(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Jarvis Alpha.lnk"),
                    exePath,
                    installDir);
            }

            SetStatus("Установка завершена.", marquee: false, value: 100);
            MessageBox.Show("Jarvis Alpha установлен.", "Jarvis Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (_launchAfterInstall.Checked)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }

            Close();
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка установки.", marquee: false, value: 0);
            SetInstalling(false);

            var message = ex is HttpRequestException
                ? "Не удалось скачать файлы установки с GitHub Releases. Проверьте интернет и доступность релиза."
                : ex.Message;

            var result = MessageBox.Show(
                $"{message}\n\nОткрыть страницу релизов?",
                "Jarvis Setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error);

            if (result == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ReleasesUrl,
                    UseShellExecute = true
                });
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_installing)
        {
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    private void SetInstalling(bool installing)
    {
        _installing = installing;
        _installPath.Enabled = !installing;
        _browseButton.Enabled = !installing;
        _desktopShortcut.Enabled = !installing;
        _startMenuShortcut.Enabled = !installing;
        _launchAfterInstall.Enabled = !installing;
        _installButton.Enabled = !installing;
        _uninstallButton.Enabled = !installing;
        _closeButton.Enabled = !installing;
    }

    private void SetStatus(string text, bool marquee, int value = 0)
    {
        _status.Text = text;
        _progress.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
        if (!marquee)
        {
            _progress.Value = Math.Clamp(value, 0, 100);
        }
    }

    private static async Task<UpdateManifest> DownloadManifestAsync()
    {
        var json = await Http.GetStringAsync(ManifestUrl);
        return JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("Не удалось прочитать update-manifest.json.");
    }

    private static string GetInstalledVersion(string exePath)
    {
        if (!File.Exists(exePath)) return "не установлена";

        var info = FileVersionInfo.GetVersionInfo(exePath);
        return string.IsNullOrWhiteSpace(info.ProductVersion)
            ? "неизвестно"
            : info.ProductVersion;
    }

    private void Uninstall()
    {
        var installDir = EnsureJarvisFolder(_installPath.Text.Trim());
        if (string.IsNullOrWhiteSpace(installDir))
        {
            MessageBox.Show("Выберите папку установки.", "Jarvis Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var exePath = Path.Combine(installDir, "Jarvis.exe");
        if (!File.Exists(exePath))
        {
            MessageBox.Show("Jarvis.exe не найден в выбранной папке.", "Jarvis Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Удалить Jarvis Alpha из папки?\n\n{installDir}",
            "Jarvis Setup",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        try
        {
            SetInstalling(true);
            SetStatus("Останавливаю Jarvis...", marquee: true);
            foreach (var process in Process.GetProcessesByName("Jarvis"))
            {
                try { process.Kill(entireProcessTree: true); }
                catch { }
            }

            SetStatus("Удаляю ярлыки и автозапуск...", marquee: true);
            DeleteIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jarvis Alpha.lnk"));
            DeleteIfExists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Jarvis Alpha.lnk"));
            RemoveAutostart();

            SetStatus("Удаляю файлы приложения...", marquee: true);
            Directory.Delete(installDir, recursive: true);

            SetStatus("Jarvis Alpha удалён.", marquee: false, value: 100);
            MessageBox.Show("Jarvis Alpha удалён.", "Jarvis Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка удаления.", marquee: false, value: 0);
            MessageBox.Show(ex.Message, "Jarvis Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetInstalling(false);
        }
    }

    private static string EnsureJarvisFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed).Equals("Jarvis", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(trimmed, "Jarvis");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void RemoveAutostart()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
        key?.DeleteValue("JarvisAlpha", throwOnMissingValue: false);
    }

    private static async Task DownloadFileAsync(string url, string path)
    {
        await using var stream = await Http.GetStreamAsync(url);
        await using var file = File.Create(path);
        await stream.CopyToAsync(file);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, target));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, target), overwrite: true);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
        dynamic shortcut = shell!.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [shortcutPath])!;
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = "Jarvis Alpha";
        shortcut.Save();
    }

    private sealed record UpdateManifest(string Version, string PackageUrl, string? Sha256, string? Notes);
}
