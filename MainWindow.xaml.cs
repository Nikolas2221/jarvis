using System.Text;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Jarvis;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTime _startedAt = DateTime.Now;
    private CancellationTokenSource? _cts;
    private MicrophoneRecorder? _recorder;
    private ISpeechSynthesizer? _tts;
    private Task? _worker;
    private Forms.NotifyIcon? _trayIcon;
    private AppSettings _settings = new();
    private DateTime _conversationUntil;
    private bool _fullscreen;
    private bool _forceExit;

    public MainWindow()
    {
        InitializeComponent();
        Console.OutputEncoding = Encoding.UTF8;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/Jarvis.ico"));
        _settings = AppSettings.Load();
        Environment.SetEnvironmentVariable("OPENAI_MODEL", _settings.OpenAiModel);
        SetupTray();
        ApplySettingsToUi();

        _clock.Tick += (_, _) =>
        {
            ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
            UptimeText.Text = $"UPTIME: {DateTime.Now - _startedAt:hh\\:mm\\:ss}";
        };
        _clock.Start();

        Loaded += (_, _) => StartJarvis();
        Closing += MainWindow_Closing;
    }

    private void SetupTray()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Jarvis Alpha",
            Icon = new System.Drawing.Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Jarvis.ico")),
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _trayIcon.ContextMenuStrip.Items.Add("Открыть", null, (_, _) => ShowFromTray());
        _trayIcon.ContextMenuStrip.Items.Add("Выход", null, (_, _) =>
        {
            _forceExit = true;
            Close();
        });
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ApplySettingsToUi()
    {
        AutostartCheck.IsChecked = StartupManager.IsEnabled();
        TrayCheck.IsChecked = _settings.MinimizeToTray;
        WakeText.Text = $"WAKE WORD: {string.Join(" / ", _settings.WakeWords).ToUpperInvariant()}";
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
                    _conversationUntil = DateTime.Now.AddSeconds(_settings.ConversationSeconds);
                    _tts.Speak($"Слушаю, {_settings.UserName}.");
                    continue;
                }

                SetStatus("Выполняю", "Проверяю команду.");

                if (!await commands.HandleAsync(commandText, ct))
                {
                    Dispatcher.Invoke(Close);
                    break;
                }
                _conversationUntil = DateTime.Now.AddSeconds(_settings.ConversationSeconds);
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
        _trayIcon?.Dispose();
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

    private string? ExtractCommand(string text)
    {
        var normalized = text.Trim();
        var lower = normalized.ToLowerInvariant();

        foreach (var wake in _settings.WakeWords)
        {
            var index = lower.IndexOf(wake, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            _conversationUntil = DateTime.Now.AddSeconds(_settings.ConversationSeconds);
            var command = normalized.Remove(index, wake.Length);
            return command.Trim(' ', ',', '.', '!', '?', ':', ';', '-');
        }

        if (DateTime.Now <= _conversationUntil)
        {
            return normalized;
        }

        if (lower.Contains("выход", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("пока", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return null;
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ShowFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

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

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckUpdatesAsync(interactive: true);
    }

    private async Task CheckUpdatesAsync(bool interactive)
    {
        try
        {
            var updater = new AppUpdater();
            AddLogCore("UPDATE", "Проверяю обновления.");
            var result = await updater.CheckAsync(CancellationToken.None);
            AddLogCore("UPDATE", result.Message);

            if (!result.HasUpdate || result.Manifest == null)
            {
                if (interactive)
                {
                    System.Windows.MessageBox.Show(result.Message, "Jarvis Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var answer = System.Windows.MessageBox.Show(
                $"{result.Message}\n\n{result.Manifest.Notes}\n\nУстановить обновление сейчас?",
                "Jarvis Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            await updater.DownloadAndInstallAsync(result.Manifest, message => AddLog("UPDATE", message), CancellationToken.None);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AddLogCore("UPDATE", ex.Message);
            if (interactive)
            {
                System.Windows.MessageBox.Show(ex.Message, "Jarvis Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void AutostartCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var enabled = AutostartCheck.IsChecked == true;
        StartupManager.SetEnabled(enabled);
        _settings.StartWithWindows = enabled;
        _settings.Save();
        AddLogCore("SETTINGS", enabled ? "Автозапуск включён." : "Автозапуск выключен.");
    }

    private void TrayCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.MinimizeToTray = TrayCheck.IsChecked == true;
        _settings.Save();
        AddLogCore("SETTINGS", _settings.MinimizeToTray ? "Сворачивание в трей включено." : "Сворачивание в трей выключено.");
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_forceExit && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(1200, "Jarvis Alpha", "Джарвис продолжает работать в трее.", Forms.ToolTipIcon.Info);
            return;
        }

        StopJarvis();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _forceExit = true;
        Close();
    }
}
