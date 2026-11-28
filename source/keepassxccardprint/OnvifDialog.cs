using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

internal static class OnvifDialog
{
    private static string T(string ru,string uk,string en)=>ScannerResultDialog.T(ru,uk,en);
    internal sealed record LinkData(string Endpoint,string User,string Password,bool AllowHttp,string ProfileToken,string Service,string Identity,string Name,string StreamUrl);
    private static Window Window(Window owner,string title,double height)
    {
        var w=new Window{Title=title,Width=680,Height=height,MinWidth=360,MinHeight=240,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false};
        PrintWindowIcons.Attach(w);var screen=owner.Screens.ScreenFromWindow(owner)??owner.Screens.Primary;
        if(screen is not null){var limit=DialogScreenPolicy.ClientLimit(screen.WorkingArea,screen.Scaling,new Size(24,64));w.MinWidth=Math.Min(w.MinWidth,limit.Width);w.MinHeight=Math.Min(w.MinHeight,limit.Height);w.Width=Math.Min(w.Width,limit.Width);w.Height=Math.Min(w.Height,limit.Height);}
        return w;
    }
    private static Button Button(Panel panel,string text,Icon icon,Action? action=null)
    {var b=PrintDialogButtons.Create(text,ActionIconVectors.Create(icon));b.Margin=new Thickness(0,0,5,3);panel.Children.Add(b);if(action is not null)b.Click+=(_,_)=>action();return b;}
    private static void Field(Panel panel,string label,Control control)
    {var row=new Grid{ColumnDefinitions=new ColumnDefinitions("165,*"),ColumnSpacing=8};row.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(control,1);control.HorizontalAlignment=HorizontalAlignment.Stretch;row.Children.Add(control);panel.Children.Add(row);}
    private static TextBlock Info(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap};
    private static string Failure(Exception e)=>e switch
    {
        OperationCanceledException=>T("Отменено или истёк тайм-аут.","Скасовано або минув тайм-аут.","Cancelled or timed out."),
        OnvifException {Message:"Authentication"}=>T("Ошибка авторизации ONVIF.","Помилка авторизації ONVIF.","ONVIF authentication failed."),
        HttpRequestException {HttpRequestError:HttpRequestError.SecureConnectionError}=>T("Не удалось проверить TLS-сертификат ONVIF.","Не вдалося перевірити TLS-сертифікат ONVIF.","ONVIF TLS verification failed."),
        _=>T("Операция ONVIF не выполнена. Проверьте адрес, права и поддержку функции камерой.","Операцію ONVIF не виконано. Перевірте адресу, права й підтримку функції камерою.","ONVIF operation failed. Check the endpoint, permissions and camera capabilities.")
    };
    private static (StackPanel Body,WrapPanel Footer) Layout(Window w)
    {
        var root=new DockPanel{LastChildFill=true};var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(12,4,12,10)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var body=new StackPanel{Margin=new Thickness(12),Spacing=6};root.Children.Add(new ScrollViewer{Content=body,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto});w.Content=root;return(body,footer);
    }
    internal static async Task<LinkData?> Link(Window owner,NetworkCameraProfile? initial,string streamUrl,string streamUser,string streamPassword)
    {
        var win=Window(owner,T("Подключение ONVIF","Підключення ONVIF","ONVIF connection"),520);var (body,footer)=Layout(win);
        var form=new StackPanel{Spacing=6};body.Children.Add(form);
        var discovered=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};var endpoint=new TextBox{MaxLength=2048,Text=initial?.OnvifAddress??"",PlaceholderText="192.168.1.10:80 / https://…"};
        if(string.IsNullOrWhiteSpace(endpoint.Text)&&Uri.TryCreate(streamUrl,UriKind.Absolute,out var uri))endpoint.Text=uri.HostNameType==UriHostNameType.IPv6?"["+uri.IdnHost+"]":uri.IdnHost;
        bool linked=!string.IsNullOrEmpty(initial?.OnvifAddress);
        var user=new TextBox{MaxLength=128,Text=linked?initial!.OnvifUser:streamUser};var pass=new TextBox{MaxLength=512,PasswordChar='●',Text=linked?initial!.OnvifPassword:streamPassword};
        var plain=new CheckBox{Content=T("Разрешить незашифрованное управление ONVIF (HTTP)","Дозволити незашифроване керування ONVIF (HTTP)","Allow unencrypted ONVIF control (HTTP)"),IsChecked=linked&&initial!.OnvifAllowHttp};
        var actions=new WrapPanel();form.Children.Add(actions);var search=Button(actions,T("Поиск в сети","Пошук у мережі","Discover"),Icon.ArrowSync);var connect=Button(actions,T("Подключиться","Підключитися","Connect"),Icon.Link);
        Field(form,T("Обнаруженные камеры","Виявлені камери","Discovered cameras"),discovered);Field(form,T("Адрес ONVIF","Адреса ONVIF","ONVIF endpoint"),endpoint);Field(form,T("Пользователь","Користувач","Username"),user);Field(form,T("Пароль","Пароль","Password"),pass);form.Children.Add(plain);
        var info=Info("");form.Children.Add(info);var profiles=new ComboBox();Field(form,T("Видеопоток","Відеопотік","Media profile"),profiles);
        var feedback=Info("");body.Children.Add(feedback);var progress=new ProgressBar{IsIndeterminate=true,Height=3,IsVisible=false};body.Children.Add(progress);
        var choose=Button(footer,T("Выбрать поток","Обрати потік","Select stream"),Icon.Checkmark);var cancel=Button(footer,T("Отмена","Скасувати","Cancel"),Icon.Dismiss);choose.IsEnabled=false;
        LinkData? result=null;OnvifClient? client=null;CancellationTokenSource? operation=null;
        void Invalidate(){client?.Dispose();client=null;profiles.ItemsSource=null;info.Text="";choose.IsEnabled=false;}
        void Busy(bool b){form.IsEnabled=!b;choose.IsEnabled=!b&&client is not null&&profiles.SelectedItem is OnvifStream;progress.IsVisible=b;}
        async Task Run(Func<CancellationToken,Task> work)
        {
            if(operation is not null)return;using var token=new CancellationTokenSource();operation=token;Busy(true);feedback.Text="";
            try{await work(token.Token);}catch(Exception ex){feedback.Text=Failure(ex);}finally{operation=null;Busy(false);}
        }
        endpoint.TextChanged+=(_,_)=>{plain.IsChecked=false;Invalidate();};user.TextChanged+=(_,_)=>Invalidate();pass.TextChanged+=(_,_)=>Invalidate();plain.IsCheckedChanged+=(_,_)=>Invalidate();
        discovered.SelectionChanged+=(_,_)=>{if(discovered.SelectedItem is OnvifDiscovery.Camera camera){endpoint.Text=camera.Endpoint;plain.IsChecked=false;}};
        profiles.SelectionChanged+=(_,_)=>choose.IsEnabled=operation is null&&client is not null&&profiles.SelectedItem is OnvifStream;
        search.Click+=async (_,_)=>await Run(async ct=>{var found=await OnvifDiscovery.Find(ct);discovered.ItemsSource=found;feedback.Text=found.Length==0?T("Ответов ONVIF нет. Укажите адрес и порт ONVIF вручную. Проверьте, что устройства в одной сети и UDP 3702 не блокируется.","Відповідей ONVIF немає. Вкажіть адресу й порт ONVIF вручну. Перевірте, що пристрої в одній мережі та UDP 3702 не блокується.","No ONVIF replies. Enter the ONVIF address and port manually. Check that devices share a network and UDP 3702 is not blocked."):T("Найдено адресов ONVIF: ","Знайдено адрес ONVIF: ","Discovered ONVIF endpoints: ")+found.Length;if(found.Length==1)discovered.SelectedIndex=0;});
        connect.Click+=async (_,_)=>await Run(async ct=>
        {
            Invalidate();var c=new OnvifClient(endpoint.Text??"",user.Text??"",pass.Text??"",plain.IsChecked==true,initial?.TimeoutSeconds??10);
            try{await c.Connect(ct);ct.ThrowIfCancellationRequested();client=c;profiles.ItemsSource=c.Streams;profiles.SelectedIndex=Math.Max(0,c.Streams.ToList().FindIndex(s=>s.Token==initial?.OnvifProfileToken));info.Text=c.DeviceName+" · "+c.Firmware+" · "+string.Join(", ",new[]{c.Has(OnvifClient.Media2)?"Media2":"Media",c.Has(OnvifClient.Imaging)?"Imaging":"",c.Has(OnvifClient.Ptz)?"PTZ":""}.Where(x=>x.Length>0));}
            catch{c.Dispose();throw;}
        });
        choose.Click+=async (_,_)=>await Run(async ct=>
        {
            if(client is not { } c||profiles.SelectedItem is not OnvifStream s)return;
            string url=await c.StreamUri(s,ct);ct.ThrowIfCancellationRequested();
            result=new(c.Endpoint.AbsoluteUri,user.Text??"",pass.Text??"",plain.IsChecked==true,s.Token,s.Service.NamespaceName,c.Identity,c.DeviceName,url);
            // The outer network editor must still approve its independent RTSP transport.
            Avalonia.Threading.Dispatcher.UIThread.Post(()=>win.Close());
        });
        cancel.Click+=(_,_)=>{if(operation is not null)operation.Cancel();else win.Close();};win.Closing+=(_,e)=>{if(operation is not null){operation.Cancel();e.Cancel=true;}};
        await win.ShowDialog(owner);client?.Dispose();pass.Text="";return result;
    }
    private static string Label(string path)
    {
        string n=path.Split('/')[^1];return n switch
        {
            "Brightness"=>T("Яркость","Яскравість","Brightness"),"ColorSaturation"=>T("Насыщенность","Насиченість","Saturation"),"Contrast"=>T("Контраст","Контраст","Contrast"),"Sharpness"=>T("Резкость","Різкість","Sharpness"),
            "AutoFocusMode"=>T("Автофокус","Автофокус","Autofocus"),"ExposureTime"=>T("Выдержка","Витримка","Exposure time"),"Gain"=>T("Усиление","Підсилення","Gain"),"Iris"=>T("Диафрагма","Діафрагма","Iris"),
            "Mode" when path.StartsWith("Exposure/")=>T("Экспозиция","Експозиція","Exposure"),"Mode" when path.StartsWith("WhiteBalance/")=>T("Баланс белого","Баланс білого","White balance"),"Mode" when path.StartsWith("BacklightCompensation/")=>T("Контровой свет","Контрове світло","Backlight compensation"),"Mode" when path.StartsWith("WideDynamicRange/")=>"WDR",
            "Level" when path.StartsWith("BacklightCompensation/")=>"BLC · "+T("Уровень","Рівень","Level"),"Level" when path.StartsWith("WideDynamicRange/")=>"WDR · "+T("Уровень","Рівень","Level"),"Level" when path.Contains("NoiseReduction")=>T("Шумоподавление","Шумозаглушення","Noise reduction"),
            "IrCutFilter"=>T("ИК-фильтр","ІЧ-фільтр","IR cut filter"),"Resolution"=>T("Разрешение","Роздільна здатність","Resolution"),"Quality"=>T("Качество","Якість","Quality"),"BitrateLimit"=>T("Битрейт (кбит/с)","Бітрейт (кбіт/с)","Bitrate (kbps)"),"FrameRateLimit"=>"FPS","GovLength" or "@GovLength"=>"GOP",
            "EncodingInterval"=>T("Интервал кодирования","Інтервал кодування","Encoding interval"),"DefaultSpeed"=>T("Скорость фокуса","Швидкість фокуса","Focus speed"),"NearLimit"=>T("Ближняя граница","Ближня межа","Near limit"),"FarLimit"=>T("Дальняя граница","Дальня межа","Far limit"),"CrGain"=>"Cr", "CbGain"=>"Cb",_=>path.Replace("/"," · ")
        };
    }
    internal static async Task Control(Window owner,NetworkCameraProfile profile)
    {
        var win=Window(owner,T("Управление ONVIF","Керування ONVIF","ONVIF controls"),650);var (body,footer)=Layout(win);
        var feedback=Info("");body.Children.Add(feedback);var progress=new ProgressBar{IsIndeterminate=true,Height=3,IsVisible=false};body.Children.Add(progress);
        var tools=new WrapPanel();body.Children.Add(tools);var reload=Button(tools,T("Обновить","Оновити","Refresh"),Icon.ArrowSync);
        var content=new StackPanel{Spacing=8};body.Children.Add(content);var close=Button(footer,T("Закрыть","Закрити","Close"),Icon.Dismiss);
        OnvifClient? client=null;CancellationTokenSource? operation=null;
        async Task Run(Func<CancellationToken,Task> action)
        {
            if(operation is not null)return;using var token=new CancellationTokenSource();operation=token;reload.IsEnabled=content.IsEnabled=false;progress.IsVisible=true;feedback.Text="";
            try{await action(token.Token);}catch(Exception ex){feedback.Text=Failure(ex);}finally{operation=null;reload.IsEnabled=content.IsEnabled=true;progress.IsVisible=false;}
        }
        void SettingsPanel(OnvifSettings settings,string title)
        {
            var section=new StackPanel{Spacing=5};content.Children.Add(new ScannerView.ScannerSection{Header=title,Content=section,IsExpanded=true});
            var fields=new StackPanel{Spacing=4};section.Children.Add(fields);var read=new Dictionary<string,Func<string>>();
            var consent=new CheckBox{Content=T("Подтверждаю изменение настроек на самой камере","Підтверджую зміну налаштувань на самій камері","Confirm changes on the camera itself")};
            void Build()
            {
                fields.Children.Clear();read.Clear();foreach(var f in settings.Fields())
                {
                    if(f.Choices.Length>0){var combo=new ComboBox{ItemsSource=f.Choices,SelectedItem=f.Value,IsEnabled=f.Writable};Field(fields,Label(f.Path),combo);read[f.Path]=()=>combo.SelectedItem as string??f.Value;}
                    else{var input=new TextBox{Text=f.Value,IsReadOnly=!f.Writable};ToolTip.SetTip(input,$"{f.Min} – {f.Max}");Field(fields,Label(f.Path),input);read[f.Path]=()=>input.Text??f.Value;}
                }
                if(read.Count==0)fields.Children.Add(Info(T("Нет доступных параметров.","Немає доступних параметрів.","No supported options.")));
            }
            Build();section.Children.Add(consent);var buttons=new WrapPanel();section.Children.Add(buttons);
            var apply=Button(buttons,T("Применить","Застосувати","Apply"),Icon.Checkmark);var restore=Button(buttons,T("Вернуть исходные","Повернути початкові","Restore original"),Icon.ArrowSync);apply.IsEnabled=restore.IsEnabled=false;
            consent.IsCheckedChanged+=(_,_)=>apply.IsEnabled=restore.IsEnabled=consent.IsChecked==true;
            async Task Apply(bool original)
            {
                if(consent.IsChecked!=true)return;var values=read.ToDictionary(x=>x.Key,x=>x.Value());consent.IsChecked=false;
                await Run(async ct=>{await settings.Apply(values,original,ct);Build();feedback.Text=T("Настройки перечитаны с камеры.","Налаштування перечитано з камери.","Settings read back from camera.");});
            }
            apply.Click+=async (_,_)=>await Apply(false);restore.Click+=async (_,_)=>await Apply(true);
        }
        void MotionPanel(OnvifMotion motion)
        {
            if(!motion.Pan&&!motion.Zoom&&!motion.Focus&&!motion.Presets&&!motion.Home)return;
            var section=new StackPanel{Spacing=5};content.Children.Add(new ScannerView.ScannerSection{Header=T("Фокус и PTZ","Фокус і PTZ","Focus and PTZ"),Content=section,IsExpanded=true});
            var speed=new Slider{Minimum=.05,Maximum=1,Value=.2};Field(section,T("Шаг / скорость","Крок / швидкість","Step / speed"),speed);
            var buttons=new WrapPanel();section.Children.Add(buttons);
            void Command(string label,Icon icon,Func<CancellationToken,Task> call){var b=Button(buttons,label,icon);b.Click+=async (_,_)=>await Run(call);}
            if(motion.Pan){Command("←",Icon.ChevronLeft,ct=>motion.Move(-1,0,0,speed.Value,ct));Command("↑",Icon.ChevronUp,ct=>motion.Move(0,1,0,speed.Value,ct));Command("↓",Icon.ChevronDown,ct=>motion.Move(0,-1,0,speed.Value,ct));Command("→",Icon.ChevronRight,ct=>motion.Move(1,0,0,speed.Value,ct));}
            if(motion.Zoom){Command("Zoom +",Icon.ZoomIn,ct=>motion.Move(0,0,1,speed.Value,ct));Command("Zoom −",Icon.ZoomOut,ct=>motion.Move(0,0,-1,speed.Value,ct));}
            if(motion.HasPtz)Command(T("Стоп PTZ","Стоп PTZ","Stop PTZ"),Icon.Dismiss,motion.Stop);
            if(motion.Focus){Command(T("Ближе","Ближче","Focus near"),Icon.ChevronLeft,ct=>motion.FocusMove(-1,speed.Value,ct));Command(T("Дальше","Далі","Focus far"),Icon.ChevronRight,ct=>motion.FocusMove(1,speed.Value,ct));Command(T("Стоп фокус","Стоп фокус","Stop focus"),Icon.Dismiss,motion.FocusStop);}
            if(motion.Home)Command(T("Домой","Додому","Home"),Icon.ArrowSync,motion.GotoHome);
            if(motion.Presets)
            {
                var presets=new ComboBox();Field(section,T("Позиции","Позиції","Presets"),presets);var name=new TextBox{MaxLength=80};Field(section,T("Новая позиция","Нова позиція","New preset"),name);
                var confirm=new CheckBox{Content=T("Подтверждаю сохранение / удаление позиции","Підтверджую збереження / видалення позиції","Confirm saving / deleting preset")};section.Children.Add(confirm);
                var actions=new WrapPanel();section.Children.Add(actions);async Task Refresh(CancellationToken ct){presets.ItemsSource=await motion.GetPresets(ct);presets.SelectedIndex=0;}
                var refresh=Button(actions,T("Обновить","Оновити","Refresh"),Icon.ArrowSync);refresh.Click+=async (_,_)=>await Run(Refresh);
                var go=Button(actions,T("Перейти","Перейти","Go to"),Icon.Checkmark);go.Click+=async (_,_)=>{if(presets.SelectedItem is OnvifMotion.Preset p)await Run(ct=>motion.Goto(p,ct));};
                var save=Button(actions,T("Сохранить","Зберегти","Save"),Icon.Add);save.Click+=async (_,_)=>{if(confirm.IsChecked==true&&!string.IsNullOrWhiteSpace(name.Text)){confirm.IsChecked=false;await Run(async ct=>{await motion.Save(name.Text!,ct);await Refresh(ct);});}};
                var remove=Button(actions,T("Удалить","Видалити","Delete"),Icon.Delete);remove.Click+=async (_,_)=>{if(confirm.IsChecked==true&&presets.SelectedItem is OnvifMotion.Preset p){confirm.IsChecked=false;await Run(async ct=>{await motion.Remove(p,ct);await Refresh(ct);});}};
            }
        }
        async Task Load(CancellationToken ct)
        {
            content.Children.Clear();client?.Dispose();var c=new OnvifClient(profile.OnvifAddress,profile.OnvifUser,profile.OnvifPassword,profile.OnvifAllowHttp,profile.TimeoutSeconds);client=c;
            await c.Connect(ct);var stream=c.Streams.FirstOrDefault(s=>s.Token==profile.OnvifProfileToken&&s.Service.NamespaceName==profile.OnvifMediaService)??throw new OnvifException("Missing profile");
            feedback.Text=c.DeviceName+" · "+c.Firmware+" · "+stream.Label;
            var errors=new List<string>();
            foreach(bool imaging in new[]{true,false})
            {
                try{if(await OnvifSettings.Load(c,stream,imaging,ct) is { } settings)SettingsPanel(settings,imaging?T("Изображение","Зображення","Imaging"):T("Видеопоток","Відеопотік","Video encoder"));}
                catch(Exception) when(!ct.IsCancellationRequested){errors.Add(imaging?"Imaging":"Media");}
            }
            var motion=new OnvifMotion(c,stream);await motion.Load(ct);MotionPanel(motion);
            if(errors.Count>0)feedback.Text+="\n"+T("Недоступные настройки: ","Недоступні налаштування: ","Unavailable settings: ")+string.Join(", ",errors);
            if(content.Children.Count==0)content.Children.Add(Info(T("Камера не предоставила управляемых параметров.","Камера не надала керованих параметрів.","The camera did not expose controllable settings.")));
        }
        reload.Click+=async (_,_)=>await Run(Load);close.Click+=(_,_)=>{if(operation is not null)operation.Cancel();else win.Close();};win.Closing+=(_,e)=>{if(operation is not null){operation.Cancel();e.Cancel=true;}};
        win.Opened+=async (_,_)=>await Run(Load);await win.ShowDialog(owner);client?.Dispose();
    }
}
