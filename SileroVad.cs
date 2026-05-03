using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Jarvis;

/// <summary>
/// Обёртка над Silero VAD v5 (ONNX). Принимает кадр ровно из <see cref="WindowSize"/>
/// сэмплов 16 kHz mono PCM (short) и возвращает вероятность речи 0..1.
/// </summary>
public sealed class SileroVad : IDisposable
{
    /// <summary>Silero v5 для 16 kHz требует ровно 512 сэмплов на кадр (32 мс).</summary>
    public const int WindowSize = 512;
    public const int SampleRate = 16000;

    private const int StateRows = 2;
    private const int StateCols = 128;

    private readonly InferenceSession _session;
    private float[] _state = new float[StateRows * 1 * StateCols];
    private readonly long[] _srData = { SampleRate };
    private readonly int[] _stateShape = { StateRows, 1, StateCols };
    private readonly int[] _inputShape = { 1, WindowSize };
    private readonly int[] _srShape = { 1 };
    private readonly float[] _inputBuffer = new float[WindowSize];

    private SileroVad(InferenceSession session)
    {
        _session = session;
    }

    public static SileroVad Load(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException($"Silero VAD model not found: {modelPath}");
        }

        var options = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        return new SileroVad(new InferenceSession(modelPath, options));
    }

    /// <summary>Сбрасывает LSTM-состояние между фразами.</summary>
    public void Reset()
    {
        Array.Clear(_state, 0, _state.Length);
    }

    /// <summary>
    /// Вычисляет вероятность речи на одном кадре в 512 сэмплов.
    /// </summary>
    public float Probability(ReadOnlySpan<short> samples)
    {
        if (samples.Length != WindowSize)
        {
            throw new ArgumentException(
                $"Silero v5 expects exactly {WindowSize} samples per frame at 16 kHz, got {samples.Length}",
                nameof(samples));
        }

        for (int i = 0; i < WindowSize; i++)
        {
            _inputBuffer[i] = samples[i] / 32768f;
        }

        var inputTensor = new DenseTensor<float>(_inputBuffer.AsMemory(), _inputShape);
        var stateTensor = new DenseTensor<float>(_state.AsMemory(), _stateShape);
        var srTensor = new DenseTensor<long>(_srData.AsMemory(), _srShape);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", srTensor)
        };

        using var results = _session.Run(inputs);
        float prob = 0f;

        foreach (var r in results)
        {
            if (r.Name == "stateN" || r.Name == "state")
            {
                var newState = r.AsTensor<float>().ToArray();
                Array.Copy(newState, _state, Math.Min(newState.Length, _state.Length));
            }
            else if (r.Name == "output")
            {
                var arr = r.AsTensor<float>().ToArray();
                if (arr.Length > 0) prob = arr[0];
            }
        }

        return prob;
    }

    public void Dispose() => _session.Dispose();
}
