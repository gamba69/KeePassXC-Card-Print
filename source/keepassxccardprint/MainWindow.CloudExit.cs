using Avalonia.Controls;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public partial class MainWindow
{
    private bool _cloudExitPreparing, _cloudExitPrepared, _cloudWasStarted;
    private readonly DispatcherTimer _cloudFooterTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string _cloudFooterKey = "";

    private string CloudL(string ru, string uk, string en) => Localizer.ResolveLanguage(_settings.Language) switch
    { "uk" => uk, "en" => en, _ => ru };

    private void InitializeCloudFooter()
    {
        void Notify() => Dispatcher.UIThread.Post(RefreshCloudFooter);
        _cloudSync.Changed += Notify;
        _cloudFooterTimer.Tick += (_, _) => RefreshCloudFooter();
        _cloudFooterTimer.Start();
        Closed += (_, _) => { _cloudFooterTimer.Stop(); _cloudSync.Changed -= Notify; };
    }

    private void RefreshCloudFooter()
    {
        var panel = this.FindControl<StackPanel>("CloudFooter");
        var text = this.FindControl<TextBlock>("CloudFooterText");
        var icon = this.FindControl<ContentControl>("CloudFooterIcon");
        if (panel is null || text is null || icon is null) return;
        var activity = _cloudSync.Activity;
        var stage = activity.Stage;
        if (SettingsLocation.Mode != ConfigurationStorage.Cloud || stage == "Idle" ||
            stage == "Success" && DateTime.UtcNow - activity.ChangedUtc > TimeSpan.FromSeconds(4))
        {
            panel.IsVisible = false;
            _cloudFooterKey = "";
            return;
        }
        var label = CloudExitText.Activity(stage, _settings.Language);
        var progress = _cloudSync.LastTransferProgress;
        if (stage == "Uploading" && progress is not null &&
            CloudTransferPresentation.Matches(progress, "sync.upload", "Uploading", true) &&
            CloudTransferPresentation.Percent(progress) is double percent)
            label += $" {percent:0}%";
        var key = stage + "|" + label + "|" + activity.Detail + "|" + Localizer.ResolveLanguage(_settings.Language);
        // Match the footer label, not the default foreground of a generic icon.
        // Refresh even when the activity key is unchanged (e.g. a theme switch).
        if (icon.Content is FluentIcon currentGlyph && currentGlyph.Foreground != text.Foreground)
            currentGlyph.Foreground = text.Foreground;
        if (_cloudFooterKey == key) return;
        _cloudFooterKey = key;
        var glyph = CloudActionIcons.ForStage(stage);
        glyph.Foreground = text.Foreground;
        icon.Content = glyph;
        text.Text = label;
        ToolTip.SetTip(panel, stage is "Error" or "Conflict"
            ? CloudExitText.Reason(activity.Detail, _settings.Language) : label);
        panel.IsVisible = true;
    }

    // Called only after the ordinary unsaved-template decision. Stop polling,
    // flush UI debounce, save portable state once and then await verified data.
    private async Task<bool> PrepareCloudExitAsync()
    {
        if (SettingsLocation.Mode != ConfigurationStorage.Cloud) return true;
        if (_cloudExitPrepared) return true;
        if (_cloudExitPreparing) return false;
        _cloudExitPreparing = true;
        _cloudWasStarted = _cloudSync.IsStarted;
        _cloudSync.Stop();
        try
        {
            _preferencesSaveTimer.Stop();
            _vm.SaveVaultPreferences();
            _vm.SaveCardAppearance(throwOnFailure: true);
            CaptureLayout();
            _store.Save(_settings); // unlike ordinary debounced Persist(), do not swallow I/O failure
            if (_cloudSync.HasUnconfirmedChanges() || _cloudSync.Activity.Stage is "Uploading" or "Downloading" or "Verifying")
            {
                var result = await new CloudExitDialog(_cloudSync, _settings.Language).RunAsync(this);
                if (result == CloudExitChoice.Cancel) return false;
                // A successful dialog result is based on the actual journal and
                // local signature, not on a timer or an earlier status message.
            }
            _cloudExitPrepared = true;
            return true;
        }
        catch (Exception ex)
        {
            CloudSyncDiagnostics.Record("exit-local-save", ex);
            await AppConfirmations.AskAsync(this,
                CloudL("Не удалось сохранить локально", "Не вдалося зберегти локально", "Local save failed"),
                CloudL("Выход отменён. Проверьте доступ к локальному кэшу и повторите сохранение.",
                    "Вихід скасовано. Перевірте доступ до локального кешу та повторіть збереження.",
                    "Exit cancelled. Check access to the local cache and try saving again."),
                CloudL("Понятно", "Зрозуміло", "OK"), actionIcon: "warning",
                cancelText: CloudL("Закрыть", "Закрити", "Close"));
            return false;
        }
        finally
        {
            _cloudExitPreparing = false;
            if (!_cloudExitPrepared) ResumeCloudAfterCancelledExit();
        }
    }

    private void ResumeCloudAfterCancelledExit()
    {
        _cloudExitPrepared = false;
        if (_cloudWasStarted && SettingsLocation.Mode == ConfigurationStorage.Cloud) _cloudSync.Start();
        _cloudWasStarted = false;
    }
}
