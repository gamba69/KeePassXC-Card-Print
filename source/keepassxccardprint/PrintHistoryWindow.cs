using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

public sealed class PrintHistoryWindow : Window
{
    public PrintHistoryWindow(ShellViewModel vm)
    {
        Title="История печати";
        PrintWindowIcons.Attach(this);
        Width=710;Height=500;MinWidth=550;MinHeight=300;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        ShowInTaskbar=false;
        WindowDecorations=WindowDecorations.Full;
        var root=new DockPanel();
        var close=PrintDialogButtons.Create("Закрыть","close");
        close.Click+=(_,_)=>Close();
        var footer=new Border();footer.Classes.Add("app-footer");
        var actions=new StackPanel{Orientation=Orientation.Horizontal,
            HorizontalAlignment=HorizontalAlignment.Right};
        actions.Children.Add(close);footer.Child=actions;
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var list=new StackPanel{Spacing=7};
        if(vm.CurrentPrintLedger is null)
            list.Children.Add(new TextBlock{Text="Откройте KDBX, чтобы увидеть историю печати."});
        else if(vm.CurrentPrintLedger.History.Count==0)
            list.Children.Add(new TextBlock{Text="Подтверждённых или отправленных заданий пока нет."});
        else foreach(var job in vm.CurrentPrintLedger.History)
        {
            var card=new Border();card.Classes.Add("settings-card");
            var content=new StackPanel{Spacing=4};
            content.Children.Add(new TextBlock
            {
                Text=$"{job.Utc.ToLocalTime():dd.MM.yyyy HH:mm}  ·  {job.Printer}  ·  {job.Result}",
                FontWeight=Avalonia.Media.FontWeight.SemiBold
            });
            content.Children.Add(new TextBlock
            {
                Text=$"{job.CardCount} карточек · {job.Sheets} листов · {job.Mode}",
                FontSize=12
            });
            var titles=job.EntryIds.Select(id=>vm.VaultRecords.FirstOrDefault(x=>x.Uuid==id)?.Title??
                ("UUID "+id.ToString("N")[..8])).ToArray();
            content.Children.Add(new TextBlock{Text=string.Join(" · ",titles),FontSize=12,
                TextWrapping=Avalonia.Media.TextWrapping.Wrap});
            card.Child=content;list.Children.Add(card);
        }
        root.Children.Add(new ScrollViewer{Content=list,Margin=new Thickness(12),
            VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto});
        Content=root;
    }
}
