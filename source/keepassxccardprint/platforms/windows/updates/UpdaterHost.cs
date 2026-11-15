using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint.Platforms.Windows.Updates;

// Application lifetime glue; the service itself never accesses KDBX or UI data.
internal static class UpdaterHost
{
    internal static readonly WindowsUpdateService Service=new();
    internal static bool ReinstallRequested;
    private static DispatcherTimer? _timer;
    private static MainWindow? _window;
    private static bool _installing;
    private static bool _promptActive;
    private static string _lastPrompt="";
    internal static event Action? Changed;
    internal static bool IsInstalling=>_installing;

    internal static void Attach(MainWindow owner)
    {
        _window=owner;
        Service.CleanupCompleted();
        Service.Changed+=UpdateChanged;
        owner.Opened+=OnOpened;
        owner.Closed+=(_,_)=>
        {
            _timer?.Stop();
            _timer=null;
            Service.Changed-=UpdateChanged;
            _window=null;
        };
    }
    private static async void OnOpened(object? sender,EventArgs e)
    {
        _timer=new DispatcherTimer{Interval=TimeSpan.FromHours(24)};
        _timer.Tick+=(_,_)=>_=Service.CheckAsync(false);
        _timer.Start();
        if(ReinstallRequested)
        {
            ReinstallRequested=false;
            var owner=_window;
            if(owner is not null)
            {
                var ok=await AppConfirmations.AskAsync(owner,Caption(owner,"Восстановление приложения",
                    "Відновлення програми","Application recovery"),Caption(owner,
                    "Скачать и проверить официальный релиз текущей версии, затем восстановить файлы программы? Данные и настройки сохранятся.",
                    "Завантажити і перевірити офіційний реліз поточної версії, потім відновити файли програми? Дані й налаштування збережуться.",
                    "Download and verify this version's official release, then repair application files? User data and settings are preserved."),
                    Caption(owner,"Восстановить","Відновити","Repair"));
                if(ok)await RepairAsync(owner,true);
            }
            return;
        }
        await Service.CheckAsync(false);
    }
    private static string Caption(MainWindow owner,string ru,string uk,string en) =>
        Localizer.ResolveLanguage(owner.UpdateLanguage) switch
        {"uk"=>uk,"en"=>en,_=>ru};

    private static void UpdateChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Changed?.Invoke();
            if(_window is {IsVisible:true} owner && !_installing && !_promptActive &&
                Service.Snapshot.Status=="ready" && !Service.IsRepairReady &&
                Service.Preferences.Installation=="ask" &&
                Service.Snapshot.Version!=_lastPrompt)
            {
                _lastPrompt=Service.Snapshot.Version;
                _=PromptInstallAsync(owner);
            }
        });
    }
    internal static async Task PromptInstallAsync(MainWindow owner)
    {
        if(_promptActive || _installing || !Service.IsReady)return;
        _promptActive=true;
        try
        {
            bool repair=Service.IsRepairReady;
            var message=repair
                ?Caption(owner,"Завершить работу и восстановить файлы приложения? Программа перезапустится.",
                    "Завершити роботу й відновити файли програми? Програма перезапуститься.",
                    "Exit and restore application files? The application will restart.")
                :Caption(owner,"Завершить работу и установить обновление? Программа перезапустится.",
                    "Завершити роботу й установити оновлення? Програма перезапуститься.",
                    "Exit and install the update? The application will restart.");
            if(Service.Snapshot.Unverified)
                message+="\n\n"+Caption(owner,
                    "Внимание: GitHub Immutable Releases отключён. Подлинность обновления не подтверждена.",
                    "Увага: GitHub Immutable Releases вимкнено. Автентичність оновлення не підтверджена.",
                    "Warning: GitHub Immutable Releases is disabled. Update authenticity is unverified.");
            if(await AppConfirmations.AskAsync(owner,Caption(owner,repair?"Восстановление":"Обновление",
                repair?"Відновлення":"Оновлення",repair?"Recovery":"Update"),message,
                Caption(owner,repair?"Восстановить":"Установить",repair?"Відновити":"Установити",
                    repair?"Repair":"Install")))
                await InstallAsync(owner,confirmedRepair:true);
        }
        finally{_promptActive=false;}
    }
    internal static async Task RepairAsync(MainWindow owner,bool alreadyConfirmed=false)
    {
        if(!alreadyConfirmed && !await AppConfirmations.AskAsync(owner,
            Caption(owner,"Восстановление","Відновлення","Recovery"),
            Caption(owner,"Скачать и проверить релиз текущей версии с GitHub и восстановить программу? Настройки и KDBX не изменятся.",
                "Завантажити й перевірити реліз поточної версії з GitHub і відновити програму? Налаштування та KDBX не зміняться.",
                "Download and verify the current release from GitHub and repair the application? Settings and KDBX files will not change."),
            Caption(owner,"Восстановить","Відновити","Repair")))return;
        ServiceError="";
        await Service.CheckAsync(true,reinstall:true);
        if(Service.IsRepairReady)await InstallAsync(owner,confirmedRepair:true);
        else if(Service.IsReady)await PromptInstallAsync(owner); // A newer release appeared.
        else if(Service.Snapshot.Status=="current")
        {
            ServiceError=Caption(owner,
                "Официальный релиз установленной версии пока недоступен на GitHub.",
                "Офіційний реліз установленої версії поки недоступний на GitHub.",
                "The installed version's official GitHub release is not yet available.");
            Changed?.Invoke();
        }
    }
    internal static async Task InstallAsync(MainWindow owner,bool confirmedRepair)
    {
        if(_installing || !Service.IsReady)return;
        if(Service.IsRepairReady && !confirmedRepair)return;
        if(owner.IsExportRunning || !await owner.ReadyForUpdateAsync())return;
        try
        {
            Service.LaunchInstaller(restart:true,confirmedRepair:confirmedRepair);
            _installing=true;
            owner.CloseForUpdate();
        }
        catch(Exception ex)
        {
            // Keep state and window intact; the About pane displays the error.
            ServiceError=ex.Message;
            Changed?.Invoke();
        }
    }
    internal static string ServiceError {get;private set;}="";
    internal static void InstallOnExitIfReady()
    {
        if(_installing || !Service.IsReady || Service.IsRepairReady ||
            Service.Preferences.Installation!="exit")return;
        try
        {
            // No recovery is automatic. A regular update follows the chosen
            // 'on exit' policy, but only after the ordinary exit confirmation.
            Service.LaunchInstaller(restart:false,confirmedRepair:false);
            _installing=true;
        }
        catch(Exception ex){ServiceError=ex.Message;Changed?.Invoke();}
    }
}
