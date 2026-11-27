using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint;

// UI thread only. Frame samples and completed decoder samples have separate clocks.
internal sealed class ScannerDashboard : StackPanel
{
    private enum Grade { Unknown, Poor, Fair, Good }
    private enum Metric { Module, Detail, Contrast, Light, Change, Side, Modules, QrLight, QrEdge,
        AreaDetail, AreaContrast, AreaLight, AreaEdge, FrameDetail, FrameContrast, FrameLight, FrameEdge, Dark, Bright, Attempts, DecodeMs,
        Finders, Located, Decoded, Eligible, Invalid, Burst, SelectedDetail, RawModule, DecoderDetail, DecoderContrast, NetFps, NetKbps, NetReceive, VideoDecodeMs, NetInterval, NetBytes, NetRetries }
    private sealed record Definition(Metric Id,string Ru,string Uk,string En,string Unit,double GraphMax,
        Func<double,Grade>? Evaluate = null,bool Main = false);
    private static Grade High(double v,double bad,double good) => v<bad?Grade.Poor:v<good?Grade.Fair:Grade.Good;
    private static Grade Low(double v,double good,double bad) => v>bad?Grade.Poor:v>good?Grade.Fair:Grade.Good;
    private static Grade LightGrade(double v) => v<45||v>245?Grade.Poor:v<80||v>225?Grade.Fair:Grade.Good;
    private static readonly Definition[] Definitions =
    [
        new(Metric.Module,"Модуль QR","Модуль QR","QR module","px",8,v=>High(v,2,3.5),true),
        new(Metric.Detail,"Резкость QR","Різкість QR","QR detail","",100,v=>High(v,9,20),true),
        new(Metric.Contrast,"Контраст QR","Контраст QR","QR contrast","",255,v=>High(v,35,75),true),
        new(Metric.Change,"Изменение кадра","Зміна кадру","Frame change","",30,v=>Low(v,5,12),true),
        new(Metric.Light,"Яркость кадра","Яскравість кадру","Frame brightness","",255,LightGrade,true),
        new(Metric.Side,"Сторона QR (оценка)","Сторона QR (оцінка)","QR side (estimate)","px",0),
        new(Metric.Modules,"Матрица QR (сторона)","Матриця QR (сторона)","QR matrix (side)","",0),
        new(Metric.QrLight,"Яркость QR","Яскравість QR","QR brightness","",0,LightGrade),
        new(Metric.QrEdge,"Границы QR (отн.)","Межі QR (відн.)","QR edges (relative)","",0),
        new(Metric.AreaDetail,"Резкость выбранной области","Різкість обраної ділянки","Selected area detail","",0,v=>High(v,9,20)),
        new(Metric.AreaContrast,"Контраст выбранной области","Контраст обраної ділянки","Selected area contrast","",0,v=>High(v,35,75)),
        new(Metric.AreaLight,"Яркость выбранной области","Яскравість обраної ділянки","Selected area brightness","",0,LightGrade),
        new(Metric.AreaEdge,"Границы выбранной области","Межі обраної ділянки","Selected area edges","",0),
        new(Metric.FrameDetail,"Детализация кадра","Деталізація кадру","Frame detail","",100,null,true),
        new(Metric.FrameContrast,"Контраст кадра","Контраст кадру","Frame contrast","",255,v=>High(v,35,75),true),
        new(Metric.FrameLight,"Яркость кадра (0–255)","Яскравість кадру (0–255)","Frame brightness (0–255)","",0,LightGrade),
        new(Metric.FrameEdge,"Границы кадра (отн.)","Межі кадру (відн.)","Frame edges (relative)","",0),
        new(Metric.Dark,"Тёмные пиксели","Темні пікселі","Dark pixels","%",0),
        new(Metric.Bright,"Светлые пиксели","Світлі пікселі","Bright pixels","%",0),
        new(Metric.Attempts,"Попытки за поиск","Спроби за пошук","Attempts per scan","",0),
        new(Metric.DecodeMs,"Время поиска","Час пошуку","Scan time","ms",0),
        new(Metric.Finders,"Кандидаты ориентиров","Кандидати орієнтирів","Finder candidates","",0),
        new(Metric.Located,"Локализация","Локалізація","Localization","%",0),
        new(Metric.Decoded,"Декодировано QR","Декодовано QR","Decoded QR","",0),
        new(Metric.Eligible,"Подходящие QR","Відповідні QR","Eligible QR","",0),
        new(Metric.Invalid,"Некорректные QR","Некоректні QR","Invalid QR","",0),
        new(Metric.Burst,"Кадров в серии","Кадрів у серії","Frames in burst","",0),
        new(Metric.SelectedDetail,"Резкость выбранного кадра","Різкість обраного кадру","Selected frame detail","",0),
        new(Metric.RawModule,"Макс. оценка ориентира","Макс. оцінка орієнтира","Max finder estimate","px",0),
        new(Metric.DecoderDetail,"Детализация декодера","Деталізація декодера","Decoder detail","",0),
        new(Metric.DecoderContrast,"Контраст декодера","Контраст декодера","Decoder contrast","",0),
        new(Metric.NetFps,"Сеть: поступление кадров","Мережа: надходження кадрів","Network: received FPS","fps",0),
        new(Metric.NetKbps,"Сеть: объём JPEG (без заголовков)","Мережа: обсяг JPEG (без заголовків)","Network: JPEG payload rate","kb/s",0),
        new(Metric.NetReceive,"Ожидание готового кадра","Очікування готового кадру","Ready frame wait","ms",0),
        new(Metric.VideoDecodeMs,"Видео: декодирование и BGRA","Відео: декодування та BGRA","Video: decoding and BGRA","ms",0),
        new(Metric.NetInterval,"Сеть: интервал кадров","Мережа: інтервал кадрів","Network: frame interval","ms",0),
        new(Metric.NetBytes,"Размер JPEG / BGRA","Розмір JPEG / BGRA","JPEG / BGRA size","MiB",0),
        new(Metric.NetRetries,"Сеть: переподключения","Мережа: перепідключення","Network: reconnects","",0)
    ];
    private sealed class Entry(Definition definition)
    {
        internal Definition Def=definition;
        internal ScannerMetricWindow Window=new();
        internal double? Value;
        internal TextBlock Name=new(){TextWrapping=TextWrapping.Wrap,FontSize=11};
        internal TextBlock Current=new(){FontSize=11,HorizontalAlignment=HorizontalAlignment.Right};
        internal TextBlock P05=new(){FontSize=11,HorizontalAlignment=HorizontalAlignment.Right};
        internal TextBlock P95=new(){FontSize=11,HorizontalAlignment=HorizontalAlignment.Right};
        internal TextBlock Large=new(){FontSize=18,FontWeight=FontWeight.SemiBold};
        internal TextBlock Title=new(){FontSize=11,TextWrapping=TextWrapping.Wrap};
        internal TextBlock Rating=new(){FontSize=11,TextWrapping=TextWrapping.Wrap};
        internal Sparkline? Graph;
    }
    private readonly Dictionary<Metric,Entry> _entries=Definitions.ToDictionary(d=>d.Id,d=>new Entry(d));
    private readonly TextBlock _heading=new(){FontSize=14,FontWeight=FontWeight.SemiBold};
    private readonly TextBlock _advice=new(){FontSize=12,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _windowLabel=new(){FontSize=11,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _detailHeading=new(){FontSize=13,FontWeight=FontWeight.SemiBold};
    private readonly TextBlock _legend=new(){FontSize=11,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock[] _metricHeaders=Enumerable.Range(0,3).Select(_=>new TextBlock{FontSize=11}).ToArray(),
        _nowHeaders=Enumerable.Range(0,3).Select(_=>new TextBlock{FontSize=11,HorizontalAlignment=HorizontalAlignment.Right}).ToArray();
    private readonly Button _reset=new();
    private readonly Grid _details=new(){ColumnSpacing=3,RowSpacing=0};
    private readonly Grid _cards=new(){ColumnSpacing=5,RowSpacing=5};
    private int _cardColumns;
    private int _tableColumns;
    private Metric[] _shownMetrics=[];
    private readonly List<Grid> _metricGroups=[];
    private ScanDiagnostics? _completed;
    private ScannerImageQuality? _frame;
    private long _lastRender;
    private bool _live;
    private bool _frozen;
    private static string T(string ru,string uk,string en)=>ScannerResultDialog.T(ru,uk,en);
    private static IBrush ColorFor(Grade grade)=>new SolidColorBrush(Color.Parse(grade switch
    {Grade.Poor=>"#D65050",Grade.Fair=>"#C58A24",Grade.Good=>"#299B68",_=>"#929AA4"}));
    private static string GradeText(Grade grade)=>grade switch
    {Grade.Poor=>T("Плохо","Погано","Poor"),Grade.Fair=>T("Погранично","Гранично","Marginal"),Grade.Good=>T("Хорошо","Добре","Good"),_=>T("Нет оценки","Немає оцінки","Unknown")};
    private static string Number(double? value,Definition def)=>value is double v ? (def.Id==Metric.NetBytes?(v/(1024*1024)).ToString("F2"):def.Id is Metric.Module or Metric.Detail or Metric.Change or Metric.FrameDetail or Metric.FrameEdge or Metric.QrEdge or Metric.SelectedDetail or Metric.RawModule or Metric.DecoderDetail or Metric.AreaDetail or Metric.AreaEdge or Metric.NetFps ? v.ToString("F1") : v.ToString("F0")) : "—";
    private static Grade Rate(Entry e,double? v)=>v is double n&&e.Def.Evaluate is { } f?f(n):Grade.Unknown;
    internal ScannerDashboard(TextBlock progress)
    {
        Spacing=6;HorizontalAlignment=HorizontalAlignment.Stretch;
        var head=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};
        head.Children.Add(_heading);Grid.SetColumn(_reset,1);head.Children.Add(_reset);
        _reset.Click+=(_,_)=>ResetHistory();Children.Add(head);
        Children.Add(progress);Children.Add(_advice);
        foreach(var e in _entries.Values.Where(e=>e.Def.Main))
        {
            var content=new StackPanel{Spacing=1};content.Children.Add(e.Title);
            var value=new WrapPanel();e.Large.Margin=new Thickness(0,0,5,0);e.Rating.VerticalAlignment=VerticalAlignment.Center;
            value.Children.Add(e.Large);value.Children.Add(e.Rating);content.Children.Add(value);
            e.Graph=new Sparkline{Height=14,Scale=e.Def.GraphMax};content.Children.Add(e.Graph);
            var card=new Border{Padding=new Thickness(5),Child=content};card.Classes.Add("settings-card");_cards.Children.Add(card);
        }
        Children.Add(_cards);Children.Add(_detailHeading);Children.Add(_windowLabel);Children.Add(_details);Children.Add(_legend);
        Reflow(0);SizeChanged+=(_,e)=>Reflow(e.NewSize.Width);Localize();Refresh(true);
    }
    private void Reflow(double width)
    {
        // Change layout only when the actual sidebar width crosses a breakpoint.
        int columns=width>=720?4:width>=475?3:width>0&&width<240?1:2;
        if(columns!=_cardColumns)
        {
            _cardColumns=columns;
            _cards.ColumnDefinitions=new ColumnDefinitions(string.Join(",",Enumerable.Repeat("*",columns)));
            _cards.RowDefinitions.Clear();
            for(int i=0;i<_cards.Children.Count;i++)
            {
                if(i%columns==0)_cards.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                Grid.SetRow(_cards.Children[i],i/columns);Grid.SetColumn(_cards.Children[i],i%columns);
            }
        }
        ReflowMeasurements(width);
    }
    // Only currently applicable values are displayed. Zero is a real measurement;
    // a missing value is not. Retain historical samples for metrics that reappear.
    private void ReflowMeasurements(double width)
    {
        var ordered=_entries.Values.Where(e=>e.Value.HasValue&&e.Def.Id!=Metric.FrameLight).ToArray();
        var metrics=ordered.Select(e=>e.Def.Id).ToArray();
        int tables=Math.Min(TableColumns(width),Math.Max(1,ordered.Length));
        _detailHeading.IsVisible=_windowLabel.IsVisible=_details.IsVisible=ordered.Length>0;
        if(_tableColumns==tables&&_shownMetrics.SequenceEqual(metrics))return;
        _tableColumns=tables;_shownMetrics=metrics;
        // Remove each reusable text control from its previous group before reparenting.
        foreach(var group in _metricGroups)group.Children.Clear();
        _metricGroups.Clear();_details.Children.Clear();
        _details.ColumnDefinitions=new ColumnDefinitions(string.Join(",",Enumerable.Repeat("*",tables)));
        _details.ColumnSpacing=8;
        int first=0;
        for(int group=0;group<tables&&first<ordered.Length;group++)
        {
            // Independent tables prevent wrapped labels on one side from adding
            // empty vertical space to unrelated rows in another group.
            var table=new Grid{ColumnDefinitions=new ColumnDefinitions("*,44,38,38"),ColumnSpacing=3,RowSpacing=0,VerticalAlignment=VerticalAlignment.Top};
            _metricGroups.Add(table);Grid.SetColumn(table,group);_details.Children.Add(table);
            table.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Header(table,_metricHeaders[group],_nowHeaders[group]);
            int rows=ordered.Length/tables+(group<ordered.Length%tables?1:0);
            for(int i=0;i<rows;i++)
            {
                table.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                MetricRow(table,ordered[first+i],i+1);
            }
            first+=rows;
        }
    }
    // Physical pixels are not DIPs. Keep the existing responsive breakpoints.
    internal static int TableColumns(double width)=>width>=720?3:width>=480?2:1;
    private static void Header(Grid table,TextBlock name,TextBlock now)
    {
        Put(table,name,0,0);Put(table,now,0,1);
        Put(table,new TextBlock{Text="P₀₅",HorizontalAlignment=HorizontalAlignment.Right,FontSize=11},0,2);
        Put(table,new TextBlock{Text="P₉₅",HorizontalAlignment=HorizontalAlignment.Right,FontSize=11},0,3);
    }
    private static void MetricRow(Grid table,Entry e,int row)
    {
        if(row%2==0)
        {
            var stripe=new Border{IsHitTestVisible=false};
            stripe.Classes.Add("hyphenation-row");stripe.Classes.Add("stripe");
            Grid.SetRow(stripe,row);Grid.SetColumnSpan(stripe,4);table.Children.Add(stripe);
        }
        Put(table,e.Name,row,0);Put(table,e.Current,row,1);Put(table,e.P05,row,2);Put(table,e.P95,row,3);
    }
    private static void Put(Grid table,Control c,int row,int column){c.Margin=new Thickness(2,1);Grid.SetRow(c,row);Grid.SetColumn(c,column);table.Children.Add(c);}
    internal void Localize()
    {
        _heading.Text=T("Диагностика","Діагностика","Diagnostics");_detailHeading.Text=T("Текущие измерения","Поточні вимірювання","Current measurements");
        foreach(var header in _metricHeaders)header.Text=T("Показатель","Показник","Metric");
        foreach(var header in _nowHeaders)header.Text=T("Сейчас","Зараз","Now");
        var resetContent=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4};resetContent.Children.Add(ActionIconVectors.Create(Icon.ArrowSync));resetContent.Children.Add(new TextBlock{Text=T("Сброс","Скинути","Reset")});_reset.Content=resetContent;
        _legend.Text=T("Красный — плохо · жёлтый — погранично · зелёный — хорошо · серый — нет оценки. Пороги ориентировочные, не гарантия чтения.","Червоний — погано · жовтий — гранично · зелений — добре · сірий — немає оцінки. Пороги орієнтовні, не гарантія читання.","Red — poor · yellow — marginal · green — good · gray — unknown. Thresholds are indicative, not a decoding guarantee.");
        foreach(var e in _entries.Values){string name=T(e.Def.Ru,e.Def.Uk,e.Def.En);e.Name.Text=name+(e.Def.Unit.Length==0?"":" · "+e.Def.Unit);e.Title.Text=name;if(e.Graph is { } graph)ToolTip.SetTip(graph,T("30 секунд, шкала","30 секунд, шкала","30 seconds, scale")+$": 0–{e.Def.GraphMax}");}
        Refresh(true);
    }
    private void Set(Metric metric,double? value,long now,bool record)
    {var e=_entries[metric];e.Value=value;if(record)e.Window.Add(now,value);}
    internal void Mode(bool live,bool frozen){_live=live;_frozen=frozen;Refresh(true);}
    internal void AddFrame(ScannerImageQuality q,bool live,bool frozen)
    {
        _frame=q;_live=live;_frozen=frozen;long now=Environment.TickCount64;
        Set(Metric.Light,q.Light,now,true);Set(Metric.Change,q.Motion,now,true);Set(Metric.FrameDetail,q.Detail,now,true);
        Set(Metric.FrameLight,q.Light,now,true);Set(Metric.FrameContrast,q.Contrast,now,true);Set(Metric.FrameEdge,q.EdgeStrength,now,true);
        Set(Metric.Dark,q.Dark*100,now,true);Set(Metric.Bright,q.Bright*100,now,true);Refresh(false);
    }
    internal void AddScan(ScanDiagnostics d,int eligible,int invalid,int burst,ScannerImageQuality? selected)
    {
        _completed=d;long now=Environment.TickCount64;var qr=d.Qr;
        Set(Metric.Module,qr?.ModulePixels,now,true);Set(Metric.Detail,qr?.Detail,now,true);Set(Metric.Contrast,qr?.Contrast,now,true);
        Set(Metric.Side,qr?.SidePixels,now,true);Set(Metric.Modules,qr?.Modules,now,true);Set(Metric.QrLight,qr?.Light,now,true);Set(Metric.QrEdge,qr?.EdgeStrength,now,true);
        Set(Metric.AreaDetail,d.SelectedArea?.Detail,now,true);Set(Metric.AreaContrast,d.SelectedArea?.Contrast,now,true);
        Set(Metric.AreaLight,d.SelectedArea?.Light,now,true);Set(Metric.AreaEdge,d.SelectedArea?.Edge,now,true);
        Set(Metric.Attempts,d.Attempts,now,true);Set(Metric.DecodeMs,d.Milliseconds,now,true);Set(Metric.Finders,d.FinderPoints,now,true);
        Set(Metric.Located,d.Located?100:0,now,true);Set(Metric.Decoded,d.Decoded,now,true);Set(Metric.Eligible,eligible,now,true);Set(Metric.Invalid,invalid,now,true);
        Set(Metric.Burst,burst>0?burst:null,now,true);Set(Metric.SelectedDetail,selected?.Detail,now,true);
        Set(Metric.RawModule,d.ModulePixels>0?d.ModulePixels:null,now,true);Set(Metric.DecoderDetail,d.Sharpness,now,true);Set(Metric.DecoderContrast,d.Contrast,now,true);Refresh(true);
    }
    internal void AddNetwork(NetworkCameraSample sample)
    {
        long now=Environment.TickCount64;Set(Metric.NetFps,sample.Fps,now,true);Set(Metric.NetKbps,sample.Kbps,now,true);Set(Metric.NetReceive,sample.ReceiveMs,now,true);
        Set(Metric.VideoDecodeMs,sample.DecodeMs,now,true);Set(Metric.NetInterval,sample.IntervalMs,now,true);Set(Metric.NetBytes,sample.Bytes,now,true);Set(Metric.NetRetries,sample.Reconnects,now,true);Refresh(false);
    }
    internal void ResetHistory(){foreach(var e in _entries.Values)e.Window.Clear();Refresh(true);}
    internal void Clear(){_completed=null;_frame=null;foreach(var e in _entries.Values)e.Value=null;ResetHistory();}
    internal void FilterChanged(int eligible,int invalid){Set(Metric.Eligible,_completed is null?null:eligible,0,false);Set(Metric.Invalid,_completed is null?null:invalid,0,false);Refresh(true);}
    internal void Refresh(bool force)
    {
        long now=Environment.TickCount64;if(!force&&now-_lastRender<250)return;_lastRender=now;
        foreach(var e in _entries.Values)
        {
            // Frozen/static sources retain the snapshot; a live history is strictly rolling.
            if(_live&&!_frozen)e.Window.Trim(now);
            var grade=Rate(e,e.Value);var brush=ColorFor(grade);
            e.Current.Text=Number(e.Value,e.Def);if(e.Def.Evaluate is null&&e.Value.HasValue)e.Current.ClearValue(TextBlock.ForegroundProperty);else e.Current.Foreground=brush;
            var p05=e.Window.Percentile(.05);var p95=e.Window.Percentile(.95);
            e.P05.Text=Number(p05,e.Def);e.P95.Text=Number(p95,e.Def);
            if(e.Def.Evaluate is null&&p05.HasValue)e.P05.ClearValue(TextBlock.ForegroundProperty);else e.P05.Foreground=ColorFor(Rate(e,p05));
            if(e.Def.Evaluate is null&&p95.HasValue)e.P95.ClearValue(TextBlock.ForegroundProperty);else e.P95.Foreground=ColorFor(Rate(e,p95));
            if(e.Graph is { } graph)
            {
                var display=e;
                if(_completed?.Qr is null&&_completed?.SelectedArea is not null)
                    display=e.Def.Id switch {Metric.Detail=>_entries[Metric.AreaDetail],Metric.Contrast=>_entries[Metric.AreaContrast],_=>e};
                e.Title.Text=T(display.Def.Ru,display.Def.Uk,display.Def.En);
                var displayGrade=Rate(display,display.Value);var displayBrush=ColorFor(displayGrade);
                e.Large.Text=Number(display.Value,display.Def)+(display.Value.HasValue&&display.Def.Unit.Length>0?" "+display.Def.Unit:"");e.Large.Foreground=displayBrush;
                e.Rating.Text=GradeText(displayGrade);e.Rating.Foreground=displayBrush;
                if(display.Def.Id==Metric.FrameDetail&&display.Value.HasValue)
                {
                    e.Large.ClearValue(TextBlock.ForegroundProperty);
                    e.Rating.Text=T("Зависит от сцены","Залежить від сцени","Scene-dependent");
                    ToolTip.SetTip(e.Title,T("Перепады яркости по всему кадру зависят от содержимого. Это не самостоятельная оценка фокуса или смаза.","Перепади яскравості всього кадру залежать від вмісту. Це не самостійна оцінка фокуса або розмиття.","Whole-frame intensity changes depend on scene content; this alone does not measure focus or blur."));
                }
                graph.Set(display.Window.Points(),now,displayBrush,_live&&!_frozen);
            }
        }
        ReflowMeasurements(Bounds.Width);
        var q=_completed?.Qr;var tips=new List<string>();
        if(q is not null)
        {
            if(q.ModulePixels<2)tips.Add(T("QR слишком мелкий: приблизьте или увеличьте печатный QR.","QR надто дрібний: наблизьте або збільште друкований QR.","QR is very small: move closer or enlarge the printed QR."));
            else if(q.ModulePixels<3.5)tips.Add(T("Модуль мал: приближайте, сохраняя фокус.","Модуль малий: наближайте, зберігаючи фокус.","Small modules: move closer while keeping focus."));
            if(q.Detail<9)tips.Add(T("QR мало резкий: проверьте фокус.","QR недостатньо різкий: перевірте фокус.","Low QR detail: check focus."));
            if(q.Contrast<35)tips.Add(T("Слабый контраст QR.","Слабкий контраст QR.","Low QR contrast."));
        }
        else if(_completed?.SelectedArea is { } area)
        {
            tips.Add(T("QR не локализован; доступны измерения выбранной области в таблице.","QR не локалізовано; доступні вимірювання обраної ділянки в таблиці.","QR not localized; selected-area measurements are available in the table."));
            if(area.Detail<9)tips.Add(T("Мало деталей в области: проверьте фокус.","Мало деталей у ділянці: перевірте фокус.","Few details in selected area: check focus."));
            if(area.Contrast<35)tips.Add(T("Слабый контраст области.","Слабкий контраст ділянки.","Low selected-area contrast."));
        }
        else if(_frame is not null)tips.Add(T("QR ещё не локализован: наведите камеру или выделите область QR.","QR ще не локалізовано: наведіть камеру або виділіть ділянку QR.","QR not localized yet: aim the camera or select a QR region."));
        if(_frame is { } f)
        {
            if(f.Light<65||f.Dark>.8)tips.Add(T("Добавьте свет.","Додайте світло.","Add light."));
            if(f.Bright>.85)tips.Add(T("Проверьте блики.","Перевірте відблиски.","Check glare."));
            if(f.Motion is >12)tips.Add(T("Удерживайте карточку неподвижно.","Тримайте картку нерухомо.","Hold the card still."));
        }
        _advice.Text=tips.Count>0?string.Join(" ",tips):q is not null?T("Явных проблем по измерениям нет.","Явних проблем за вимірюваннями немає.","No obvious measured issue."):T("Ожидание измерений.","Очікування вимірювань.","Waiting for measurements.");
        _advice.ClearValue(TextBlock.ForegroundProperty);
        ToolTip.SetTip(_windowLabel,T("Размер модуля в исходных пикселях, без цифрового увеличения. Стоп-кадр сохраняет статистику.","Розмір модуля у вихідних пікселях, без цифрового збільшення. Стоп-кадр зберігає статистику.","Module size in original pixels, without digital zoom. Freeze retains statistics."));
        _windowLabel.Text=T("Последние 30 с · Сейчас / P₀₅ / P₉₅","Останні 30 с · Зараз / P₀₅ / P₉₅","Last 30 s · Now / P05 / P95")+" · "+T("Кадров / поисков","Кадрів / пошуків","Frames / scans")+$": {_entries[Metric.FrameDetail].Window.Count} / {_entries[Metric.Attempts].Window.Count}";
    }
    private sealed class Sparkline : Control
    {
        private (long Time,double Value)[] _points=[];private long _right;private IBrush _brush=Brushes.Gray;
        internal double Scale=1;
        internal void Set((long Time,double Value)[] points,long now,IBrush brush,bool rolling)
        {_points=points;_right=rolling||points.Length==0?now:points[^1].Time;_brush=brush;InvalidateVisual();}
        public override void Render(DrawingContext context)
        {
            base.Render(context);double w=Bounds.Width,h=Bounds.Height;if(w<2||h<2)return;
            var baseline=new Pen(new SolidColorBrush(Color.FromArgb(60,128,128,128)),1);
            context.DrawLine(baseline,new Point(0,h-1),new Point(w,h-1));
            var pen=new Pen(_brush,1.5);Point? prev=null;long previousTime=0;
            foreach(var p in _points)
            {
                double x=w*(1-(_right-p.Time)/(double)ScannerMetricWindow.WindowMs);
                if(x<0||x>w){prev=null;continue;}
                var next=new Point(x,1+(h-2)*(1-Math.Clamp(p.Value/Scale,0,1)));
                if(prev is { } before&&p.Time-previousTime<=4000)context.DrawLine(pen,before,next);
                else context.DrawLine(pen,new Point(Math.Max(0,x-1),next.Y),next);
                prev=next;previousTime=p.Time;
            }
        }
    }
}
