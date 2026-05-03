namespace Jarvis;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (e.Args.Contains("--test-tts", StringComparer.OrdinalIgnoreCase))
        {
            using var tts = SpeechSynthesizerFactory.Create();
            tts.Speak("Проверка голоса Джарвиса.");
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }
}
