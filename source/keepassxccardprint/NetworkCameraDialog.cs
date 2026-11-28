using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

internal static class NetworkCameraDialog
{
    private static string T(string ru,string uk,string en)=>ScannerResultDialog.T(ru,uk,en);
    internal static string StateText(NetworkCameraState state)=>state switch
    {
        NetworkCameraState.Connecting=>T("Подключение…","Підключення…","Connecting…"),
        NetworkCameraState.Receiving=>T("Кадры поступают","Кадри надходять","Receiving frames"),
        NetworkCameraState.Retrying=>T("Переподключение…","Перепідключення…","Reconnecting…"),
        NetworkCameraState.Stopped=>T("Остановлено","Зупинено","Stopped"),
        NetworkCameraState.Authentication=>T("Ошибка авторизации","Помилка авторизації","Authentication failed"),
        NetworkCameraState.Certificate=>T("Не удалось проверить TLS-сертификат","Не вдалося перевірити TLS-сертифікат","TLS certificate verification failed"),
        NetworkCameraState.DecoderMissing=>T("FFmpeg не установлен. Скачайте его или выберите SharpRTSP.","FFmpeg не встановлено. Завантажте його або оберіть SharpRTSP.","FFmpeg is not installed. Download it or select SharpRTSP."),
        NetworkCameraState.SystemCodecMissing=>T("Системный кодек отсутствует. Выберите другой декодер.","Системний кодек відсутній. Оберіть інший декодер.","System codec missing. Select another decoder."),
        NetworkCameraState.VideoInitializationFailed=>T("Ошибка инициализации видеодекодера","Помилка ініціалізації відеодекодера","Video decoder initialization failed"),
        NetworkCameraState.VideoDecodeFailed=>T("Ошибка декодирования видео","Помилка декодування відео","Video decoding failed"),
        NetworkCameraState.Unsupported=>T("Неподдерживаемый формат или превышен лимит изображения","Непідтримуваний формат або перевищено ліміт зображення","Unsupported format or image limit exceeded"),
        NetworkCameraState.Timeout=>T("Истекло время ожидания кадра","Минув час очікування кадру","Frame timeout"),
        _=>T("Не удалось получить кадр. Проверьте адрес, сеть и формат.","Не вдалося отримати кадр. Перевірте адресу, мережу та формат.","Could not receive a frame. Check address, network and format.")
    };
    internal static async Task<NetworkCameraProfile?> Edit(Window owner,NetworkCameraProfile? initial,IReadOnlyList<NetworkCameraProfile> existing,Func<NetworkCameraProfile,bool> persist,bool startOnvif=false)
    {
        var win=new Window{Title=initial is null?T("Добавить сетевую камеру","Додати мережеву камеру","Add network camera"):T("Изменить сетевую камеру","Змінити мережеву камеру","Edit network camera"),Width=680,MinWidth=420,Height=700,MinHeight=260,MaxHeight=800,SizeToContent=SizeToContent.Manual,WindowStartupLocation=WindowStartupLocation.CenterOwner,WindowDecorations=WindowDecorations.Full,ShowInTaskbar=false};
        PrintWindowIcons.Attach(win);
        var stack=new StackPanel{Spacing=8,Margin=new Thickness(12)};
        var form=new StackPanel{Spacing=6};
        var name=new TextBox{MaxLength=80};var address=new TextBox{MaxLength=2048,PlaceholderText="rtsps://192.168.1.10:322/…"};
        var user=new TextBox{MaxLength=128};var password=new TextBox{PasswordChar='●',MaxLength=512};
        var allow=new CheckBox{Content=T("Разрешить незашифрованное HTTP / RTSP","Дозволити незашифроване HTTP / RTSP","Allow unencrypted HTTP / RTSP")};
        var udp=new CheckBox{Content=T("RTSP: RTP через UDP (если TCP не поддерживается)","RTSP: RTP через UDP (якщо TCP не підтримується)","RTSP: RTP over UDP (if TCP is unsupported)")};
        var engine=new ComboBox{ItemsSource=new[]{"SharpRTSP","FFmpeg"},SelectedIndex=0};
        var decoder=new ComboBox();var decoderBox=new StackPanel{Spacing=4};
        var decoderFallback=new CheckBox{Content=T("Резервный декодер при отказе","Резервний декодер при відмові","Fallback decoder on failure"),IsChecked=false};
        var decoderChoices=new List<(string Id,string Label)>{("auto",T("Автоматически","Автоматично","Automatic")),("hardware",T("Только аппаратные","Лише апаратні","Hardware only")),("software",T("Только программные","Лише програмні","Software only"))};
        var available=VideoDecoderManager.Available();
        decoderChoices.RemoveAll(c=>c.Id=="hardware"&&!available.Any(d=>d.Hardware)||c.Id=="software"&&!available.Any(d=>!d.Hardware));
        decoderChoices.AddRange(available.Select(c=>(c.Id,c.Label)));
        void SelectDecoder(string id)
        {
            decoder.ItemsSource=decoderChoices.Select(c=>c.Label).ToArray();decoder.SelectedIndex=decoderChoices.FindIndex(c=>c.Id==id);
        }
        SelectDecoder("auto");
        string savedDecoder="auto";
        string DecoderId()=>decoder.SelectedIndex>=0&&decoder.SelectedIndex<decoderChoices.Count?decoderChoices[decoder.SelectedIndex].Id:savedDecoder;
        var engineBox=new StackPanel{Spacing=4};var engineStatus=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11};
        var reconnect=new CheckBox{Content=T("Автоматическое переподключение","Автоматичне перепідключення","Automatic reconnect"),IsChecked=true};
        var pin=new TextBox{MaxLength=160,PlaceholderText=T("Пусто: системное доверие","Порожньо: системна довіра","Empty: system trust")};
        var ca=new TextBox{IsReadOnly=true,PlaceholderText=T("Пусто: системное доверие","Порожньо: системна довіра","Empty: system trust")};
        var fps=new NumericUpDown{Minimum=1,Maximum=12,Increment=1,Value=5};var timeout=new NumericUpDown{Minimum=3,Maximum=60,Increment=1,Value=10};
        var feedback=new TextBlock{TextWrapping=TextWrapping.Wrap};
        var testDetail=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11};var testDetails=new ScannerView.ScannerSection{Header=T("Подробности проверки","Подробиці перевірки","Test details"),Content=testDetail,IsVisible=false};var security=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};
        var pinHint=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11,Text=T("Сверьте отпечаток с сертификатом на телефоне. Полученный из сети отпечаток сам по себе не доказывает подлинность.","Звірте відбиток із сертифікатом на телефоні. Отриманий з мережі відбиток сам по собі не доводить справжність.","Compare with the certificate on the phone. A fingerprint received from the network alone does not establish identity.")};
        var root=new DockPanel{LastChildFill=true};var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(12,4,12,10)};
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        // Fixed footer; a finite viewport is essential on short/high-DPI screens.
        var scroll=new ScrollViewer{Content=stack,MinHeight=0,HorizontalContentAlignment=HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};
        root.Children.Add(scroll);win.Content=root;
        var screen=owner.Screens.ScreenFromWindow(owner)??owner.Screens.Primary;
        if(screen is not null)
        {
            var limit=DialogScreenPolicy.ClientLimit(screen.WorkingArea,screen.Scaling,new Size(24,64));
            win.MinWidth=Math.Min(win.MinWidth,limit.Width);win.MinHeight=Math.Min(win.MinHeight,limit.Height);
            win.Width=Math.Min(win.Width,limit.Width);win.Height=Math.Min(win.Height,limit.Height);
        }
        Button Button(string text,Icon icon,Panel panel)
        {
            var b=PrintDialogButtons.Create(text,ActionIconVectors.Create(icon));b.Margin=new Thickness(0,0,5,3);panel.Children.Add(b);return b;
        }
        void Field(string label,Control control,Panel parent)
        {var g=new Grid{ColumnDefinitions=new ColumnDefinitions("155,*"),ColumnSpacing=8};g.Children.Add(new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center});control.HorizontalAlignment=HorizontalAlignment.Stretch;Grid.SetColumn(control,1);g.Children.Add(control);parent.Children.Add(g);}
        void Section(string title,Panel panel){panel.Children.Add(new TextBlock{Text=title,FontWeight=FontWeight.SemiBold,Margin=new Thickness(0,6,0,2)});}
        stack.Children.Add(form);Section(T("Подключение","Підключення","Connection"),form);
        Field(T("Название","Назва","Name"),name,form);Field(T("Адрес потока","Адреса потоку","Stream URL"),address,form);
        Field(T("Пользователь","Користувач","Username"),user,form);Field(T("Пароль","Пароль","Password"),password,form);
        var onvifRow=new WrapPanel();form.Children.Add(onvifRow);
        var linkOnvif=Button(T("Подключить ONVIF…","Підключити ONVIF…","Link ONVIF…"),Icon.Link,onvifRow);
        var unlinkOnvif=Button(T("Отключить ONVIF","Відключити ONVIF","Unlink ONVIF"),Icon.Dismiss,onvifRow);
        var onvifInfo=new TextBlock{TextWrapping=TextWrapping.Wrap};form.Children.Add(onvifInfo);
        var onvif=new NetworkCameraProfile();if(initial is not null)onvif.CopyOnvif(initial);
        void OnvifInfo(){unlinkOnvif.IsVisible=onvif.HasOnvif;onvifInfo.Text=onvif.HasOnvif?T("ONVIF подключён","ONVIF підключено","ONVIF linked"):T("Не подключён ONVIF","ONVIF не підключено","ONVIF not linked");}
        var securityBox=new StackPanel{Spacing=5};Section(T("Безопасность","Безпека","Security"),securityBox);
        securityBox.Children.Add(allow);
        Field(T("Движок RTSP","Рушій RTSP","RTSP engine"),engine,engineBox);
        Field(T("Видеодекодер","Відеодекодер","Video decoder"),decoder,decoderBox);decoderBox.Children.Add(decoderFallback);engineBox.Children.Add(decoderBox);
        var decoderHint=new TextBlock{FontSize=11,TextWrapping=TextWrapping.Wrap};decoderBox.Children.Add(decoderHint);
        void DecoderHint()
        {
            bool manual=decoder.SelectedIndex>=0&&DecoderId() is not ("auto" or "hardware" or "software");decoderFallback.IsEnabled=manual;decoderFallback.IsVisible=manual;
            decoderHint.Text=decoder.SelectedIndex<0?T("Сохранённый декодер недоступен. Выберите другой или сохраните прежнее значение без изменений.","Збережений декодер недоступний. Оберіть інший або збережіть попереднє значення без змін.","Saved decoder unavailable. Choose another or keep the saved value unchanged."):manual?T("Выбранный декодер обязателен, если резервирование выключено.","Обраний декодер обов’язковий, якщо резервування вимкнено.","Use the selected decoder only unless fallback is enabled."):
                T("Выбор по результатам теста: скорость и стабильность, без приоритета GPU.","Вибір за результатами тесту: швидкість і стабільність, без пріоритету GPU.","Select by tested speed and stability, without GPU preference.");
        }
        decoder.SelectionChanged+=(_,_)=>{decoderFallback.IsChecked=false;DecoderHint();};DecoderHint();
        decoderFallback.IsEnabled=false;
        var ffmpegActions=new WrapPanel();var manage=Button(T("Управление FFmpeg","Керування FFmpeg","Manage FFmpeg"),Icon.Settings,ffmpegActions);engineBox.Children.Add(engineStatus);engineBox.Children.Add(ffmpegActions);
        var securityRow=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*"),ColumnSpacing=6};var securityIcon=new ContentControl{VerticalAlignment=VerticalAlignment.Top};securityRow.Children.Add(securityIcon);Grid.SetColumn(security,1);securityRow.Children.Add(security);securityBox.Children.Add(securityRow);
        var receivedPin=new TextBox{IsReadOnly=true,IsVisible=false};
        var pinBox=new StackPanel{Spacing=4};Field(T("SHA-256 сертификата","SHA-256 сертифіката","Certificate SHA-256"),pin,pinBox);pinBox.Children.Add(pinHint);
        var pinActions=new WrapPanel();var probe=Button(T("Получить отпечаток","Отримати відбиток","Read fingerprint"),Icon.ArrowSync,pinActions);pinBox.Children.Add(pinActions);pinBox.Children.Add(receivedPin);securityBox.Children.Add(pinBox);
        var caBox=new StackPanel{Spacing=4};Field(T("PEM (CA / сертификат)","PEM (CA / сертифікат)","PEM (CA / certificate)"),ca,caBox);
        var caActions=new WrapPanel();var chooseCa=Button(T("Выбрать PEM","Обрати PEM","Choose PEM"),Icon.FolderOpen,caActions);var clearCa=Button(T("Очистить","Очистити","Clear"),Icon.Dismiss,caActions);caBox.Children.Add(caActions);securityBox.Children.Add(caBox);form.Children.Add(securityBox);
        var video=new StackPanel{Spacing=5};Section(T("Видео и подключение","Відео та підключення","Video and connection"),video);video.Children.Add(engineBox);video.Children.Add(udp);
        Field(T("Кадров/с (лимит)","Кадрів/с (ліміт)","FPS (limit)"),fps,video);Field(T("Тайм-аут, с","Тайм-аут, с","Timeout, s"),timeout,video);video.Children.Add(reconnect);
        form.Children.Add(video);
        ToolTip.SetTip(address,T("URL самого JPEG/MJPEG или RTSP-потока, а не веб-интерфейса. Профиль хранится локально под DPAPI.","URL самого JPEG/MJPEG або RTSP-потоку, а не вебінтерфейсу. Профіль зберігається локально під DPAPI.","URL of the JPEG/MJPEG or RTSP stream, not the web interface. Profile is stored locally using DPAPI."));
        stack.Children.Add(feedback);stack.Children.Add(testDetails);
        var test=Button(T("Проверить подключение","Перевірити підключення","Test connection"),Icon.ArrowSync,footer);var save=Button(T("Сохранить","Зберегти","Save"),Icon.Checkmark,footer);var close=Button(T("Отмена","Скасувати","Cancel"),Icon.Dismiss,footer);
        var progress=new ProgressBar{IsIndeterminate=true,Height=3,IsVisible=false};stack.Children.Add(progress);
        bool loading=false,busy=false;string id=Guid.NewGuid().ToString("N");
        string lastAddress="",lastEngine="sharp";
        NetworkCameraProfile? result=null;CancellationTokenSource? cancel=null;
        void Security()
        {
            Uri.TryCreate(address.Text,UriKind.Absolute,out var uri);string scheme=uri?.Scheme??"";
            bool rtsp=scheme is "rtsp" or "rtsps";bool ffmpeg=engine.SelectedIndex==1;
            engineBox.IsVisible=rtsp;decoderBox.IsVisible=!ffmpeg;ffmpegActions.IsVisible=ffmpeg;engineStatus.IsVisible=ffmpeg;engineStatus.Text=ffmpeg?FfmpegComponentDialog.Status():"";
            pinBox.IsVisible=scheme=="https"||scheme=="rtsps";pin.IsEnabled=scheme!="rtsps"||!ffmpeg||!string.IsNullOrWhiteSpace(pin.Text);caBox.IsVisible=scheme=="rtsps";probe.IsVisible=scheme=="https";allow.IsVisible=(scheme is "http" or "rtsp")||scheme=="rtsps"&&ffmpeg;allow.Content=scheme=="rtsps"?T("Разрешить непроверенный медиатранспорт FFmpeg (только демо)","Дозволити неперевірений медіатранспорт FFmpeg (лише демо)","Allow unverified FFmpeg transport (demo only)"):T("Разрешить незашифрованное HTTP / RTSP","Дозволити незашифроване HTTP / RTSP","Allow unencrypted HTTP / RTSP");udp.IsVisible=scheme=="rtsp";
            bool plain=scheme is "http" or "rtsp";
            security.ClearValue(TextBlock.ForegroundProperty);
            var marker=ActionIconVectors.Create(plain||scheme=="rtsps"&&ffmpeg?Icon.Warning:Icon.Link);
            if(plain||scheme=="rtsps"&&ffmpeg)marker.Foreground=new SolidColorBrush(Color.Parse("#C58A24"));securityIcon.Content=marker;
            security.Text=plain?T("Кадры и учётные данные могут быть перехвачены. Только демонстрационные карточки.","Кадри й облікові дані можуть бути перехоплені. Лише демонстраційні картки.","Frames and credentials can be intercepted. Demo cards only."):
                scheme=="rtsps"?(ffmpeg?T("FFmpeg запрашивает TLS, но Card Print не подтверждает защиту после возможного перенаправления. Только демонстрационные карточки; пароль виден локальным инспекторам процессов.","FFmpeg запитує TLS, але Card Print не підтверджує захист після можливого переспрямування. Лише демонстраційні картки; пароль видно локальним інспекторам процесів.","FFmpeg requests TLS, but Card Print cannot confirm protection after possible redirects. Demo cards only; local process inspectors may see credentials."):T("TLS, проверка имени и цепочки либо отпечатка, только TCP.","TLS, перевірка імені та ланцюжка або відбитка, лише TCP.","TLS, hostname and chain or pin validation, TCP only.")):
                scheme=="https"?T("TLS: системное доверие или подтверждённый отпечаток.","TLS: системна довіра або підтверджений відбиток.","TLS: system trust or verified fingerprint."):T("Введите полный адрес потока.","Введіть повну адресу потоку.","Enter the full stream URL.");
        }
        void Fill(NetworkCameraProfile p)
        {
            loading=true;lastAddress=p.Address.Trim();lastEngine=p.Engine;savedDecoder=p.Decoder;engine.SelectedIndex=p.Engine=="ffmpeg"?1:0;SelectDecoder(p.Decoder);decoderFallback.IsChecked=p.DecoderFallback&&(p.Decoder is not ("auto" or "hardware" or "software"));DecoderHint();receivedPin.Text="";receivedPin.IsVisible=false;id=p.Id;name.Text=p.Name;address.Text=p.Address;user.Text=p.User;password.Text=p.Password;allow.IsChecked=p.AllowPlaintext;udp.IsChecked=p.RtspUdp;pin.Text=p.Pin;ca.Text=p.CaFile;fps.Value=p.Fps;timeout.Value=p.TimeoutSeconds;reconnect.IsChecked=p.Reconnect;loading=false;Security();
        }
        NetworkCameraProfile? Read()
        {
            string raw=address.Text?.Trim()??"";Uri.TryCreate(raw,UriKind.Absolute,out var uri);
            var p=new NetworkCameraProfile{Id=id,Name=name.Text?.Trim()??"",Address=raw,User=user.Text??"",Password=password.Text??"",AllowPlaintext=allow.IsChecked==true,
                Engine=engine.SelectedIndex==1?"ffmpeg":"sharp",Decoder=DecoderId(),DecoderFallback=decoder.SelectedIndex<0?initial?.DecoderFallback==true:decoderFallback.IsChecked==true,Pin=uri?.Scheme is "https" or "rtsps"?NetworkCameraProfile.NormalizePin(pin.Text??""):"",CaFile=uri?.Scheme=="rtsps"?ca.Text??"":"",Fps=(int)(fps.Value??5),TimeoutSeconds=(int)(timeout.Value??10),Reconnect=reconnect.IsChecked==true,RtspUdp=uri?.Scheme=="rtsp"&&udp.IsChecked==true};
            if(p.Name.Length==0)p.Name=uri?.Host??"Camera";
            p.CopyOnvif(onvif);
            if(p.HasOnvif)
            {
                if(!Uri.TryCreate(p.OnvifAddress,UriKind.Absolute,out var controlUri)||uri is null||!string.Equals(uri.IdnHost,controlUri.IdnHost,StringComparison.OrdinalIgnoreCase))
                {feedback.Text=T("Адрес потока и ONVIF относятся к разным устройствам. Переподключите или отключите ONVIF.","Адреса потоку й ONVIF належать різним пристроям. Перепідключіть або відключіть ONVIF.","Stream and ONVIF addresses refer to different hosts. Relink or unlink ONVIF.");return null;}
                if(existing.Any(x=>x.Id!=p.Id&&x.HasOnvif&&x.OnvifProfileToken==p.OnvifProfileToken&&(string.Equals(x.OnvifAddress,p.OnvifAddress,StringComparison.OrdinalIgnoreCase)||p.OnvifIdentity.Length>0&&x.OnvifIdentity==p.OnvifIdentity)))
                {feedback.Text=T("Этот профиль ONVIF уже добавлен.","Цей профіль ONVIF вже додано.","This ONVIF profile already exists.");return null;}
            }
            if(!p.Validate(out _)){feedback.Text=T("Проверьте адрес (без логина/пароля в URL), параметры TLS и разрешение небезопасного транспорта. FFmpeg RTSPS не поддерживает точный отпечаток; не удаляйте его ради подключения секретных карточек.","Перевірте адресу (без логіна/пароля в URL), параметри TLS і дозвіл небезпечного транспорту. FFmpeg RTSPS не підтримує точний відбиток; не видаляйте його заради секретних карток.","Check the URL (no embedded credentials), TLS settings and insecure transport permission. FFmpeg RTSPS does not support exact pins; do not remove a pin to scan secrets.");return null;}
            if(existing.Any(x=>x.Id!=p.Id&&string.Equals(x.Name,p.Name,StringComparison.OrdinalIgnoreCase))){feedback.Text=T("Камера с таким именем уже существует.","Камера з такою назвою вже існує.","A camera with this name already exists.");return null;}
            return p;
        }
        void Busy(bool value)
        {
            busy=value;form.IsEnabled=test.IsEnabled=save.IsEnabled=!value;progress.IsVisible=value;
            if(value){cancel=new CancellationTokenSource();testDetails.IsVisible=false;testDetail.Text="";}else{cancel?.Dispose();cancel=null;}
            var content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=5};content.Children.Add(ActionIconVectors.Create(Icon.Dismiss));content.Children.Add(new TextBlock{Text=value?T("Отменить проверку","Скасувати перевірку","Cancel test"):T("Отмена","Скасувати","Cancel")});close.Content=content;
        }
        engine.SelectionChanged+=(_,_)=>
        {
            string next=engine.SelectedIndex==1?"ffmpeg":"sharp";
            if(!loading&&next!=lastEngine&&NetworkCameraProfile.ConsentScopeChanged(lastAddress,lastEngine,lastAddress,next))allow.IsChecked=false;
            lastEngine=next;Security();
        };
        manage.Click+=async (_,_)=>{await FfmpegComponentDialog.Show(win);Security();};
        address.PropertyChanged+=(_,e)=>
        {
            if(e.Property!=TextBox.TextProperty)return;
            string next=address.Text?.Trim()??"";
            if(!loading&&next!=lastAddress)
            {
                if(NetworkCameraProfile.ConsentScopeChanged(lastAddress,lastEngine,next,lastEngine))allow.IsChecked=false;
                udp.IsChecked=false;pin.Text="";ca.Text="";receivedPin.Text="";receivedPin.IsVisible=false;
            }
            lastAddress=next;Security();
        };
        chooseCa.Click+=async (_,_)=>
        {
            var files=await win.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("PEM / CRT"){Patterns=["*.pem","*.crt","*.cer"]}]});
            if(files.Count>0)ca.Text=files[0].TryGetLocalPath()??"";
        };
        clearCa.Click+=(_,_)=>ca.Text="";
        probe.Click+=async (_,_)=>
        {
            if(busy||!Uri.TryCreate(address.Text,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0)return;Busy(true);
            try{var read=await NetworkCameraSource.ReadPeerPin(uri,(int)(timeout.Value??10),cancel!.Token);if(!cancel!.IsCancellationRequested)feedback.Text=T("Отпечаток получен (НЕ проверен). Сверьте и вручную вставьте в поле SHA-256:","Відбиток отримано (НЕ перевірено). Звірте й вручну вставте у поле SHA-256:","Fingerprint received (NOT verified). Compare and manually paste into SHA-256 field:")+"\n"+read;
                // Display as selectable text; do not install a new pin automatically.
                if(!cancel!.IsCancellationRequested){receivedPin.Text=read;receivedPin.IsVisible=true;}}
            catch{if(!cancel!.IsCancellationRequested)feedback.Text=StateText(NetworkCameraState.Certificate);}finally{if(cancel?.IsCancellationRequested==true)feedback.Text=T("Проверка отменена","Перевірку скасовано","Test cancelled");Busy(false);}
        };
        test.Click+=async (_,_)=>
        {
            if(busy||Read() is not { } p)return;
            if(p.Engine=="ffmpeg"&&p.Validate(out var testUri)&&(testUri.Scheme is "rtsp" or "rtsps")&&!NetworkFfmpegManager.IsReady){await FfmpegComponentDialog.Show(win);Security();if(!NetworkFfmpegManager.IsReady)return;}
            Busy(true);feedback.Text=StateText(NetworkCameraState.Connecting);
            try
            {
                NetworkCameraSample? sample=null;
                await Task.Run(()=>NetworkCameraSource.Run(p,(pixels,s)=>{sample=s;ScannerPixels.ClearAndDispose(pixels);},(_,_)=>{},cancel!.Token,true));
                if(!cancel!.IsCancellationRequested&&sample is { } s)feedback.Text=T("Кадр успешно получен: ","Кадр успішно отримано: ","Frame received: ")+$"{s.Width}×{s.Height} · {s.Format} · {s.Bytes:N0} B · {s.ReceiveMs:F0} ms · "+(s.Tls?T("TLS проверен","TLS перевірено","TLS verified"):T("Защита не подтверждена","Захист не підтверджено","Protection not verified"));
            }
            catch(NetworkCameraException ex){if(!cancel!.IsCancellationRequested){feedback.Text=StateText(ex.State);testDetail.Text=ex.Detail;testDetails.IsVisible=ex.Detail.Length>0;}}
            catch{if(!cancel!.IsCancellationRequested)feedback.Text=StateText(NetworkCameraState.Error);}finally{if(cancel?.IsCancellationRequested==true)feedback.Text=T("Проверка отменена","Перевірку скасовано","Test cancelled");Busy(false);}
        };
        save.Click+=(_,_)=>
        {
            if(busy||Read() is not { } p)return;
            if(!persist(p)){feedback.Text=T("Не удалось сохранить профиль. Изменения остались в редакторе.","Не вдалося зберегти профіль. Зміни залишились у редакторі.","Could not save profile. Your edits remain in the editor.");return;}
            result=p;win.Close();
        };
        close.Click+=(_,_)=>{if(busy){cancel?.Cancel();feedback.Text=T("Остановка проверки…","Зупинка перевірки…","Stopping test…");}else win.Close();};
        win.Closing+=(_,e)=>{if(busy){cancel?.Cancel();e.Cancel=true;feedback.Text=T("Остановка проверки…","Зупинка перевірки…","Stopping test…");}};
        Fill(initial??new());
        OnvifInfo();
        async Task LinkOnvif()
        {
            if(busy)return;
            var link=await OnvifDialog.Link(win,onvif,address.Text??"",user.Text??"",password.Text??"");if(link is null)return;
            onvif.OnvifAddress=link.Endpoint;onvif.OnvifUser=link.User;onvif.OnvifPassword=link.Password;onvif.OnvifAllowHttp=link.AllowHttp;
            onvif.OnvifProfileToken=link.ProfileToken;onvif.OnvifMediaService=link.Service;onvif.OnvifIdentity=link.Identity;
            if(string.IsNullOrWhiteSpace(name.Text))name.Text=link.Name.Length>80?link.Name[..80]:link.Name;
            address.Text=link.StreamUrl;
            if(string.IsNullOrWhiteSpace(user.Text)&&string.IsNullOrEmpty(password.Text)){user.Text=link.User;password.Text=link.Password;}
            OnvifInfo();
        }
        linkOnvif.Click+=async (_,_)=>await LinkOnvif();
        unlinkOnvif.Click+=(_,_)=>{onvif=new NetworkCameraProfile();OnvifInfo();};
        if(startOnvif)win.Opened+=async (_,_)=>await LinkOnvif();
        var inventoryPanel=new WrapPanel();var inspect=Button(T("Диагностика видеодекодеров…","Діагностика відеодекодерів…","Video decoder diagnostics…"),Icon.Settings,inventoryPanel);stack.Children.Add(inventoryPanel);
        inspect.Click+=async (_,_)=>{if(!busy)await VideoDecoderDialog.Show(win);};
        await win.ShowDialog(owner);password.Text="";return result;
    }
}
