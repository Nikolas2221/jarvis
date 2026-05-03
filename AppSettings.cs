using System.IO;
using System.Text.Json;

namespace Jarvis;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string UserName { get; set; } = "сэр";
    public string[] WakeWords { get; set; } = ["джарвис", "jarvis"];
    public int ConversationSeconds { get; set; } = 15;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string OpenAiModel { get; set; } = "gpt-4.1-mini";
    public string VoiceStyle { get; set; } = "jarvis";
    public string VoiceProvider { get; set; } = "auto";
    public string XttsUrl { get; set; } = "http://127.0.0.1:8765";

    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(Path))
        {
            var settings = new AppSettings();
            settings.Save();
            return settings;
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save() => File.WriteAllText(Path, JsonSerializer.Serialize(this, JsonOptions));
}
