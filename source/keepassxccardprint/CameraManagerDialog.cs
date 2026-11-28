using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

// Managing saved profiles never opens a live scanner session. Only the explicit
// Choose action changes the scanner's selected camera.
internal static class CameraManagerDialog
{
    internal sealed record Result(string? SelectedKey,HashSet<string> ChangedIds);
    private sealed record Item(string Key,string Name,string Type,string Status,NetworkCameraProfile? Profile);
    private static string T(string ru,string uk,string en)=>ScannerResultDialog.T(ru,uk,en);
    internal static async Task<Result> Show(Window owner,string[] local,string? selectedKey,Func<Task<string[]>>? refreshLocal)
    {
        var win=new Window{Title=T("Управление камерами","Керування камерами","Manage cameras"),Width=650,Height=350,MinWidth=460,MinHeight=270,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
        PrintWindowIcons.Attach(win);
        var root=new Grid{Margin=new Thickness(12),RowDefinitions=new RowDefinitions("Auto,Auto,*,Auto,Auto"),RowSpacing=6};win.Content=root;
        var help=new TextBlock{Text=T("Локальные камеры обнаруживаются автоматически. Сетевые камеры: ONVIF или прямой адрес. Выбор камеры не запускает её.","Локальні камери виявляються автоматично. Мережеві камери: ONVIF або пряма адреса. Вибір камери не запускає її.","Local cameras are detected automatically. Network cameras: ONVIF or direct URL. Choosing a camera does not start it."),TextWrapping=TextWrapping.Wrap};root.Children.Add(help);
        var actions=new WrapPanel();Grid.SetRow(actions,1);root.Children.Add(actions);
        Button Button(string title,Icon icon,Panel panel){var b=PrintDialogButtons.Create(title,ActionIconVectors.Create(icon));b.Margin=new Thickness(0,0,5,2);panel.Children.Add(b);return b;}
        Button Tool(string title,Icon icon)
        {
            var button=new Button{Content=ActionIconVectors.Create(icon,16),Margin=new Thickness(0,0,5,2)};
            button.Classes.Add("preview-tool");ToolTip.SetTip(button,title);Avalonia.Automation.AutomationProperties.SetName(button,title);actions.Children.Add(button);return button;
        }
        var add=Tool(T("Добавить сетевую камеру","Додати мережеву камеру","Add network camera"),Icon.Add);
        var addOnvif=Tool(T("Поиск и добавление ONVIF","Пошук і додавання ONVIF","Discover and add ONVIF"),Icon.Link);
        var edit=Tool(T("Изменить камеру","Змінити камеру","Edit camera"),Icon.Rename);
        var delete=Tool(T("Удалить камеру","Видалити камеру","Delete camera"),Icon.Delete);
        var refresh=Tool(T("Обновить список локальных камер","Оновити список локальних камер","Refresh local cameras"),Icon.ArrowSync);
        if(refreshLocal is null)ToolTip.SetTip(refresh,T("Для обновления остановите работающую камеру.","Для оновлення зупиніть активну камеру.","Stop the running camera before refreshing."));
        var header=new Grid{ColumnDefinitions=new ColumnDefinitions("16,*,100,100"),ColumnSpacing=8,Margin=new Thickness(6,4)};
        void Header(string text,int col){var label=new TextBlock{Text=text,FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};Grid.SetColumn(label,col);header.Children.Add(label);}
        Header(T("Название","Назва","Name"),1);Header(T("Тип","Тип","Type"),2);Header(T("Состояние","Стан","Status"),3);var table=new Grid{RowDefinitions=new RowDefinitions("Auto,*")};table.Children.Add(header);
        var list=new ListBox{HorizontalAlignment=HorizontalAlignment.Stretch,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0)};
        list.Classes.Add("mapping-list");list.Classes.Add("camera-list");Grid.SetRow(list,1);table.Children.Add(list);
        var tableCard=new Border{Padding=new Thickness(0),Child=table};tableCard.Classes.Add("settings-card");Grid.SetRow(tableCard,2);root.Children.Add(tableCard);
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap};Grid.SetRow(feedback,3);root.Children.Add(feedback);
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};Grid.SetRow(footer,4);root.Children.Add(footer);
        var choose=Button(T("Выбрать камеру","Обрати камеру","Choose camera"),Icon.Checkmark,footer);var close=Button(T("Закрыть","Закрити","Close"),Icon.Dismiss,footer);
        var changed=new HashSet<string>();string? chosen=null;List<NetworkCameraProfile> profiles=[];List<Item> items=[];bool storeOk=true,busy=false;
        try{profiles=NetworkCameraProfiles.Load();}catch{storeOk=false;feedback.Text=T("Не удалось открыть хранилище сетевых камер. Существующие данные не изменены.","Не вдалося відкрити сховище мережевих камер. Наявні дані не змінено.","Could not open network camera storage. Existing data has not been changed.");}
        Item? Current()=>list.SelectedIndex>=0&&list.SelectedIndex<items.Count?items[list.SelectedIndex]:null;
        void Controls(){refresh.IsEnabled=refreshLocal is not null&&!busy;add.IsEnabled=addOnvif.IsEnabled=storeOk&&!busy;edit.IsEnabled=delete.IsEnabled=storeOk&&!busy&&Current()?.Profile is not null;choose.IsEnabled=!busy&&Current() is not null;list.IsEnabled=!busy;close.IsEnabled=!busy;}
        void Reload(string? key)
        {
            items=local.Select(name=>new Item(name,name,T("Локальная","Локальна","Local"),T("Обнаружена","Виявлена","Detected"),null))
                .Concat(profiles.Select(p=>new Item("network:"+p.Id,p.Name,Uri.TryCreate(p.Address,UriKind.Absolute,out var uri)?uri.Scheme.ToUpperInvariant():T("Сетевая","Мережева","Network"),T("Сохранена","Збережена","Saved"),p))).ToList();
            list.ItemsSource=items.Select(item=>
            {
                var row=new Grid{ColumnDefinitions=new ColumnDefinitions("16,*,100,100"),ColumnSpacing=8,Margin=new Thickness(6,2)};row.Children.Add(ActionIconVectors.Create(item.Profile is null?Icon.Camera:Icon.Link));
                void Cell(string text,int col){var label=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(label,col);row.Children.Add(label);}
                Cell(item.Name,1);Cell(item.Type,2);Cell(item.Status,3);
                return new ListBoxItem{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            }).ToArray();
            list.SelectedIndex=items.FindIndex(i=>i.Key==key);Controls();
            if(items.Count==0&&storeOk)feedback.Text=T("Камер нет. Добавьте сетевую камеру или подключите локальную.","Камер немає. Додайте мережеву камеру або підключіть локальну.","No cameras. Add a network camera or connect a local camera.");
        }
        bool Save(List<NetworkCameraProfile> next)
        {
            try{NetworkCameraProfiles.Save(next);profiles=next;return true;}
            catch{feedback.Text=T("Не удалось сохранить список камер. Изменения не применены.","Не вдалося зберегти список камер. Зміни не застосовано.","Could not save the camera list. Changes were not applied.");return false;}
        }
        async Task Edit(NetworkCameraProfile? old,bool startOnvif=false)
        {
            if(busy||!storeOk)return;busy=true;Controls();
            try
            {
                var p=await NetworkCameraDialog.Edit(win,old,profiles,updated=>Save(profiles.Where(x=>x.Id!=updated.Id).Append(updated).OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList()),startOnvif);if(p is null)return;
                changed.Add(p.Id);feedback.Text=T("Камера сохранена. Для запуска выберите её на вкладке «Сканирование».","Камеру збережено. Для запуску оберіть її на вкладці «Сканування».","Camera saved. Select it on the Scan tab to start.");Reload("network:"+p.Id);
            }
            finally{busy=false;Controls();}
        }
        refresh.Click+=async (_,_)=>
        {
            if(busy||refreshLocal is null)return;var key=Current()?.Key;busy=true;Controls();
            feedback.Text=T("Поиск локальных камер…","Пошук локальних камер…","Discovering local cameras…");
            try{local=await refreshLocal();feedback.Text="";Reload(key);}catch{feedback.Text=T("Не удалось обновить камеры","Не вдалося оновити камери","Could not refresh cameras");}
            finally{busy=false;Controls();}
        };
        add.Click+=async (_,_)=>await Edit(null);edit.Click+=async (_,_)=>{if(Current()?.Profile is { } p)await Edit(p);};
        addOnvif.Click+=async (_,_)=>await Edit(null,true);
        delete.Click+=async (_,_)=>
        {
            if(busy||!storeOk||Current()?.Profile is not { } p)return;
            busy=true;Controls();
            try
            {
                var confirm=new Window{Title=T("Удалить камеру?","Видалити камеру?","Delete camera?"),Width=420,SizeToContent=SizeToContent.Height,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};PrintWindowIcons.Attach(confirm);
                var body=new StackPanel{Margin=new Thickness(12),Spacing=10};confirm.Content=body;
                body.Children.Add(new TextBlock{Text=T("Удалить сохранённый профиль камеры и её учётные данные?","Видалити збережений профіль камери та її облікові дані?","Delete the saved camera profile and its credentials?")+"\n"+p.Name,TextWrapping=TextWrapping.Wrap});
                var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};body.Children.Add(buttons);bool yes=false;
                Button(T("Удалить","Видалити","Delete"),Icon.Dismiss,buttons).Click+=(_,_)=>{yes=true;confirm.Close();};Button(T("Отмена","Скасувати","Cancel"),Icon.Dismiss,buttons).Click+=(_,_)=>confirm.Close();
                await confirm.ShowDialog(win);
                if(yes&&Save(profiles.Where(x=>x.Id!=p.Id).ToList())){changed.Add(p.Id);feedback.Text=T("Камера удалена","Камеру видалено","Camera deleted");Reload(null);}
            }
            finally{busy=false;Controls();}
        };
        list.SelectionChanged+=(_,_)=>Controls();choose.Click+=(_,_)=>{chosen=Current()?.Key;win.Close();};close.Click+=(_,_)=>win.Close();win.Closing+=(_,e)=>{if(busy)e.Cancel=true;};
        Reload(selectedKey);await win.ShowDialog(owner);return new(chosen,changed);
    }
}
