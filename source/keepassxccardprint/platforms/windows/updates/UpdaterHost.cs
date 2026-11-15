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
    private static bool _preparing;
    private static bool _promptActive;
    private static string _lastPrompt="";
    internal static event Action? Changed;
    internal static bool IsInstalling=>_installing || _preparing;

    internal static void Attach(MainWindow owner)
    {
        _window=owner;
        if(Path.GetDirectoryName(Environment.ProcessPath) is { } installDirectory)
            UpdateOperationResults.CaptureLegacyCompleted(WindowsUpdateService.DefaultRoot,
                installDirectory,AppBuildInfo.Version);
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
        // Render the main window before displaying a once-only result. The
        // result survives staging cleanup and even a later manual launch.
        await Task.Yield();
        var host=_window;
        if(host is not null)
        {
            var target=Path.GetDirectoryName(Environment.ProcessPath!);
            if(target is not null)
            {
                var result=UpdateOperationResults.Read(WindowsUpdateService.DefaultRoot,target);
                if(result is not null)
                {
                    // A pending "installed" record isn't proof that this EXE
                    // actually started at the target version.
                    if(result.Status!="installed" ||
                       string.Equals(result.ToVersion,AppBuildInfo.Version,StringComparison.Ordinal))
                    {
                        try
                        {
                            await UpdateOperationWindow.ShowResultAsync(host,host.UpdateLanguage,result);
                            UpdateOperationResults.Consume(WindowsUpdateService.DefaultRoot,target);
                        }
                        catch(Exception ex)
                        {
                            // Never hide an unacknowledged result if the dialog
                            // itself fails; retry on the next normal startup.
                            ServiceError=ex.Message;
                            Changed?.Invoke();
                        }
                    }
                }
            }
        }
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
            if(_window is {IsVisible:true} owner && !_installing && !_preparing && !_promptActive &&
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
    internal static async Task CheckInteractivelyAsync(MainWindow owner)
    {
        if(_installing || _preparing)return;
        _preparing=true;
        try
        {
            var dialog=new UpdateOperationWindow(owner.UpdateLanguage,"update",
                AppBuildInfo.Version,AppBuildInfo.Version);
            await dialog.RunPreparationAsync(owner,async ct=>
            {
                var report=new Progress<UpdateOperationProgress>(dialog.SetProgress);
                await Service.CheckAsync(true,ct,progress:report);
                if(Service.Snapshot.Status=="error")
                    throw new InvalidOperationException(Service.Snapshot.Detail);
                return true;
            });
        }
        catch(Exception ex){ServiceError=ex.Message;}
        finally{_preparing=false;Changed?.Invoke();}
    }

    internal static async Task RepairAsync(MainWindow owner,bool alreadyConfirmed=false)
    {
        if(_installing || _preparing)return;
        if(!alreadyConfirmed && !await AppConfirmations.AskAsync(owner,
            Caption(owner,"Восстановление","Відновлення","Recovery"),
            Caption(owner,"Скачать и проверить релиз текущей версии с GitHub и восстановить программу? Настройки и KDBX не изменятся.",
                "Завантажити й перевірити реліз поточної версії з GitHub і відновити програму? Налаштування та KDBX не зміняться.",
                "Download and verify the current release from GitHub and repair the application? Settings and KDBX files will not change."),
            Caption(owner,"Восстановить","Відновити","Repair")))return;
        if(owner.IsExportRunning || !await owner.ReadyForUpdateAsync())return;
        ServiceError="";
        _preparing=true;
        try
        {
            var dialog=new UpdateOperationWindow(owner.UpdateLanguage,"repair",
                AppBuildInfo.Version,AppBuildInfo.Version);
            bool installerStarted=false;
            bool completed=await dialog.RunPreparationAsync(owner,async ct=>
            {
                var report=new Progress<UpdateOperationProgress>(dialog.SetProgress);
                await Service.CheckAsync(true,ct,reinstall:true,progress:report);
                if(Service.IsRepairReady)
                {
                    await PrepareAndLaunchAsync(dialog,ct,confirmedRepair:true,owner.UpdateLanguage);
                    installerStarted=true;
                    return true;
                }
                if(Service.IsReady)return true; // Newer release: confirmation is separate.
                throw new InvalidOperationException(Service.Snapshot.Status=="current"
                    ?Caption(owner,"Официальный релиз текущей версии недоступен.",
                        "Офіційний реліз поточної версії недоступний.",
                        "The current version's official release is unavailable.")
                    :Service.Snapshot.Detail);
            });
            if(installerStarted)owner.CloseForUpdate();
            else if(completed && Service.IsReady && !Service.IsRepairReady)
            {
                _preparing=false;
                await PromptInstallAsync(owner);
            }
        }
        catch(Exception ex){ServiceError=ex.Message;Changed?.Invoke();}
        finally{_preparing=false;Changed?.Invoke();}
    }

    private static async Task PrepareAndLaunchAsync(UpdateOperationWindow dialog,
        CancellationToken cancellationToken,bool confirmedRepair,string language)
    {
        cancellationToken.ThrowIfCancellationRequested();
        dialog.SetProgress(new("verifying"));
        await Task.Run(Service.ValidateInstallation,cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        dialog.DisableCancellation();
        dialog.SetProgress(new("handoff"));
        Service.LaunchInstaller(restart:true,confirmedRepair:confirmedRepair,language:language);
        _installing=true;
    }

    internal static async Task InstallAsync(MainWindow owner,bool confirmedRepair)
    {
        if(_installing || _preparing || !Service.IsReady)return;
        if(Service.IsRepairReady && !confirmedRepair)return;
        if(owner.IsExportRunning || !await owner.ReadyForUpdateAsync())return;
        _preparing=true;
        try
        {
            var dialog=new UpdateOperationWindow(owner.UpdateLanguage,
                Service.IsRepairReady?"repair":"update",AppBuildInfo.Version,
                Service.Snapshot.Version.TrimStart('v'));
            bool launched=await dialog.RunPreparationAsync(owner,async ct=>
            {
                await PrepareAndLaunchAsync(dialog,ct,confirmedRepair,owner.UpdateLanguage);
                return true;
            });
            if(launched)owner.CloseForUpdate();
        }
        catch(Exception ex){ServiceError=ex.Message;Changed?.Invoke();}
        finally{_preparing=false;Changed?.Invoke();}
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
            Service.LaunchInstaller(restart:false,confirmedRepair:false,
                language:_window?.UpdateLanguage??"auto");
            _installing=true;
        }
        catch(Exception ex){ServiceError=ex.Message;Changed?.Invoke();}
    }
}
