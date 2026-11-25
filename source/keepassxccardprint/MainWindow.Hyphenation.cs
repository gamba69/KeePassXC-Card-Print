using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public partial class MainWindow
{
    internal bool CanChangeHyphenation => !_vm.Busy && !_switchingStorage && _printExportCancellation is null && !_cloudExitPreparing && !_cloudExitPrepared;
    internal event Action? HyphenationPackagesChanged;
    internal void HyphenationChanged()
    {
        _vm.RefreshHyphenation();
        HyphenationPackagesChanged?.Invoke();
    }
    private async Task RestoreHyphenationAsync()
    {
        string root = SettingsLocation.DirectoryPath;
        try
        {
            if (HyphenationPackages.Read(root).Count == 0) return;
            await HyphenationPackages.ReconcileAsync(root);
            if (SettingsLocation.DirectoryPath == root) HyphenationChanged();
        }
        catch (Exception ex)
        {
            if (SettingsLocation.DirectoryPath != root) return;
            _vm.SetPreviewNotice(Localizer.ResolveLanguage(_settings.Language) switch
            {
                "uk" => "Не вдалося відновити правила переносу. Перевірте пакети в налаштуваннях.",
                "en" => "Could not restore hyphenation rules. Check packages in Settings.",
                _ => "Не удалось восстановить правила переносов. Проверьте пакеты в настройках."
            });
            CloudSyncDiagnostics.Record("hyphenation-restore", ex);
        }
    }
}
