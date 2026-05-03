using System.IO;

namespace Jarvis;

/// <summary>
/// Простое логирование TTS-событий в %TEMP%\JarvisTTS.log,
/// чтобы пользователь мог увидеть причину молчания провайдеров.
/// </summary>
internal static class TtsLog
{
    private static readonly object Lock = new();
    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "JarvisTTS.log");

    public static void Write(string source, string message)
    {
        try
        {
            lock (Lock)
            {
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // никогда не падаем на логировании
        }
    }
}
