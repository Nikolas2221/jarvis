using System.Windows;

namespace Jarvis;

public partial class UpdateWindow : Window
{
    private readonly AppUpdater _updater = new();
    private UpdateManifest? _manifest;

    public UpdateWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await CheckAsync();
    }

    private async Task CheckAsync()
    {
        try
        {
            SetBusy("Проверяю обновления...");
            var result = await _updater.CheckAsync(CancellationToken.None);
            StatusText.Text = result.Message;

            if (!result.HasUpdate || result.Manifest == null)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = 100;
                VersionText.Text = "Обновление не требуется";
                return;
            }

            _manifest = result.Manifest;
            VersionText.Text = $"Доступна версия {_manifest.Version}";
            NotesText.Text = _manifest.Notes ?? "";
            StatusText.Text = "Готово к скачиванию.";
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            InstallButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            VersionText.Text = "Ошибка проверки";
            StatusText.Text = ex.Message;
        }
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (_manifest == null) return;

        InstallButton.IsEnabled = false;
        try
        {
            SetBusy("Скачиваю и устанавливаю обновление...");
            await _updater.DownloadAndInstallAsync(_manifest, message => Dispatcher.Invoke(() => StatusText.Text = message), CancellationToken.None);
            StatusText.Text = "Обновление скачано. Перезапускаю Jarvis...";
            await Task.Delay(400);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            StatusText.Text = ex.Message;
            InstallButton.IsEnabled = true;
        }
    }

    private void SetBusy(string text)
    {
        StatusText.Text = text;
        Progress.IsIndeterminate = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
