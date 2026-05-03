using System.IO;
using System.Net.Http;
using NAudio.Wave;

namespace Jarvis;

public sealed class GoogleTranslateTtsSynthesizer : ISpeechSynthesizer
{
    private const int MaxTextLength = 180;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static GoogleTranslateTtsSynthesizer()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    }

    public event Action? SpeakStarted;
    public event Action? SpeakFinished;

    public void Speak(string text)
    {
        Console.WriteLine($"[Джарвис]: {text}");
        SpeakStarted?.Invoke();
        try
        {
            foreach (var part in SplitText(text))
            {
                var mp3 = SynthesizeAsync(part).GetAwaiter().GetResult();
                PlayMp3(mp3);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Online TTS] Ошибка: {ex.Message}");
        }
        finally
        {
            SpeakFinished?.Invoke();
        }
    }

    private static async Task<byte[]> SynthesizeAsync(string text)
    {
        var url = "https://translate.google.com/translate_tts" +
                  "?ie=UTF-8&client=tw-ob&tl=ru&q=" +
                  Uri.EscapeDataString(text);

        return await Http.GetByteArrayAsync(url);
    }

    private static IEnumerable<string> SplitText(string text)
    {
        var remaining = text.Trim();
        while (remaining.Length > MaxTextLength)
        {
            var splitAt = remaining.LastIndexOfAny(['.', ',', '!', '?', ';', ':', ' '], MaxTextLength);
            if (splitAt <= 0) splitAt = MaxTextLength;

            yield return remaining[..splitAt].Trim();
            remaining = remaining[splitAt..].Trim();
        }

        if (remaining.Length > 0)
        {
            yield return remaining;
        }
    }

    private static void PlayMp3(byte[] mp3)
    {
        using var ms = new MemoryStream(mp3);
        using var reader = new Mp3FileReader(ms);
        using var output = new WaveOutEvent();
        output.Init(reader);
        output.Play();
        while (output.PlaybackState == PlaybackState.Playing)
        {
            Thread.Sleep(50);
        }
    }

    public void Dispose() { }
}
