namespace Jarvis;

internal static class SpeechSynthesizerFactory
{
    public static ISpeechSynthesizer Create()
    {
        var windowsTts = new WindowsSpeechSynthesizer();
        if (windowsTts.HasRussianVoice)
        {
            return windowsTts;
        }

        windowsTts.Dispose();
        return new GoogleTranslateTtsSynthesizer();
    }
}
