using System.IO;
using System.Threading.Channels;
using NAudio.Wave;

namespace Jarvis;

/// <summary>
/// Запись с микрофона с интеллектуальной детекцией речи через Silero VAD.
/// Если ONNX-модель недоступна — деградирует до RMS-порога.
/// </summary>
public sealed class MicrophoneRecorder : IDisposable
{
    private const int SampleRate = SileroVad.SampleRate;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int BufferMs = 96; // ≈ 3 окна Silero (3 × 32 мс) — кратно 512 сэмплам
    private const int SilenceMsBeforeStop = 800;
    private const int MinUtteranceMs = 300;
    private const int MaxUtteranceMs = 15000;
    private const int PreRollChunks = 3;

    private const float VadStartThreshold = 0.55f;
    private const float VadStopThreshold = 0.35f;
    private const double RmsThreshold = 0.020;

    private readonly Channel<byte[]> _utterances = Channel.CreateUnbounded<byte[]>();
    public ChannelReader<byte[]> Utterances => _utterances.Reader;

    private readonly SileroVad? _vad;
    private readonly List<short> _vadBuffer = new(SileroVad.WindowSize * 4);

    private WaveInEvent? _waveIn;
    private readonly List<byte> _current = new();
    private readonly Queue<byte[]> _preRoll = new();
    private bool _capturing;
    private bool _muted;
    private int _silentMs;
    private int _capturedMs;

    public MicrophoneRecorder()
    {
        var modelPath = Path.Combine(AppContext.BaseDirectory, "Assets", "silero_vad.onnx");
        try
        {
            _vad = SileroVad.Load(modelPath);
            Console.WriteLine("[VAD] Silero ONNX загружен.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VAD] Silero не загружен ({ex.Message}). Откат на RMS.");
            _vad = null;
        }
    }

    public void Mute() => _muted = true;
    public void Unmute()
    {
        ResetState();
        _muted = false;
    }

    public void Start()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            BufferMilliseconds = BufferMs
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.StartRecording();
    }

    public void Stop()
    {
        _waveIn?.StopRecording();
        _utterances.Writer.TryComplete();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_muted) return;

        var chunk = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, chunk, e.BytesRecorded);
        var hasSpeech = DetectSpeech(chunk);

        if (!_capturing)
        {
            _preRoll.Enqueue(chunk);
            while (_preRoll.Count > PreRollChunks) _preRoll.Dequeue();

            if (hasSpeech)
            {
                _capturing = true;
                _silentMs = 0;
                _capturedMs = 0;
                _current.Clear();
                foreach (var pre in _preRoll) _current.AddRange(pre);
                _current.AddRange(chunk);
                _capturedMs += BufferMs;
            }
        }
        else
        {
            _current.AddRange(chunk);
            _capturedMs += BufferMs;
            _silentMs = hasSpeech ? 0 : _silentMs + BufferMs;

            var stop = _silentMs >= SilenceMsBeforeStop || _capturedMs >= MaxUtteranceMs;
            if (!stop) return;

            if (_capturedMs >= MinUtteranceMs)
            {
                _utterances.Writer.TryWrite(_current.ToArray());
            }
            ResetState();
        }
    }

    private bool DetectSpeech(byte[] pcm16)
    {
        if (_vad == null) return Rms(pcm16) > RmsThreshold;

        // Перекладываем PCM16 -> short[] и докидываем в скользящий буфер кадров VAD.
        int sampleCount = pcm16.Length / 2;
        for (int i = 0, j = 0; i < pcm16.Length; i += 2, j++)
        {
            _vadBuffer.Add((short)(pcm16[i] | (pcm16[i + 1] << 8)));
        }

        // Прогоняем все полные окна по 512 сэмплов; берём максимум вероятности по чанку.
        float maxProb = 0f;
        while (_vadBuffer.Count >= SileroVad.WindowSize)
        {
            var window = new short[SileroVad.WindowSize];
            for (int i = 0; i < SileroVad.WindowSize; i++) window[i] = _vadBuffer[i];
            _vadBuffer.RemoveRange(0, SileroVad.WindowSize);

            var p = _vad.Probability(window);
            if (p > maxProb) maxProb = p;
        }

        // Гистерезис: пока не записываем — высокий порог; уже пишем — низкий.
        return _capturing ? maxProb > VadStopThreshold : maxProb > VadStartThreshold;
    }

    private void ResetState()
    {
        _capturing = false;
        _silentMs = 0;
        _capturedMs = 0;
        _current.Clear();
        _preRoll.Clear();
        _vadBuffer.Clear();
        _vad?.Reset();
    }

    private static double Rms(byte[] pcm16)
    {
        var sampleCount = pcm16.Length / 2;
        if (sampleCount == 0) return 0;
        long sumSquares = 0;
        for (var i = 0; i < pcm16.Length; i += 2)
        {
            short sample = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            sumSquares += sample * sample;
        }
        return Math.Sqrt(sumSquares / (double)sampleCount) / 32768.0;
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _vad?.Dispose();
    }
}
