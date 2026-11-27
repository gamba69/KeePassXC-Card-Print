using Avalonia.Interactivity;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

public partial class SettingsView
{
    private void RefreshFfmpegSettings()
    {
        FfmpegCaption.Text=L("Сетевые камеры","Мережеві камери","Network cameras");
        FfmpegState.Text="FFmpeg · "+FfmpegComponentDialog.Status()+" · "+L("Доступная версия: ","Доступна версія: ","Available version: ")+NetworkFfmpegManager.Version;
        VideoDecoderDiagnosticsLabel.Text=L("Диагностика видеодекодеров…","Діагностика відеодекодерів…","Video decoder diagnostics…");
        FfmpegManageLabel.Text=L("Управление…","Керування…","Manage…");
    }
    private async void OnVideoDecoderDiagnostics(object? sender,RoutedEventArgs e)
    {
        if(Owner is { } owner)await VideoDecoderDialog.Show(owner);
    }
    private async void OnManageFfmpeg(object? sender,RoutedEventArgs e)
    {
        if(Owner is not { } owner)return;
        await FfmpegComponentDialog.Show(owner);
        RefreshFfmpegSettings();
    }
}
