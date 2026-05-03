namespace Jarvis;

public interface ISpeechSynthesizer : IDisposable
{
    void Speak(string text);
    event Action? SpeakStarted;
    event Action? SpeakFinished;
}
