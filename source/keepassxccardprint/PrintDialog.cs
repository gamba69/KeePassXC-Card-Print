using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Interactivity;
using KeePassXCCardPrint.Rendering;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Platforms;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint;

/// <summary>A compact dialog for direct printer submission. No external PDF viewer or temp files.</summary>
public sealed class PrintDialog : Window
{
    private readonly IPrinterService _printing = PlatformServices.Current.Printing;
    private readonly ShellViewModel _vm;
    private readonly UiSettings _settings;
    private readonly CardAppearance _appearance;
    private readonly IReadOnlyList<CardPrintSheet> _sheets;
    private readonly Action _save;
    private readonly ComboBox _printer=new(){MinWidth=290};
    private readonly ComboBox _method=new(){MinWidth=235};
    private readonly ComboBox _dpi=new(){MinWidth=130};
    private readonly ComboBox _sides=new(){MinWidth=240};
    private readonly ComboBox _duplex=new(){MinWidth=280};
    private readonly TextBlock _methodHelp=new();
    private readonly TextBlock _feedHelp=new();
    private bool _hardwareDuplexAvailable;
    private bool _xpsAvailable;
    private bool _printModeInitialized;
    private sealed record PrintModeOption(string Code, string Caption)
    {
        public override string ToString() => UiText.Tr(Caption);
    }
    private sealed record FeedModeOption(string Code, string Caption)
    {
        public override string ToString() => UiText.Tr(Caption);
    }
    private string SelectedPrintMethod => (_method.SelectedItem as PrintModeOption)?.Code ?? "auto";
    private string SelectedFeedMode => (_duplex.SelectedItem as FeedModeOption)?.Code ?? "manual";
    private readonly TextBlock _printerInfo=new(){FontSize=11,
        TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly Button _properties=PrintDialogButtons.Create("Свойства…","settings");
    private readonly TextBox[] _offsets=[new(),new(),new(),new()];
    private readonly TextBlock _notice=new(){TextWrapping=Avalonia.Media.TextWrapping.Wrap};
    private readonly Button _start=PrintDialogButtons.Create("Напечатать","print");
    private readonly Button _test=PrintDialogButtons.Create("Тестовый лист","page");


    private CancellationTokenSource? _running;
    private PrinterDeviceOptions? _driverMode;

    public PrintDialog(ShellViewModel vm,UiSettings settings,
        (CardAppearance Appearance,IReadOnlyList<CardPrintSheet> Sheets) job,Action save)
    {
        _vm=vm;_settings=settings;_appearance=job.Appearance;_sheets=job.Sheets;_save=save;
        Title="Печать карточек";
        PrintWindowIcons.Attach(this);
        Width=570;MinWidth=500;Height=600;MinHeight=500;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;

        // Reuse the same settings-card surfaces and typography as the main
        // window. Only the contents scroll: print actions stay visible.
        var sections=new StackPanel{Spacing=9,Margin=new Thickness(12,12,12,8)};
        var scroll=new ScrollViewer{Content=sections,
            VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        var root=new DockPanel{LastChildFill=true};
        var footer=new Border();
        footer.Classes.Add("app-footer");
        var footerStack=new StackPanel{Spacing=5};
        _notice.Classes.Add("hint");
        _notice.Text="После печати подтвердите результат, прежде чем отметить карточки напечатанными.";
        footerStack.Children.Add(_notice);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=6,
            HorizontalAlignment=HorizontalAlignment.Right,
            VerticalAlignment=VerticalAlignment.Center};
        var cancel=PrintDialogButtons.Create("Отмена","cancel");
        cancel.Click+=async (_,_)=>
        {
            if(_running is null){Close();return;}
            if(await AppConfirmations.AskAsync(this,"Прервать печать?",
                "Некоторые страницы уже могли попасть в очередь принтера. После прерывания проверьте физические листы перед повторной печатью.",
                "Прервать печать",true,"cancel"))
                _running?.Cancel();
        };
        actions.Children.Add(_start);actions.Children.Add(cancel);
        footerStack.Children.Add(actions);
        footer.Child=footerStack;
        DockPanel.SetDock(footer,Dock.Bottom);
        root.Children.Add(footer);root.Children.Add(scroll);
        Content=root;
        UiText.Enable(this, translateText: true);

        // Printer section
        var printers=_printing.Printers();
        _printer.ItemsSource=printers;
        string preferred=printers.Contains(_settings.LastPrintDevice)?_settings.LastPrintDevice:_printing.DefaultPrinter();
        _printer.SelectedItem=printers.Contains(preferred)?preferred:printers.FirstOrDefault();
        _printer.SelectionChanged += (_, _) => UpdatePrinterChoice();
        var choose=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=7};
        _printer.HorizontalAlignment=HorizontalAlignment.Stretch;
        _printer.MinWidth=0;
        choose.Children.Add(_printer);
        Grid.SetColumn(_properties,1);
        _properties.Click+=(_,_)=>ShowProperties();
        choose.Children.Add(_properties);
        _printerInfo.Classes.Add("hint");
        sections.Children.Add(Section("Принтер",choose,_printerInfo));

        // Print method section: XPS and raster have different guarantees.
        _method.SelectionChanged+=(_,_)=>UpdateMethod();
        _dpi.ItemsSource=new[]{"300 DPI","600 DPI","1200 DPI"};
        _dpi.SelectedIndex=_settings.PrintRasterDpi switch {300=>0,1200=>2,_=>1};
        _dpi.MinWidth=110;
        var methodRow=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7};
        methodRow.Children.Add(_method);methodRow.Children.Add(_dpi);
        _methodHelp.Classes.Add("hint");
        sections.Children.Add(Section("Способ печати",methodRow,_methodHelp));

