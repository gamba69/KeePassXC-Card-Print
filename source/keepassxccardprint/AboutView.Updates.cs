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
    private static readonly string[] UpdateModes={"exit","ask","manual"};

    private void InitializeUpdates()
    {
        // Avalonia 12 exposes IsCheckedChanged, not XAML Checked/Unchecked events.
        UpdateCheck.IsCheckedChanged += (_, _) => OnUpdateCheckChanged();
        UpdateDownload.IsCheckedChanged += (_, _) => OnUpdateDownloadChanged();
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
        _updatingControls=true;
        try
        {
            var updater=UpdaterHost.Service;
            var prefs=updater.Preferences;
            UpdateCheck.IsChecked=prefs.CheckAutomatically;
            UpdateDownload.IsChecked=prefs.DownloadAutomatically;
            UpdateDownload.IsEnabled=prefs.CheckAutomatically;
            UpdateMode.ItemsSource=new[]{
                L("При выходе","Під час виходу","On exit"),
                L("Предлагать перезапуск","Пропонувати перезапуск","Offer restart"),
                L("Только вручную","Лише вручну","Manual only")};
            UpdateMode.SelectedIndex=Array.IndexOf(UpdateModes,prefs.Installation);
            UpdateHeader.Text=L("Обновления приложения","Оновлення програми","Application updates");
            UpdateCheck.Content=L("Проверять обновления автоматически",
                "Перевіряти оновлення автоматично","Check for updates automatically");
            UpdateDownload.Content=L("Скачивать обновления автоматически",
                "Автоматично завантажувати оновлення","Download updates automatically");
            UpdateModeCaption.Text=L("Установка обновлений","Установлення оновлень","Install updates");
            UpdateCheckButton.Content=L("Проверить обновления","Перевірити оновлення","Check for updates");
            UpdateInstallButton.Content=L("Установить сейчас","Установити зараз","Install now");
            UpdateRepairButton.Content=L("Восстановить…","Відновити…","Repair…");
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
    private void OnUpdateCheckChanged()
    {
        if(_updatingControls)return;
        UpdaterHost.Service.SavePreferences(UpdaterHost.Service.Preferences with
        {CheckAutomatically=UpdateCheck.IsChecked==true});
        RefreshUpdates();
    }
    private void OnUpdateDownloadChanged()
    {
        if(_updatingControls)return;
        UpdaterHost.Service.SavePreferences(UpdaterHost.Service.Preferences with
        {DownloadAutomatically=UpdateDownload.IsChecked==true});
    }
    private void OnUpdateModeChanged(object? sender,SelectionChangedEventArgs e)
    {
        if(_updatingControls || UpdateMode.SelectedIndex is <0 or >2)return;
        UpdaterHost.Service.SavePreferences(UpdaterHost.Service.Preferences with
        {Installation=UpdateModes[UpdateMode.SelectedIndex]});
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
