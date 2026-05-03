using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Jarvis;

public sealed class AppLauncher
{
    private readonly Dictionary<string, string> _apps;

    public AppLauncher()
    {
        _apps = LoadApps();
    }

    public bool TryLaunchFromCommand(string text, ISpeechSynthesizer tts)
    {
        var normalized = text.ToLowerInvariant();
        if (!normalized.Contains("открой") && !normalized.Contains("запусти"))
        {
            return false;
        }

        foreach (var app in _apps)
        {
            if (!normalized.Contains(app.Key, StringComparison.OrdinalIgnoreCase)) continue;

            Process.Start(new ProcessStartInfo
            {
                FileName = app.Value,
                UseShellExecute = true
            });
            tts.Speak($"Открываю {app.Key}.");
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> LoadApps()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "apps.json");
        if (!File.Exists(path))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "apps.example.json"), path, overwrite: false);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? [];
        }
        catch
        {
            return [];
        }
    }
}