        // One piece of paper at a time in manual duplex; hide the hardware
        // choice completely when the driver does not advertise auto duplex.
        var paper=_sheets[0].Paper;
        var paperInfo=new TextBlock
        {
            Text=$"{paper.Format} · {paper.WidthMm:0.#} × {paper.HeightMm:0.#} мм · " +
                $"{_sheets.Select(s=>s.SheetNumber).Distinct().Count()} лист(ов) · " +
                $"{_sheets.SelectMany(s=>s.Slots).Select(s=>s.CardIndex).Distinct().Count()} карточек"
        };
        paperInfo.Classes.Add("hint");
        _sides.ItemsSource=new[]{"Лицевые и оборотные","Только лицевые","Только оборотные"}.Select(x=>UiText.Tr(x)).ToArray();
        _sides.SelectedIndex=_appearance.DuplexEnabled?0:1;
        _sides.MinWidth=240;
        // Feed method is independent of what sides are included. Even with a
        // simplex-only device, the user may print ALL faces and backs in a
        // single job, each on its own sheet, without stopping or flipping.
        _duplex.SelectionChanged+=(_,_)=>UpdateSides();
        _sides.SelectionChanged+=(_,_)=>UpdateSides();
        _feedHelp.Classes.Add("hint");
        var sideFields=new StackPanel{Spacing=5};
        sideFields.Children.Add(_sides);
        sideFields.Children.Add(_duplex);
        sideFields.Children.Add(_feedHelp);
        sections.Children.Add(Section("Стороны и подача",paperInfo,sideFields));
        if(!_appearance.DuplexEnabled) _sides.IsEnabled=false;

