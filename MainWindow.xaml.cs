using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Jarvis;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTime _startedAt = DateTime.Now;
    private CancellationTokenSource? _cts;
    private MicrophoneRecorder? _recorder;
    private ISpeechSynthesizer? _tts;
    private Task? _worker;
    private bool _fullscreen;

    public MainWindow()
    {
        InitializeComponent();
        Console.OutputEncoding = Encoding.UTF8;

        _clock.Tick += (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            UptimeText.Text = $"UPTIME: {DateTime.Now - _startedAt:hh\\:mm\\:ss}";
        };
        _clock.Start();

        Loaded += (_, _) => StartJarvis();
        Closing += (_, _) => StopJarvis();
    }

    private void StartJarvis()
    {
        if (_worker != null) return;

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunJarvisAsync(_cts.Token));
    }

    private async Task RunJarvisAsync(CancellationToken ct)
    {
        try
        {
            SetStatus("Запуск", "Подключаю голосовые модули.");

            _tts = new UiSpeechSynthesizer(
                SpeechSynthesizerFactory.Create(),
                text => AddAssistant(text));

            var recognizer = new GoogleSpeechRecognizer();
            var ai = CreateAiAssistant();
            var commands = new CommandHandler(_tts, ai);
            _recorder = new MicrophoneRecorder();

            _tts.SpeakStarted += () =>
            {
                _recorder?.Mute();
                SetStatus("Говорю", "Микрофон временно приглушён.");
            };
            _tts.SpeakFinished += () =>
            {
                _recorder?.Unmute();
                SetStatus("Слушаю", "Готов к голосовой команде.");
            };

            _recorder.Start();
            AddLog("SYSTEM", "Микрофон активен.");
            _tts.Speak("Джарвис в сети. Готов к работе.");

            await foreach (var audio in _recorder.Utterances.ReadAllAsync(ct))
            {
                SetStatus("Распознаю", "Отправляю фразу на распознавание.");

                string? text;
                try
                {
                    text = await recognizer.RecognizeAsync(audio, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AddLog("ASR", ex.Message);
                    SetStatus("Слушаю", "Не удалось распознать фразу.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    AddLog("ASR", "Фраза не распознана.");
                    SetStatus("Слушаю", "Жду следующую команду.");
                    continue;
                }

                AddUser(text);

                var commandText = ExtractCommand(text);
                if (commandText == null)
                {
                    AddLog("WAKE", "Фраза проигнорирована: нет обращения к Джарвису.");
                    SetStatus("Слушаю", "Скажите 'Джарвис' перед командой.");
                    continue;
                }

                if (commandText.Length == 0)
                {
                    _tts.Speak("Слушаю, сэр.");
                    continue;
                }

                SetStatus("Выполняю", "Проверяю команду.");

                if (!await commands.HandleAsync(commandText, ct))
                {
                    Dispatcher.Invoke(Close);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("SYSTEM", "Остановлено.");
        }
        catch (Exception ex)
        {
            AddLog("ERROR", ex.Message);
            SetStatus("Ошибка", "Проверьте журнал событий.");
        }
        finally
        {
            _recorder?.Stop();
            _recorder?.Dispose();
            _tts?.Dispose();
        }
    }

    private void StopJarvis()
    {
        _cts?.Cancel();
        _recorder?.Stop();
    }

    private void AddUser(string text)
    {
        Dispatcher.Invoke(() =>
        {
            LastUserText.Text = text;
            AddLogCore("YOU", text);
        });
    }

    private void AddAssistant(string text)
    {
        Dispatcher.Invoke(() =>
        {
            LastAssistantText.Text = text;
            AddLogCore("JARVIS", text);
        });
    }

    private void AddLog(string source, string text)
    {
        Dispatcher.Invoke(() => AddLogCore(source, text));
    }

    private void AddLogCore(string source, string text)
    {
        LogList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} [{source}] {text}");
        while (LogList.Items.Count > 80)
        {
            LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }
    }

    private void SetStatus(string status, string backend)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = status;
            BackendText.Text = backend;
        });
    }

    private OpenAiAssistant? CreateAiAssistant()
    {
        try
        {
            var ai = new OpenAiAssistant();
            AddLog("GPT", "OpenAI API подключён.");
            return ai;
        }
        catch (Exception ex)
        {
            AddLog("GPT", ex.Message);
            return null;
        }
    }

    private static string? ExtractCommand(string text)
    {
        var normalized = text.Trim();
        var lower = normalized.ToLowerInvariant();
        var wakeWords = new[] { "джарвис", "jarvis" };

        foreach (var wake in wakeWords)
        {
            var index = lower.IndexOf(wake, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            var command = normalized.Remove(index, wake.Length);
            return command.Trim(' ', ',', '.', '!', '?', ':', ';', '-');
        }

        if (lower.Contains("выход", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("пока", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return null;
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void TopmostButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        AddLogCore("UI", Topmost ? "Режим поверх окон включён." : "Режим поверх окон выключен.");
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        _fullscreen = !_fullscreen;
        WindowStyle = _fullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        WindowState = _fullscreen ? WindowState.Maximized : WindowState.Normal;
        AddLogCore("UI", _fullscreen ? "Полноэкранный режим включён." : "Полноэкранный режим выключен.");
    }

    private void StopButton_Click(object sender, RoutedEventArgs e) => Close();
}
