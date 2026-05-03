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
            // Цепочка: Edge (нейронный Dmitry) → Google Translate (всегда работает)
            // → Windows TTS (если стоит русский голос) → как последняя инстанция всё равно Google.
            return new FallbackSpeechSynthesizer(
                ("EdgeTTS", () => new EdgeTtsSynthesizer()),
                ("GoogleTranslateTTS", () => new GoogleTranslateTtsSynthesizer()),
                ("WindowsTTS", MakeWindowsTtsOrThrow));
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

        return new FallbackSpeechSynthesizer(
            ("EdgeTTS", () => new EdgeTtsSynthesizer()),
            ("GoogleTranslateTTS", () => new GoogleTranslateTtsSynthesizer()));
    }

    private static ISpeechSynthesizer MakeWindowsTtsOrThrow()
    {
        var w = new WindowsSpeechSynthesizer();
        if (!w.HasRussianVoice)
        {
            w.Dispose();
            throw new InvalidOperationException("Windows TTS: русский голос не установлен.");
        }
        return w;
    }
}
