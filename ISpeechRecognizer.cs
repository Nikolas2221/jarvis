namespace Jarvis;

public interface ISpeechRecognizer
{
    Task<string?> RecognizeAsync(byte[] pcm16Mono16k, CancellationToken ct);
}