        // Calibration section
        var calibration=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,85,Auto,85"),
            RowDefinitions=new RowDefinitions("Auto,Auto"),ColumnSpacing=8,RowSpacing=6};
        string[] labels=new[]{"Лицо X","Лицо Y","Оборот X","Оборот Y"}.Select(x=>UiText.Tr(x)).ToArray();
        for(int i=0;i<4;i++)
        {
            var label=new TextBlock{Text=labels[i],VerticalAlignment=VerticalAlignment.Center};
            Grid.SetColumn(label,(i%2)*2);Grid.SetRow(label,i/2);calibration.Children.Add(label);
            _offsets[i].MinWidth=0;
            _offsets[i].PlaceholderText="0,0";
            Grid.SetColumn(_offsets[i],(i%2)*2+1);Grid.SetRow(_offsets[i],i/2);
            calibration.Children.Add(_offsets[i]);
        }
        var reset=PrintDialogButtons.Create("Сбросить смещения","reset");
        reset.HorizontalAlignment=HorizontalAlignment.Left;
        reset.Click+=async (_,_)=>
        {
            if(await AppConfirmations.AskAsync(this,"Сбросить калибровку?",
                "Смещения лицевой и оборотной сторон будут установлены в 0 мм.",
                "Сбросить",false,"reset"))
                foreach(var input in _offsets)input.Text="0,0";
        };
        var calibrationActions=new StackPanel{Orientation=Orientation.Horizontal,Spacing=7};
        calibrationActions.Children.Add(_test);
        calibrationActions.Children.Add(reset);
        sections.Children.Add(Section("Калибровка · смещение, мм",calibration,calibrationActions));

        _start.Click+=async (_,_)=>await ExecuteAsync(false);
        _test.Click+=async (_,_)=>await ExecuteAsync(true);
        Closed+=(_,_)=>_running?.Cancel();
        LoadCalibration();UpdatePrinterInfo();UpdateMethod();UpdateSides();
    }

    private bool _interruptionPromptOpen;
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if(_running is not null)
        {
            e.Cancel=true;
            if(_interruptionPromptOpen)return;
            _interruptionPromptOpen=true;
            try
            {
                if(await AppConfirmations.AskAsync(this,"Прервать печать?",
                    "Задание может быть частично отправлено принтеру. Проверьте распечатанные листы перед повтором.",
                    "Прервать печать",true,"cancel"))
                    _running?.Cancel();
            }
            finally{_interruptionPromptOpen=false;}
            return;
        }
        base.OnClosing(e);
    }

    private static Border Section(string title,params Control[] children)
    {
        var stack=new StackPanel{Spacing=7};
        var heading=new TextBlock{Text=title};
        heading.Classes.Add("section-heading");
        stack.Children.Add(heading);
        foreach(var child in children)stack.Children.Add(child);
        var panel=new Border{Child=stack};
        panel.Classes.Add("settings-card");
        return panel;
    }

    private void UpdatePrinterChoice()
    {
        _driverMode=null;
        LoadCalibration();
        UpdatePrinterInfo();
    }

    private string PrinterName => _printer.SelectedItem as string??"";
    private void UpdatePrinterInfo()
    {
        string previousMode=SelectedPrintMethod;
        bool printerSelected=!string.IsNullOrWhiteSpace(PrinterName);
        _hardwareDuplexAvailable=false;
        _xpsAvailable=false;
        string quality = "Качество драйвера: см. «Свойства…»";
        string duplex = "Автоматический дуплекс: не подтверждён";
        if(printerSelected)
        {
            try
            {
                bool knownManualOnly=_printing.IsKnownManualDuplexOnly(PrinterName);
                _hardwareDuplexAvailable=_printing.SupportsDuplex(PrinterName);
                var current=_printing.CurrentDriverResolution(PrinterName,_driverMode);
                var listed=_printing.SupportedResolutions(PrinterName);
                string currentText=current is { } active
                    ? (active.X==active.Y ? $"{active.X}" : $"{active.X}×{active.Y}") + " DPI"
                    : "уточните в свойствах драйвера";
                quality=$"Рабочее разрешение драйвера: {currentText}";
                if(listed.Count>0)
                    quality+=" · Режимы: "+string.Join(", ",listed.Take(5).Select(d=>
                        d.X==d.Y?$"{d.X}":$"{d.X}×{d.Y}"))+(listed.Count>5?" …":"")+" DPI";
                if(knownManualOnly)
                {
                    quality+=" · HP FastRes 600/1200 — в «Свойствах…»";
                    duplex="Двусторонняя печать: только вручную (HP P1102w)";
                }
                else if(_hardwareDuplexAvailable)
                    duplex="Автодуплекс: заявлен драйвером; проверьте наличие механизма";
                // The spool path check does not prove vectors survive
                // in a particular printer's firmware or its legacy driver.
                _xpsAvailable=_printing.IsVectorAvailable(PrinterName);
            }
            catch { /* Unknown hardware capability must not become "supported". */ }
        }
        _printerInfo.Text=printerSelected
            ? UiText.Tr(quality,_settings.Language)+"\n"+UiText.Tr(duplex,_settings.Language)
            : UiText.Tr("Принтер не выбран.",_settings.Language);

        var feedModes=new List<FeedModeOption>
        {
            new("manual", "Ручной переворот каждого листа"),
            new("sequential", "Все страницы подряд — без переворота")
        };
        if(_hardwareDuplexAvailable)
            feedModes.Add(new("hardware", "Аппаратный дуплекс (принтер переворачивает)"));
        string previousFeed=SelectedFeedMode;
        _duplex.ItemsSource=feedModes;
        _duplex.SelectedItem=feedModes.FirstOrDefault(x=>x.Code==previousFeed) ?? feedModes[0];

        var methods=_xpsAvailable
            ? new[]
            {
                new PrintModeOption("auto",$"Автоматически ({_printing.VectorFormatName} / 600 DPI)"),
                new PrintModeOption("vector",$"Векторный документ {_printing.VectorFormatName}"),
                new PrintModeOption("raster","Растровая печать")
            }
            : new[]
            {
                new PrintModeOption("auto","Автоматически (растр 600 DPI)"),
                new PrintModeOption("raster","Растровая печать")
            };
        string targetMode=_printModeInitialized?previousMode:_settings.PrintMethod;
        _method.ItemsSource=methods;
        _method.SelectedItem=methods.FirstOrDefault(x=>x.Code==targetMode) ?? methods[0];
        _printModeInitialized=true;
        _method.IsEnabled=printerSelected && _running is null;
        _start.IsEnabled=printerSelected && _running is null;
        _test.IsEnabled=printerSelected && _running is null;
        _properties.IsEnabled=printerSelected && _running is null;
        UpdateMethod();UpdateSides();
    }

    private void UpdateSides()
    {
        bool both=_appearance.DuplexEnabled && _sides.SelectedIndex==0;
        _duplex.IsVisible=both;
        _duplex.IsEnabled=both && _running is null;
        _feedHelp.Text=both ? SelectedFeedMode switch
        {
            "sequential" => "Без пауз: лицо листа 1, оборот листа 1, лицо листа 2, оборот листа 2… Каждая страница на ОТДЕЛЬНОМ листе бумаги.",
            "hardware" => "Обе стороны одного листа печатает механизм принтера; ручной переворот не требуется.",
            _ => "По одному листу: лицо → остановка → переворот этого листа → оборот → проверка."
        } : "Один проход: все выбранные стороны печатаются подряд на отдельных листах.";
    }

    private string CalibrationKey => PrinterName+"|"+_sheets[0].Paper.Format+"|"+
        (_sheets[0].Paper.WidthMm>_sheets[0].Paper.HeightMm?"L":"P");
    private void LoadCalibration()
    {
        var p=_settings.PrintCalibrations.TryGetValue(CalibrationKey,out var existing)?existing:new PrintCalibration();
        var nums=new[]{p.FrontX,p.FrontY,p.BackX,p.BackY};
        for(int i=0;i<4;i++)_offsets[i].Text=nums[i].ToString("0.0",CultureInfo.CurrentCulture);
    }
    private PrintCalibration? ReadCalibration()
    {
        var values=new double[4];
        for(int i=0;i<4;i++)
        {
            string raw=(_offsets[i].Text??"").Trim().Replace(',', '.');
            if(!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out values[i])||
                !double.IsFinite(values[i])||Math.Abs(values[i])>10)return null;
        }
        return new PrintCalibration{FrontX=values[0],FrontY=values[1],BackX=values[2],BackY=values[3]};
    }
    private void UpdateMethod()
    {
        string mode=SelectedPrintMethod;
        bool raster=mode=="raster";
        _dpi.IsEnabled=raster && _running is null;
        _dpi.IsVisible=raster;
        _methodHelp.Text=mode switch
        {
            "vector" => $"Векторный {_printing.VectorFormatName}-документ передаётся в {_printing.SpoolerName}. Драйвер может преобразовать его в растр.",
            "raster" => "Разрешение изображения приложения: 300 / 600 / 1200 DPI. Качество принтера — в «Свойствах…».",
            _ when _xpsAvailable => $"Сначала {_printing.VectorFormatName}; если отправка ещё не началась и векторный режим недоступен — растр 600 DPI.",
            _ => "Доступна растровая печать 600 DPI. Векторная печать для этого устройства не подтверждена."
        };
    }
    private void ShowProperties()
    {
        if(string.IsNullOrWhiteSpace(PrinterName))return;
        try
        {
            _driverMode=_printing.Properties(PrinterName,
                TryGetPlatformHandle()?.Handle??IntPtr.Zero,_driverMode);
            UpdatePrinterInfo();
        }
        catch(Exception){_notice.Text=$"Не удалось открыть свойства драйвера {_printing.SpoolerName}.";}
    }

    private async Task ExecuteAsync(bool test)
    {
        if(_running is not null ||string.IsNullOrWhiteSpace(PrinterName))return;
        var calibration=ReadCalibration();
        if(calibration is null){_notice.Text="Смещения должны быть числами от −10 до +10 мм.";return;}
        string printer=PrinterName;
        if(!test && _printing.MaySaveDocumentToFile(printer))
        {
            if(!await AppConfirmations.ConfirmSecretFileAsync(this,_settings.Language,true))return;
        }
        int methodChoice=SelectedPrintMethod switch {"vector"=>1,"raster"=>2,_=>0};
        int dpi=methodChoice==2?_dpi.SelectedIndex switch {0=>300,2=>1200,_=>600}:600;
        int side=_sides.SelectedIndex;
        string feedMode=SelectedFeedMode;
        if(feedMode=="hardware" && (!_printing.SupportsDuplex(printer) || !_hardwareDuplexAvailable))
        {
            _notice.Text="Драйвер не заявил поддержку автоматического дуплекса. Выберите ручной.";
            return;
        }
        var pages=_sheets.Where(s=>side==0 || (side==1&&!s.Reverse) || (side==2&&s.Reverse)).ToArray();
        if(test)
        {
            var paper=_sheets[0].Paper;
            pages=side switch
            {
                1 => [new CardPrintSheet(paper,1,false,[])],
                2 => [new CardPrintSheet(paper,1,true,[])],
                _ => [new CardPrintSheet(paper,1,false,[]),
                    new CardPrintSheet(paper,1,true,[])]
            };
            // A manual test still asks to turn one sheet, whereas the
            // sequential mode deliberately prints two separate test sheets.
        }
        if(pages.Length==0){_notice.Text="Для выбранной стороны нет печатных страниц.";return;}
        _settings.LastPrintDevice=printer;
        _settings.PrintMethod=SelectedPrintMethod;
        _settings.PrintRasterDpi=_dpi.SelectedIndex switch {0=>300,2=>1200,_=>600};
        _settings.PrintCalibrations[CalibrationKey]=calibration;_save();
        _running=new CancellationTokenSource();
        _start.IsEnabled=false;_test.IsEnabled=false;
        _printer.IsEnabled=false;_properties.IsEnabled=false;
        _method.IsEnabled=false;_dpi.IsEnabled=false;_sides.IsEnabled=false;
        _duplex.IsEnabled=false;
        var token=_running.Token;
        var submitted=new List<CardPrintSheet>();
        string actualMethod=(methodChoice==2 || (methodChoice==0 && !_xpsAvailable))
            ?$"Растр {dpi} DPI":$"Вектор {_printing.VectorFormatName}";
        try
        {
            var mode=_driverMode??_printing.DefaultJobSettings(printer);
            var front=pages.Where(s=>!s.Reverse).ToArray();
            var back=pages.Where(s=>s.Reverse).ToArray();
            async Task Send(IReadOnlyList<CardPrintSheet> group,int driverDuplex)
            {
                if(group.Count==0)return;
                try
                {
                    var request = new PrintSubmission(printer,mode,group,_appearance,
                        methodChoice switch {1=>PrintRenderingMode.Vector,2=>PrintRenderingMode.Raster,
                            _=>PrintRenderingMode.Automatic},
                        dpi,calibration.FrontX,calibration.FrontY,calibration.BackX,
                        calibration.BackY,driverDuplex);
                    var result=await Task.Run(() => _printing.Submit(request,token),token);
                    actualMethod=result.MethodLabel;
                }
                catch
                {
                    // A failed call may have delivered pages to the spooler.
                    // Keep the group in history as unconfirmed, never silently retry it.
                    submitted.AddRange(group);
                    throw;
                }
                submitted.AddRange(group);
            }
            _notice.Text=$"Отправка страниц в {_printing.SpoolerName}…";
            if(feedMode=="hardware" && side==0 && back.Length>0)
            {
                // The driver must report duplex AND the user explicitly choose
                // this mode: the printer cannot guarantee a physical auto-flip.
                if(!await PrintQuestion.AskYesNo(this,"Автоматический дуплекс",
                    "Убедитесь, что принтер ФИЗИЧЕСКИ переворачивает лист сам. " +
                    "Параметр драйвера не доказывает наличие механизма автопереворота. " +
                    "Печатать обе стороны одним заданием?","Печатать"))return;
                await Send(pages,_settings.PrintShortEdgeFlip?2:1);
            }
            else if(feedMode=="manual" && side==0 && front.Length>0 && back.Length>0)
            {
                // ONE physical sheet: front -> user flips -> same sheet back
                // -> user confirms result -> next sheet. No stack reversal.
                var sheets=ManualDuplexPlan.Create(pages);
                for(int i=0;i<sheets.Count;i++)
                {
                    token.ThrowIfCancellationRequested();
                    var current=sheets[i];
                    CardPrintSheet[] one=[current.Front,current.Back];
                    _notice.Text=$"Лист {i+1} из {sheets.Count}: печать лицевой стороны…";
                    await Send([current.Front],0);
                    if(!await PrintQuestion.AskYesNo(this,$"Лист {i+1} из {sheets.Count} — переворот",
                        "Возьмите ТОЛЬКО ЧТО напечатанный лист. Переверните его по " +
                        "проверенной на тестовом листе схеме подачи вашего принтера " +
                        "и вставьте обратно. Не переворачивайте всю стопку.",
                        "Печатать оборот"))throw new OperationCanceledException(token);
                    _notice.Text=$"Лист {i+1} из {sheets.Count}: печать оборота…";
                    await Send([current.Back],0);
                    if(test)
                    {
                        _notice.Text="Тестовый лист отправлен. Сравните стороны на просвет.";
                        return;
                    }
                    // A sheet is completed only after physical inspection.
                    var oneResult=await PrintQuestion.ConfirmAsync(this,one);
                    if(oneResult is null)
                    {
                        _vm.AddPrintHistory(printer,actualMethod,"Не подтверждено",one);
                        submitted.Clear();
                        _notice.Text="Лист оставлен без подтверждения. Остальные листы не печатались.";
                        return;
                    }
                    _vm.ConfirmPrint(one,oneResult);
                    _vm.AddPrintHistory(printer,actualMethod,
                        oneResult.Count==0?"Подтверждено":$"Брак: {oneResult.Count}",one);
                    submitted.Clear();
                }
                _notice.Text="Все листы поштучно напечатаны и проверены.";
                return;
            }
            else
            {
                // Simplex job. With both sides selected the pages are printed
                // as 1 front, 1 back, 2 front, 2 back, ... on DIFFERENT sheets.
                // One spool job, no manual prompts and no accidental duplex.
                await Send(SingleSidedPrintPlan.Create(pages),0);
            }
            if(test){_notice.Text="Тестовый лист отправлен. Проверьте геометрию и поправьте смещения.";return;}
            var result=await PrintQuestion.ConfirmAsync(this,pages);
            if(result is not null)
            {
                _vm.ConfirmPrint(pages,result);
                _vm.AddPrintHistory(printer,actualMethod,result.Count==0?"Подтверждено":$"Брак: {result.Count}",pages);
                _notice.Text="Результат записан в историю и состояния карточек.";
            }
            else
            {
                _vm.AddPrintHistory(printer,actualMethod,"Не подтверждено",pages);
                _notice.Text="Задание не подтверждено; статусы карточек не менялись.";
            }
        }
        catch(OperationCanceledException)
        {
            if(!test && submitted.Count>0)_vm.AddPrintHistory(printer,actualMethod,"Прервано / проверить вручную",submitted);
            _notice.Text="Печать прервана. Возможны уже отпечатанные листы, статусы не изменены.";
        }
        catch(Exception error)
        {
            if(!test && submitted.Count>0)_vm.AddPrintHistory(printer,actualMethod,"Ошибка / проверить вручную",submitted);
            _notice.Text="Ошибка печати: " + error.Message +
                $" Статусы карточек не изменены. Если задание могло уйти в {_printing.SpoolerName}, не повторяйте печать до проверки листов.";
        }
        finally
        {
            _running?.Dispose();_running=null;
            _start.IsEnabled=true;_test.IsEnabled=true;
            _printer.IsEnabled=true;_properties.IsEnabled=true;
            _method.IsEnabled=true;UpdateMethod();
            _sides.IsEnabled=_appearance.DuplexEnabled;
            UpdateSides();
        }
    }
}


