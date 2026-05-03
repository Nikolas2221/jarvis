using System.Threading.Channels;
using NAudio.Wave;

namespace Jarvis;

public sealed class MicrophoneRecorder : IDisposable
{
    private const int SampleRate = 16000;
    private const int Channels = 1;
    private const int BitsPerSample = 16;
    private const int BufferMs = 100;
    private const int SilenceMsBeforeStop = 1000;
    private const int MinUtteranceMs = 300;
    private const int MaxUtteranceMs = 15000;
    private const int PreRollChunks = 2;
    private const double RmsThreshold = 0.020;

    private readonly Channel<byte[]> _utterances = Channel.CreateUnbounded<byte[]>();
    public ChannelReader<byte[]> Utterances => _utterances.Reader;

    private WaveInEvent? _waveIn;
    private readonly List<byte> _current = new();
    private readonly Queue<byte[]> _preRoll = new();
    private bool _capturing;
    private bool _muted;
    private int _silentMs;
    private int _capturedMs;

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
        var rms = Rms(chunk);
        var hasSpeech = rms > RmsThreshold;

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

    private void ResetState()
    {
        _capturing = false;
        _silentMs = 0;
        _capturedMs = 0;
        _current.Clear();
        _preRoll.Clear();
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

    public void Dispose() => _waveIn?.Dispose();
}
