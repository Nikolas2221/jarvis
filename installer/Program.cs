using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    private const string ManifestUrl = "https://github.com/Nikolas2221/jarvis/releases/latest/download/update-manifest.json";
    private const string ReleasesUrl = "https://github.com/Nikolas2221/jarvis/releases";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    [STAThread]
    private static async Task Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Jarvis");

            Directory.CreateDirectory(installDir);
            var tempRoot = Path.Combine(Path.GetTempPath(), "JarvisInstaller", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            var manifest = await DownloadManifestAsync();
            var zipPath = Path.Combine(tempRoot, "Jarvis.zip");
            await DownloadFileAsync(manifest.PackageUrl, zipPath);

            var extractDir = Path.Combine(tempRoot, "app");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            CopyDirectory(extractDir, installDir);

            File.WriteAllText(
                Path.Combine(installDir, "update-settings.json"),
                JsonSerializer.Serialize(new { manifestUrl = ManifestUrl }, new JsonSerializerOptions { WriteIndented = true }));

            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Jarvis Alpha.lnk"),
                Path.Combine(installDir, "Jarvis.exe"),
                installDir);

            MessageBox.Show("Jarvis Alpha установлен.", "Jarvis Installer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(installDir, "Jarvis.exe"),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            var message = ex is HttpRequestException
                ? "Не удалось скачать файлы установки с GitHub Releases.\n\n" +
                  "Проверьте, что Release уже создан, workflow GitHub Actions завершился успешно, " +
                  "и репозиторий/Release доступны публично. Для приватного репозитория GitHub отдаёт 404 без авторизации.\n\n" +
                  $"Страница релизов:\n{ReleasesUrl}"
                : ex.Message;

            var result = MessageBox.Show(
                message + "\n\nОткрыть страницу релизов?",
                "Jarvis Installer",
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

    private static async Task<UpdateManifest> DownloadManifestAsync()
    {
        var json = await Http.GetStringAsync(ManifestUrl);
        return JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? throw new InvalidOperationException("Не удалось прочитать update-manifest.json.");
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
