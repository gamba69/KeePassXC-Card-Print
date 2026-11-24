using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Rendering;

namespace KeePassXCCardPrint;

public partial class MainWindow
{
    private void OnTemplateFieldFocused(object? sender, FocusChangedEventArgs e)
    {
        if (sender is Control { DataContext: AppearanceField field })
        {
            var group = TemplateEditorLayout.Groups.FirstOrDefault(g=>g.Keys.Contains(field.Key));
            if (group is not null) _vm.HighlightTemplateGroup(group.Id);
        }
    }

    private PreviewHitRegion? _templatePreviewPress;
    private SkiaPreviewScene? _templatePreviewPressedScene;
    private Point _templatePreviewPressPoint;
    private Point? _templatePreviewPointer;
    private bool _templatePreviewGesture;
    private bool _templatePreviewDragged;
    private bool _templatePreviewControlHeld;

    private void InitializeTemplatePreviewInteraction()
    {
        AddHandler(KeyDownEvent, OnTemplatePreviewKeyDown, RoutingStrategies.Tunnel, handledEventsToo:true);
        AddHandler(KeyUpEvent, OnTemplatePreviewKeyUp, RoutingStrategies.Tunnel, handledEventsToo:true);
        if(this.FindControl<Canvas>("PreviewViewport") is { } viewport)
            viewport.AddHandler(PointerPressedEvent, OnTemplatePreviewViewportPressed, RoutingStrategies.Tunnel, handledEventsToo:true);
        Deactivated+=(_,_)=>ClearTemplatePreviewHover();
    }
    private void OnTemplatePreviewViewportPressed(object? sender,PointerPressedEventArgs e)
    {
        // Includes the empty canvas around the card, not just the page host.
        if(sender is Canvas viewport && e.GetCurrentPoint(viewport).Properties.IsLeftButtonPressed &&
            (e.KeyModifiers & KeyModifiers.Control)==0)
        {
            _vm.SetTemplatePreviewHover(null);
            _vm.ClearTemplatePreviewSelection();
        }
    }
    private void ClearTemplatePreviewHover()
    {
        _templatePreviewControlHeld=false;
        _vm.SetTemplatePreviewHover(null);
    }
    private void OnTemplatePreviewKeyDown(object? sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape)
        {
            ClearTemplatePreviewHover();
            _vm.ClearTemplatePreviewSelection();
            return;
        }
        if(e.Key is not (Key.LeftCtrl or Key.RightCtrl))return;
        _templatePreviewControlHeld=true;
        if(!_previewPanning && _templatePreviewPointer is Point pointer && this.FindControl<Grid>("PreviewPageHost") is { } page)
            _vm.SetTemplatePreviewHover(PreviewElementAt(pointer,page));
    }
    private void OnTemplatePreviewKeyUp(object? sender,KeyEventArgs e)
    {
        if(e.Key is Key.LeftCtrl or Key.RightCtrl)ClearTemplatePreviewHover();
    }
    private PreviewHitRegion? PreviewElementAt(Point point, Grid page)
    {
        if (_vm.PreviewUpdating || page.Bounds.Width <= 0 || page.Bounds.Height <= 0 ||
            point.X<0 || point.Y<0 || point.X>page.Bounds.Width || point.Y>page.Bounds.Height) return null;
        var scene = _vm.PreviewScene;
        if (scene is null || (_vm.PreviewPreviousScene is not null && _vm.PreviewOldOpacity > .1)) return null;
        return scene.HitTest((float)(point.X/page.Bounds.Width*scene.Width), (float)(point.Y/page.Bounds.Height*scene.Height));
    }
    private void UpdateTemplatePreviewPointer(Grid page, PointerEventArgs e)
    {
        _templatePreviewPointer=e.GetPosition(page);
        _templatePreviewControlHeld=(e.KeyModifiers & KeyModifiers.Control)!=0;
        _vm.SetTemplatePreviewHover(_templatePreviewControlHeld && !_previewPanning
            ? PreviewElementAt(_templatePreviewPointer.Value,page) : null);
    }
    private void OnTemplatePreviewPointerEntered(object? sender,PointerEventArgs e)
    {
        if(sender is Grid page)UpdateTemplatePreviewPointer(page,e);
    }
    private void OnTemplatePreviewPointerExited(object? sender,PointerEventArgs e)
    {
        _templatePreviewPointer=null;
        _vm.SetTemplatePreviewHover(null);
    }
    private void OnTemplatePreviewPointerPressed(object? sender,PointerPressedEventArgs e)
    {
        if(sender is Grid page && e.GetCurrentPoint(page).Properties.IsLeftButtonPressed &&
           (e.KeyModifiers & KeyModifiers.Control)!=0)
        {
            if(this.FindControl<Canvas>("PreviewViewport") is not { } viewport)return;
            StopPreviewZoomAtCurrentFrame();
            UpdateTemplatePreviewPointer(page,e);
            _templatePreviewGesture=true;
            _templatePreviewDragged=false;
            _templatePreviewPress=PreviewElementAt(e.GetPosition(page),page);
            _templatePreviewPressedScene=_vm.PreviewScene;
            _templatePreviewPressPoint=e.GetPosition(viewport);
            e.Pointer.Capture(page); e.Handled=true; return;
        }
        _vm.SetTemplatePreviewHover(null);
        if(sender is Grid normalPage && e.GetCurrentPoint(normalPage).Properties.IsLeftButtonPressed)
            _vm.ClearTemplatePreviewSelection();
        OnPreviewPanPressed(sender,e);
    }
    private void OnTemplatePreviewPointerMoved(object? sender,PointerEventArgs e)
    {
        if(sender is not Grid page)return;
        if(_previewPanning)
        {
            OnPreviewPanMoved(sender,e);
            _templatePreviewPointer=e.GetPosition(page);
            return;
        }
        if(_templatePreviewGesture && this.FindControl<Canvas>("PreviewViewport") is { } viewport)
        {
            var delta=e.GetPosition(viewport)-_templatePreviewPressPoint;
            if(Math.Abs(delta.X)>=4 || Math.Abs(delta.Y)>=4)_templatePreviewDragged=true;
        }
        UpdateTemplatePreviewPointer(page,e);
        if(_templatePreviewGesture)e.Handled=true;
    }
    private void OnTemplatePreviewPointerReleased(object? sender,PointerReleasedEventArgs e)
    {
        if(!_templatePreviewGesture) { OnPreviewPanReleased(sender,e); return; }
        bool sameScene=ReferenceEquals(_templatePreviewPressedScene,_vm.PreviewScene);
        var pressed=sameScene?_templatePreviewPress:null;
        _templatePreviewGesture=false;_templatePreviewPress=null;_templatePreviewPressedScene=null;
        if(sender is Grid page && this.FindControl<Canvas>("PreviewViewport") is { } viewport)
        {
            var delta=e.GetPosition(viewport)-_templatePreviewPressPoint;
            var hit=PreviewElementAt(e.GetPosition(page),page);
            bool ctrl=(e.KeyModifiers & KeyModifiers.Control)!=0;
            if(PreviewHitRegion.CanCommit(pressed,hit,ctrl,_templatePreviewDragged,delta.X,delta.Y))
            {
                if(Equals(_vm.TemplateSelectedRegion,hit))
                    _vm.ClearTemplatePreviewSelection();
                else
                    SelectPreviewElement(hit!);
            }
            else if(sameScene && ctrl && pressed is null && hit is null && !_templatePreviewDragged &&
                    Math.Abs(delta.X)<4 && Math.Abs(delta.Y)<4)
                _vm.ClearTemplatePreviewSelection();
            UpdateTemplatePreviewPointer(page,e);
        }
        e.Pointer.Capture(null);e.Handled=true;
    }
    private void OnTemplatePreviewPointerCaptureLost(object? sender,PointerCaptureLostEventArgs e)
    {
        _templatePreviewGesture=false;_templatePreviewPress=null;_templatePreviewPressedScene=null;
        _vm.SetTemplatePreviewHover(null);
        OnPreviewPanCaptureLost(sender,e);
    }
    private void SelectPreviewElement(PreviewHitRegion hit)
    {
        _vm.NavigateTemplateEditor(hit.Section);
        _vm.FixTemplatePreviewRegion(hit);
        Dispatcher.UIThread.Post(() =>
        {
            var scroll = this.FindControl<ScrollViewer>("TemplateAppearanceScroll");
            var group = scroll?.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b=>b.DataContext is TemplateEditorGroup g && g.Id==hit.Group && b.IsVisible);
            group?.BringIntoView();
        },DispatcherPriority.Loaded);
    }

    // The color-source selector and RGB/HEX editor share a single dialog.
    // No model update occurs until the user confirms the source or literal.
    private async void OnTemplateColorPick(object? sender,RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: AppearanceField field } || !field.IsEditable || !field.IsColor) return;
        string L(string ru,string uk,string en) => TemplateEditorLayout.L(_settings.Language,ru,uk,en);
        var dialog = new Window
        {
            Title=field.Label, Width=340, SizeToContent=SizeToContent.Height, CanResize=false,
            WindowStartupLocation=WindowStartupLocation.CenterOwner, ShowInTaskbar=false,
            RequestedThemeVariant=ActualThemeVariant
        };
        var panel=new StackPanel { Spacing=8, Margin=new Thickness(12) };
        dialog.Content=panel;
        var swatch=new Border { Height=44, CornerRadius=new CornerRadius(4) };
        var hex=new TextBox { Text=field.EffectiveColorHex, MaxLength=7 };
        var sourceCodes=new List<string>();
        ComboBox? sourcePicker=null;
        const string custom="custom";
        if(!CardPalette.IsRole(field.Key) && !field.IconCustomTintOnly)
        {
            if(field.Key is not ("header_group_color" or "footer_uuid_color" or "footer_date_color" or "back_art_icon_color"))
                sourceCodes.Add(CardPalette.DefaultSource);
            sourceCodes.AddRange(CardPalette.RoleKeys.Select(role=>CardPalette.Prefix+role));
            sourceCodes.Add(custom);
            var sourceCaptions=sourceCodes.Select(code=>code==custom
                ? L("Индивидуальный цвет","Індивідуальний колір","Individual color")
                : code==CardPalette.DefaultSource ? L("По умолчанию","За замовчуванням","Default")
                : CardPalette.RoleLabel(code[CardPalette.Prefix.Length..],_settings.Language)+" · "+field.ColorHexForSource(code)).ToArray();
            int index=sourceCodes.IndexOf(field.TextValue);
            sourcePicker=new ComboBox { ItemsSource=sourceCaptions, SelectedIndex=index>=0?index:sourceCodes.Count-1,
                HorizontalAlignment=HorizontalAlignment.Stretch };
            panel.Children.Add(new TextBlock { Text=L("Источник цвета","Джерело кольору","Color source") });
            panel.Children.Add(sourcePicker);
        }
        string SelectedSource() => sourcePicker is null || sourcePicker.SelectedIndex<0 || sourcePicker.SelectedIndex>=sourceCodes.Count
            ? custom : sourceCodes[sourcePicker.SelectedIndex];
        panel.Children.Add(swatch);
        panel.Children.Add(hex);
        var sliders=new Slider[3];
        var captions=new[]{"R","G","B"};
        bool syncing=false;
        for(int i=0;i<3;i++)
        {
            var row=new Grid { ColumnDefinitions=new ColumnDefinitions("24,*"), ColumnSpacing=6 };
            row.Children.Add(new TextBlock { Text=captions[i], VerticalAlignment=VerticalAlignment.Center });
            var slider=new Slider { Minimum=0, Maximum=255, TickFrequency=1, IsSnapToTickEnabled=true };
            sliders[i]=slider;
            Grid.SetColumn(slider,1); row.Children.Add(slider); panel.Children.Add(row);
        }
        var palette=new WrapPanel { Orientation=Orientation.Horizontal };
        foreach(var value in new[]{"#FFFFFF","#F4F7FA","#CCD5E0","#53616E","#1A2B40","#36618D","#000000","#8A3131","#32674D","#D9B44A"})
        {
            var b=new Button { Width=27,Height=27,Padding=new Thickness(2),Margin=new Thickness(2),
                Content=new Border { Width=19,Height=19,Background=new SolidColorBrush(Color.Parse(value)),CornerRadius=new CornerRadius(2) } };
            ToolTip.SetTip(b,value); b.Click+=(_,_)=>hex.Text=value; palette.Children.Add(b);
        }
        panel.Children.Add(palette);
        var actions=new StackPanel { Orientation=Orientation.Horizontal,Spacing=6,HorizontalAlignment=HorizontalAlignment.Right };
        Button Action(string text,string icon)
        {
            var content=new StackPanel { Orientation=Orientation.Horizontal,Spacing=5 };
            content.Children.Add(ActionIconVectors.Create(icon));
            content.Children.Add(new TextBlock { Text=text,VerticalAlignment=VerticalAlignment.Center });
            return new Button { Content=content };
        }
        var ok=Action(L("Выбрать","Вибрати","Choose"),"check");
        var cancel=Action(L("Отмена","Скасувати","Cancel"),"cancel");
        actions.Children.Add(cancel); actions.Children.Add(ok); panel.Children.Add(actions);
        bool TryHex(out Color color)
        {
            var text=hex.Text ?? "";
            color=default;
            return text.Length==7 && text.StartsWith('#') && Color.TryParse(text,out color);
        }
        string customHex=field.EffectiveColorHex;
        string previousSource=SelectedSource();
        void ReadHex()
        {
            if(syncing)return;
            ok.IsEnabled=TryHex(out var color);
            if(!ok.IsEnabled)return;
            if(SelectedSource()==custom)customHex=hex.Text!;
            syncing=true;
            sliders[0].Value=color.R; sliders[1].Value=color.G; sliders[2].Value=color.B;
            swatch.Background=new SolidColorBrush(color);
            syncing=false;
        }
        hex.TextChanged+=(_,_)=>ReadHex();
        foreach(var slider in sliders) slider.ValueChanged+=(_,_)=>
        {
            if(syncing)return;
            hex.Text=$"#{(int)sliders[0].Value:X2}{(int)sliders[1].Value:X2}{(int)sliders[2].Value:X2}";
        };
        void UpdateSource()
        {
            string source=SelectedSource();
            if(previousSource==custom && TryHex(out _))customHex=hex.Text!;
            previousSource=source;
            bool individual=source==custom;
            hex.IsEnabled=individual;
            palette.IsEnabled=individual;
            foreach(var slider in sliders)slider.IsEnabled=individual;
            hex.Text=individual?customHex:field.ColorHexForSource(source);
            ReadHex();
        }
        if(sourcePicker is not null)sourcePicker.SelectionChanged+=(_,_)=>UpdateSource();
        ok.Click+=(_,_)=>
        {
            string source=SelectedSource();
            if(source!=custom)dialog.Close(source);
            else if(TryHex(out _))dialog.Close(hex.Text);
        };
        cancel.Click+=(_,_)=>dialog.Close(null);
        UpdateSource();
        try
        {
            var result=await dialog.ShowDialog<string?>(this);
            if(result is not null && _vm.CanEditAppearance && _vm.AppearanceFields.Contains(field) && field.IsEditable)
                field.SetColorSource(result);
        }
        catch(Exception ex) { CloudSyncDiagnostics.Record("template-color-dialog",ex); }
    }

    private async void OnTemplatePalettePick(object? sender,RoutedEventArgs e)
    {
        if(sender is not Control { DataContext: TemplateEditorGroup group } || !group.IsPalette || !_vm.CanEditAppearance) return;
        string L(string ru,string uk,string en) => TemplateEditorLayout.L(_settings.Language,ru,uk,en);
        var fields=group.Fields.ToDictionary(f=>f.Key,StringComparer.Ordinal);
        var dialog=new Window
        {
            Title=L("Цветовая схема","Колірна схема","Color scheme"), Width=360, SizeToContent=SizeToContent.Height,
            CanResize=false, ShowInTaskbar=false, WindowStartupLocation=WindowStartupLocation.CenterOwner,
            RequestedThemeVariant=ActualThemeVariant
        };
        var panel=new StackPanel { Margin=new Thickness(12), Spacing=7 };
        dialog.Content=panel;
        panel.Children.Add(new TextBlock { Text=L("Меняются семь общих цветов и общая окраска иконок. Индивидуальные настройки сохраняются.",
            "Змінюються сім спільних кольорів і спільне забарвлення іконок. Індивідуальні налаштування зберігаються.",
            "The seven shared colors and shared icon coloring change. Individual settings are preserved."), TextWrapping=TextWrapping.Wrap });
        for(int i=0;i<CardPalette.Schemes.Length;i++)
        {
            int choice=i;
            var scheme=CardPalette.Schemes[i];
            var row=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto"), ColumnSpacing=8 };
            row.Children.Add(new TextBlock { Text=L(scheme.Ru,scheme.Uk,scheme.En), VerticalAlignment=VerticalAlignment.Center });
            var colors=new StackPanel { Orientation=Orientation.Horizontal, Spacing=3, VerticalAlignment=VerticalAlignment.Center };
            foreach(var color in scheme.Colors)
                colors.Children.Add(new Border { Width=18,Height=18,CornerRadius=new CornerRadius(2),
                    Background=new SolidColorBrush(Color.Parse(color)), BorderBrush=Brushes.Gray, BorderThickness=new Thickness(1) });
            Grid.SetColumn(colors,1);row.Children.Add(colors);
            var button=new Button { Content=row, HorizontalAlignment=HorizontalAlignment.Stretch,
                HorizontalContentAlignment=HorizontalAlignment.Stretch };
            button.Click+=(_,_)=>dialog.Close(choice); panel.Children.Add(button);
        }
        var cancelContent=new StackPanel { Orientation=Orientation.Horizontal,Spacing=5 };
        cancelContent.Children.Add(ActionIconVectors.Create("cancel"));
        cancelContent.Children.Add(new TextBlock { Text=L("Отмена","Скасувати","Cancel"),VerticalAlignment=VerticalAlignment.Center });
        var cancel=new Button { Content=cancelContent,HorizontalAlignment=HorizontalAlignment.Right };
        cancel.Click+=(_,_)=>dialog.Close(-1);panel.Children.Add(cancel);
        try
        {
            int? result=await dialog.ShowDialog<int?>(this);
            if(result is not int selected || selected<0 || selected>=CardPalette.Schemes.Length || !_vm.CanEditAppearance) return;
            if(CardPalette.RoleKeys.Any(key=>!fields.TryGetValue(key,out var field) || !field.IsEditable || !_vm.AppearanceFields.Contains(field)))return;
            if(!fields.TryGetValue("palette_icon_color_mode",out var iconColorMode) || !iconColorMode.IsEditable || !_vm.AppearanceFields.Contains(iconColorMode))return;
            var scheme=CardPalette.Schemes[selected];
            var iconChoice=iconColorMode.Choices.FirstOrDefault(c=>c.Code==scheme.IconColorMode);
            if(iconChoice is null)return;
            for(int i=0;i<CardPalette.RoleKeys.Length;i++)
                fields[CardPalette.RoleKeys[i]].SetColorSource(scheme.Colors[i]);
            iconColorMode.SelectedChoice=iconChoice;
        }
        catch(Exception ex) { CloudSyncDiagnostics.Record("template-palette-dialog",ex); }
    }
}
