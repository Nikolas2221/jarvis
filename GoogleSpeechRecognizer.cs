using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;

namespace Jarvis;

public sealed class GoogleSpeechRecognizer : ISpeechRecognizer
{
    private const string ApiKey = "AIzaSyBOti4mM-6x9WDnZIjIeyEU21OpBXqWBgw";
    private const string Language = "ru-RU";
    private const int SampleRate = 16000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<string?> RecognizeAsync(byte[] pcm16Mono16k, CancellationToken ct)
    {
        var url = $"https://www.google.com/speech-api/v2/recognize" +
                  $"?output=json&lang={Language}&key={ApiKey}&client=chromium";

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new ByteArrayContent(pcm16Mono16k);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/l16")
        {
            Parameters = { new NameValueHeaderValue("rate", SampleRate.ToString()) }
        };

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"[Google] HTTP {(int)resp.StatusCode}");
            return null;
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        return ParseTranscript(body);
    }

    private static string? ParseTranscript(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("result", out var result)) continue;
                if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0) continue;

                var alt = result[0].GetProperty("alternative");
                if (alt.GetArrayLength() == 0) continue;

                return alt[0].GetProperty("transcript").GetString();
            }
            catch (JsonException) { }
        }
        return null;
    }
}
