using System.Speech.Synthesis;

namespace Jarvis;

public sealed class WindowsSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly SpeechSynthesizer _synth = new();

    public bool HasRussianVoice { get; private set; }

    public event Action? SpeakStarted;
    public event Action? SpeakFinished;

    public WindowsSpeechSynthesizer()
    {
        _synth.SetOutputToDefaultAudioDevice();
        _synth.Rate = 1;
        _synth.Volume = 100;

        var ru = _synth.GetInstalledVoices()
            .Where(v => v.Enabled)
            .FirstOrDefault(v => v.VoiceInfo.Culture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase));

        if (ru != null)
        {
            _synth.SelectVoice(ru.VoiceInfo.Name);
            HasRussianVoice = true;
            Console.WriteLine($"[TTS] Голос: {ru.VoiceInfo.Name}");
        }
        else
        {
            Console.WriteLine("[TTS] Русский голос Windows не найден. Переключаюсь на онлайн TTS.");
        }
    }

    public void Speak(string text)
    {
        Console.WriteLine($"[Джарвис]: {text}");
        SpeakStarted?.Invoke();
        try
        {
            _synth.Speak(text);
        }
        finally
        {
            SpeakFinished?.Invoke();
        }
    }

    public void Dispose() => _synth.Dispose();
}
