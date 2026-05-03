using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using NAudio.Wave;

namespace Jarvis;

/// <summary>
/// TTS через бесплатный endpoint Microsoft Edge Read-Aloud (онлайн, нейронные голоса).
/// </summary>
public sealed class EdgeTtsSynthesizer : ISpeechSynthesizer
{
    private const string TrustedToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    private const string Voice = "ru-RU-DmitryNeural";
    private const string Rate = "+0%";
    private const string Pitch = "+0Hz";
    private const string Volume = "+0%";

    public event Action? SpeakStarted;
    public event Action? SpeakFinished;

    public void Speak(string text)
    {
        Console.WriteLine($"[Джарвис]: {text}");
        SpeakStarted?.Invoke();
        try
        {
            var mp3 = SynthesizeAsync(text).GetAwaiter().GetResult();
            if (mp3.Length == 0)
            {
                TtsLog.Write("EdgeTTS", "Получено 0 байт MP3 — отдаю наверх ошибку для fallback.");
                throw new InvalidOperationException("Edge TTS вернул пустой аудио-поток.");
            }
            PlayMp3(mp3);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            TtsLog.Write("EdgeTTS", $"{ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            SpeakFinished?.Invoke();
        }
    }

    private static async Task<byte[]> SynthesizeAsync(string text)
    {
        var connectionId = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var sec = GenerateSecMsGec();
        var url = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1" +
                  $"?TrustedClientToken={TrustedToken}" +
                  $"&Sec-MS-GEC={sec}" +
                  "&Sec-MS-GEC-Version=1-131.0.2903.86" +
                  $"&ConnectionId={connectionId}";

        TtsLog.Write("EdgeTTS", $"connect: sec={sec[..16]}..., conn={connectionId[..16]}...");

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        ws.Options.SetRequestHeader(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");

        // Короткий таймаут — если эндпоинт блокирован/не отвечает, быстрее идём в fallback.
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await ws.ConnectAsync(new Uri(url), connectCts.Token);

        var configMsg =
            $"X-Timestamp:{DateTimeOffset.UtcNow:O}\r\n" +
            "Content-Type:application/json; charset=utf-8\r\n" +
            "Path:speech.config\r\n\r\n" +
            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":" +
            "{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"}," +
            "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
        await SendText(ws, configMsg);

        var requestId = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var ssmlMsg =
            $"X-RequestId:{requestId}\r\n" +
            "Content-Type:application/ssml+xml\r\n" +
            $"X-Timestamp:{DateTimeOffset.UtcNow:O}\r\n" +
            "Path:ssml\r\n\r\n" + BuildSsml(text);
        await SendText(ws, ssmlMsg);

        var audio = new MemoryStream();
        var buffer = new byte[8192];
        using var recvCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        while (true)
        {
            using var msgStream = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, recvCts.Token);
                msgStream.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close) break;

            var data = msgStream.ToArray();

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var msg = Encoding.UTF8.GetString(data);
                if (msg.Contains("Path:turn.end")) break;
            }
            else if (result.MessageType == WebSocketMessageType.Binary && data.Length >= 2)
            {
                int headerLen = (data[0] << 8) | data[1];
                int audioStart = 2 + headerLen;
                if (data.Length > audioStart)
                {
                    audio.Write(data, audioStart, data.Length - audioStart);
                }
            }
        }

        try
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        catch { /* ignore close errors */ }

        return audio.ToArray();
    }

    private static Task SendText(ClientWebSocket ws, string msg) =>
        ws.SendAsync(
            Encoding.UTF8.GetBytes(msg),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

    private static string BuildSsml(string text)
    {
        var escaped = SecurityElement.Escape(text) ?? text;
        return "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='ru-RU'>" +
               $"<voice name='{Voice}'>" +
               $"<prosody pitch='{Pitch}' rate='{Rate}' volume='{Volume}'>{escaped}</prosody>" +
               "</voice></speak>";
    }

    private static string GenerateSecMsGec()
    {
        // Алгоритм должен битово совпадать с python edge-tts:
        //   ticks_seconds = unix_seconds + WIN_EPOCH (= 11644473600, секунды между 1601-01-01 и 1970-01-01)
        //   ticks_seconds -= ticks_seconds % 300            # round down to 5 min
        //   ticks *= 1e7                                    # float умножение с потерей точности
        //   sha256(f"{ticks:.0f}{TRUSTED_TOKEN}")
        // Точная integer-математика DateTime.ToFileTimeUtc даёт ДРУГОЙ хэш (чему рад только сервер 403).
        const long WinEpochSeconds = 11644473600L;
        long unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long roundedSeconds = (unixSeconds + WinEpochSeconds) - ((unixSeconds + WinEpochSeconds) % 300L);

        double ticksFloat = (double)roundedSeconds * 1e7;
        string ticksStr = ticksFloat.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);

        var input = ticksStr + TrustedToken;
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(input));
        return Convert.ToHexString(hash);
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
