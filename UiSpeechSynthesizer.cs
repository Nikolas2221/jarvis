namespace Jarvis;

public sealed class UiSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly ISpeechSynthesizer _inner;
    private readonly Action<string> _onSpeech;

    public UiSpeechSynthesizer(ISpeechSynthesizer inner, Action<string> onSpeech)
    {
        _inner = inner;
        _onSpeech = onSpeech;
        _inner.SpeakStarted += () => SpeakStarted?.Invoke();
        _inner.SpeakFinished += () => SpeakFinished?.Invoke();
    }

    public event Action? SpeakStarted;
    public event Action? SpeakFinished;

    public void Speak(string text)
    {
        _onSpeech(text);
        _inner.Speak(text);
    }

    public void Dispose() => _inner.Dispose();
}
