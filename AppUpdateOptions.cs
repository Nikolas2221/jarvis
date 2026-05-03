using System.IO;
using System.Text.Json;

namespace Jarvis;

public sealed class AppUpdateOptions
{
    public string? ManifestUrl { get; set; }

    public static AppUpdateOptions Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "update-settings.json");
        if (!File.Exists(path))
        {
            return new AppUpdateOptions();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppUpdateOptions>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? new AppUpdateOptions();
        }
        catch
        {
            return new AppUpdateOptions();
        }
    }
}
