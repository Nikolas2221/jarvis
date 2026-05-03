using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using NAudio.Wave;

namespace Jarvis;

public sealed class XttsSpeechSynthesizer : ISpeechSynthesizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly ISpeechSynthesizer _fallback;
    private readonly string _speakUrl;

    public XttsSpeechSynthesizer(AppSettings settings, ISpeechSynthesizer fallback)
    {
        _fallback = fallback;
        _speakUrl = settings.XttsUrl.TrimEnd('/') + "/speak";
        _fallback.SpeakStarted += () => SpeakStarted?.Invoke();
        _fallback.SpeakFinished += () => SpeakFinished?.Invoke();
    }

    public event Action? SpeakStarted;
    public event Action? SpeakFinished;

    public void Speak(string text)
    {
        SpeakStarted?.Invoke();
        try
        {
            var wav = SynthesizeAsync(text).GetAwaiter().GetResult();
            PlayWav(wav);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XTTS] {ex.Message}");
            SpeakFinished?.Invoke();
            _fallback.Speak(text);
            return;
        }

        SpeakFinished?.Invoke();
    }

    private async Task<byte[]> SynthesizeAsync(string text)
    {
        var json = JsonSerializer.Serialize(new { text }, JsonOptions);
        using var response = await _http.PostAsync(_speakUrl, new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static void PlayWav(byte[] wav)
    {
        using var ms = new MemoryStream(wav);
        using var reader = new WaveFileReader(ms);
        using var output = new WaveOutEvent();
        output.Init(reader);
        output.Play();
        while (output.PlaybackState == PlaybackState.Playing)
        {
            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _fallback.Dispose();
    }
}
