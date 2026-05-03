namespace Jarvis;

internal static class SpeechSynthesizerFactory
{
    public static ISpeechSynthesizer Create()
    {
        var settings = AppSettings.Load();
        if (settings.VoiceProvider.Equals("xtts", StringComparison.OrdinalIgnoreCase) ||
            settings.VoiceProvider.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var fallback = CreateBuiltIn(settings);
            return new XttsSpeechSynthesizer(settings, fallback);
        }

        return CreateBuiltIn(settings);
    }

    private static ISpeechSynthesizer CreateBuiltIn(AppSettings settings)
    {
        if (settings.VoiceStyle.Equals("jarvis", StringComparison.OrdinalIgnoreCase))
        {
            // Нейронный мужской русский голос Microsoft Edge (Dmitry Neural) —
            // звучит близко к актёрской озвучке, никаких локальных серверов не нужно.
            return new EdgeTtsSynthesizer();
        }

        try
        {
            var windowsTts = new WindowsSpeechSynthesizer();
            if (windowsTts.HasRussianVoice)
            {
                return windowsTts;
            }

            windowsTts.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS] Windows TTS недоступен: {ex.Message}");
        }

        return new EdgeTtsSynthesizer();
    }
}
