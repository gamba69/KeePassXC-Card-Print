using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

internal static class VideoDecoderDialog
{
    private static string T(string ru,string uk,string en)=>ScannerResultDialog.T(ru,uk,en);
    private static string State(VideoProbeStatus state)=>state switch
    {
        VideoProbeStatus.Working=>T("Работает","Працює","Working"),
        VideoProbeStatus.Unsupported=>T("Нет поддержки","Немає підтримки","Unsupported"),
        VideoProbeStatus.RuntimeMissing=>T("Нет библиотеки","Немає бібліотеки","Runtime missing"),
        VideoProbeStatus.SessionUnavailable=>T("Нет сеанса GPU","Немає сеансу GPU","GPU session unavailable"),
        VideoProbeStatus.CodecMissing=>T("Нет кодека","Немає кодека","Codec missing"),
        VideoProbeStatus.InitializationFailed=>T("Не запустился","Не запустився","Start failed"),
        VideoProbeStatus.DecodeFailed=>T("Ошибка кадров","Помилка кадрів","Decode failed"),
        VideoProbeStatus.UnverifiedHardware=>T("GPU не подтверждён","GPU не підтверджено","GPU unverified"),
        _=>T("Не проверено","Не перевірено","Not tested")
    };
    private static Icon StateIcon(VideoProbeStatus state)=>state switch
    {
        VideoProbeStatus.Working=>Icon.Checkmark,
        VideoProbeStatus.Unsupported or VideoProbeStatus.NotTested=>Icon.Subtract,
        VideoProbeStatus.DecodeFailed=>Icon.DismissCircle,
        _=>Icon.Warning
    };
    private static string Explanation(VideoProbeStatus state)=>state switch
    {
        VideoProbeStatus.Working=>T("Тестовые кадры декодированы, размеры и изображение проверены.","Тестові кадри декодовано, розміри й зображення перевірено.","Test pictures decoded; dimensions and image content verified."),
        VideoProbeStatus.Unsupported=>T("Профиль или формат не поддерживается этим декодером либо не объявлен драйвером.","Профіль або формат не підтримується декодером чи не оголошений драйвером.","This backend or driver does not advertise this profile/output format."),
        VideoProbeStatus.RuntimeMissing=>T("Библиотека драйвера или диспетчер производителя недоступны.","Бібліотека драйвера або диспетчер виробника недоступні.","Vendor driver runtime or dispatcher unavailable."),
        VideoProbeStatus.SessionUnavailable=>T("Библиотека загружена, но не удалось создать аппаратный сеанс для выбранного GPU. Причина может быть в драйвере, совместимости runtime или привязке устройства.","Бібліотеку завантажено, але не вдалося створити апаратний сеанс для вибраного GPU. Причина може бути в драйвері, сумісності runtime або прив’язці пристрою.","The library loaded, but a hardware session for the selected GPU could not be created. Driver, runtime compatibility or device binding may be responsible."),
        VideoProbeStatus.CodecMissing=>T("Windows не нашла совместимый компонент Media Foundation. Это не означает отсутствие аппаратной поддержки.","Windows не знайшла сумісний компонент Media Foundation. Це не означає відсутність апаратної підтримки.","Windows found no compatible Media Foundation component. This does not establish lack of GPU support."),
        VideoProbeStatus.InitializationFailed=>T("Компонент не удалось подготовить к декодированию.","Компонент не вдалося підготувати до декодування.","The decoder could not be initialized."),
        VideoProbeStatus.DecodeFailed=>T("Не получено корректное тестовое изображение.","Не отримано коректне тестове зображення.","Valid test pictures were not obtained."),
        VideoProbeStatus.UnverifiedHardware=>T("Получение поверхности аппаратного декодера не подтверждено. CPU не выдаётся за GPU.","Отримання поверхні апаратного декодера не підтверджено. CPU не видається за GPU.","A decoder-bound GPU surface was not confirmed. CPU output is not reported as GPU decoding."),
        _=>T("Проверка ещё не выполнена.","Перевірку ще не виконано.","Not tested yet.")
    };
    private static string Details(VideoDecoderCandidate c,string codec,VideoDecoderProbe? p)
    {
        if(p is null)return $"{c.Label} / {codec}\n{Explanation(VideoProbeStatus.NotTested)}";
        var lines=new List<string>{$"{c.Label} / {codec}: {State(p.Status)}",Explanation(p.Status)};
        if(c.Hardware&&c.Vendor==0)
        {
            lines.Add(T("Профиль D3D11: ","Профіль D3D11: ","D3D11 profile: ")+(p.CapabilityResult switch
            {0=>T("объявлен драйвером","оголошений драйвером","advertised"),1=>T("не объявлен","не оголошений","not advertised"),int hr when hr<0=>$"HRESULT=0x{hr:X8}",_=>T("не проверен","не перевірений","not checked")}));
            if(p.CapabilityResult==0)lines.Add(T("Конфигурация 1920 × 1080: ","Конфігурація 1920 × 1080: ","1920 × 1080 configuration: ")+(p.At1080?T("найдена","знайдена","advertised"):T("не подтверждена","не підтверджена","not confirmed")));
        }
        lines.Add(T("Тестовых кадров (640 × 368): ","Тестових кадрів (640 × 368): ","Test pictures (640 × 368): ")+$"{p.Pictures}/24");
        if(p.Passed)
        {
            lines.Add(T("Время на готовый кадр: ","Час на готовий кадр: ","Time per ready picture: ")+$"{p.Milliseconds:F2} ms");
            lines.Add(T("Первый кадр / P95 вызова: ","Перший кадр / P95 виклику: ","First picture / call P95: ")+$"{p.FirstPictureMs:F1} / {p.P95Ms:F1} ms");
        }
        lines.Add(T("Что делать: ","Що робити: ","What to do: ")+Advice(c,p.Codec,p));
        lines.Add(T("Технические данные: ","Технічні дані: ","Technical details: ")+p.Detail);
        return string.Join("\n",lines);
    }
    private static string Advice(VideoDecoderCandidate c,string codec,VideoDecoderProbe? p)
    {
        if(p is null)return T("Запустите проверку.","Запустіть перевірку.","Run the test.");
        if(p.Passed)return T("Ничего устанавливать не нужно. Можно использовать этот декодер в профиле камеры. Время теста не гарантирует такую же скорость на 1080p.","Нічого встановлювати не потрібно. Можна використовувати цей декодер у профілі камери. Час тесту не гарантує таку саму швидкість на 1080p.","No installation required. Use this decoder in the camera profile. The test does not guarantee the same speed at 1080p.");
        string alternative=codec=="H264"?T(" Выберите работающий H.264 или режим «Автоматически»."," Виберіть робочий H.264 або режим «Автоматично»."," Choose a working H.264 decoder or Automatic."):
            T(" Для H.265 можно использовать встроенный libde265 (CPU), без покупки кодека."," Для H.265 можна використовувати вбудований libde265 (CPU), без купівлі кодека."," For H.265, use the bundled libde265 (CPU) without buying a codec.");
        if(p.Status==VideoProbeStatus.Unsupported&&c.Id=="libde265")return T("libde265 предназначен только для H.265. Для H.264 выберите другой декодер.","libde265 призначений лише для H.265. Для H.264 виберіть інший декодер.","libde265 supports H.265 only. Choose another decoder for H.264.");
        if(p.Status==VideoProbeStatus.Unsupported)return T("Установка кодека не добавит отсутствующий аппаратный профиль. Проверьте спецификацию GPU; если профиль действительно поддерживается, проверьте совместимый драйвер.","Встановлення кодека не додасть відсутній апаратний профіль. Перевірте специфікацію GPU; якщо профіль справді підтримується, перевірте сумісний драйвер.","Installing a codec cannot add a missing hardware profile. Check GPU specifications; if the profile is supported, check the compatible driver.")+alternative;
        if(p.Status==VideoProbeStatus.CodecMissing&&codec!="H264")return T("Для системного HEVC можно проверить HEVC Video Extensions в Microsoft Store (может быть платным). Это необязательно и не добавляет аппаратных возможностей GPU.","Для системного HEVC можна перевірити HEVC Video Extensions у Microsoft Store (може бути платним). Це необов’язково і не додає апаратних можливостей GPU.","For system HEVC, check HEVC Video Extensions in Microsoft Store (may be paid). This is optional and cannot add GPU hardware capabilities.")+alternative;
        if(p.Status is VideoProbeStatus.RuntimeMissing or VideoProbeStatus.SessionUnavailable)
        {
            if(c.Vendor==2)return (p.Status==VideoProbeStatus.RuntimeMissing?T("Проверьте наличие KpcVendorVideo.dll и libvpl.dll в папке приложения. Если их нет, переустановите текущую версию Card Print. ","Перевірте наявність KpcVendorVideo.dll і libvpl.dll у папці програми. Якщо їх немає, перевстановіть поточну версію Card Print. ","Check for KpcVendorVideo.dll and libvpl.dll in the application directory. If missing, reinstall the current Card Print version. "):"")+T("Диспетчер oneVPL поставляется с Card Print. Для старых Intel нужен совместимый Intel Media SDK runtime из графического драйвера. Проверьте драйвер производителя ноутбука или Intel для вашей ОС; не устанавливайте неподдерживаемый драйвер. SDK для разработки не требуется.","Диспетчер oneVPL постачається з Card Print. Для старих Intel потрібен сумісний Intel Media SDK runtime з графічного драйвера. Перевірте драйвер виробника ноутбука або Intel для вашої ОС; не встановлюйте непідтримуваний драйвер. SDK для розробки не потрібен.","The oneVPL dispatcher ships with Card Print. Older Intel GPUs need a compatible Intel Media SDK runtime from the graphics driver. Check the laptop manufacturer's or Intel's driver for your OS; do not install an unsupported driver. No development SDK is needed.")+alternative;
            return T("Проверьте комплектность дистрибутива Card Print и совместимый графический драйвер производителя. Переустановка SDK для разработки не нужна. Повторите тест после изменения драйвера.","Перевірте комплектність дистрибутива Card Print і сумісний графічний драйвер виробника. Перевстановлення SDK для розробки не потрібне. Повторіть тест після зміни драйвера.","Check the Card Print installation and the compatible vendor graphics driver. No development SDK is needed. Retest after a driver change.")+alternative;
        }
        return T("Проверьте обновления Windows и совместимый драйвер GPU, повторите тест. Если проблема остаётся, скопируйте отчёт для разработчика.","Перевірте оновлення Windows і сумісний драйвер GPU, повторіть тест. Якщо проблема залишається, скопіюйте звіт для розробника.","Check Windows updates and a compatible GPU driver, then retest. If the problem persists, copy the report for the developer.")+alternative;
    }
    private static (string Label,string Url)? HelpLink(VideoDecoderCandidate c,string codec,VideoDecoderProbe? p)
    {
        if(p is null||p.Passed||p.Status==VideoProbeStatus.Unsupported)return null;
        if(p.Status==VideoProbeStatus.CodecMissing&&codec!="H264")return (T("HEVC в Microsoft Store","HEVC у Microsoft Store","HEVC in Microsoft Store"),"https://apps.microsoft.com/detail/9nmzlz57r3t7");
        if(c.Vendor==2||c.Id.Contains("-8086-",StringComparison.OrdinalIgnoreCase))return (T("Драйверы Intel","Драйвери Intel","Intel drivers"),c.Label.Contains("4600",StringComparison.Ordinal)?"https://www.intel.com/content/www/us/en/support/products/81496/graphics/processor-graphics/intel-hd-graphics-family/intel-hd-graphics-4600.html":"https://www.intel.com/content/www/us/en/support/detect.html");
        if(c.Vendor==1||c.Id.Contains("-10DE-",StringComparison.OrdinalIgnoreCase))return (T("Драйверы NVIDIA","Драйвери NVIDIA","NVIDIA drivers"),"https://www.nvidia.com/en-us/drivers/");
        if(c.Vendor==3||c.Id.Contains("-1002-",StringComparison.OrdinalIgnoreCase))return (T("Драйверы AMD","Драйвери AMD","AMD drivers"),"https://www.amd.com/en/support/download/drivers.html");
        return null;
    }
    internal static async Task Show(Window owner)
    {
        var win=new Window{Title=T("Видеодекодеры","Відеодекодери","Video decoders"),Width=840,MinWidth=540,Height=520,MinHeight=270,MaxHeight=580,SizeToContent=SizeToContent.Manual,WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowInTaskbar=false};
        PrintWindowIcons.Attach(win);
        var root=new DockPanel{Margin=new Thickness(12),LastChildFill=true};win.Content=root;
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,6,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var copy=PrintDialogButtons.Create(T("Копировать отчёт","Копіювати звіт","Copy report"),ActionIconVectors.Create(Icon.Copy));copy.IsEnabled=false;
        var repeat=PrintDialogButtons.Create(T("Повторить проверку","Повторити перевірку","Retest"),ActionIconVectors.Create(Icon.ArrowSync));
        var stop=PrintDialogButtons.Create(T("Отменить","Скасувати","Cancel"),ActionIconVectors.Create(Icon.Dismiss));stop.IsVisible=false;
        var close=PrintDialogButtons.Create(T("Закрыть","Закрити","Close"),ActionIconVectors.Create(Icon.Dismiss));foreach(var button in new[]{copy,repeat,stop,close}){button.Margin=new Thickness(5,2,0,0);footer.Children.Add(button);}
        var header=new StackPanel{Spacing=4,Margin=new Thickness(0,0,0,6)};DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap};header.Children.Add(status);
        var progressBar=new ProgressBar{Minimum=0,Maximum=1,Height=4,IsVisible=false};header.Children.Add(progressBar);
        var stack=new StackPanel{Spacing=6};root.Children.Add(new ScrollViewer{Content=stack,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalContentAlignment=HorizontalAlignment.Stretch});
        var table=new StackPanel{Spacing=0};var tableCard=new Border{Padding=new Thickness(0),Child=table};tableCard.Classes.Add("settings-card");stack.Children.Add(tableCard);
        var summary=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};
        var summaryTitle=new TextBlock{Text=T("Итоги проверки","Підсумки перевірки","Test summary")};summaryTitle.Classes.Add("section-heading");
        var summaryBody=new StackPanel{Spacing=4};summaryBody.Children.Add(summaryTitle);summaryBody.Children.Add(summary);
        var summaryCard=new Border{Padding=new Thickness(7),Child=summaryBody,IsVisible=false};summaryCard.Classes.Add("settings-card");stack.Children.Add(summaryCard);
        var detailText=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=12};
        var helpLabel=new TextBlock{VerticalAlignment=VerticalAlignment.Center};
        var helpContent=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6};helpContent.Children.Add(ActionIconVectors.Create(Icon.DocumentOnePage));helpContent.Children.Add(helpLabel);
        var help=new Button{Content=helpContent,HorizontalAlignment=HorizontalAlignment.Left,IsVisible=false};
        var detailPanel=new StackPanel{Spacing=6};detailPanel.Children.Add(detailText);detailPanel.Children.Add(help);
        var detailCard=new Border{Padding=new Thickness(7),Child=detailPanel};detailCard.Classes.Add("settings-card");stack.Children.Add(detailCard);
        stack.Children.Add(new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=11,Text=T("Выберите ячейку: причина, что делать, технические данные. Время включает декодирование и получение пикселей BGRA, без PNG. Тест 640 × 368, не тест 1080p. Установка драйверов и кодеков — только вручную.","Виберіть комірку: причина, що робити, технічні дані. Час включає декодування й отримання пікселів BGRA, без PNG. Тест 640 × 368, не тест 1080p. Встановлення драйверів і кодеків — лише вручну.","Select a cell: reason, next steps, technical data. Timing includes decoding and BGRA readback, without PNG. Test is 640 × 368, not 1080p. Drivers and codecs are installed manually only.")});
        string[] codecs=["H264","H265","Main10"],labels=["H.264","H.265 Main","Main10"];
        var cells=new Dictionary<(string,string),Button>();var completed=new Dictionary<(string,string),VideoDecoderProbe>();
        VideoDecoderCandidate[] candidates=[];bool busy=false;CancellationTokenSource? cancel=null;string report="",stage="";string? helpUrl=null;
        (string Id,string Codec)? selected=null;int run=0,done=0,total=0;var watch=new System.Diagnostics.Stopwatch();
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        void UpdateStatus(){if(busy)status.Text=$"{stage}   {watch.Elapsed:mm\\:ss}";}
        timer.Tick+=(_,_)=>UpdateStatus();
        void ShowDetail(VideoDecoderCandidate c,string codec)
        {
            selected=(c.Id,codec);completed.TryGetValue((c.Id,codec),out var p);
            detailText.Text=Details(c,labels[Array.IndexOf(codecs,codec)],p);var link=HelpLink(c,codec,p);
            helpUrl=link?.Url;help.IsVisible=link is not null;ToolTip.SetTip(help,link?.Label??"");
            if(link is not null)helpLabel.Text=link.Value.Label;
        }
        help.Click+=async (_,_)=>{var url=helpUrl;if(url is null)return;try{if(!await win.Launcher.LaunchUriAsync(new Uri(url)))detailText.Text+="\n"+T("Не удалось открыть ссылку: ","Не вдалося відкрити посилання: ","Could not open link: ")+url;}catch(Exception){detailText.Text+="\n"+url;}};
        void SetCell(VideoDecoderCandidate c,string codec,VideoDecoderProbe? p,bool running=false)
        {
            if(!cells.TryGetValue((c.Id,codec),out var cell))return;
            var state=p?.Status??VideoProbeStatus.NotTested;
            var content=new StackPanel{Orientation=Orientation.Horizontal,Spacing=5,HorizontalAlignment=HorizontalAlignment.Center};
            content.Children.Add(ActionIconVectors.Create(running?Icon.ArrowSync:StateIcon(state),14));
            content.Children.Add(new TextBlock{Text=running?T("Проверяется…","Перевіряється…","Testing…"):p?.Passed==true?$"{p.Milliseconds:F1} ms":State(state),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,FontSize=12});
            cell.Content=content;ToolTip.SetTip(cell,running?T("Идёт тестирование","Триває тестування","Test in progress"):State(state)+"\n"+Explanation(state));
            if(selected is { } key&&key.Id==c.Id&&key.Codec==codec)ShowDetail(c,codec);
        }
        void BuildTable(VideoDecoderCandidate[] inventory)
        {
            candidates=inventory;table.Children.Clear();cells.Clear();
            Grid Row(string title,bool heading,int index)
            {
                var g=new Grid{ColumnDefinitions=new ColumnDefinitions("2*,*,*,*"),ColumnSpacing=6};
                g.Children.Add(new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap,FontWeight=heading?FontWeight.SemiBold:FontWeight.Normal,VerticalAlignment=VerticalAlignment.Center});
                var border=new Border{Padding=new Thickness(6,2),Child=g};border.Classes.Add("hyphenation-row");if(!heading&&index%2==1)border.Classes.Add("stripe");table.Children.Add(border);return g;
            }
            var heading=Row(T("Декодер","Декодер","Decoder"),true,0);
            for(int i=0;i<3;i++){var h=new TextBlock{Text=labels[i],FontWeight=FontWeight.SemiBold,HorizontalAlignment=HorizontalAlignment.Center};Grid.SetColumn(h,i+1);heading.Children.Add(h);}
            int row=0;
            foreach(var c in inventory)
            {
                var g=Row(c.Label,false,row++);
                for(int i=0;i<3;i++)
                {
                    var codec=codecs[i];var cell=new Button{HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center,Padding=new Thickness(3,2),MinHeight=26,BorderThickness=new Thickness(0)};cell.Classes.Add("table-sort");
                    cell.Click+=(_,_)=>ShowDetail(c,codec);Grid.SetColumn(cell,i+1);g.Children.Add(cell);cells[(c.Id,codec)]=cell;SetCell(c,codec,null);
                }
            }
        }
        void Accept(VideoDecoderProbe p)
        {
            completed[(p.Id,p.Codec)]=p;var c=candidates.FirstOrDefault(x=>x.Id==p.Id);if(c is not null)SetCell(c,p.Codec,p);
        }
        void BuildSummary()
        {
            var lines=new List<string>();
            foreach(var codec in codecs)
            {
                var passed=completed.Values.Where(p=>p.Codec==codec&&p.Passed).OrderBy(p=>p.Milliseconds).ToArray();
                var best=passed.FirstOrDefault();var name=labels[Array.IndexOf(codecs,codec)];
                if(best is null){lines.Add(name+": "+T("нет подтверждённого декодера","немає підтвердженого декодера","no verified decoder"));continue;}
                var c=candidates.FirstOrDefault(x=>x.Id==best.Id);lines.Add($"{name}: "+T("работает: ","працює: ","working: ")+$"{passed.Length} · "+T("быстрее в тесте — ","швидше в тесті — ","fastest in test — ")+(c?.Label??best.Id)+$" ({best.Milliseconds:F1} ms)");
                var recommended=VideoDecoderManager.Rank(candidates.Where(x=>VideoDecoderManager.Supports(x,codec)).ToArray(),completed.Values.ToArray(),"auto",true,codec).FirstOrDefault();
                if(recommended is not null&&recommended.Id!=best.Id)lines.Add("  "+T("Автовыбор: ","Автовибір: ","Automatic choice: ")+recommended.Label);
                var gpu=passed.FirstOrDefault(p=>candidates.Any(x=>x.Id==p.Id&&x.Hardware));
                if(gpu is not null&&gpu.Id!=best.Id)lines.Add("  "+T("Быстрейший GPU в тесте: ","Найшвидший GPU у тесті: ","Fastest GPU in test: ")+candidates.First(x=>x.Id==gpu.Id).Label+$" ({gpu.Milliseconds:F1} ms)");
            }
            summary.Text=string.Join("\n",lines);summaryCard.IsVisible=true;
            if(codecs.All(codec=>completed.Values.Any(p=>p.Codec==codec&&p.Passed)))summary.Text+="\n"+T("Все три тестовых формата доступны. Дополнительные декодеры для них не нужны.","Усі три тестові формати доступні. Додаткові декодери для них не потрібні.","All three test formats passed. No additional decoder installation is needed for these formats.");
        }
        copy.Click+=async (_,_)=>{try{if(win.Clipboard is not null)await win.Clipboard.SetTextAsync(report);}catch(Exception){status.Text=T("Не удалось скопировать отчёт","Не вдалося скопіювати звіт","Could not copy report");}};
        async Task Refresh(bool force)
        {
            if(busy)return;busy=true;int id=++run;repeat.IsEnabled=false;copy.IsEnabled=false;close.IsEnabled=false;stop.IsVisible=true;stop.IsEnabled=true;cancel=new();
            done=total=0;stage=T("Поиск декодеров…","Пошук декодерів…","Discovering decoders…");watch.Restart();timer.Start();UpdateStatus();
            progressBar.IsVisible=true;progressBar.IsIndeterminate=true;progressBar.Value=0;completed.Clear();selected=null;table.Children.Clear();cells.Clear();candidates=[];summary.Text="";summaryCard.IsVisible=false;detailText.Text="";help.IsVisible=false;helpUrl=null;
            try
            {
                var token=cancel.Token;var inventory=await Task.Run(VideoDecoderManager.Candidates,token);token.ThrowIfCancellationRequested();BuildTable(inventory);
                total=inventory.Length*3;progressBar.Maximum=Math.Max(1,total);progressBar.IsIndeterminate=false;
                stage=T("Проверка кеша и возможностей…","Перевірка кешу й можливостей…","Checking cache and capabilities…");UpdateStatus();
                var updates=new Progress<VideoDecoderProgress>(p=>
                {
                    if(!busy||run!=id||token.IsCancellationRequested)return;
                    done=p.Completed;progressBar.Value=done;var c=p.Candidate;var codec=p.Codec;if(c is null||codec is null)return;
                    if(p.Result is not null)Accept(p.Result);else SetCell(c,codec,null,true);
                    stage=$"{done}/{total} · {c.Label} · {labels[Array.IndexOf(codecs,codec)]}";UpdateStatus();
                });
                var results=await Task.Run(()=>VideoDecoderManager.ProbeAll(force,token,inventory,updates),token);token.ThrowIfCancellationRequested();
                foreach(var p in results)Accept(p);done=completed.Count;progressBar.Value=done;BuildSummary();
                report=string.Join("\n\n",candidates.SelectMany(c=>codecs.Select((codec,i)=>Details(c,labels[i],completed.GetValueOrDefault((c.Id,codec))))));copy.IsEnabled=true;
                if(selected is null)detailText.Text=T("Выберите результат в таблице, чтобы увидеть конкретные действия.","Виберіть результат у таблиці, щоб побачити конкретні дії.","Select a result in the table for specific next steps.");
                status.Text=T("Проверка завершена","Перевірку завершено","Decoder checks completed")+$" · {done}/{total} · {watch.Elapsed:mm\\:ss}";
            }
            catch(OperationCanceledException){status.Text=T("Отменено","Скасовано","Canceled")+$" · {done}/{total}";foreach(var c in candidates)foreach(var codec in codecs)SetCell(c,codec,completed.GetValueOrDefault((c.Id,codec)));}
            catch(Exception ex){status.Text=T("Не удалось завершить проверку: ","Не вдалося завершити перевірку: ","Could not complete test: ")+ex.GetType().Name;}
            finally{timer.Stop();watch.Stop();cancel?.Dispose();cancel=null;busy=false;repeat.IsEnabled=true;close.IsEnabled=true;stop.IsVisible=false;progressBar.IsVisible=false;}
        }
        repeat.Click+=async (_,_)=>await Refresh(true);stop.Click+=(_,_)=>{stop.IsEnabled=false;cancel?.Cancel();stage=T("Отмена после текущего вызова декодера…","Скасування після поточного виклику декодера…","Canceling after the current decoder call…");UpdateStatus();};close.Click+=(_,_)=>win.Close();
        win.Closing+=(_,e)=>{if(busy){e.Cancel=true;cancel?.Cancel();stage=T("Отмена после текущего вызова декодера…","Скасування після поточного виклику декодера…","Canceling after the current decoder call…");UpdateStatus();}};
        win.Opened+=async (_,_)=>await Refresh(false);
        await win.ShowDialog(owner);
    }
}
