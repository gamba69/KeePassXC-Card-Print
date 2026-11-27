using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

internal static class FfmpegComponentDialog
{
    private static string T(string ru,string uk,string en)=>ScannerResultDialog.T(ru,uk,en);
    internal static string Status()=>NetworkFfmpegManager.InstalledVersion is { } version
        ? T("Установлен: ","Встановлено: ","Installed: ")+version
        : T("Не установлен","Не встановлено","Not installed");
    internal static async Task Show(Window owner)
    {
        var win=new Window{Title="FFmpeg",Width=520,MinWidth=420,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowDecorations=WindowDecorations.Full,ShowInTaskbar=false};
        PrintWindowIcons.Attach(win);
        var stack=new StackPanel{Spacing=9,Margin=new Thickness(14)};win.Content=stack;
        stack.Children.Add(new TextBlock{Text=T("Альтернативный движок сетевых камер. Не нужен для встроенного SharpRTSP / Media Foundation.","Альтернативний рушій мережевих камер. Не потрібний для вбудованого SharpRTSP / Media Foundation.","Optional network-camera engine. Not required by built-in SharpRTSP / Media Foundation."),TextWrapping=TextWrapping.Wrap});
        var installed=new TextBlock();stack.Children.Add(installed);
        stack.Children.Add(new TextBlock{Text=T("Доступная версия: ","Доступна версія: ","Available version: ")+NetworkFfmpegManager.Version});
        stack.Children.Add(new TextBlock{Text=T("Загрузка: ","Завантаження: ","Download: ")+$"{NetworkFfmpegManager.ArchiveBytes/1024.0/1024:0.#} MiB · SHA-256 · LGPL",TextWrapping=TextWrapping.Wrap,FontSize=11});
        var bar=new ProgressBar{Minimum=0,Maximum=100,Height=7,IsVisible=false};stack.Children.Add(bar);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap};stack.Children.Add(status);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};stack.Children.Add(buttons);
        Button Action(string caption,Icon icon){var b=PrintDialogButtons.Create(caption,ActionIconVectors.Create(icon));b.Margin=new Thickness(0,0,5,3);buttons.Children.Add(b);return b;}
        var install=Action("",Icon.ArrowDownload);var remove=Action(T("Удалить","Видалити","Remove"),Icon.Delete);var close=Action(T("Закрыть","Закрити","Close"),Icon.Dismiss);
        var installLabel=new TextBlock{VerticalAlignment=VerticalAlignment.Center};var closeLabel=new TextBlock{VerticalAlignment=VerticalAlignment.Center};
        var installContent=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};installContent.Children.Add(ActionIconVectors.Create(Icon.ArrowDownload));installContent.Children.Add(installLabel);install.Content=installContent;
        var closeContent=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};closeContent.Children.Add(ActionIconVectors.Create(Icon.Dismiss));closeContent.Children.Add(closeLabel);close.Content=closeContent;
        bool busy=false;CancellationTokenSource? cancel=null;
        void Refresh()
        {
            installed.Text=Status();
            string label=NetworkFfmpegManager.InstalledVersion is null?T("Скачать","Завантажити","Download"):
                NetworkFfmpegManager.IsReady?T("Переустановить","Перевстановити","Reinstall"):T("Обновить","Оновити","Update");
            // Use the same Fluent icon/button factory for every state.
            installLabel.Text=label;
            install.IsEnabled=!busy;remove.IsEnabled=!busy&&NetworkFfmpegManager.InstalledVersion is not null;
            closeLabel.Text=busy?T("Отмена","Скасувати","Cancel"):T("Закрыть","Закрити","Close");
        }
        install.Click+=async (_,_)=>
        {
            if(busy)return;busy=true;cancel=new CancellationTokenSource();bar.IsVisible=true;bar.Value=0;status.Text=T("Загрузка и проверка…","Завантаження та перевірка…","Downloading and verifying…");Refresh();
            try
            {
                var progress=new Progress<double>(value=>{bar.Value=100*value;});
                await NetworkFfmpegManager.InstallAsync(progress,cancel.Token);
                status.Text=T("Установлено. Архив и версия проверены.","Встановлено. Архів і версію перевірено.","Installed. Archive and version verified.");
            }
            catch(OperationCanceledException){status.Text=T("Операция отменена","Операцію скасовано","Canceled");}
            catch{status.Text=T("Не удалось установить FFmpeg. Проверьте сеть, свободное место и остановите сетевую камеру.","Не вдалося встановити FFmpeg. Перевірте мережу, вільне місце та зупиніть мережеву камеру.","Could not install FFmpeg. Check network, free disk space and stop the network camera.");}
            finally{cancel?.Dispose();cancel=null;busy=false;bar.IsVisible=false;Refresh();}
        };
        remove.Click+=(_,_)=>
        {
            if(busy)return;
            try{NetworkFfmpegManager.Remove();status.Text=T("Удалено","Видалено","Removed");}
            catch{status.Text=T("Не удалось удалить. Остановите сетевую камеру.","Не вдалося видалити. Зупиніть мережеву камеру.","Could not remove. Stop the network camera.");}Refresh();
        };
        close.Click+=(_,_)=>{if(busy)cancel?.Cancel();else win.Close();};
        win.Closing+=(_,e)=>{if(busy){e.Cancel=true;cancel?.Cancel();}};
        Refresh();await win.ShowDialog(owner);
    }
}
