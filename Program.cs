namespace Jarvis;

internal static class SpeechSynthesizerFactory
{
    public static ISpeechSynthesizer Create()
    {
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

        return new GoogleTranslateTtsSynthesizer();
    }
}