/// <summary>
/// Consistent 26-DIP action buttons for the print dialog and all of its modal
/// confirmations. MinWidth=0 overrides Fluent minimums; width follows text.
/// Native window scaling applies to all buttons uniformly.
/// </summary>
internal static class PrintDialogButtons
{
    internal const double ActionHeight = 26;

    internal static Button Create(string caption,string icon="") => new()
    {
        Content = new StackPanel
        {
            Orientation=Orientation.Horizontal,Spacing=6,
            VerticalAlignment=VerticalAlignment.Center,
            Children=
            {
                ActionIconVectors.Create(string.IsNullOrEmpty(icon)?"page":icon),
                new TextBlock{Text=UiText.Tr(caption),VerticalAlignment=VerticalAlignment.Center}
            }
        },
        Height = ActionHeight,
        MinHeight = 0,
        MinWidth = 0,
        Padding = new Thickness(8, 2),
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };
}

internal static class PrintQuestion
{
    public static Task<bool> AskYesNo(Window owner,string title,string message,
        string confirmText="Продолжить")
        => AppConfirmations.AskAsync(owner,title,message,confirmText,
            false,confirmText.Contains("оборот",StringComparison.OrdinalIgnoreCase)?"page":"check");

    public static async Task<HashSet<int>?> ConfirmAsync(Window owner,IReadOnlyList<CardPrintSheet> pages)
    {
        var win=new Window{Title=UiText.Tr("Подтверждение печати"),Width=480,
            MinWidth=400,MinHeight=170,MaxHeight=530,SizeToContent=SizeToContent.Height,
            CanResize=false,ShowInTaskbar=false,WindowDecorations=WindowDecorations.Full,
            WindowStartupLocation=WindowStartupLocation.CenterOwner};
        PrintWindowIcons.Attach(win);
        var root=new DockPanel();
        var bottom=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8,
            HorizontalAlignment=HorizontalAlignment.Right};
        var all=PrintDialogButtons.Create("Всё напечатано","check");
        var partial=PrintDialogButtons.Create("Сохранить с браком","save");
        var later=PrintDialogButtons.Create("Отмена","cancel");
        bottom.Children.Add(all);bottom.Children.Add(partial);bottom.Children.Add(later);
        var footer=new Border();footer.Classes.Add("app-footer");footer.Child=bottom;
        DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var cards=pages.SelectMany(p=>p.Slots).GroupBy(s=>s.CardIndex).Select(g=>g.First()).ToArray();
        var choices=new List<(int Index,CheckBox Box)>();
        var col=new StackPanel{Spacing=8};
        col.Children.Add(new TextBlock{Text=UiText.Tr("Какие физические карточки НЕ получились? Отметьте брак."),
            TextWrapping=Avalonia.Media.TextWrapping.Wrap});
        foreach(var slot in cards)
        {
            var box=new CheckBox{Content=$"{UiText.Tr("Позиция")} {slot.CardIndex+1} · {slot.Card.Front.Record.Title}",
                IsChecked=false};
            col.Children.Add(box);choices.Add((slot.CardIndex,box));
        }
        var listCard=new Border{Child=col,Margin=new Thickness(12)};
        listCard.Classes.Add("settings-card");
        root.Children.Add(new ScrollViewer{Content=listCard,MaxHeight=410,
            VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto});
        win.Content=root;
        UiText.Enable(win, translateText: true);
        all.Click+=(_,_)=>win.Close(new HashSet<int>());
        partial.Click+=(_,_)=>win.Close(choices.Where(c=>c.Box.IsChecked==true).Select(c=>c.Index).ToHashSet());
        later.Click+=(_,_)=>win.Close((HashSet<int>?)null);
        win.KeyDown+=(_,e)=>
        {
            if(e.Key==Avalonia.Input.Key.Escape)
            {
                e.Handled=true;
                win.Close((HashSet<int>?)null);
            }
        };
        return await win.ShowDialog<HashSet<int>?>(owner);
    }
}
