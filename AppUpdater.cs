using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Jarvis;

public sealed class AppUpdater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct)
    {
        var options = AppUpdateOptions.Load();
        if (string.IsNullOrWhiteSpace(options.ManifestUrl))
        {
            return UpdateCheckResult.Disabled();
        }

        var json = await Http.GetStringAsync(options.ManifestUrl, ct);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
                       ?? throw new InvalidOperationException("Манифест обновления пустой.");

        var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var latest = Version.Parse(manifest.Version);
        if (latest <= current)
        {
            return UpdateCheckResult.UpToDate(ToDisplayVersion(current), manifest.Version);
        }

        return UpdateCheckResult.Available(ToDisplayVersion(current), manifest);
    }

    public async Task DownloadAndInstallAsync(UpdateManifest manifest, Action<UpdateProgress> progress, CancellationToken ct)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var tempRoot = Path.Combine(Path.GetTempPath(), "JarvisUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var zipPath = Path.Combine(tempRoot, "jarvis-update.zip");
        progress(new UpdateProgress("Скачиваю обновление..."));
        using var response = await Http.GetAsync(manifest.PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;

        await using (var stream = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(zipPath))
        {
            var buffer = new byte[128 * 1024];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                progress(new UpdateProgress("Скачиваю обновление...", downloaded, totalBytes));
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(File.OpenRead(zipPath), ct));
            if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("SHA256 обновления не совпал с манифестом.");
            }
        }

        var extractDir = Path.Combine(tempRoot, "package");
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var scriptPath = Path.Combine(tempRoot, "apply-update.ps1");
        var exePath = Environment.ProcessPath ?? Path.Combine(appDir, "Jarvis.exe");
        File.WriteAllText(scriptPath, BuildUpdateScript(appDir, extractDir, exePath));

        progress(new UpdateProgress("Перезапускаю Джарвиса для установки обновления."));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Pid {Environment.ProcessId}",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string BuildUpdateScript(string appDir, string extractDir, string exePath) =>
        $$"""
        param([int]$Pid)
        try {
          Wait-Process -Id $Pid -Timeout 30 -ErrorAction SilentlyContinue
        } catch {}

        $source = '{{extractDir}}'
        $target = '{{appDir}}'

        Get-ChildItem -Path $source -Force | ForEach-Object {
          Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
        }

        Start-Process -FilePath '{{exePath}}'
        """;

    private static string ToDisplayVersion(Version version) =>
        version.Build > 0 ? version.ToString(3) : version.ToString();
}

public sealed record UpdateManifest(
    string Version,
    string PackageUrl,
    string? Sha256,
    string? Notes);

public sealed class UpdateCheckResult
{
    private UpdateCheckResult(bool enabled, bool hasUpdate, string? currentVersion, UpdateManifest? manifest, string message)
    {
        Enabled = enabled;
        HasUpdate = hasUpdate;
        CurrentVersion = currentVersion;
        Manifest = manifest;
        Message = message;
    }

    public bool Enabled { get; }
    public bool HasUpdate { get; }
    public string? CurrentVersion { get; }
    public UpdateManifest? Manifest { get; }
    public string Message { get; }

    public static UpdateCheckResult Disabled() =>
        new(false, false, null, null, "Online updates выключены: update-settings.json не содержит ManifestUrl.");

    public static UpdateCheckResult UpToDate(string current, string latest) =>
        new(true, false, current, null, $"Версия актуальна: {current}. Последняя: {latest}.");

    public static UpdateCheckResult Available(string current, UpdateManifest manifest) =>
        new(true, true, current, manifest, $"Доступна версия {manifest.Version}. Текущая: {current}.");
}
