using Avalonia.Controls;
using System.ComponentModel;
using Avalonia.Interactivity;
using KeePassXCCardPrint.Platforms.Windows.Updates;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public partial class AboutView
{
    private bool _updatingControls;
    private bool _updatesAttached;
    private AboutViewModel? _observedUpdateLanguage;

    private void InitializeUpdates()
    {
        AttachedToVisualTree+=(_,_)=>
        {
            _updatesAttached=true;
            UpdaterHost.Changed+=RefreshUpdates;
            ObserveUpdateLanguage();
            RefreshUpdates();
        };
        DetachedFromVisualTree+=(_,_)=>DetachUpdates();
        DataContextChanged+=(_,_)=>
        {
            ObserveUpdateLanguage();
            RefreshUpdates();
        };
    }

    private void DetachUpdates()
    {
        _updatesAttached=false;
        UpdaterHost.Changed-=RefreshUpdates;
        ObserveUpdateLanguage();
    }

    // The About view stays alive while tabs change; explicit language changes
    // do not recreate its DataContext. Listen to the view-model notification.
    private void ObserveUpdateLanguage()
    {
        var current=_updatesAttached ? DataContext as AboutViewModel : null;
        if(ReferenceEquals(current,_observedUpdateLanguage))return;
        if(_observedUpdateLanguage is not null)
            _observedUpdateLanguage.PropertyChanged-=OnUpdateLanguageChanged;
        _observedUpdateLanguage=current;
        if(current is not null)
            current.PropertyChanged+=OnUpdateLanguageChanged;
    }

    private void OnUpdateLanguageChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName is nameof(AboutViewModel.UpdateLanguage) or null or "")
            RefreshUpdates();
    }

    private string L(string ru,string uk,string en) =>
        Localizer.ResolveLanguage((DataContext as AboutViewModel)?.UpdateLanguage??"auto") switch
        { "uk"=>uk,"en"=>en,_=>ru };

    private void RefreshUpdates()
    {
        if (_updatingControls) return;
        _updatingControls=true;
        try
        {
            var updater=UpdaterHost.Service;
            UpdateHeader.Text=L("Обновления приложения","Оновлення програми","Application updates");
            UpdateCheckButtonLabel.Text=L("Проверить обновления","Перевірити оновлення","Check for updates");
            UpdateInstallButtonLabel.Text=L("Установить сейчас","Установити зараз","Install now");
            UpdateRepairButtonLabel.Text=L("Восстановить…","Відновити…","Repair…");
            var snapshot=updater.Snapshot;
            var description=snapshot.Status switch
            {
                "checking"=>L("Проверка обновлений…","Перевірка оновлень…","Checking for updates…"),
                "available"=>L("Новая версия доступна","Доступна нова версія","New version available"),
                "ready" when snapshot.Reinstall=>L("Восстановление готово","Відновлення готове","Repair ready"),
                "ready"=>L("Обновление готово","Оновлення готове","Update ready"),
                "current"=>L("Установлена актуальная версия","Установлена актуальна версія","Already up to date"),
                "error"=>L("Ошибка обновления","Помилка оновлення","Update failed"),
                _=>L("Обновления ещё не проверялись","Оновлення ще не перевірялися","Not checked yet")
            };
            UpdateStatus.Text=description+(snapshot.Version.Length==0?"":" · "+snapshot.Version)+
                (snapshot.Unverified?"\n"+L(
                    "Внимание: GitHub Immutable Releases отключён; подлинность не подтверждена.",
                    "Увага: GitHub Immutable Releases вимкнено; автентичність не підтверджена.",
                    "Warning: GitHub Immutable Releases is disabled; authenticity is unverified."):"")+
                (snapshot.Status=="error"?"\n"+snapshot.Detail:"")+
                (UpdaterHost.ServiceError.Length>0?"\n"+UpdaterHost.ServiceError:"");
            var offerRepair=snapshot.Status is "current" or "error" ||
                updater.IsRepairReady && snapshot.Reinstall;
            UpdateCheckButton.IsEnabled=snapshot.Status!="checking" && !UpdaterHost.IsInstalling;
            UpdateInstallButton.IsVisible=!offerRepair;
            UpdateInstallButton.IsEnabled=!offerRepair && updater.IsReady && !updater.IsRepairReady &&
                !UpdaterHost.IsInstalling;
            UpdateRepairButton.IsVisible=offerRepair;
            UpdateRepairButton.IsEnabled=offerRepair && !UpdaterHost.IsInstalling;
        }
        finally{_updatingControls=false;}
    }
    private async void OnCheckUpdatesClicked(object? sender,RoutedEventArgs e)
    {
        if(TopLevel.GetTopLevel(this) is MainWindow owner)
            await UpdaterHost.CheckInteractivelyAsync(owner);
        RefreshUpdates();
    }
    private async void OnInstallUpdateClicked(object? sender,RoutedEventArgs e)
    {
        if(TopLevel.GetTopLevel(this) is MainWindow owner)
            await UpdaterHost.PromptInstallAsync(owner);
    }
    private async void OnRepairUpdateClicked(object? sender,RoutedEventArgs e)
    {
        if(TopLevel.GetTopLevel(this) is MainWindow owner)
            await UpdaterHost.RepairAsync(owner);
        RefreshUpdates();
    }
}
