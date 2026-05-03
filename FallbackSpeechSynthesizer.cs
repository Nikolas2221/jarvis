namespace Jarvis;

/// <summary>
/// Цепочка TTS-провайдеров: пробуем активный; если Speak() бросает —
/// диспоузим, переключаемся на следующий и больше к павшему не возвращаемся.
/// </summary>
public sealed class FallbackSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly List<(string Name, Func<ISpeechSynthesizer> Factory)> _backends;
    private ISpeechSynthesizer? _active;
    private int _index = -1;

    public event Action? SpeakStarted;
    public event Action? SpeakFinished;

    public FallbackSpeechSynthesizer(params (string Name, Func<ISpeechSynthesizer> Factory)[] backends)
    {
        _backends = backends.ToList();
        EnsureActive();
    }

    private bool EnsureActive()
    {
        while (_active == null && _index + 1 < _backends.Count)
        {
            _index++;
            var (name, factory) = _backends[_index];
            try
            {
                var instance = factory();
                instance.SpeakStarted += OnInnerStarted;
                instance.SpeakFinished += OnInnerFinished;
                _active = instance;
                TtsLog.Write("Fallback", $"Активный backend: {name}");
                return true;
            }
            catch (Exception ex)
            {
                TtsLog.Write("Fallback", $"Не создался {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return _active != null;
    }

    private void OnInnerStarted() => SpeakStarted?.Invoke();
    private void OnInnerFinished() => SpeakFinished?.Invoke();

    public void Speak(string text)
    {
        for (var attempt = 0; attempt < _backends.Count; attempt++)
        {
            if (!EnsureActive())
            {
                TtsLog.Write("Fallback", "Все backend'ы недоступны — фраза не озвучена.");
                return;
            }

            var name = _backends[_index].Name;
            try
            {
                _active!.Speak(text);
                return;
            }
            catch (Exception ex)
            {
                TtsLog.Write("Fallback", $"Backend {name} упал: {ex.GetType().Name}: {ex.Message}. Переключаюсь.");
                try { _active!.Dispose(); } catch { /* ignore */ }
                _active = null;
            }
        }

        TtsLog.Write("Fallback", "Перебрал все backend'ы, ни один не сработал.");
    }

    public void Dispose() => _active?.Dispose();
}
