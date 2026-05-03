using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Jarvis;

public sealed class CommandHandler
{
    private readonly ISpeechSynthesizer _tts;
    private readonly OpenAiAssistant? _ai;
    private static readonly CultureInfo Ru = new("ru-RU");

    public CommandHandler(ISpeechSynthesizer tts, OpenAiAssistant? ai = null)
    {
        _tts = tts;
        _ai = ai;
    }

    /// <summary>Returns false to signal exit.</summary>
    public async Task<bool> HandleAsync(string text, CancellationToken ct = default)
    {
        var t = text.ToLowerInvariant().Trim();

        if (Has(t, "выход", "пока", "отключись", "до свидания", "завершить работу"))
        {
            _tts.Speak("До встречи, сэр.");
            return false;
        }

        if (Has(t, "привет", "здравствуй"))
        {
            _tts.Speak("Здравствуйте, сэр. Чем могу помочь?");
            return true;
        }

        if (Has(t, "как тебя зовут", "кто ты"))
        {
            _tts.Speak("Я Джарвис, ваш голосовой помощник.");
            return true;
        }

        if (Has(t, "врем", "сколько час"))
        {
            _tts.Speak($"Сейчас {DateTime.Now:HH:mm}");
            return true;
        }

        if (Has(t, "дат", "какое число", "сегодня"))
        {
            _tts.Speak($"Сегодня {DateTime.Now.ToString("d MMMM, dddd", Ru)}");
            return true;
        }

        if (Has(t, "система", "статус пк", "информация о компьютере"))
        {
            SpeakSystemStatus();
            return true;
        }

        if (Has(t, "ютуб", "youtube"))
        {
            OpenUrl("https://www.youtube.com");
            _tts.Speak("Открываю YouTube.");
            return true;
        }

        if (Has(t, "браузер", "хром", "google"))
        {
            OpenUrl("https://www.google.com");
            _tts.Speak("Открываю браузер.");
            return true;
        }

        if (Has(t, "найди", "поищи", "поиск"))
        {
            var query = StripKeywords(t, "найди", "поищи", "поиск", "в гугле", "в интернете", "пожалуйста");
            if (string.IsNullOrEmpty(query))
            {
                _tts.Speak("Что искать?");
            }
            else
            {
                OpenUrl($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
                _tts.Speak($"Ищу: {query}");
            }
            return true;
        }

        if (HandleLaunchCommand(t))
        {
            return true;
        }

        if (HandleTimerCommand(t))
        {
            return true;
        }

        if (Has(t, "громкость") || Has(t, "звук") || Has(t, "громче") || Has(t, "тише"))
        {
            HandleVolume(t);
            return true;
        }

        if (Has(t, "выключи компьютер", "выключи пк", "выключение компьютера"))
        {
            _tts.Speak("Выключаю через минуту. Скажите отмена выключения, чтобы остановить.");
            Run("shutdown", "/s /t 60");
            return true;
        }

        if (Has(t, "перезагрузи компьютер", "перезагрузка"))
        {
            _tts.Speak("Перезагружаю через минуту. Скажите отмена выключения, чтобы остановить.");
            Run("shutdown", "/r /t 60");
            return true;
        }

        if (Has(t, "отмена выключения", "отмени выключение", "не выключай"))
        {
            Run("shutdown", "/a");
            _tts.Speak("Выключение отменено.");
            return true;
        }

        if (Has(t, "блокировка", "заблокируй"))
        {
            _tts.Speak("Блокирую.");
            Run("rundll32.exe", "user32.dll,LockWorkStation");
            return true;
        }

        if (_ai == null)
        {
            _tts.Speak("Не понял команду.");
            return true;
        }

        try
        {
            _tts.Speak("Секунду, сэр.");
            var answer = await _ai.AskAsync(text, ct);
            _tts.Speak(answer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAI] {ex.Message}");
            _tts.Speak("Не удалось получить ответ от GPT.");
        }

        return true;
    }

    private bool HandleLaunchCommand(string t)
    {
        if (Has(t, "открой блокнот", "запусти блокнот", "notepad"))
        {
            Run("notepad.exe", "");
            _tts.Speak("Открываю блокнот.");
            return true;
        }

        if (Has(t, "открой калькулятор", "запусти калькулятор", "calculator"))
        {
            Run("calc.exe", "");
            _tts.Speak("Открываю калькулятор.");
            return true;
        }

        if (Has(t, "открой проводник", "запусти проводник", "папки"))
        {
            Run("explorer.exe", "");
            _tts.Speak("Открываю проводник.");
            return true;
        }

        if (Has(t, "открой загрузки", "папку загрузки"))
        {
            OpenPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads");
            _tts.Speak("Открываю загрузки.");
            return true;
        }

        if (Has(t, "открой рабочий стол", "папку рабочий стол"))
        {
            OpenPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            _tts.Speak("Открываю рабочий стол.");
            return true;
        }

        if (Has(t, "диспетчер задач", "task manager"))
        {
            Run("taskmgr.exe", "");
            _tts.Speak("Открываю диспетчер задач.");
            return true;
        }

        return false;
    }

    private bool HandleTimerCommand(string t)
    {
        if (!Has(t, "таймер", "напомни")) return false;

        var minutes = ExtractNumberBefore(t, "минут");
        var seconds = ExtractNumberBefore(t, "секунд");

        if (minutes == null && seconds == null)
        {
            _tts.Speak("На сколько поставить таймер?");
            return true;
        }

        var delay = TimeSpan.FromSeconds(seconds ?? 0) + TimeSpan.FromMinutes(minutes ?? 0);
        if (delay <= TimeSpan.Zero)
        {
            _tts.Speak("Не понял длительность таймера.");
            return true;
        }

        _tts.Speak($"Таймер установлен на {DescribeDelay(delay)}.");
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay);
            _tts.Speak("Таймер завершён.");
        });
        return true;
    }

    private void SpeakSystemStatus()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var memory = GC.GetGCMemoryInfo();
        var totalMemoryMb = memory.TotalAvailableMemoryBytes > 0
            ? memory.TotalAvailableMemoryBytes / 1024 / 1024
            : 0;

        var text = $"Система активна. ОС: {RuntimeInformation.OSDescription}. " +
                   $"Аптайм {uptime.Days} дней {uptime.Hours} часов. ";
        if (totalMemoryMb > 0)
        {
            text += $"Доступная память процесса: около {totalMemoryMb} мегабайт.";
        }

        _tts.Speak(text);
    }

    private void HandleVolume(string t)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var current = device.AudioEndpointVolume.MasterVolumeLevelScalar;

            if (Has(t, "выше", "вверх", "громче", "прибавь", "увеличь"))
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Min(1f, current + 0.1f);
                _tts.Speak("Громче.");
            }
            else if (Has(t, "ниже", "вниз", "тише", "убавь", "уменьш"))
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Max(0f, current - 0.1f);
                _tts.Speak("Тише.");
            }
            else if (Has(t, "выключи звук", "отключи звук", "беззвуч", "мут"))
            {
                device.AudioEndpointVolume.Mute = true;
                _tts.Speak("Звук отключён.");
            }
            else if (Has(t, "включи звук"))
            {
                device.AudioEndpointVolume.Mute = false;
                _tts.Speak("Звук включён.");
            }
            else
            {
                _tts.Speak($"Громкость {(int)(current * 100)} процентов.");
            }
        }
        catch (Exception ex)
        {
            _tts.Speak("Не получилось изменить громкость.");
            Console.WriteLine($"[Volume] {ex.Message}");
        }
    }

    private static bool Has(string text, params string[] keywords) =>
        keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string StripKeywords(string text, params string[] keywords)
    {
        var result = text;
        foreach (var k in keywords)
        {
            result = result.Replace(k, "", StringComparison.OrdinalIgnoreCase);
        }
        return result.Trim(' ', ',', '.', '!', '?');
    }

    private static int? ExtractNumberBefore(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;

        var before = text[..index].Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (before == null) return null;

        return int.TryParse(before, out var value) ? value : WordToNumber(before);
    }

    private static int? WordToNumber(string word) => word switch
    {
        "одну" or "один" or "одна" => 1,
        "две" or "два" => 2,
        "три" => 3,
        "четыре" => 4,
        "пять" => 5,
        "шесть" => 6,
        "семь" => 7,
        "восемь" => 8,
        "девять" => 9,
        "десять" => 10,
        _ => null
    };

    private static string DescribeDelay(TimeSpan delay)
    {
        if (delay.TotalMinutes >= 1)
        {
            return $"{(int)delay.TotalMinutes} минут";
        }

        return $"{(int)delay.TotalSeconds} секунд";
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo
    {
        FileName = url,
        UseShellExecute = true
    });

    private static void OpenPath(string path) => Process.Start(new ProcessStartInfo
    {
        FileName = path,
        UseShellExecute = true
    });

    private static void Run(string file, string args) => Process.Start(new ProcessStartInfo
    {
        FileName = file,
        Arguments = args,
        CreateNoWindow = true,
        UseShellExecute = false
    });
}
