using System.IO;
using System.Net.Http;
using NAudio.Wave;

namespace Jarvis;

public sealed class GoogleTranslateTtsSynthesizer : ISpeechSynthesizer
{
    private const int MaxTextLength = 180;
    private readonly AppSettings _settings = AppSettings.Load();

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
                if (mp3.Length == 0)
                {
                    throw new InvalidOperationException("Google Translate TTS вернул пустой MP3.");
                }
                PlayMp3(mp3, _settings.VoiceStyle);
            }
        }
        catch (Exception ex)
        {
            TtsLog.Write("GoogleTranslate", $"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            SpeakFinished?.Invoke();
        }
    }

    private static async Task<byte[]> SynthesizeAsync(string text)
    {
        var url = "https://translate.google.com/translate_tts" +
                  "?ie=UTF-8&client=tw-ob&tl=ru&ttsspeed=0.85&q=" +
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

    private static void PlayMp3(byte[] mp3, string voiceStyle)
    {
        TtsLog.Write("GoogleTranslate", $"PlayMp3: bytes={mp3.Length}, style={voiceStyle}");
        if (mp3.Length == 0)
        {
            TtsLog.Write("GoogleTranslate", "Пустой MP3 — пропускаю воспроизведение.");
            return;
        }

        using var ms = new MemoryStream(mp3);
        Mp3FileReader reader;
        try
        {
            reader = new Mp3FileReader(ms);
        }
        catch (Exception ex)
        {
            TtsLog.Write("GoogleTranslate", $"Mp3FileReader не открылся: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        TtsLog.Write("GoogleTranslate",
            $"MP3 OK: duration={reader.TotalTime.TotalMilliseconds:F0}ms, " +
            $"sr={reader.WaveFormat.SampleRate}, ch={reader.WaveFormat.Channels}");

        using (reader)
        using (var output = new WaveOutEvent())
        {
            // Эксперимент с «эффектом Джарвиса» (бит-краш + металлическое эхо)
            // часто превращает короткие MP3 от Google в неразборчивую кашу.
            // По умолчанию воспроизводим как есть; включается только если в settings.json
            // явно включён JarvisVoiceEffect = true.
            output.Init(reader);
            output.Play();

            var started = DateTime.Now;
            while (output.PlaybackState == PlaybackState.Playing ||
                   DateTime.Now - started < TimeSpan.FromMilliseconds(250))
            {
                Thread.Sleep(50);
            }
            TtsLog.Write("GoogleTranslate",
                $"Playback finished after {(DateTime.Now - started).TotalMilliseconds:F0} ms.");
        }
    }

    public void Dispose() { }
}

internal sealed class JarvisVoiceSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _delay;
    private int _delayIndex;
    private double _phase;

    public JarvisVoiceSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        _delay = new float[Math.Max(1, WaveFormat.SampleRate / 28 * WaveFormat.Channels)];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var channels = WaveFormat.Channels;

        for (var i = 0; i < read; i++)
        {
            var sample = buffer[offset + i];
            var metallic = 0.82f + 0.18f * (float)Math.Sin(_phase);
            _phase += 2.0 * Math.PI * 55.0 / WaveFormat.SampleRate;
            if (_phase > Math.PI * 2) _phase -= Math.PI * 2;

            var echo = _delay[_delayIndex] * 0.28f;
            var crushed = (float)Math.Round(sample * 28f) / 28f;
            var processed = Math.Clamp(crushed * metallic + echo, -1f, 1f);
            _delay[_delayIndex] = processed;
            _delayIndex = (_delayIndex + 1) % _delay.Length;

            // Keep stereo channels aligned by applying the same light digital character per sample.
            if (channels > 0)
            {
                buffer[offset + i] = processed;
            }
        }

        return read;
    }
}
