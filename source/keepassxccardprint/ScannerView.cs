using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlashCap;
using FlashCap.Devices;
using FluentIcons.Common;
using KeePassXCCardPrint.Services;
using SkiaSharp;

namespace KeePassXCCardPrint;

public sealed class ScannerView : UserControl
{
    private readonly Canvas _surface = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _placeholder = new() { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(20) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _diagnosticsText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly TextBlock _settingsTitle = new() { FontSize = 14, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _sourceLabel = new(), _resolutionLabel = new(), _sensitivityLabel = new(), _timeoutLabel = new();
    private readonly RadioButton _cameraSource = new(){GroupName="ScannerInput"}, _fileSource = new(){GroupName="ScannerInput"}, _clipboardSource = new(){GroupName="ScannerInput"};
    private readonly Grid _cameraActions = new(){ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto"),ColumnSpacing=6,HorizontalAlignment=HorizontalAlignment.Left,MaxWidth=660};
    private readonly WrapPanel _fileActions = new(), _clipboardActions = new();
    private ScannerInputKind _inputKind=ScannerInputKind.Camera;
    private readonly Button _selectRegion = new(), _clearRegion = new(), _network = new();
    private readonly TextBlock _networkStatus=new(){TextWrapping=TextWrapping.Wrap};
    private readonly ContentControl _networkProtection=new(){IsVisible=false,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,1,5,0)};
    private readonly TextBlock _decoderNotice=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,IsVisible=false};
    private readonly StackPanel _sourceOverview=new(){Spacing=3};
    private readonly Grid _sourceLayout=new(){ColumnSpacing=12,RowSpacing=6};
    private readonly StackPanel _sourceOptions=new(){Spacing=5};
    private string? _streamCodec;
    private string? _codecProfileKey;
    private readonly TextBlock _sourceTitle=new(){FontWeight=FontWeight.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _sourceInfo=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock _networkTechnical=new(){TextWrapping=TextWrapping.Wrap,FontSize=11};
    private readonly TextBlock _decoderLabel=new();
    private readonly StackPanel _networkSettings=new(){Spacing=5};
    private readonly StackPanel _decoderSettings=new(){Spacing=5};
    private readonly ComboBox _decoderChoice=new();
    private readonly CheckBox _decoderFallback=new();
    private readonly Button _decoderDiagnostics=new();
    private readonly Button _onvifControls=new(){IsVisible=false,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly ProgressBar _sourceProgress=new(){IsIndeterminate=true,Height=3,IsVisible=false};
    private readonly ContentControl _sourceIcon=new(){VerticalAlignment=VerticalAlignment.Top};
    private sealed record CameraChoice(string Key,string Label,int LocalIndex,NetworkCameraProfile? Profile)
    { public override string ToString()=>Label; }
    private List<NetworkCameraProfile> _profiles=[];
    private List<(string Id,string Label)> _decoderChoices=[];
    private CameraChoice? SelectedCamera=>_devices.SelectedItem as CameraChoice;
    private NetworkCameraProfile? SelectedProfile=>SelectedCamera?.Profile;
    private int LocalIndex=>SelectedCamera?.LocalIndex??-1;
    private bool _networkEnded,_loadingDecoder,_profileStoreOk=true;
    private string _staticSource="",_sourceError="";
    private double? _networkFps,_networkDecodeMs;
    private int _networkWidth,_networkHeight;
    private (Icon Icon,string Color)? _sourceIconState;
    private volatile bool _keepPreview;
    private ScannerWorkspaceState Workspace=>ScannerWorkspace.Resolve(_cameraLive,NetworkLive,_networkEnded,_networkState,_frozen,_pdf is not null,_bitmap is not null,_textSource);
    private bool StaticSource=>Workspace is ScannerWorkspaceState.Image or ScannerWorkspaceState.Pdf or ScannerWorkspaceState.Text;
    private bool NetworkFailed=>Workspace==ScannerWorkspaceState.NetworkFailed;
    private CancellationTokenSource? _networkCancel;
    private Task? _networkTask;
    private string _networkDetail="";
    private NetworkCameraState _networkState=NetworkCameraState.Stopped;
    private string _networkProtocol="",_networkEngine="",_networkFormat="";
    private int _networkRetries,_networkTelemetryPending;
    private long _networkLastTelemetry;
    private bool _networkTls;
    private bool NetworkLive=>_networkCancel is not null;
    private readonly ScannerDashboard _dashboard;
    private readonly Border _diagnosticCard;
    private volatile ScannerSearchRegion? _searchRegion;
    private ScannerSearchRegion? _dragRegion;
    private Point? _regionStart;
    private bool _selectingRegion, _shownAnalyzed;
    private readonly ComboBox _sensitivity = new() { MinWidth = 160 };
    private readonly Button _rescan = new(), _freeze = new(), _probeControls = new();
    private readonly TextBlock _controlStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly StackPanel _hardware = new() { Spacing = 3 };
    private readonly WrapPanel _hardwareRows = new();
    private readonly object _sampleGate = new();
    private ScannerPixels? _bestPixels, _latestPixels, _displayPixels;
    private byte[] _thumbnail = [];
    private ScannerImageQuality? _quality, _bestQuality, _latestQuality, _analyzedQuality;
    private int _sampleCount, _freezeEpoch, _selectedBurstCount;
    private long _burstStarted, _latestSampleAt;
    private string? _cameraIdentity;
    private ScannerCameraProperty? _focusControl, _exposureControl;
    private bool _hardwareProbed, _hardwareError;
    private volatile bool _frozen;
    private ScanDiagnostics? _diagnostics;
    private volatile ScannerSensitivity _scanSensitivity = ScannerSensitivity.Adaptive;
    private int _previewPending, _scanSequence;
    private long _lastPreview;
    private readonly TextBlock _pageText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _devices = new() { MinWidth = 90, MaxWidth = 360 };
    private readonly ComboBox _modes = new() { MinWidth = 130, MaxWidth = 300 };
    private readonly CheckBox _all = new(), _sound = new(), _hide = new();
    private readonly ComboBox _timeout = new();
    private readonly StackPanel _cameraSettings;
    private readonly WrapPanel _pdfOptions = new();
    private readonly Button _camera = new(), _file = new(), _paste = new(), _clear = new(), _prev = new(), _next = new(), _rotate = new(), _zoomIn = new(), _zoomOut = new(), _fit = new();
    private UiSettings _settings = new();
    private Action? _preferencesChanged;
    private CaptureDeviceDescriptor[] _descriptors = [];
    private CaptureDevice? _capture;
    private ScannerPdf? _pdf;
    private Bitmap? _bitmap;
    private List<ScannerHit> _hits = [];
    private readonly List<(Rect Rect, ScannerHit Hit)> _regions = [];
    private readonly Dictionary<string, int> _lastSeen = new();
    private readonly HashSet<string> _suppressed = new();
    private int _generation, _pending, _page, _rotation;
    private double _zoom = 1;
    private volatile bool _active, _modal, _cameraLive;
    private volatile bool _busy;
    private bool _configuring, _textSource;
    private Window? Owner => TopLevel.GetTopLevel(this) as Window;
    private static string T(string ru, string uk, string en) => ScannerResultDialog.T(ru, uk, en);

    public ScannerView()
    {
        Focusable = true;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,4,380"), Margin = new Thickness(8) };
        root.ColumnDefinitions[0].MinWidth=280; root.ColumnDefinitions[2].MinWidth=300;
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 6 };
        var toolbar = new StackPanel{Spacing=5};
        var sources=new WrapPanel();
        _sourceLabel.VerticalAlignment=VerticalAlignment.Center;_sourceLabel.Margin=new Thickness(0,0,12,0);sources.Children.Add(_sourceLabel);
        foreach(var option in new[]{_cameraSource,_fileSource,_clipboardSource}){option.Margin=new Thickness(0,0,12,0);sources.Children.Add(option);}
        _cameraSource.IsChecked=true;toolbar.Children.Add(sources);
        _devices.MaxWidth=440;_devices.HorizontalAlignment=HorizontalAlignment.Stretch;
        _cameraActions.Children.Add(_devices);
        Grid.SetColumn(_network,1);_cameraActions.Children.Add(_network);
        Grid.SetColumn(_camera,2);_cameraActions.Children.Add(_camera);
        toolbar.Children.Add(_cameraActions);
        _file.Margin=new Thickness(0,0,8,2);_fileActions.Children.Add(_file);
        _fileActions.Children.Add(_pdfOptions);toolbar.Children.Add(_fileActions);
        _clipboardActions.Children.Add(_paste);toolbar.Children.Add(_clipboardActions);
        left.Children.Add(toolbar);
        var host = new Grid(); _scroll.Content = _surface; host.Children.Add(_scroll); host.Children.Add(_placeholder);
        var border = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = host };
        Grid.SetRow(border, 1); left.Children.Add(border);
        var viewTools = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new[] { _freeze,_rotate,_zoomOut,_fit,_zoomIn,_selectRegion,_clearRegion,_rescan,_clear }) { c.Classes.Add("preview-tool");c.Margin=new Thickness(0,0,5,3); viewTools.Children.Add(c); }
        Grid.SetRow(viewTools,2);left.Children.Add(viewTools);
        _status.FontSize=12;Grid.SetRow(_status,3);left.Children.Add(_status);root.Children.Add(left);
        var splitter=new GridSplitter { HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Stretch, Background=Brushes.Transparent };
        Grid.SetColumn(splitter,1);root.Children.Add(splitter);
        var right=new StackPanel { Spacing=8,Margin=new Thickness(7,0,5,0) };
        var settings = new StackPanel { Spacing = 5 };
        var source=new StackPanel{Spacing=5};source.Children.Add(_sourceLayout);_sourceOverview.Children.Add(_sourceTitle);
        var statusRow=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*"),ColumnSpacing=6};
        statusRow.Children.Add(_sourceIcon);Grid.SetColumn(_sourceInfo,1);statusRow.Children.Add(_sourceInfo);_sourceOverview.Children.Add(statusRow);_sourceOverview.Children.Add(_sourceProgress);
        _modes.MaxWidth=double.PositiveInfinity; _modes.HorizontalAlignment=HorizontalAlignment.Stretch;
        var cameraSettings=new StackPanel{Spacing=4};cameraSettings.Children.Add(Labeled(_resolutionLabel,_modes));
        _sourceOptions.Children.Add(cameraSettings);_cameraSettings=cameraSettings;
        var formatRow=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*")};
        var protectionMarker=ActionIconVectors.Create(Icon.Warning,14);
        protectionMarker.Foreground=new SolidColorBrush(Color.Parse("#C58A24"));
        _networkProtection.Content=protectionMarker;formatRow.Children.Add(_networkProtection);
        Grid.SetColumn(_networkStatus,1);formatRow.Children.Add(_networkStatus);_sourceOverview.Children.Add(formatRow);
        var decoderControls=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=6,VerticalAlignment=VerticalAlignment.Center};
        _decoderChoice.HorizontalAlignment=HorizontalAlignment.Stretch;_decoderChoice.VerticalAlignment=VerticalAlignment.Center;_decoderChoice.Height=29;_decoderChoice.MinHeight=29;decoderControls.Children.Add(_decoderChoice);
        _decoderDiagnostics.Classes.Add("preview-tool");_decoderDiagnostics.VerticalAlignment=VerticalAlignment.Center;
        Grid.SetColumn(_decoderDiagnostics,1);decoderControls.Children.Add(_decoderDiagnostics);
        _decoderSettings.Children.Add(Labeled(_decoderLabel,decoderControls));_decoderSettings.Children.Add(_decoderNotice);
        _decoderSettings.Children.Add(_decoderFallback);_networkSettings.Children.Add(_decoderSettings);
        _sourceOverview.Children.Add(_networkTechnical);_sourceOptions.Children.Add(_networkSettings);
        _networkSettings.Children.Add(_onvifControls);
        _probeControls.Classes.Add("preview-tool");
        _hardware.Children.Add(_hardwareRows);_hardware.Children.Add(_controlStatus);_sourceOptions.Children.Add(_hardware);
        _sourceLayout.Children.Add(_sourceOverview);_sourceLayout.Children.Add(_sourceOptions);
        void LayoutSource(double width){bool wide=width>=620;_sourceLayout.ColumnDefinitions=new ColumnDefinitions(wide?"*,*":"*");_sourceLayout.RowDefinitions=new RowDefinitions(wide?"Auto":"Auto,Auto");Grid.SetColumn(_sourceOptions,wide?1:0);Grid.SetRow(_sourceOptions,wide?0:1);}
        LayoutSource(0);bool sourceWide=false;_sourceLayout.SizeChanged+=(_,e)=>{bool wide=e.NewSize.Width>=620;if(wide!=sourceWide){sourceWide=wide;LayoutSource(e.NewSize.Width);}};
        foreach (var c in new Control[] { _prev, _pageText, _next }) { c.Margin = new Thickness(0,0,5,3); _pdfOptions.Children.Add(c); }
        var sourceCard=new Border{Padding=new Thickness(9),Child=source};sourceCard.Classes.Add("settings-card");right.Children.Add(sourceCard);
        settings.Children.Add(_settingsTitle);
        var recognition=new WrapPanel();
        // Keep each label and its input together, but let independent settings wrap.
        _sensitivity.MinWidth=0;_sensitivity.Width=142;_timeout.Width=76;
        var sensitivityRow=CompactPair(_sensitivityLabel,_sensitivity);
        var timeoutRow=CompactPair(_timeoutLabel,_timeout);
        foreach(var c in new Control[]{sensitivityRow,_all,_sound,_hide,timeoutRow})
        {c.Margin=new Thickness(0,0,8,3);c.VerticalAlignment=VerticalAlignment.Center;recognition.Children.Add(c);}
        settings.Children.Add(recognition);
        var settingsCard=new Border{Padding=new Thickness(9),Child=settings};settingsCard.Classes.Add("settings-card");right.Children.Add(settingsCard);
        _dashboard=new ScannerDashboard(_diagnosticsText);_diagnosticCard=new Border{Padding=new Thickness(9),Child=_dashboard};_diagnosticCard.Classes.Add("settings-card");right.Children.Add(_diagnosticCard);
        var sidebar=new ScrollViewer{Content=right,HorizontalContentAlignment=HorizontalAlignment.Stretch,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Grid.SetColumn(sidebar,2);root.Children.Add(sidebar);Content=root;
        _scroll.SizeChanged += (_, _) => RenderSurface();
        _surface.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed || _modal || _busy) return;
            var pt = e.GetPosition(_surface);
            if(_selectingRegion && _bitmap is not null)
            {
                _regionStart=ClampPoint(pt);_dragRegion=null;e.Pointer.Capture(_surface);e.Handled=true;return;
            }
            var hit = _regions.Where(r => r.Rect.Contains(pt) && r.Hit.Payload.Allowed(_all.IsChecked == true)).OrderBy(r => r.Rect.Width*r.Rect.Height).FirstOrDefault();
            if (hit.Hit is not null) { e.Handled = true; await Present(hit.Hit, false); }
        };
        _surface.PointerMoved += (_, e) =>
        {
            if(_regionStart is not { } start||!_selectingRegion)return;
            _dragRegion=RegionFromDisplay(start,ClampPoint(e.GetPosition(_surface)));RenderSurface();e.Handled=true;
        };
        _surface.PointerReleased += async (_, e) =>
        {
            if(_regionStart is not { } start||!_selectingRegion)return;
            var end=ClampPoint(e.GetPosition(_surface));var region=RegionFromDisplay(start,end);
            _regionStart=null;_dragRegion=null;_selectingRegion=false;e.Pointer.Capture(null);e.Handled=true;
            if(Math.Abs(end.X-start.X)*_surface.Width>=12&&Math.Abs(end.Y-start.Y)*_surface.Height>=12)await ChangeRegion(region);
            else { Localize();RenderSurface(); }
        };
        _surface.PointerCaptureLost += (_,_)=>{if(_regionStart is not null){_regionStart=null;_dragRegion=null;_selectingRegion=false;Localize();RenderSurface();}};
        _selectRegion.Click+=(_,_)=>{_selectingRegion=!_selectingRegion;_regionStart=null;_dragRegion=null;Localize();RenderSurface();};
        _clearRegion.Click+=async (_,_)=>await ChangeRegion(null);
        DragDrop.SetAllowDrop(border, true);
        border.AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        border.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            var f = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault();
            if (f is not null) { e.Handled = true; await Run(() => LoadFile(f),true); }
        });
        _cameraSource.Click += async (_,_)=>await SelectInput(ScannerInputKind.Camera);
        _fileSource.Click += async (_,_)=>await SelectInput(ScannerInputKind.File);
        _clipboardSource.Click += async (_,_)=>await SelectInput(ScannerInputKind.Clipboard);
        _camera.Click += async (_, _) => await Run(ToggleSelectedCamera);
        _network.Click += async (_, _) => await Run(ManageCameras,true);
        _decoderDiagnostics.Click+=async (_,_)=>await Run(async()=>{if(Owner is { } owner)await VideoDecoderDialog.Show(owner);PopulateDecoder();},true);
        _onvifControls.Click+=async (_,_)=>await Run(async()=>{if(Owner is { } owner&&SelectedProfile is { HasOnvif:true } profile)await OnvifDialog.Control(owner,profile);},true);
        _decoderChoice.SelectionChanged+=async (_,_)=>await DecoderSelectionChanged(true);
        _decoderFallback.IsCheckedChanged+=async (_,_)=>await DecoderSelectionChanged(false);
        _file.Click += async (_, _) => await Run(PickFile,true);
        _paste.Click += async (_, _) => await PasteAsync();
        _clear.Click += async (_, _) => await Run(ClearSource);
        _rescan.Click += async (_, _) => await Run(Rescan);
        _freeze.Click += async (_, _) => await Run(ToggleFreeze);
        _probeControls.Click += async (_, _) => await Run(ProbeHardware);
        _sensitivity.SelectionChanged += (_, _) => { if (!_configuring && _sensitivity.SelectedIndex >= 0) { _scanSensitivity = (ScannerSensitivity)_sensitivity.SelectedIndex; _settings.ScannerSensitivity = (int)_scanSensitivity; _dashboard.ResetHistory(); _preferencesChanged?.Invoke(); } };
        _prev.Click += async (_, _) => await Run(() => LoadPage(_page - 1));
        _next.Click += async (_, _) => await Run(() => LoadPage(_page + 1));
        _rotate.Click += (_, _) => { _rotation = (_rotation + 1) % 4; RenderSurface(); };
        _zoomIn.Click += (_, _) => { _zoom = Math.Min(8, _zoom * 1.25); RenderSurface(); };
        _zoomOut.Click += (_, _) => { _zoom = Math.Max(.25, _zoom / 1.25); RenderSurface(); };
        _fit.Click += (_, _) => { _zoom = 1; RenderSurface(); };
        _devices.SelectionChanged += async (_, _) =>
        {
            if(_configuring||!_active||_busy||_modal)return;
            await Run(async()=>{if(_cameraLive)await ClearSource();RememberCamera();PopulateModes();PopulateDecoder();UpdateStatus();});
        };
        _modes.SelectionChanged += (_, _) => { if (!_configuring && _modes.SelectedIndex >= 0) { _settings.ScannerResolution = _modes.SelectedIndex == 0 ? "" : _modes.SelectedItem?.ToString() ?? ""; _preferencesChanged?.Invoke(); } };
        _all.IsCheckedChanged += (_, _) => { if (!_configuring) { _settings.ScannerAllCodes = _all.IsChecked == true; _preferencesChanged?.Invoke(); _suppressed.Clear(); RenderSurface(); UpdateStatus(); } };
        _sound.IsCheckedChanged += (_, _) => { if (!_configuring) { _settings.ScannerSound = _sound.IsChecked == true; _preferencesChanged?.Invoke(); } };
        _hide.IsCheckedChanged += (_, _) => { if (!_configuring) { _settings.ScannerHideSecrets = _hide.IsChecked == true; _preferencesChanged?.Invoke(); } };
        _timeout.SelectionChanged += (_, _) => { if (!_configuring && _timeout.SelectedItem is string s && int.TryParse(s.Split(' ')[0], out int seconds)) { _settings.ScannerClipboardSeconds = seconds; _preferencesChanged?.Invoke(); } };
        AddHandler(KeyDownEvent, async (_, e) => { if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control) { e.Handled = true; await PasteAsync(); } });
        DetachedFromVisualTree += async (_, _) => await DeactivateAsync();
        Localize(); UpdateControls();
    }

    // Use the application's ordinary themed button/card surfaces, not the
    // platform Expander template (which has unrelated dark/light backgrounds).
    internal sealed class ScannerSection : StackPanel
    {
        private readonly Button _toggle=new(){HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(7,4)};
        private readonly TextBlock _label=new(){VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap};
        private readonly ContentControl _arrow=new(){VerticalAlignment=VerticalAlignment.Center};
        private readonly ContentControl _body=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,IsVisible=false};
        internal ScannerSection()
        {
            Spacing=4;
            var header=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto"),ColumnSpacing=6};
            header.Children.Add(_label);Grid.SetColumn(_arrow,1);header.Children.Add(_arrow);_toggle.Content=header;
            Children.Add(_toggle);Children.Add(_body);_toggle.Click+=(_,_)=>IsExpanded=!IsExpanded;IsExpanded=false;
        }
        internal string Header
        {
            set{_label.Text=value;Avalonia.Automation.AutomationProperties.SetName(_toggle,value);}
        }
        internal Control? Content {set=>_body.Content=value;}
        internal bool IsExpanded
        {
            get=>_body.IsVisible;
            set{_body.IsVisible=value;_arrow.Content=ActionIconVectors.Create(value?Icon.ChevronUp:Icon.ChevronDown);}
        }
    }

    private static StackPanel CompactPair(TextBlock label,Control control)
    {
        label.VerticalAlignment=VerticalAlignment.Center;control.VerticalAlignment=VerticalAlignment.Center;
        var pair=new StackPanel{Orientation=Orientation.Horizontal,Spacing=5,VerticalAlignment=VerticalAlignment.Center};
        pair.Children.Add(label);pair.Children.Add(control);return pair;
    }

    private static Grid Labeled(TextBlock label,Control control)
    {
        var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("105,*"),ColumnSpacing=6};label.VerticalAlignment=VerticalAlignment.Center;label.TextWrapping=TextWrapping.Wrap;
        control.HorizontalAlignment=HorizontalAlignment.Stretch;grid.Children.Add(label);Grid.SetColumn(control,1);grid.Children.Add(control);return grid;
    }
    private Point ClampPoint(Point p)=>new(Math.Clamp(p.X/Math.Max(1,_surface.Width),0,1),Math.Clamp(p.Y/Math.Max(1,_surface.Height),0,1));
    private Point Unrotate(Point p)=>_rotation switch {1=>new(p.Y,1-p.X),2=>new(1-p.X,1-p.Y),3=>new(1-p.Y,p.X),_=>p};
    private ScannerSearchRegion RegionFromDisplay(Point a,Point b)
    {
        a=Unrotate(a);b=Unrotate(b);return new((float)Math.Min(a.X,b.X),(float)Math.Min(a.Y,b.Y),(float)Math.Abs(a.X-b.X),(float)Math.Abs(a.Y-b.Y));
    }
    private async Task ChangeRegion(ScannerSearchRegion? region)
    {
        if(_busy||_modal||!_active||_bitmap is null)return;
        _selectingRegion=false;_regionStart=null;_dragRegion=null;
        Interlocked.Increment(ref _freezeEpoch);_searchRegion=region;ResetSamples();DropHits();_diagnostics=null;_shownAnalyzed=false;
        _quality=_analyzedQuality=null;_selectedBurstCount=0;_dashboard.Clear();_lastSeen.Clear();_suppressed.Clear();Localize();RenderSurface();UpdateControls();
        if(!_cameraLive||_frozen)await Run(Rescan);
    }
    private static void Caption(Button b, Icon icon, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(ActionIconVectors.Create(icon)); row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        b.Content = row; ToolTip.SetTip(b, text);
    }
    private static void ToolIcon(Button button,Icon icon,string title,string? hint=null)
    {
        button.Content=ActionIconVectors.Create(icon,16);
        ToolTip.SetTip(button,hint??title);
        Avalonia.Automation.AutomationProperties.SetName(button,title);
    }
    internal void Localize()
    {
        _settingsTitle.Text=T("Распознавание","Розпізнавання","Recognition");
        _decoderLabel.Text=T("Видеодекодер","Відеодекодер","Video decoder");
        _decoderFallback.Content=T("Резервирование при отказе","Резервування при відмові","Fallback on failure");
        ToolIcon(_decoderDiagnostics,Icon.Settings,T("Диагностика видеодекодеров…","Діагностика відеодекодерів…","Video decoder diagnostics…"));
        Caption(_onvifControls,Icon.Settings,T("Управление ONVIF…","Керування ONVIF…","ONVIF controls…"));
        _sourceLabel.Text=T("Источник","Джерело","Source");
        _cameraSource.Content=T("Камера","Камера","Camera");_fileSource.Content=T("Файл","Файл","File");_clipboardSource.Content=T("Буфер обмена","Буфер обміну","Clipboard");
        _resolutionLabel.Text=T("Разрешение","Роздільність","Resolution");_sensitivityLabel.Text=T("Режим поиска","Режим пошуку","Search mode");_timeoutLabel.Text=T("Очистка буфера","Очищення буфера","Clipboard clear");
        string regionTitle=_selectingRegion?T("Отменить выделение","Скасувати виділення","Cancel selection"):T("Область поиска","Ділянка пошуку","Search region");
        ToolIcon(_selectRegion,_selectingRegion?Icon.Dismiss:Icon.QrCode,regionTitle,_selectingRegion?regionTitle:regionTitle+" — "+T("выделите QR вместе с белыми полями мышью","виділіть QR разом із білими полями мишею","drag around the QR including its quiet zone"));
        ToolIcon(_clearRegion,Icon.DismissCircle,T("Весь кадр — снять ограничение области поиска","Увесь кадр — зняти обмеження ділянки пошуку","Full frame — clear search region"));
        _dashboard.Localize();
        string manage=T("Управление камерами…","Керування камерами…","Manage cameras…");
        _network.Content=ActionIconVectors.Create(Icon.Settings);
        ToolTip.SetTip(_network,manage);Avalonia.Automation.AutomationProperties.SetName(_network,manage);
        ShowNetworkState();
        Caption(_file, Icon.FolderOpen, T("Открыть файл…", "Відкрити файл…", "Open file…"));
        Caption(_paste, Icon.ClipboardPaste, T("Вставить", "Вставити", "Paste"));
        ToolIcon(_clear, Icon.Delete, T("Очистить изображение", "Очистити зображення", "Clear image"));
        _prev.Content=ActionIconVectors.Create(Icon.ChevronLeft);_next.Content=ActionIconVectors.Create(Icon.ChevronRight);
        string previousPage=T("Предыдущая страница","Попередня сторінка","Previous page"),nextPage=T("Следующая страница","Наступна сторінка","Next page");
        ToolTip.SetTip(_prev,previousPage);ToolTip.SetTip(_next,nextPage);
        Avalonia.Automation.AutomationProperties.SetName(_prev,previousPage);Avalonia.Automation.AutomationProperties.SetName(_next,nextPage);
        ToolIcon(_rotate, Icon.ArrowRotateClockwise, T("Повернуть", "Повернути", "Rotate"));
        ToolIcon(_zoomIn, Icon.ZoomIn, T("Увеличить", "Збільшити", "Zoom in"));
        ToolIcon(_zoomOut, Icon.ZoomOut, T("Уменьшить", "Зменшити", "Zoom out"));
        ToolIcon(_fit, Icon.ArrowFit, T("Вписать", "Вмістити", "Fit"));
        ToolIcon(_rescan, Icon.ArrowSync, T("Распознать снова", "Розпізнати знову", "Scan again"));
        string freezeTitle=_frozen?T("Продолжить камеру","Продовжити камеру","Resume camera"):T("Стоп-кадр","Стоп-кадр","Freeze");
        ToolIcon(_freeze,_frozen?Icon.Play:Icon.Camera,freezeTitle,_frozen?freezeTitle:freezeTitle+" — "+T("зафиксировать свежий кадр и выполнить глубокое распознавание","зафіксувати свіжий кадр і виконати глибоке розпізнавання","freeze a recent frame and run deep recognition"));
        ToolIcon(_probeControls,Icon.ArrowSync,T("Проверить фокус и экспозицию","Перевірити фокус та експозицію","Check focus and exposure"));
        RenderHardware();
        bool wasConfiguring = _configuring; _configuring = true;
        _sensitivity.ItemsSource = new[] { T("Быстрый", "Швидкий", "Fast"), T("Адаптивный", "Адаптивний", "Adaptive"), T("Максимальный", "Максимальний", "Maximum") };
        _sensitivity.SelectedIndex = (int)_scanSensitivity; _configuring = wasConfiguring;
        ToolTip.SetTip(_sensitivity, T("Быстрый — меньше попыток; адаптивный — дополнительные попытки по необходимости; максимальный — наиболее тщательный поиск.", "Швидкий — менше спроб; адаптивний — додаткові спроби за потреби; максимальний — найретельніший пошук.", "Fast — fewer attempts; adaptive — additional attempts as needed; maximum — most thorough search."));
        _all.Content = T("Все QR-коды", "Усі QR-коди", "All QR codes");
        _sound.Content = T("Звук", "Звук", "Sound");
        _hide.Content = T("Скрывать секреты", "Приховувати секрети", "Hide secrets");
        ToolTip.SetTip(_devices, T("Камера", "Камера", "Camera"));
        ToolTip.SetTip(_modes, T("Разрешение", "Роздільна здатність", "Resolution"));
        ToolTip.SetTip(_timeout, T("Очистка буфера обмена после копирования", "Очищення буфера обміну після копіювання", "Clipboard clearing after copying"));
        UpdateInputPresentation();
        UpdateStatus();
    }
    internal async Task ActivateAsync(UiSettings settings, Action? preferencesChanged = null)
    {
        _settings = settings; _preferencesChanged = preferencesChanged;
        bool alreadyActive = _active;
        _active = true; _configuring = true;
        _scanSensitivity = (ScannerSensitivity)Math.Clamp(settings.ScannerSensitivity,0,2);
        _all.IsChecked = settings.ScannerAllCodes; _sound.IsChecked = settings.ScannerSound; _hide.IsChecked = settings.ScannerHideSecrets;
        var times = new[] { 5, 10, 15, 30, 60, 120, 300, Math.Clamp(settings.ScannerClipboardSeconds, 5, 300) }.Distinct().Order().Select(s => s + " s").ToArray();
        _timeout.ItemsSource = times; _timeout.SelectedItem = Math.Clamp(settings.ScannerClipboardSeconds, 5, 300) + " s";
        _configuring = false; Localize();
        if (!alreadyActive) await Run(RefreshDevices);
    }
    internal async Task DeactivateAsync()
    {
        if (!_active && !_cameraLive && _capture is null && _bitmap is null && _hits.Count == 0) return;
        _active = false;
        await ClearSource();
    }
    private async Task Run(Func<Task> action,bool keepPreview=false)
    {
        if (!_active || _busy || _modal) return;
        _keepPreview=keepPreview;_busy = true; _sourceError="";UpdateControls();
        try { await action(); }
        catch { if (_active) _sourceError = T("Не удалось прочитать источник. Проверьте формат, устройство и разрешения.", "Не вдалося прочитати джерело. Перевірте формат, пристрій і дозволи.", "Could not read source. Check format, device and permissions."); }
        finally { _keepPreview=false;_busy = false; UpdateControls(); }
    }
    internal Task PasteAsync() => Run(Paste,true);
    private async Task SelectInput(ScannerInputKind requested)
    {
        // A radio button can change its checked state before Click. Always restore
        // the committed source if the request is rejected or is a no-op.
        if(!_active||_busy||_modal||requested==_inputKind){UpdateInputPresentation();return;}
        await Run(async()=>
        {
            await ClearSource();
            if(!_active)return;
            _inputKind=requested;UpdateStatus();
        });
    }
    private void UpdateInputPresentation()
    {
        var actions=ScannerWorkspace.InputActions(_inputKind);
        _cameraSource.IsChecked=actions.Camera;_fileSource.IsChecked=actions.File;_clipboardSource.IsChecked=actions.Clipboard;
        _cameraActions.IsVisible=actions.Camera;_fileActions.IsVisible=actions.File;_clipboardActions.IsVisible=actions.Clipboard;
        _placeholder.Text=_inputKind switch
        {
            ScannerInputKind.File=>T("Откройте изображение или PDF либо перетащите файл сюда.","Відкрийте зображення або PDF чи перетягніть файл сюди.","Open an image or PDF, or drop a file here."),
            ScannerInputKind.Clipboard=>T("Нажмите «Вставить» или Ctrl+V для чтения изображения или текста.","Натисніть «Вставити» або Ctrl+V, щоб прочитати зображення чи текст.","Press Paste or Ctrl+V to read an image or text."),
            _=>SelectedCamera is null?T("Добавьте сетевую камеру через шестерёнку или подключите локальную.","Додайте мережеву камеру через шестерню або підключіть локальну.","Use the settings button to add a network camera or connect a local camera."):
                _cameraLive?T("Ожидание изображения…","Очікування зображення…","Waiting for image…"):T("Выберите камеру и нажмите «Запустить».","Оберіть камеру й натисніть «Запустити».","Choose a camera and press Start.")
        };
    }
    private async Task<string[]> DiscoverDevices()
    {
        var devices=await Task.Run(()=>OperatingSystem.IsWindowsVersionAtLeast(6,1)
            ? new MediaFoundationDevices().GetDescriptors().Where(d=>d.Characteristics.Length>0).ToArray()
            : Array.Empty<CaptureDeviceDescriptor>());
        if(_active)_descriptors=devices;
        return devices.Select(d=>d.Name).ToArray();
    }
    private async Task RefreshDevices()
    {
        try{await DiscoverDevices();}catch{_sourceError=T("Не удалось обновить локальные камеры.","Не вдалося оновити локальні камери.","Could not refresh local cameras.");}
        if(!_active)return;ReloadProfiles();PopulateCameras(PreferredCameraKey());
    }
    private void ReloadProfiles()
    {
        try{_profiles=NetworkCameraProfiles.Load();_profileStoreOk=true;}
        catch{if(!NetworkLive)_profiles=[];_profileStoreOk=false;}
    }
    private void PopulateCameras(string? key)
    {
        bool was=_configuring;_configuring=true;
        var choices=_descriptors.Select((d,i)=>new CameraChoice(d.Name,d.Name+" · "+T("локальная","локальна","local"),i,null))
            .Concat(_profiles.Select(p=>new CameraChoice("network:"+p.Id,p.Name+" · "+T("сетевая","мережева","network"),-1,p))).ToList();
        _devices.ItemsSource=choices;int index=choices.FindIndex(c=>c.Key==key);_devices.SelectedIndex=index>=0?index:choices.Count>0?0:-1;
        _configuring=was;PopulateModes();PopulateDecoder();UpdateControls();
    }
    private string PreferredCameraKey()
    {
        string? id=null;try{id=NetworkCameraProfiles.LoadSelectedId();}catch{}
        return id is not null?"network:"+id:_settings.ScannerCameraName;
    }
    private void RememberCamera()
    {
        if(SelectedCamera is not { } choice)return;
        try{NetworkCameraProfiles.SaveSelectedId(choice.Profile?.Id);}catch{_sourceError=T("Не удалось запомнить выбранную камеру.","Не вдалося запам’ятати обрану камеру.","Could not remember selected camera.");}
        if(choice.Profile is null&&_settings.ScannerCameraName!=choice.Key){_settings.ScannerCameraName=choice.Key;_preferencesChanged?.Invoke();}
    }
    private VideoCharacteristics[] Characteristics()=>LocalIndex>=0&&LocalIndex<_descriptors.Length?_descriptors[LocalIndex].Characteristics:[];
    private bool UsesSharpRtsp=>SelectedProfile is { Engine:"sharp" } p&&Uri.TryCreate(p.Address,UriKind.Absolute,out var u)&&(u.Scheme is "rtsp" or "rtsps");
    private static string CodecKey(NetworkCameraProfile p)=>p.Id+"\n"+p.Address+"\n"+p.Engine;
    private string? CurrentStreamCodec=>SelectedProfile is { } p&&_codecProfileKey==CodecKey(p)?_streamCodec:null;
    private void PopulateDecoder()
    {
        bool fallback=SelectedProfile?.DecoderFallback==true;
        _loadingDecoder=true;
        try
        {
            _decoderChoices=[];
            if(!UsesSharpRtsp||SelectedProfile is not { } profile){_decoderChoice.ItemsSource=null;return;}
            var codec=CurrentStreamCodec;
            VideoDecoderCandidate[] available=[];
            try{available=VideoDecoderManager.Available(codec);}catch{}
            if(codec!="JPEG")_decoderChoices.Add(("auto",T("Автоматически","Автоматично","Automatic")));
            if(available.Any(c=>c.Hardware))_decoderChoices.Add(("hardware",T("Только аппаратные","Лише апаратні","Hardware only")));
            if(available.Any(c=>!c.Hardware))_decoderChoices.Add(("software",T("Только программные","Лише програмні","Software only")));
            _decoderChoices.AddRange(available.Select(c=>(c.Id,c.Label)));
            var wanted=profile.Decoder;
            _decoderChoice.ItemsSource=_decoderChoices.Select(c=>c.Label).ToArray();
            _decoderChoice.SelectedIndex=_decoderChoices.FindIndex(c=>c.Id==wanted);
            _decoderFallback.IsChecked=fallback&&_decoderChoice.SelectedIndex>=0&&ManualDecoder;
            UpdateDecoderNotice();
        }
        finally{_loadingDecoder=false;}
    }
    private string DecoderId=>_decoderChoice.SelectedIndex>=0&&_decoderChoice.SelectedIndex<_decoderChoices.Count?_decoderChoices[_decoderChoice.SelectedIndex].Id:"auto";
    private void UpdateDecoderNotice()
    {
        string? codec=CurrentStreamCodec;
        _decoderNotice.Text=codec=="JPEG"?T("JPEG: видеодекодер не нужен.","JPEG: відеодекодер не потрібний.","JPEG: no video decoder needed."):
            _decoderChoice.SelectedIndex<0?T("Сохранённый декодер недоступен. Выберите другой.","Збережений декодер недоступний. Оберіть інший.","Saved decoder unavailable. Choose another."):
            codec is null?T("Формат уточняется при подключении; список будет отфильтрован.","Формат уточнюється під час підключення; список буде відфільтровано.","Format is detected on connection; choices will be filtered."):"";
        _decoderNotice.IsVisible=_decoderNotice.Text.Length>0;
    }
    private bool ManualDecoder=>DecoderId is not ("auto" or "hardware" or "software");
    // Only user changes are committed. Probe refreshes, stream-codec discovery and
    // camera-list updates may change SelectedIndex without changing preferences.
    private async Task DecoderSelectionChanged(bool choiceChanged)
    {
        if(_loadingDecoder||_configuring||!_active||_busy||_modal||!_profileStoreOk||!UsesSharpRtsp||CurrentStreamCodec=="JPEG"||_decoderChoice.SelectedIndex<0||SelectedProfile is not { } old)return;
        if(choiceChanged&&old.Decoder!=DecoderId)
        {
            _loadingDecoder=true;
            try{_decoderFallback.IsChecked=false;}
            finally{_loadingDecoder=false;}
        }
        if(old.Decoder==DecoderId&&old.DecoderFallback==(ManualDecoder&&_decoderFallback.IsChecked==true))return;
        await Run(ApplyDecoder);
    }
    private async Task ApplyDecoder()
    {
        if(!_profileStoreOk||!UsesSharpRtsp||_decoderChoice.SelectedIndex<0||SelectedProfile is not { } old)return;
        var updated=old.WithDecoder(DecoderId,ManualDecoder&&_decoderFallback.IsChecked==true);
        var profiles=_profiles.Select(p=>p.Id==updated.Id?updated:p).ToList();
        try{NetworkCameraProfiles.Save(profiles);}catch{PopulateDecoder();_sourceError=T("Не удалось сохранить настройки камеры.","Не вдалося зберегти налаштування камери.","Could not save camera settings.");return;}
        bool restart=NetworkLive&&!NetworkFailed;await StopCameraIfLive();if(!_active)return;_profiles=profiles;PopulateCameras("network:"+updated.Id);
        if(restart&&_active)await StartNetwork(updated);
    }
    private async Task StopCameraIfLive(){if(_cameraLive||NetworkLive)await ClearSource();}
    private async Task ManageCameras()
    {
        if(Owner is not { } owner)return;
        var before=SelectedCamera?.Key;
        var result=await CameraManagerDialog.Show(owner,_descriptors.Select(d=>d.Name).ToArray(),before,_cameraLive?null:DiscoverDevices);
        if(!_active)return;
        if(result.ChangedIds.Contains(SelectedProfile?.Id??"")||result.SelectedKey is { } key&&key!=before)await StopCameraIfLive();
        ReloadProfiles();PopulateCameras(result.SelectedKey??before);RememberCamera();
    }
    private void PopulateModes()
    {
        bool was=_configuring;_configuring = true;
        var choices = new[] { T("Авто", "Авто", "Auto") }.Concat(Characteristics().Select(c => c.ToString())).ToArray();
        _modes.ItemsSource = choices;
        int index = Array.IndexOf(choices, _settings.ScannerResolution); _modes.SelectedIndex = Math.Max(0,index);
        _configuring = was;
    }
    private void UpdateControls()
    {
        bool ready = _active && !_busy && !_modal;
        var workspace=Workspace;var command=ScannerWorkspace.CameraCommand(workspace);
        bool hasCamera=SelectedCamera is not null,hasImage=_bitmap is not null;
        var actions=ScannerWorkspace.InputActions(_inputKind);
        UpdateInputPresentation();
        _cameraSource.IsEnabled=_fileSource.IsEnabled=_clipboardSource.IsEnabled=ready;
        _camera.IsEnabled=ready&&actions.Camera&&(hasCamera||_cameraLive);
        string action=command switch{ScannerCameraCommand.Retry=>T("Повторить","Повторити","Retry"),ScannerCameraCommand.Cancel=>T("Прервать подключение","Перервати підключення","Abort connection"),ScannerCameraCommand.Stop=>T("Остановить","Зупинити","Stop"),_=>T("Запустить","Запустити","Start")};
        Caption(_camera,command is ScannerCameraCommand.Stop or ScannerCameraCommand.Cancel?Icon.Dismiss:Icon.Camera,action);
        _network.IsEnabled=ready&&actions.Camera;_file.IsEnabled=ready&&actions.File;_paste.IsEnabled=ready&&actions.Clipboard;
        // A running camera has Stop/Cancel; Clear removes static content or dismisses a failed source.
        _clear.IsVisible=StaticSource||NetworkFailed||!_cameraLive&&_sourceError.Length>0;_clear.IsEnabled=ready&&_clear.IsVisible;
        _devices.IsEnabled=ready&&actions.Camera&&hasCamera;
        _cameraSettings.IsVisible=actions.Camera&&LocalIndex>=0&&!NetworkLive;
        _modes.IsEnabled=ready&&actions.Camera&&!_cameraLive;
        _networkSettings.IsVisible=actions.Camera&&SelectedProfile is not null;
        _decoderSettings.IsVisible=UsesSharpRtsp;_decoderChoice.IsEnabled=ready&&_profileStoreOk&&_decoderChoices.Count>0;UpdateDecoderNotice();
        _decoderFallback.IsVisible=CurrentStreamCodec!="JPEG"&&_decoderChoice.SelectedIndex>=0&&ManualDecoder;_decoderFallback.IsEnabled=ready&&_profileStoreOk&&_decoderFallback.IsVisible;
        _decoderDiagnostics.IsEnabled=ready;
        _onvifControls.IsVisible=actions.Camera&&SelectedProfile is { HasOnvif:true };_onvifControls.IsEnabled=ready&&_onvifControls.IsVisible;
        _networkTechnical.IsVisible=actions.Camera&&NetworkLive&&_networkTechnical.Text?.Length>0;
        _selectRegion.IsEnabled=ready&&hasImage;_clearRegion.IsEnabled=ready&&hasImage&&_searchRegion is not null;
        _freeze.IsVisible=_cameraLive&&!NetworkFailed;_freeze.IsEnabled=ready&&_cameraLive&&(_frozen||_quality is not null);
        _hardware.IsVisible=actions.Camera&&_cameraLive&&!NetworkLive;_hardware.IsEnabled=ready&&!_frozen;
        foreach(var c in new[]{_rotate,_zoomOut,_fit,_zoomIn}){c.IsVisible=hasImage;c.IsEnabled=ready;}
        _rescan.IsVisible=hasImage&&(!_cameraLive||_frozen);_rescan.IsEnabled=ready&&hasImage&&(!_cameraLive||_frozen);
        _selectRegion.IsVisible=_clearRegion.IsVisible=hasImage;
        _pdfOptions.IsVisible=actions.File&&_pdf is not null;
        _prev.IsEnabled=ready&&_page>0;_next.IsEnabled=ready&&_pdf is not null&&_page+1<_pdf.Count;
        _pageText.Text=_pdf is null?"":$"{_page+1} / {_pdf.Count}";
        _all.IsEnabled=_sound.IsEnabled=_hide.IsEnabled=_timeout.IsEnabled=_sensitivity.IsEnabled=ready;
        _diagnosticCard.IsVisible=actions.Camera||hasImage;
        UpdateSourceSummary();
    }
    private void UpdateSourceSummary()
    {
        _sourceTitle.Text=_inputKind switch
        {
            ScannerInputKind.File=>_pdf is not null?T("PDF-документ","PDF-документ","PDF document"):T("Файл","Файл","File"),
            ScannerInputKind.Clipboard=>T("Буфер обмена","Буфер обміну","Clipboard"),
            _=>SelectedCamera?.Label??T("Камера не выбрана","Камеру не обрано","No camera selected")
        };
        bool warning=_sourceError.Length>0||NetworkFailed;
        var iconState=(warning?Icon.Warning:_cameraLive?Icon.Camera:_inputKind==ScannerInputKind.Clipboard?Icon.ClipboardPaste:_inputKind==ScannerInputKind.File?Icon.DocumentOnePage:Icon.Camera,warning?"#C58A24":"");
        if(_sourceIconState is not { } previousIcon||previousIcon!=iconState){var glyph=ActionIconVectors.Create(iconState.Item1);if(iconState.Item2.Length>0)glyph.Foreground=new SolidColorBrush(Color.Parse(iconState.Item2));_sourceIcon.Content=glyph;_sourceIconState=iconState;}
        _sourceProgress.IsVisible=_busy||NetworkLive&&!_networkEnded&&(_networkState is NetworkCameraState.Connecting or NetworkCameraState.Retrying);
        _sourceInfo.Text=_sourceError.Length>0?_sourceError:_busy&&!_keepPreview?T("Выполняется операция…","Виконується операція…","Working…"):StaticSource?(_staticSource+(_bitmap is null?"":$" · {_bitmap.PixelSize.Width} × {_bitmap.PixelSize.Height}")):
            NetworkLive?(_frozen?T("Стоп-кадр","Стоп-кадр","Frozen"):NetworkCameraDialog.StateText(_networkState))+(_networkWidth>0?$" · {_networkWidth}×{_networkHeight}":"")+(_networkFps is { } fps?$" · {fps:F1} FPS":""):_cameraLive?(_frozen?T("Стоп-кадр","Стоп-кадр","Frozen"):_bitmap is null?T("Ожидание кадра…","Очікування кадру…","Waiting for frame…"):T("Камера работает","Камера працює","Camera running")+$" · {_bitmap.PixelSize.Width} × {_bitmap.PixelSize.Height}"):
            _inputKind==ScannerInputKind.File?T("Файл не открыт","Файл не відкрито","No file open"):
            _inputKind==ScannerInputKind.Clipboard?T("Нет загруженных данных","Немає завантажених даних","No imported data"):
            !_profileStoreOk?T("Хранилище сетевых камер недоступно. Локальные камеры доступны.","Сховище мережевих камер недоступне. Локальні камери доступні.","Network camera storage unavailable. Local cameras remain available."):
            SelectedCamera is null?T("Камера не выбрана","Камеру не обрано","No camera selected"):
            T("Камера остановлена","Камеру зупинено","Camera stopped");
    }
    private static string PassName(ScanPass pass) => pass switch
    {
        ScanPass.Original => T("Исходный кадр", "Вихідний кадр", "Original"),
        ScanPass.Global => T("Глобальный порог", "Глобальний поріг", "Global threshold"),
        ScanPass.Contrast => T("Контраст", "Контраст", "Contrast"),
        ScanPass.Sharp => T("Резкость", "Різкість", "Sharpen"),
        ScanPass.Local => T("Локальный порог", "Локальний поріг", "Local threshold"),
        ScanPass.Inverted => T("Инверсия", "Інверсія", "Inversion"),
        ScanPass.Region => T("Область кадра", "Ділянка кадру", "Region"),
        ScanPass.Enlarged => T("Увеличение", "Збільшення", "Upscale"),
        _ => T("Уменьшение", "Зменшення", "Downscale")
    };
    private void UpdateStatus()
    {
        if (_hits.Count > 0)
        {
            int valid = _hits.Count(h => h.Payload.Allowed(_all.IsChecked == true));
            int invalid = _hits.Count(h => h.Payload.Kind == ScannerKind.Invalid);
            _status.Text = T("Декодировано", "Декодовано", "Decoded") + $": {_hits.Count} · " + T("Подходящих", "Відповідних", "Eligible") + $": {valid} · " + T("Некорректных", "Некоректних", "Invalid") + $": {invalid}" + (valid > 0 ? " · " + T("Нажмите на QR-код", "Натисніть на QR-код", "Select a QR code") : " · " + T("Нет подходящих данных", "Немає відповідних даних", "No eligible payloads"));
        }
        else if (_diagnostics is { } d)
            _status.Text = d.Located ? T("QR локализован, но данные не прочитаны", "QR локалізовано, але дані не прочитано", "QR located; decoding failed") : d.FinderPoints > 0 ? T("Найдены возможные ориентиры, QR не локализован", "Знайдено можливі орієнтири, QR не локалізовано", "Possible finder patterns; QR not located") : T("QR не обнаружен", "QR не виявлено", "No QR detected");
        else _status.Text = _cameraLive ? T("Ожидание кадра…", "Очікування кадру…", "Waiting for frame…") : _bitmap is not null ? T("QR-коды не найдены", "QR-коди не знайдено", "No QR codes found") : T("Ожидание данных", "Очікування даних", "Waiting for input");
        ShowDiagnostics(_diagnostics); _dashboard.Mode(_cameraLive,_frozen);UpdateSourceSummary();
        _dashboard.FilterChanged(_hits.Count(h=>h.Payload.Allowed(_all.IsChecked==true)),_hits.Count(h=>h.Payload.Kind==ScannerKind.Invalid));
    }
    private void ShowDiagnostics(ScanDiagnostics? value)
    {
        _diagnosticsText.IsVisible = value is not null;
        if (value is not { } d) { _diagnosticsText.Text = ""; return; }
        _diagnosticsText.Text = T("Метод","Метод","Method")+": "+PassName(d.Pass)+" · "+T("Попыток","Спроб","Attempts")+$": {d.Attempts} · {d.Milliseconds} ms"+(d.SuccessfulPass is { } success?" · "+T("Успех","Успіх","Success")+": "+PassName(success):"");
        if (!d.Complete) _status.Text = T("Распознавание", "Розпізнавання", "Recognizing") + ": " + PassName(d.Pass) + (d.Located ? " · " + T("QR локализован", "QR локалізовано", "QR located") : d.FinderPoints > 0 ? " · " + T("Поиск по ориентирам", "Пошук за орієнтирами", "Finder search") : "");
    }
    private Action<ScanDiagnostics> Progress(int generation, bool live, int epoch = -1)
    {
        long last = 0;
        return d => {
            long now = Environment.TickCount64;
            if (now-last < 90) return; last = now;
            Dispatcher.UIThread.Post(() => { if (_active && generation == _generation && !_modal && (!live || Current(generation,epoch))) ShowDiagnostics(d); });
        };
    }
    private void DropHits()
    {
        foreach (var h in _hits) h.Dispose();
        _hits.Clear(); _regions.Clear();
    }
    private void DropImage()
    {
        _surface.Children.Clear(); _bitmap?.Dispose(); _bitmap = null; _displayPixels?.Dispose(); _displayPixels=null; DropHits(); _textSource = false; _diagnostics = null;_shownAnalyzed=false;
    }
    private async Task ClearSource()
    {
        Interlocked.Increment(ref _generation); _cameraLive = false; _frozen = false; Interlocked.Increment(ref _freezeEpoch);
        ResetSamples(); _quality = _analyzedQuality = null; _selectedBurstCount = 0;
        _searchRegion=_dragRegion=null;_regionStart=null;_selectingRegion=false;_dashboard.Clear();
        _cameraIdentity = null; _focusControl = _exposureControl = null; _hardwareProbed = _hardwareError = false;
        var cancel=_networkCancel;var networkTask=_networkTask;_networkCancel=null;_networkTask=null;
        cancel?.Cancel();_networkState=NetworkCameraState.Stopped;_networkProtocol=_networkEngine=_networkFormat="";_networkTls=false;_networkRetries=0;
        var capture = _capture; _capture = null;
        DropImage(); _pdf?.Dispose(); _pdf = null; _page = _rotation = 0; _zoom = 1;
        _networkEnded=false;_networkFps=_networkDecodeMs=null;_networkWidth=_networkHeight=0;_staticSource=_sourceError=_networkDetail="";
        _lastSeen.Clear(); _suppressed.Clear(); RenderSurface(); Localize(); UpdateControls();
        if(networkTask is not null){try{await networkTask;}catch{}}cancel?.Dispose();
        if (capture is not null)
        {
            try { await capture.StopAsync(); }
            catch { }
            finally { try { capture.Dispose(); } catch { } }
        }
    }
    private async Task ToggleSelectedCamera()
    {
        if(_inputKind!=ScannerInputKind.Camera)return;
        if(ScannerWorkspace.CameraCommand(Workspace) is ScannerCameraCommand.Stop or ScannerCameraCommand.Cancel){await ClearSource();return;}
        if(SelectedProfile is { } p){await StartNetwork(p);return;}
        await StartLocal();
    }
    private async Task StartLocal()
    {
        int selected = LocalIndex;
        if (selected < 0 || selected >= _descriptors.Length) return;
        var descriptor = _descriptors[selected]; var modes = descriptor.Characteristics;
        int mode = _modes.SelectedIndex;
        var characteristic = mode > 0 && mode <= modes.Length ? modes[mode - 1] : modes.OrderBy(c => Math.Abs((long)c.Width*c.Height - 1920L*1080)).First();
        int expected=_generation+1;await ClearSource();if(!_active||_generation!=expected)return;
        int generation = _generation; _cameraLive = true; _lastPreview = 0; _scanSequence = 0;
        try
        {
            var capture = await descriptor.OpenAsync(characteristic, scope => OnCameraFrame(scope, generation));
            if (!_active || generation != _generation) { capture.Dispose(); return; }
            _capture = capture; await capture.StartAsync();
            if (!_active || generation != _generation) return;
            _cameraIdentity=descriptor.Identity as string;RememberCamera();Localize();UpdateControls();
            await ProbeHardware();
        }
        catch { if(generation==_generation)await ClearSource();throw; }
    }
    private async Task StartNetwork(NetworkCameraProfile profile)
    {
        if(!_active||!_profileStoreOk||!profile.Validate(out var uri)){_sourceError=T("Проверьте профиль через шестерёнку рядом со списком камер.","Перевірте профіль через шестерню біля списку камер.","Check the profile using the settings button next to the camera list.");return;}
        if(profile.Engine=="ffmpeg"&&(uri.Scheme is "rtsp" or "rtsps")&&!NetworkFfmpegManager.IsReady)
        {
            if(Owner is not { } owner)return;
            await FfmpegComponentDialog.Show(owner);if(!NetworkFfmpegManager.IsReady||!_active)return;
        }
        int expected=_generation+1;await ClearSource();if(!_active||_generation!=expected)return;
        int generation=_generation;_cameraLive=true;_lastPreview=0;_scanSequence=0;RememberCamera();
        _networkDetail="";_networkProtocol=uri.Scheme.ToUpperInvariant();_networkEngine=uri.Scheme is "rtsp" or "rtsps"?(profile.Engine=="ffmpeg"?"FFmpeg":"SharpRTSP"):"HTTP";_networkState=NetworkCameraState.Connecting;_networkTls=false;_networkRetries=0;_networkLastTelemetry=0;
        var cancel=new CancellationTokenSource();_networkCancel=cancel;
        Localize();UpdateControls();
        _networkTask=Task.Run(async ()=>
        {
            try
            {
                await NetworkCameraSource.Run(profile,(bytes,sample)=>OnNetworkFrame(bytes,sample,generation),
                    (state,retries)=>Dispatcher.UIThread.Post(()=>NetworkStateChanged(generation,state,retries)),cancel.Token,diagnostic:detail=>Dispatcher.UIThread.Post(()=>{if(generation==_generation&&NetworkLive)_networkDetail=detail;}),
                    streamCodec:codec=>Dispatcher.UIThread.Post(()=>{if(generation==_generation&&NetworkLive&&(_streamCodec!=codec||_codecProfileKey!=CodecKey(profile))){_streamCodec=codec;_codecProfileKey=CodecKey(profile);PopulateDecoder();UpdateControls();}}));
            }
            catch{Dispatcher.UIThread.Post(()=>NetworkStateChanged(generation,NetworkCameraState.Error,_networkRetries));}
            finally
            {
                Dispatcher.UIThread.Post(()=>
                {
                    if(generation!=_generation||!NetworkLive||_networkEnded)return;
                    NetworkStateChanged(generation,NetworkCameraState.Error,_networkRetries);
                });
            }
        });
    }
    private void NetworkStateChanged(int generation,NetworkCameraState state,int retries)
    {
        if(!_active||generation!=_generation||!NetworkLive||_networkEnded)return;
        var previous=_networkState;
        bool wasWaiting=previous is NetworkCameraState.Connecting or NetworkCameraState.Retrying;
        bool waiting=state is NetworkCameraState.Connecting or NetworkCameraState.Retrying;
        _networkState=state;_networkRetries=retries;
        _networkEnded=!waiting&&state!=NetworkCameraState.Receiving;
        // Invalidate outstanding frames and clear diagnostics only on actual stream loss.
        if(previous==NetworkCameraState.Receiving&&state!=NetworkCameraState.Receiving)
        {
            _networkTls=false;_networkFps=_networkDecodeMs=null;_networkWidth=_networkHeight=0;_networkFormat="";
            if(!_frozen)
            {
                Interlocked.Increment(ref _freezeEpoch);ResetSamples();DropImage();_quality=_analyzedQuality=null;_selectedBurstCount=0;
                _dashboard.Clear();_lastSeen.Clear();_suppressed.Clear();RenderSurface();UpdateStatus();
            }
        }
        ShowNetworkState();
        // A retry-counter change does not change the workspace or its camera command.
        if(previous!=state&&!(wasWaiting&&waiting))UpdateControls();
    }
    private void ShowNetworkState()
    {
        _networkStatus.IsVisible=_inputKind==ScannerInputKind.Camera&&NetworkLive;
        if(!NetworkLive){_networkStatus.Text=_networkTechnical.Text="";_networkProtection.IsVisible=_networkTechnical.IsVisible=false;ToolTip.SetTip(_networkProtection,null);ToolTip.SetTip(_networkStatus,null);return;}
        bool plain=_networkProtocol is "HTTP" or "RTSP";bool unverified=_networkProtocol=="RTSPS"&&_networkEngine=="FFmpeg";
        string protection=unverified?T("Защита потока не подтверждена.","Захист потоку не підтверджено.","Transport protection is not verified."):
            plain?T("Без шифрования. Кадры и учётные данные могут быть перехвачены.","Без шифрування. Кадри й облікові дані можуть бути перехоплені.","Unencrypted. Frames and credentials may be intercepted."):
            _networkTls?T("TLS проверен","TLS перевірено","TLS verified"):T("TLS ещё не подтверждён","TLS ще не підтверджено","TLS not yet verified");
        // The format already includes the engine when emitted by SharpRTSP or FFmpeg.
        // Keep transport, actual decoder and essential stream facts, without repeated labels.
        string format=_networkFormat.Length==0?_networkEngine:_networkFormat;
        if(!format.StartsWith(_networkEngine+" / ",StringComparison.OrdinalIgnoreCase)&&format!=_networkEngine)
            format=_networkEngine+" / "+format;
        _networkStatus.Text=$"{_networkProtocol} · {format}";
        _networkProtection.IsVisible=plain||unverified;
        ToolTip.SetTip(_networkProtection,_networkProtection.IsVisible?protection:null);
        Avalonia.Automation.AutomationProperties.SetName(_networkProtection,protection);
        bool retrying=_networkState is NetworkCameraState.Connecting or NetworkCameraState.Retrying;
        ToolTip.SetTip(_networkStatus,protection+(_networkDecodeMs is { } ms?"\n"+T("Декодирование и BGRA","Декодування та BGRA","Decoding and BGRA")+$": {ms:F1} ms":"")+
            (retrying&&_networkDetail.Length>0?"\n"+_networkDetail:""));
        // Reserve the counter row from the start. Retry details must not resize the panel.
        _networkTechnical.Text=T("Переподключений","Перепідключень","Reconnects")+$": {_networkRetries}"+
            (!retrying&&_networkDetail.Length>0?"\n"+_networkDetail:"");
        _networkTechnical.IsVisible=_networkTechnical.Text.Length>0;
        UpdateSourceSummary();
    }
    private void OnNetworkFrame(SKBitmap bitmap,NetworkCameraSample sample,int generation)
    {
        // Keep telemetry and preview bounded; the network reader never queues image buffers.
        long now=Environment.TickCount64;
        if(_active&&generation==Volatile.Read(ref _generation)&&!_frozen&&now-Interlocked.Read(ref _networkLastTelemetry)>=250&&Interlocked.CompareExchange(ref _networkTelemetryPending,1,0)==0)
        {
            Interlocked.Exchange(ref _networkLastTelemetry,now);
            Dispatcher.UIThread.Post(()=>
            {
                try{if(_active&&generation==_generation&&NetworkLive&&!_frozen&&_networkState==NetworkCameraState.Receiving){_networkTls=sample.Tls;_networkFormat=sample.Format;_networkFps=sample.Fps;_networkDecodeMs=sample.DecodeMs;_networkWidth=sample.Width;_networkHeight=sample.Height;_dashboard.AddNetwork(sample);ShowNetworkState();}}
                finally{Interlocked.Exchange(ref _networkTelemetryPending,0);}
            });
        }
        int epoch=Volatile.Read(ref _freezeEpoch);
        if(!Current(generation,epoch)||_modal||(_busy&&!_keepPreview)||now-Interlocked.Read(ref _lastPreview)<85||Interlocked.CompareExchange(ref _previewPending,1,0)!=0)
        {ScannerPixels.ClearAndDispose(bitmap);return;}
        Interlocked.Exchange(ref _lastPreview,now);
        var pixels=new ScannerPixels(bitmap);
        _=Task.Run(()=>SampleCamera(pixels,generation,epoch));
    }
    private void ResetSamples()
    {
        lock (_sampleGate)
        {
            _bestPixels?.Dispose();
            _latestPixels?.Dispose();
            CryptographicOperations.ZeroMemory(_thumbnail);
            _bestPixels = _latestPixels = null; _bestQuality = _latestQuality = null;
            _thumbnail = []; _sampleCount = 0; _burstStarted = _latestSampleAt = 0;
        }
    }
    private bool Current(int generation,int epoch) => _active && _cameraLive && !_frozen && generation == Volatile.Read(ref _generation) && epoch == Volatile.Read(ref _freezeEpoch);
    private void OnCameraFrame(PixelBufferScope scope, int generation)
    {
        int epoch = Volatile.Read(ref _freezeEpoch);
        if (!Current(generation,epoch) || _modal || (_busy&&!_keepPreview)) return;
        long now=Environment.TickCount64;
        if(now-Interlocked.Read(ref _lastPreview)<85 || Interlocked.CompareExchange(ref _previewPending,1,0)!=0)return;
        Interlocked.Exchange(ref _lastPreview,now);
        try { var bytes=scope.Buffer.CopyImage(); _=Task.Run(()=>SampleUsbCamera(bytes,generation,epoch)); }
        catch { Interlocked.Exchange(ref _previewPending,0); }
    }
    private void SampleUsbCamera(byte[] bytes,int generation,int epoch)
    {
        ScannerPixels? pixels=null;
        try{pixels=new ScannerPixels(OpenBitmap(bytes));}
        catch{Interlocked.Exchange(ref _previewPending,0);}
        finally{CryptographicOperations.ZeroMemory(bytes);}
        if(pixels is not null)SampleCamera(pixels,generation,epoch);
    }
    private void SampleCamera(ScannerPixels pixels,int generation,int epoch)
    {
        ScannerPixels? selected=null,preview=null;ScannerImageQuality? selectedQuality=null;int burst=0;bool posted=false;
        try
        {
            var bitmap=pixels.Bitmap;
            ScannerImageQuality q;
            lock(_sampleGate)
            {
                if(!Current(generation,epoch))return;
                var thumb=new byte[ScannerQualityMeter.SampleWidth*ScannerQualityMeter.SampleHeight];
                q=ScannerQualityMeter.Measure(bitmap,thumb,_thumbnail);
                CryptographicOperations.ZeroMemory(_thumbnail); _thumbnail=thumb;
                _latestPixels?.Dispose();_latestPixels=pixels.Share();_latestQuality=q;
                long now=Environment.TickCount64;_latestSampleAt=now;
                if(_sampleCount==0)_burstStarted=now;
                if(_bestQuality is null||q.Score>_bestQuality.Score)
                {
                    _bestPixels?.Dispose();_bestPixels=pixels.Share();_bestQuality=q;
                }
                _sampleCount++;
                if(_sampleCount>=3||now-_burstStarted>=350)
                {
                    if(!_busy&&Interlocked.CompareExchange(ref _pending,1,0)==0)
                    {selected=_bestPixels;selectedQuality=_bestQuality;burst=_sampleCount;}
                    else _bestPixels?.Dispose();
                    _bestPixels=null;_bestQuality=null;_sampleCount=0;
                }
            }
            if(selected is not null&&selectedQuality is not null)
            {
                var input=selected;var quality=selectedQuality;
                var mode=_scanSensitivity;int seq=Interlocked.Increment(ref _scanSequence);
                _=Task.Run(()=>AnalyzeCamera(input,generation,epoch,mode,seq,quality,burst));selected=null;
            }
            preview=pixels.Share();var previewLease=preview;
            Dispatcher.UIThread.Post(()=>
            {
                try
                {
                    if(!Current(generation,epoch)||_modal||(_busy&&!_keepPreview))return;
                    bool first=_bitmap is null;_quality=q;_dashboard.AddFrame(q,true,false);
                    if(_hits.Count>0)return;
                    // Reuse the Avalonia framebuffer while resolution is unchanged.
                    if(_bitmap is WriteableBitmap existing&&existing.PixelSize.Width==previewLease.Bitmap.Width&&existing.PixelSize.Height==previewLease.Bitmap.Height)
                        CopyPreview(previewLease.Bitmap,existing);
                    else
                    {
                        var next=CreatePreview(previewLease.Bitmap);
                        _surface.Children.Clear();_bitmap?.Dispose();_bitmap=next;
                    }
                    _displayPixels?.Dispose();_displayPixels=previewLease.Share();_shownAnalyzed=false;RenderSurface();if(first)UpdateControls();else UpdateSourceSummary();
                }
                catch{}
                finally{previewLease.Dispose();Interlocked.Exchange(ref _previewPending,0);}
            });
            preview=null;posted=true;
        }
        catch{if(selected is not null){selected.Dispose();Interlocked.Exchange(ref _pending,0);}}
        finally{preview?.Dispose();pixels.Dispose();if(!posted)Interlocked.Exchange(ref _previewPending,0);}
    }
    private void AnalyzeCamera(ScannerPixels pixels,int generation,int epoch,ScannerSensitivity mode,int sequence,ScannerImageQuality quality,int burst)
    {
        Frame? frame=null;
        try
        {
            if(!Current(generation,epoch)){Interlocked.Exchange(ref _pending,0);return;}
            var progress=Progress(generation,true,epoch);
            frame=DecodeBitmap(pixels.Bitmap,mode,true,sequence,d=>{if(Current(generation,epoch))progress(d);},_searchRegion,pixels);
            var result=frame;
            Dispatcher.UIThread.Post(async ()=>
            {
                try
                {
                    if(!Current(generation,epoch)||_modal||_busy)return;
                    _analyzedQuality=quality; _selectedBurstCount=burst;
                    InstallFrame(result); await AutoPresent(true);
                }
                catch { if(Current(generation,epoch))_status.Text=T("Ошибка распознавания","Помилка розпізнавання","Recognition failed"); }
                finally { result.Dispose(); Interlocked.Exchange(ref _pending,0); }
            });
            frame=null;
        }
        catch { frame?.Dispose(); Interlocked.Exchange(ref _pending,0); Dispatcher.UIThread.Post(()=>{if(Current(generation,epoch))_status.Text=T("Ошибка обработки кадра","Помилка обробки кадру","Frame processing failed");}); }
        finally { pixels.Dispose(); }
    }
    private async Task ToggleFreeze()
    {
        if(!_cameraLive)return;
        if(_frozen)
        {
            Interlocked.Increment(ref _freezeEpoch); _frozen=false; ResetSamples(); DropImage();
            _quality=_analyzedQuality=null; _selectedBurstCount=0; _rotation=0; _zoom=1;_dashboard.Clear();
            RenderSurface(); Localize(); UpdateControls(); return;
        }
        ScannerPixels? pixels; ScannerImageQuality? quality;
        lock(_sampleGate)
        {
            // Prefer a recent best candidate, but never use a stale previous burst.
            long now=Environment.TickCount64;
            if(now-_latestSampleAt>2000) { _status.Text=T("Нет свежего кадра камеры","Немає свіжого кадру камери","No recent camera frame"); return; }
            bool useBest=_bestPixels is not null && now-_burstStarted<=500;
            pixels=(useBest?_bestPixels:_latestPixels)?.Share(); quality=useBest?_bestQuality:_latestQuality;
        }
        if(pixels is null)return;
        _frozen=true; Interlocked.Increment(ref _freezeEpoch); ResetSamples(); Localize(); UpdateControls();
        int gen=_generation; _quality=_analyzedQuality=quality; _selectedBurstCount=0;
        try
        {
            // Display and deeply scan the same owned pixels; no encode/decode.
            if(!_active||gen!=_generation||!_frozen)return;
            var preview=CreatePreview(pixels.Bitmap);
            DropImage();_bitmap=preview;_displayPixels=pixels.Share();RenderSurface();
            _status.Text=T("Стоп-кадр: глубокое распознавание…","Стоп-кадр: глибоке розпізнавання…","Frozen frame: deep recognition…");
            using var result=await Task.Run(()=>DecodeBitmap(pixels.Bitmap,ScannerSensitivity.Maximum,false,0,Progress(gen,false),_searchRegion,pixels));
            if(!_active||gen!=_generation||!_frozen)return;
            InstallFrame(result);await AutoPresent(true);
        }
        finally{pixels.Dispose();}
    }
    private async Task ProbeHardware()
    {
        if(!_cameraLive||NetworkLive)return;
        if(_cameraIdentity is not { } path){_hardwareProbed=true;RenderHardware();return;}
        int gen=_generation;
        (ScannerCameraProperty?,ScannerCameraProperty?) result;
        try { result=await Task.Run(()=>(ScannerCameraControls.Read(path,ScannerCameraControls.Focus),ScannerCameraControls.Read(path,ScannerCameraControls.Exposure))); }
        catch { result=(null,null); }
        if(gen!=_generation||!_cameraLive)return;
        (_focusControl,_exposureControl)=result; _hardwareProbed=true; RenderHardware();
    }
    private void RenderHardware()
    {
        _hardwareRows.Children.Clear();
        string status=_hardwareError?T("Драйвер отклонил изменение. Проверьте параметры.","Драйвер відхилив зміну. Перевірте параметри.","Driver rejected the change. Check settings."):
            !_hardwareProbed?T("Возможности ещё не проверены","Можливості ще не перевірено","Capabilities not checked"):
            _focusControl is null&&_exposureControl is null?T("Управление недоступно через драйвер KS. Это не доказывает фиксированный фокус.","Керування недоступне через драйвер KS. Це не доводить фіксований фокус.","Controls unavailable via KS driver. This does not prove fixed focus."):
            T("Параметры драйвера; единицы и диапазоны зависят от камеры.","Параметри драйвера; одиниці й діапазони залежать від камери.","Driver values; units and ranges depend on the camera.");
        _controlStatus.Text=status;_controlStatus.IsVisible=_hardwareError;
        AddHardwareRow(T("Фокус","Фокус","Focus"),_focusControl,status);
        AddHardwareRow(T("Экспозиция","Експозиція","Exposure"),_exposureControl,status);
        _probeControls.VerticalAlignment=VerticalAlignment.Center;_probeControls.Margin=new Thickness(0,0,0,3);
        _hardwareRows.Children.Add(_probeControls);
    }
    private void AddHardwareRow(string title,ScannerCameraProperty? p,string hint)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=4,Margin=new Thickness(0,0,8,3),VerticalAlignment=VerticalAlignment.Center};
        row.Children.Add(new TextBlock{Text=title+":",VerticalAlignment=VerticalAlignment.Center});
        ToolTip.SetTip(row,hint);
        if(p is null){row.Children.Add(new TextBlock{Text=_hardwareProbed?T("Недоступно","Недоступно","Unavailable"):T("Не проверено","Не перевірено","Not checked"),VerticalAlignment=VerticalAlignment.Center});_hardwareRows.Children.Add(row);return;}
        var auto=new CheckBox{Content=T("Авто","Авто","Auto"),IsChecked=p.Auto,IsEnabled=(p.CanAuto&&p.CanManual)||(p.Auto&&p.CanManual)};
        if(p.CanAuto&&!p.CanManual&&!p.Auto)auto.IsEnabled=true;
        var number=new NumericUpDown{Minimum=p.HasRange?p.Min:p.Value,Maximum=p.HasRange?p.Max:p.Value,Increment=p.HasRange?p.Step:1,Value=p.Value,Width=86,IsEnabled=p.CanManual&&!p.Auto,FormatString="0"};
        var apply=new Button{IsEnabled=p.Auto?p.CanAuto:p.CanManual,VerticalAlignment=VerticalAlignment.Center};apply.Classes.Add("preview-tool");ToolIcon(apply,Icon.Checkmark,T("Применить","Застосувати","Apply")+": "+title);
        auto.VerticalAlignment=number.VerticalAlignment=VerticalAlignment.Center;
        auto.IsCheckedChanged+=(_,_)=>{number.IsEnabled=p.CanManual&&auto.IsChecked!=true; apply.IsEnabled=auto.IsChecked==true?p.CanAuto:p.CanManual;};
        apply.Click+=async (_,_)=>await Run(async ()=>
        {
            if(_cameraIdentity is not { } path)return; int gen=_generation;
            bool automatic=auto.IsChecked==true; int value=(int)(number.Value??p.Value);
            Interlocked.Increment(ref _freezeEpoch);
            bool ok; try { ok=await Task.Run(()=>ScannerCameraControls.Apply(path,p,automatic,value)); } catch { ok=false; }
            if(gen!=_generation)return; _hardwareError=!ok; ResetSamples(); DropImage();
            _quality=_analyzedQuality=null; _selectedBurstCount=0; _dashboard.Clear(); RenderSurface(); UpdateStatus(); await ProbeHardware();
        });
        row.Children.Add(auto);row.Children.Add(number);row.Children.Add(apply);_hardwareRows.Children.Add(row);
    }
    private async Task PickFile()
    {
        if (Owner is not { } owner) return;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = T("Открыть изображение или PDF", "Відкрити зображення або PDF", "Open image or PDF"), AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Images / PDF") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif", "*.pdf"] }, FilePickerFileTypes.All] });
        if (files.Count > 0 && _active) await LoadFile(files[0]);
    }
    private static async Task<byte[]> ReadBounded(Stream stream)
    {
        const int max = 100 * 1024 * 1024;
        if (stream.CanSeek && stream.Length > max) throw new FormatException();
        using var ms = new MemoryStream(); var block = new byte[65536];
        try
        {
            int n; while ((n = await stream.ReadAsync(block)) > 0) { if (ms.Length+n > max) throw new FormatException(); ms.Write(block,0,n); }
            return ms.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(block); CryptographicOperations.ZeroMemory(ms.GetBuffer()); }
    }
    private async Task LoadFile(IStorageFile file)
    {
        int gen=_generation;
        await using var stream=await file.OpenReadAsync();byte[] bytes=await ReadBounded(stream);
        ScannerPdf? pdf=null;
        try
        {
            if(gen!=_generation||!_active)return;
            bool isPdf=file.Name.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase)||bytes.AsSpan().StartsWith("%PDF-"u8);
            if(isPdf){pdf=await Task.Run(()=>new ScannerPdf(bytes));bytes=[];}
            using var frame=pdf is { } document?await Task.Run(()=>{using var image=document.Render(0,_scanSensitivity==ScannerSensitivity.Maximum);return DecodeBitmap(image,_scanSensitivity,false,0,Progress(gen,false));}):
                await Task.Run(()=>DecodeImage(bytes,_scanSensitivity,false,0,Progress(gen,false)));
            if(gen!=_generation||!_active)return;
            await ClearSource();if(!_active||_generation!=gen+1)return;
            _inputKind=ScannerInputKind.File;_staticSource=file.Name;_pdf=pdf;pdf=null;_page=0;InstallFrame(frame);await AutoPresent(false);
        }
        finally{pdf?.Dispose();CryptographicOperations.ZeroMemory(bytes);}
    }
    private async Task LoadPage(int index, bool preserveView = false)
    {
        if (_pdf is not { } pdf || index < 0 || index >= pdf.Count) return;
        int gen=_generation;var region=preserveView?_searchRegion:null;
        using var frame=await Task.Run(()=>{using var image=pdf.Render(index,_scanSensitivity==ScannerSensitivity.Maximum);return DecodeBitmap(image,_scanSensitivity,false,0,Progress(gen,false),region);});
        if(gen!=_generation||!_active)return;
        if(!preserveView){_rotation=0;_zoom=1;_searchRegion=_dragRegion=null;_regionStart=null;_selectingRegion=false;_dashboard.Clear();}
        _page = index; InstallFrame(frame); await AutoPresent(false);
    }
    private async Task Paste()
    {
        if(Owner?.Clipboard is not { } clipboard)return;
        int gen=_generation;using var image=await clipboard.TryGetBitmapAsync();
        if(gen!=_generation||!_active)return;
        if(image is not null)
        {
            using var ms=new MemoryStream();image.Save(ms,PngBitmapEncoderOptions.Default);byte[] bytes=ms.ToArray();
            try
            {
                using var frame=await Task.Run(()=>DecodeImage(bytes,_scanSensitivity,false,0,Progress(gen,false)));
                if(gen!=_generation||!_active)return;await ClearSource();if(!_active||_generation!=gen+1)return;
                _inputKind=ScannerInputKind.Clipboard;_staticSource=T("Из буфера обмена","З буфера обміну","From clipboard");InstallFrame(frame);await AutoPresent(false);
            }
            finally{CryptographicOperations.ZeroMemory(bytes);CryptographicOperations.ZeroMemory(ms.GetBuffer());}
            return;
        }
        string? text=await clipboard.TryGetTextAsync();
        if(gen!=_generation||!_active)return;
        if(string.IsNullOrEmpty(text)){_sourceError=T("В буфере нет изображения или текста.","У буфері немає зображення чи тексту.","Clipboard contains no image or text.");return;}
        if(text.Length>65536)throw new FormatException();
        ScannerPayload? payload=ScannerPayload.Parse(text);
        try
        {
            await ClearSource();if(!_active||_generation!=gen+1)return;
            _inputKind=ScannerInputKind.Clipboard;_staticSource=T("Из буфера обмена","З буфера обміну","From clipboard");_textSource=true;_hits=[new(payload,.1f,.35f,.8f,.3f)];payload=null;
            RenderSurface();UpdateStatus();UpdateControls();await AutoPresent(false);
        }
        finally{payload?.Dispose();}
    }
    private async Task Rescan()
    {
        if (_bitmap is null || (_cameraLive && !_frozen)) return;
        if (_pdf is not null) { await LoadPage(_page,true); return; }
        int gen = _generation;
        using var pixels=_displayPixels?.Share();if(pixels is null)return;
        using var frame=await Task.Run(()=>DecodeBitmap(pixels.Bitmap,_frozen?ScannerSensitivity.Maximum:_scanSensitivity,false,0,Progress(gen,false),_searchRegion,pixels));
        if(gen==_generation&&_active){InstallFrame(frame);await AutoPresent(_frozen);}
    }
    private sealed class Frame(ScannerPixels pixels,List<ScannerHit> hits,ScanDiagnostics diagnostics,ScannerImageQuality? quality) : IDisposable
    {
        internal ScannerPixels Pixels=pixels;
        internal List<ScannerHit> Hits=hits;
        internal ScanDiagnostics Diagnostics=diagnostics;
        internal ScannerImageQuality? Quality=quality;
        public void Dispose(){Pixels.Dispose();foreach(var h in Hits)h.Dispose();Hits.Clear();}
    }
    private static SKBitmap OpenBitmap(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new FormatException();
        if (codec.Info.Width < 1 || codec.Info.Height < 1 || (long)codec.Info.Width*codec.Info.Height > 32_000_000) throw new FormatException();
        return SKBitmap.Decode(codec) ?? throw new FormatException();
    }
    private static Frame DecodeImage(byte[] bytes,ScannerSensitivity mode,bool live,int sequence,Action<ScanDiagnostics> progress,ScannerSearchRegion? selection=null)
    {
        using var bitmap = OpenBitmap(bytes);
        return DecodeBitmap(bitmap,mode,live,sequence,progress,selection);
    }
    private static WriteableBitmap CreatePreview(SKBitmap source)
    {
        var preview=new WriteableBitmap(new PixelSize(source.Width,source.Height),new Vector(96,96),Avalonia.Platform.PixelFormat.Bgra8888,Avalonia.Platform.AlphaFormat.Premul);
        try{CopyPreview(source,preview);return preview;}
        catch{preview.Dispose();throw;}
    }
    private static unsafe void CopyPreview(SKBitmap source,WriteableBitmap target)
    {
        using var fb=target.Lock();
        if(source.ColorType==SKColorType.Bgra8888&&source.AlphaType==SKAlphaType.Opaque&&source.GetPixels()!=IntPtr.Zero)
        {
            int stride=checked(source.Width*4),rowBytes=source.RowBytes,height=source.Height;
            if(fb.RowBytes<stride||rowBytes<stride)throw new InvalidOperationException("Invalid framebuffer stride");
            byte* src=(byte*)source.GetPixels();byte* dst=(byte*)fb.Address;
            for(int y=0;y<height;y++)new ReadOnlySpan<byte>(src+y*rowBytes,stride).CopyTo(new Span<byte>(dst+y*fb.RowBytes,stride));
            GC.KeepAlive(source);return;
        }
        var info=new SKImageInfo(source.Width,source.Height,SKColorType.Bgra8888,SKAlphaType.Premul);
        using var pixmap=source.PeekPixels();
        if(pixmap is null||!pixmap.ReadPixels(info,fb.Address,fb.RowBytes,0,0))throw new InvalidOperationException("Cannot copy scanner pixels");
    }
    private static Frame DecodeBitmap(SKBitmap bitmap,ScannerSensitivity mode,bool live,int sequence,Action<ScanDiagnostics> progress,ScannerSearchRegion? selection=null,ScannerPixels? owner=null)
    {
        var hits = ScannerDecoder.Decode(bitmap,mode,live,sequence,out var diagnostics,progress,selection);
        try
        {
            ScannerImageQuality? quality=null;
            if(!live){var thumb=new byte[ScannerQualityMeter.SampleWidth*ScannerQualityMeter.SampleHeight];try{quality=ScannerQualityMeter.Measure(bitmap,thumb,[]);}finally{CryptographicOperations.ZeroMemory(thumb);}}
            return new Frame(owner?.Share()??new ScannerPixels(bitmap.Copy()??throw new InvalidOperationException("Cannot retain scanner pixels")),hits,diagnostics,quality);
        }
        catch { foreach (var h in hits) h.Dispose(); throw; }
    }
    private void InstallFrame(Frame frame)
    {
        var bitmap=CreatePreview(frame.Pixels.Bitmap);
        DropImage(); _bitmap=bitmap;_displayPixels=frame.Pixels.Share(); _diagnostics = frame.Diagnostics; _hits = frame.Hits; frame.Hits = []; _shownAnalyzed=true;
        if(frame.Quality is { } q && (!_cameraLive||_frozen))_dashboard.AddFrame(_frozen&&_analyzedQuality is { } frozenQuality?frozenQuality:q,_cameraLive,_frozen);
        _dashboard.AddScan(frame.Diagnostics,_hits.Count(h=>h.Payload.Allowed(_all.IsChecked==true)),_hits.Count(h=>h.Payload.Kind==ScannerKind.Invalid),_selectedBurstCount,_analyzedQuality);
        frame.Dispose(); RenderSurface(); UpdateStatus(); UpdateControls();
    }
    private async Task AutoPresent(bool live)
    {
        var valid = _hits.Where(h => h.Payload.Allowed(_all.IsChecked == true)).ToList();
        if (live)
        {
            var visible = valid.Select(h => h.Payload.Fingerprint).ToHashSet();
            foreach (var old in _lastSeen.Keys.ToArray())
            {
                if (visible.Contains(old)) _lastSeen[old] = 0;
                else if ((_hits.Count > 0 || _diagnostics is { Located: false, FinderPoints: 0 }) && ++_lastSeen[old] >= 3) { _lastSeen.Remove(old); _suppressed.Remove(old); }
            }
            foreach (var id in visible) _lastSeen[id] = 0;
            if (valid.Count > 1) { foreach (var id in visible) _suppressed.Add(id); return; }
            if (valid.Count == 1 && _suppressed.Contains(valid[0].Payload.Fingerprint)) return;
        }
        if (valid.Count == 1 && !_selectingRegion) await Present(valid[0], true);
    }
    private async Task Present(ScannerHit hit, bool automatic)
    {
        if (_modal || !_active || Owner is not { } owner || !hit.Payload.Allowed(_all.IsChecked == true)) return;
        _modal = true; UpdateControls();
        // Independent payload lifetime: changing/detaching the source cannot zero a live dialog's secret.
        using var payload = hit.Payload.Kind == ScannerKind.Binary ? ScannerPayload.Parse("", hit.Payload.Bytes.ToArray()) : ScannerPayload.Parse(hit.Payload.Text);
        if (_cameraLive) _suppressed.Add(hit.Payload.Fingerprint);
        try
        {
            if (automatic && _sound.IsChecked == true) ScannerNative.Beep();
            await ScannerResultDialog.ShowAsync(owner, payload, _settings);
        }
        catch { if (_active) _status.Text = T("Не удалось открыть результат", "Не вдалося відкрити результат", "Could not open result"); }
        finally { _modal = false; UpdateControls(); }
    }
    private Rect Rotate(ScannerHit h) => Rotate(new ScannerSearchRegion(h.X,h.Y,h.Width,h.Height));
    private Rect Rotate(ScannerSearchRegion h) => _rotation switch
    {
        1 => new Rect(1-h.Y-h.Height,h.X,h.Height,h.Width),
        2 => new Rect(1-h.X-h.Width,1-h.Y-h.Height,h.Width,h.Height),
        3 => new Rect(h.Y,1-h.X-h.Width,h.Height,h.Width),
        _ => new Rect(h.X,h.Y,h.Width,h.Height)
    };
    private void Outline(ScannerSearchRegion region,Color color,double width,double height)
    {
        var r=Rotate(region);var mark=new Border{Width=r.Width*width,Height=r.Height*height,BorderBrush=new SolidColorBrush(color),BorderThickness=new Thickness(2),IsHitTestVisible=false};
        Canvas.SetLeft(mark,r.X*width);Canvas.SetTop(mark,r.Y*height);_surface.Children.Add(mark);
    }
    private void RenderSurface()
    {
        _surface.Children.Clear(); _regions.Clear();
        _placeholder.IsVisible = _bitmap is null && !_textSource;
        double vw = Math.Max(1,_scroll.Bounds.Width-4), vh = Math.Max(1,_scroll.Bounds.Height-4);
        if (_bitmap is null && !_textSource) { _surface.Width = vw; _surface.Height = vh; return; }
        double width = vw, height = vh;
        if (_bitmap is not null)
        {
            double w = _bitmap.PixelSize.Width, h = _bitmap.PixelSize.Height;
            double rw = _rotation % 2 == 0 ? w : h, rh = _rotation % 2 == 0 ? h : w;
            double scale = Math.Min(vw/rw,vh/rh)*_zoom;
            width = rw*scale; height = rh*scale;
            var image = new Image { Source = _bitmap, Width = w*scale, Height = h*scale, Stretch = Stretch.Fill, IsHitTestVisible = false, RenderTransform = new RotateTransform(_rotation * 90), RenderTransformOrigin = RelativePoint.Center };
            Canvas.SetLeft(image, (width-w*scale)/2); Canvas.SetTop(image,(height-h*scale)/2); _surface.Children.Add(image);
        }
        _surface.Width = width; _surface.Height = height;
        if(_bitmap is not null)
        {
            if(_shownAnalyzed && _hits.Count==0 && _diagnostics?.Qr is { } candidate)Outline(candidate.Region,Color.Parse("#C58A24"),width,height);
            if((_dragRegion??_searchRegion) is { } search)Outline(search,Color.Parse("#528ACB"),width,height);
        }
        foreach (var hit in _hits)
        {
            Rect r = Rotate(hit); var rect = new Rect(r.X*width,r.Y*height,r.Width*width,r.Height*height); _regions.Add((rect,hit));
            var color = hit.Payload.Kind == ScannerKind.Invalid ? Color.Parse("#D49B48") : hit.Payload.Allowed(_all.IsChecked == true) ? Color.Parse("#369E69") : Color.Parse("#89929A");
            var brush = new SolidColorBrush(color);
            var frame = new Border { Width = rect.Width, Height = rect.Height, BorderThickness = new Thickness(2), BorderBrush = brush, CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
            Canvas.SetLeft(frame,rect.X); Canvas.SetTop(frame,rect.Y); _surface.Children.Add(frame);
            var label = new Border { Background = brush, Padding = new Thickness(5,2), IsHitTestVisible = false, Child = new TextBlock { Text = hit.Payload.Label, Foreground = Brushes.White, FontSize = 12 } };
            Canvas.SetLeft(label,rect.X); Canvas.SetTop(label,Math.Max(0,rect.Y-20)); _surface.Children.Add(label);
        }
    }
}
