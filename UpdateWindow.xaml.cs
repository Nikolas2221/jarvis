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
            VersionText.Text = $"Установлена версия {AppVersion.Current}";
            var result = await _updater.CheckAsync(CancellationToken.None);
            StatusText.Text = result.Message;

            if (!result.HasUpdate || result.Manifest == null)
            {
                Progress.IsIndeterminate = false;
                Progress.Value = 100;
                VersionText.Text = $"Установлена актуальная версия {AppVersion.Current}";
                return;
            }

            _manifest = result.Manifest;
            VersionText.Text = $"Установлена {AppVersion.Current}; доступна {_manifest.Version}";
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
            await _updater.DownloadAndInstallAsync(
                _manifest,
                progress => Dispatcher.Invoke(() => ApplyProgress(progress)),
                CancellationToken.None);
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
        DownloadText.Text = "";
        Progress.IsIndeterminate = true;
    }

    private void ApplyProgress(UpdateProgress progress)
    {
        StatusText.Text = progress.Message;
        DownloadText.Text = progress.DisplayText;

        if (progress.Percent == null)
        {
            Progress.IsIndeterminate = true;
            return;
        }

        Progress.IsIndeterminate = false;
        Progress.Value = progress.Percent.Value;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
