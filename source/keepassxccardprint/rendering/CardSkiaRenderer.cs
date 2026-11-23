using KeePassXCCardPrint.Platforms;
using System.Globalization;
using System.Text;
using SkiaSharp;
using QRCoder;
using Avalonia.Platform;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

/// <summary>One drawing implementation for card preview, high-resolution raster output and
/// vector PDF sheets. Each render owns its Skia canvas; no decoded secrets are written to disk
/// except by an explicit user export. Work in canonical 7.5 px/mm design coordinates.</summary>
public static class CardSkiaRenderer
{
    internal const float PixelsPerMillimetre = 7.5f;
    private static float Px(double mm)=>(float)mm*PixelsPerMillimetre;
    private static float Pt(double pt)=>Px(pt*CardLayoutEngine.MmPerPoint);
    private static SKColor Color(string? input,SKColor fallback)=>
        !string.IsNullOrEmpty(input) && SKColor.TryParse(input,out var c)?c:fallback;
    private static SKColor Styled(string? original,string originalDefault,string styled) =>
        string.Equals(original,originalDefault,StringComparison.OrdinalIgnoreCase)?Color(styled,SKColors.Black):Color(original,SKColors.Black);
    private static SKPaint Paint(SKColor color,bool outline=false,float width=1) =>new()
    {Color=color,IsAntialias=true,Style=outline?SKPaintStyle.Stroke:SKPaintStyle.Fill,StrokeWidth=width};

    public static byte[] Render(PhysicalCard card,CardAppearance a,bool back,float dpi=190.5f)
    {
        if (!float.IsFinite(dpi) || dpi < 48 || dpi > 1200)
            throw new ArgumentOutOfRangeException(nameof(dpi));
        int w=checked((int)Math.Round(a.WidthMm*dpi/25.4d));
        int h=checked((int)Math.Round(a.HeightMm*dpi/25.4d));
        if (w < 50 || h < 50 || (long)w*h > 65_000_000)
            throw new InvalidOperationException("Растровый документ превышает безопасный размер.");
        using var surface=SKSurface.Create(new SKImageInfo(w,h,SKColorType.Rgba8888,SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Не удалось выделить буфер изображения.");
        surface.Canvas.Scale(dpi/25.4f/PixelsPerMillimetre);
        Draw(surface.Canvas,card,a,back);
        using var image=surface.Snapshot();
        using var data=image.Encode(SKEncodedImageFormat.Png,100)
            ?? throw new InvalidOperationException("Не удалось кодировать PNG.");
        return data.ToArray();
    }

    /// <summary>Record the exact print drawing commands once on a worker.
    /// Playback into Avalonia's Skia canvas is resolution independent.</summary>
    internal static SkiaPreviewScene Record(PhysicalCard card,CardAppearance appearance,bool back)
    {
        float width=(float)(appearance.WidthMm*PixelsPerMillimetre);
        float height=(float)(appearance.HeightMm*PixelsPerMillimetre);
        using var recorder=new SKPictureRecorder();
        var canvas=recorder.BeginRecording(new SKRect(0,0,width,height));
        Draw(canvas,card,appearance,back);
        return new SkiaPreviewScene(recorder.EndRecording(),width,height);
    }

    /// <summary>Draw into an existing canvas in canonical units. The caller owns clipping,
    /// page placement, the destination resolution and canvas lifetime.</summary>
    internal static void Draw(SKCanvas c,PhysicalCard card,CardAppearance a,bool back)
        => Draw(new SkiaCardCanvas(c),card,a,back);

    internal static void Draw(ICardCanvas c,PhysicalCard card,CardAppearance a,bool back)
    {
        int w=(int)Math.Round(Px(a.WidthMm)),h=(int)Math.Round(Px(a.HeightMm));
        if (w < 100 || h < 100) throw new InvalidOperationException("Недопустимый размер карточки.");
        PortableFonts.RequireSecretFont(a.SecretFont);
        if(!back)
        {
            CardBackgroundRenderer.Draw(c,w,h,a,CardBackgroundRenderer.Resolve(a,"front"));
            DrawFront(c,w,h,a,card.Front);
        }
        else if(card.Continuation is not null)
        {
            CardBackgroundRenderer.Draw(c,w,h,a,CardBackgroundRenderer.Resolve(a,"back"));
            DrawFront(c,w,h,a,card.Continuation);
        }
        else if(a.DuplexBackFill!="blank")
        {
            CardBackgroundRenderer.Draw(c,w,h,a,CardBackgroundRenderer.Resolve(a,"empty"));
            DrawBack(c,w,h,a,card.Front);
        }
        else
        {
            using var paper=Paint(SKColors.White);
            c.DrawRect(0,0,w,h,paper);
        }
    }

    private static float Width(string value,float pixels,string family,string weight="400",float track=0) =>
        CardTextMetrics.MeasureMm(value,pixels/Pt(1),family,weight,track/Pt(1))*PixelsPerMillimetre;

    private static float DrawText(ICardCanvas c,string text,float left,float baseline,float pixels,
        SKColor ink,string family,string weight="400",float tracking=0,bool markZero=false) =>
        ShapedTextEngine.Draw(c,text,left,baseline,pixels,ink,family,weight,
            Pt(tracking),markZero);

    private static void Fit(ICardCanvas c,string text,float x,float baseline,float maxWidth,float fontPx,
        float minPercent,SKColor color,string family,string weight="400",float trackingPt=0,bool markZero=false)
    {
        if(string.IsNullOrEmpty(text))return;
        float width=CardTextMetrics.MeasureMm(text,fontPx/Pt(1),family,weight,trackingPt)*PixelsPerMillimetre;
        if(width>maxWidth+.5f)
        {
            float scale=maxWidth/width;
            if(scale<minPercent/100f-.005f)
                throw new InvalidOperationException("Текст не помещается при минимально допустимом размере.");
            fontPx*=scale;
            trackingPt*=scale;
        }
        DrawText(c,text,x,baseline,fontPx,color,family,weight,trackingPt,markZero);
    }
    private static void Frame(ICardCanvas c,int w,int h,CardAppearance a)
    {
        if(!a.Frame)return;
        float stroke=Math.Max(.55f,Pt(a.FrameWidthPt));
        using var pen=Paint(Color(a.BorderColor,new SKColor(193,200,208)),true,stroke);
        c.DrawRoundRect(new SKRect(stroke,stroke,w-stroke,h-stroke),Px(a.CornerRadiusMm),Px(a.CornerRadiusMm),pen);
    }
    private static float CenteredBaseline(string text,float centerY,float sizePx,string family,string weight)
    {
        using var font=new SKFont(CardTextMetrics.Typeface(family,weight),Math.Max(2,sizePx));
        using var fallback=new SKFont(CardTextMetrics.Typeface(PlatformServices.Current.FallbackTextFont,weight),Math.Max(2,sizePx));
        float top=float.PositiveInfinity,bottom=float.NegativeInfinity;
        foreach(var rune in text.EnumerateRunes())
        {
            var glyph=rune.ToString();
            var chosen=font.ContainsGlyphs(glyph)?font:fallback;
            chosen.MeasureText(glyph,out SKRect inkBounds);
            if(inkBounds.Bottom<=inkBounds.Top)continue;
            top=Math.Min(top,inkBounds.Top);
            bottom=Math.Max(bottom,inkBounds.Bottom);
        }
        return bottom>top ? centerY-(top+bottom)*.5f : centerY+sizePx*.34f;
    }

    private static void DrawHeaderText(ICardCanvas c,CardHeaderLayout.TextBlock block,
        string alignment,SKColor color)
    {
        float sizePx=Pt(block.SizePt);
        float areaWidth=Px(block.Width);
        foreach(var (line,index) in block.Lines.Select((line,index)=>(line,index)))
        {
            float lineWidth=Width(line,sizePx,block.Font,block.Weight);
            float alignOffset=alignment switch
            {
                "center"=>(areaWidth-lineWidth)*.5f,
                "right"=>areaWidth-lineWidth,
                _=>0f
            };
            float centerY=Px(block.Y+(index+.5f)*block.LineHeight);
            float baseline=CenteredBaseline(line,centerY,sizePx,block.Font,block.Weight);
            Fit(c,line,Px(block.X)+Math.Max(0,alignOffset),baseline,areaWidth,
                sizePx,99,color,block.Font,block.Weight);
        }
    }

    private static void DrawHeader(ICardCanvas c,int w,int h,CardAppearance a,CardPart part,
        SKColor ink,SKColor muted,SKColor accent,SKColor rule)
    {
        var plan=CardHeaderLayout.Measure(a,part.Record);
        float radius=Px(Math.Clamp(a.CornerRadiusMm,0,6));
        // Header paint stops at the actual header boundary; the spacing below it
        // is transparent and only moves the start of the card's contents.
        float decorBottom=Px(plan.DecorationBottom);
        var decor=new SKRect(Px(.8),Px(.9),w-Px(.8),decorBottom);
        bool fill=a.HeaderFill && a.Style!="minimal";
        if(fill)
        {
            string defaultFill=a.Style switch
            {"classic"=>"#EFF1F4","table"=>"#EAF0F5",_=>"#F4F7FA"};
            using var paper=Paint(Styled(a.HeaderColor,"#F4F7FA",defaultFill));
            c.DrawRoundRect(decor,radius,radius,paper);
        }
        if(a.HeaderAccent)
        {
            float stripeX=Px(.9);
            float stripeWidth=Px(Math.Clamp(a.HeaderAccentWidthMm,.2,3));
            float stripeTop=Px(.9);
            float stripeBottom=decor.Bottom;
            using var paint=Paint(accent);
            if(fill && radius>.01f)
            {
                using var clip=new SKRoundRect(decor,radius,radius);
                c.Save();
                c.ClipRoundRect(clip,SKClipOperation.Intersect,true);
                c.DrawRect(stripeX,stripeTop,stripeWidth,stripeBottom-stripeTop,paint);
                c.Restore();
            }
            else
            {
                float smallRadius=Math.Min(radius,stripeWidth*.5f);
                c.DrawRoundRect(new SKRect(stripeX,stripeTop,stripeX+stripeWidth,stripeBottom),
                    smallRadius,smallRadius,paint);
            }
        }
        else if(a.SectionRules)
        {
            using var pen=Paint(rule,true,.8f);
            c.DrawLine(Px(a.PaddingMm),decor.Bottom,w-Px(a.PaddingMm),decor.Bottom,pen);
        }

        // Icon, title and the 01/01 badge share one vertical centre.
        if(plan.IconSize>0)
        {
            float iconLeft=Px(plan.IconX),iconTop=Px(plan.IconY),iconSize=Px(plan.IconSize);
            try
            {
                SKBitmap? icon=null;
                if(part.Record.CustomIconPng is {Length:>0} png)icon=SKBitmap.Decode(png);
                else if(a.EntryIconMode=="all")
                {
                    int id=Math.Clamp(part.Record.IconId,0,68);
                    using var stream=AssetLoader.Open(new Uri($"avares://KeePassXCCardPrint/assets/icons/standard-{id:00}.png"));
                    icon=SKBitmap.Decode(stream);
                }
                using(icon)
                {
                    if(icon is not null)
                    {
                        if(a.EntryIconPrintMode is "auto" or "backdrop")
                        {
                            using var iconFill=Paint(Color(a.HeaderColor,SKColors.White));
                            c.DrawRoundRect(new SKRect(iconLeft,iconTop,iconLeft+iconSize,iconTop+iconSize),
                                Px(.8),Px(.8),iconFill);
                        }
                        using var styledIcon=TransformIcon(icon,a.EntryIconPrintMode);
                        c.DrawBitmap(styledIcon,new SKRect(iconLeft,iconTop,iconLeft+iconSize,iconTop+iconSize),
                            new SKSamplingOptions(SKCubicResampler.Mitchell));
                    }
                }
            }
            catch(Exception error) when(c is not IDirectVectorCardCanvas &&
                (error is ArgumentException or IOException or InvalidOperationException))
            { /* Preview tolerates missing icons; XPS must not silently lose icons. */ }
        }

        DrawHeaderText(c,plan.Title,a.TitleAlign,ink);
        if(plan.Group is not null)
            DrawHeaderText(c,plan.Group,a.HeaderGroupAlign,Color(a.HeaderGroupColor,muted));

        // Two-digit part/total badge, including 01/01.
        string chip=$"{part.Number:D2}/{part.Total:D2}";
        float chipPx=Pt(Math.Clamp(a.FooterSizePt,2,15));
        float chipWidth=Width(chip,chipPx,a.LabelFont,a.LabelWeight);
        float chipLeft=Px(plan.BadgeX)+Math.Max(0,(Px(plan.BadgeWidth)-chipWidth)*.5f);
        float chipCenter=Px(plan.RowY+plan.RowHeight*.5f);
        float chipBaseline=CenteredBaseline(chip,chipCenter,chipPx,a.LabelFont,a.LabelWeight);
        if(a.Style=="archive" && fill)
        {
            using var tint=Paint(new SKColor(229,235,242));
            c.DrawRoundRect(new SKRect(chipLeft-Px(.5),chipCenter-chipPx*.70f,
                chipLeft+chipWidth+Px(.5),chipCenter+chipPx*.70f),Px(.45),Px(.45),tint);
        }
        DrawText(c,chip,chipLeft,chipBaseline,chipPx,muted,a.LabelFont,a.LabelWeight);
    }

    private static void DrawFooter(ICardCanvas c,int w,int h,CardAppearance a,CardPart part)
    {
        float left=Px(a.PaddingMm),right=w-left;
        float footerTop=h-Px(a.FooterBottomInsetMm+a.FooterHeightMm);
        float footerBottom=h-Px(a.FooterBottomInsetMm);
        if(footerBottom<=footerTop)return;
        if(a.SectionRules)
        {
            using var pen=Paint(Color(a.RuleColor,new SKColor(204,213,224)),true,.8f);
            c.DrawLine(left,footerTop-Px(.35),right,footerTop-Px(.35),pen);
        }

        float centerY=(footerTop+footerBottom)*.5f;
        float maxFontPx=(footerBottom-footerTop)*.83f;
        float dateLeft=right;
        if(a.ShowDate)
        {
            string format=a.FooterDateFormat switch
            {"dd.MM.yyyy"=>"dd.MM.yyyy","yyyy/MM/dd"=>"yyyy/MM/dd",_=>"yyyy-MM-dd"};
            string date=DateTime.Now.ToString(format,CultureInfo.InvariantCulture);
            float font=Math.Min(Pt(Math.Clamp(a.FooterDateSizePt,2,18)),maxFontPx);
            float dateWidth=Width(date,font,a.LabelFont,a.FooterDateWeight);
            if(dateWidth>right-left)
                throw new InvalidOperationException("Дата не помещается в подвале карточки.");
            dateLeft=right-dateWidth;
            float baseline=CenteredBaseline(date,centerY,font,a.LabelFont,a.FooterDateWeight);
            DrawText(c,date,dateLeft,baseline,font,Color(a.FooterDateColor,new SKColor(83,97,110)),
                a.LabelFont,a.FooterDateWeight);
        }
        if(a.FooterUuidShow)
        {
            string uuid=CardUuidFormatter.Format(part.Record.Uuid,a,part.UuidLength);
            float font=Math.Min(Pt(Math.Clamp(a.FooterUuidSizePt,2,18)),maxFontPx);
            float maxWidth=dateLeft-left-(a.ShowDate?Px(.7):0);
            if(maxWidth<=0)
                throw new InvalidOperationException("Нет места для UUID в подвале.");
            float needed=Width(uuid,font,a.SecretFont,a.FooterUuidWeight);
            if(needed>maxWidth)
            {
                // Never shorten the configured UUID behind the user's back.
                // Reduce only the visual font size, preserving every character.
                font*=maxWidth/needed;
                if(font<Pt(2))
                    throw new InvalidOperationException("UUID не помещается в подвале даже при минимальном размере шрифта.");
            }
            float baseline=CenteredBaseline(uuid,centerY,font,a.SecretFont,a.FooterUuidWeight);
            DrawText(c,uuid,left,baseline,font,Color(a.FooterUuidColor,new SKColor(83,97,110)),
                a.SecretFont,a.FooterUuidWeight,markZero:a.MarkZero);
        }
    }

    private static void DrawFront(ICardCanvas c,int w,int h,CardAppearance a,CardPart part)
    {
        var ink=Styled(a.InkColor,"#1A2B40",a.Style switch {"technical"=>"#0C1420","minimal"=>"#202020","table"=>"#16283A",_=>"#1A2B40"});
        var muted=Styled(a.MutedColor,"#53616E",a.Style=="minimal"?"#5B6067":"#53616E");
        var accent=Styled(a.AccentColor,"#36618D",a.Style switch {"technical"=>"#242D36","minimal"=>"#4B5159","table"=>"#284C6B",_=>"#36618D"});
        var rule=Styled(a.RuleColor,"#CCD5E0",a.Style=="minimal"?"#DADCE0":"#CCD5E0");
        float density=CardLayoutEngine.DensityScale(a);
        float x=Px(a.PaddingMm),right=w-x,contentW=right-x;
        DrawHeader(c,w,h,a,part,ink,muted,accent,rule);
        float y=Px(CardLayoutEngine.HeaderHeight(a,part.Record));
        float end=h-Px(a.FooterBottomInsetMm+a.FooterHeightMm+.9);
        for(int lineIndex=0;lineIndex<part.Lines.Count;lineIndex++)
        {
            var line=part.Lines[lineIndex];
            float lineHeight=Px(line.HeightMm);
            if(y+lineHeight>end+.9f)
                throw new InvalidOperationException("Текст выходит за пределы карточки.");
            string weight=line.Weight=="inherit"?a.ValueWeight:line.Weight;
            float tracking=(float)(line.TrackingPt??a.ValueTrackingPt);
            float size=Pt(line.FontPt);
            switch(line.Kind)
            {
                case CardLineKind.Gap:break;
                case CardLineKind.Label:
                    Fit(c,line.Text,x,y+Math.Min(size,lineHeight*.82f),contentW,size,95,muted,a.LabelFont,a.LabelWeight);break;
                case CardLineKind.Section:
                    if(a.SectionRules && a.Style!="minimal")
                    {
                        using var pen=Paint(rule,true,1);
                        c.DrawLine(x,y,right,y,pen);
                    }
                    Fit(c,line.Text,x,y+Math.Min(size,lineHeight*.83f),contentW,size,90,accent,a.LabelFont,a.LabelWeight);break;
                case CardLineKind.Meta:
                    Fit(c,line.Text,x,y+Math.Min(size,lineHeight*.84f),contentW,size,95,muted,a.LabelFont,a.LabelWeight);break;
                case CardLineKind.Recovery:
                    Recovery(c,line,x,y,contentW,a,ink,muted,density);break;
                case CardLineKind.Qr:
                    Qr(c,line,x,y,contentW,a,ink,muted,density);break;
                default:
                    float valueX=x,valueWidth=contentW;
                    if(line.Label is {Length:>0} label && a.LabelsPosition=="left")
                    {
                        float labelWidth=contentW*(float)Math.Clamp(a.LabelColumnPct,15,55)/100f;
                        Fit(c,label,x,y+Math.Min(Pt(a.LabelSizePt*density),lineHeight*.82f),labelWidth-Px(1),
                            Pt(a.LabelSizePt*density),75,muted,a.LabelFont,a.LabelWeight);
                        valueX+=labelWidth+Px(1.2);valueWidth-=labelWidth+Px(1.2);
                    }
                    string font=a.SecretFont;
                    float valueBaseline=y+Math.Min(size,lineHeight*.82f);
                    if(line.GroupedValue is { } groups)
                        DrawSeparatedGroups(c,groups,valueX,valueBaseline,valueWidth,size,ink,font,
                            weight,tracking,line.GroupGapMm,a.MarkZero);
                    else
                        Fit(c,line.Text,valueX,valueBaseline,valueWidth,size,95,ink,font,weight,
                            tracking,markZero:a.MarkZero && font==a.SecretFont);
                    // Never draw a second line directly above a section rule,
                    // nor a line underneath each wrapped fragment of one value.
                    if(a.Style=="table" && ShouldDrawTableRule(part.Lines,lineIndex))
                    {
                        using var pen=Paint(rule,true,.55f);c.DrawLine(x,y+lineHeight-1,right,y+lineHeight-1,pen);
                    }
                    break;
            }
            y+=lineHeight;
        }
        DrawFooter(c,w,h,a,part);
        Frame(c,w,h,a);
    }

    // An optional row rule separates different fields. A section owns its
    // leading rule, so the preceding value must not draw an adjacent duplicate.
    // Gaps and wrapped lines do not introduce new boundaries.
    internal static bool ShouldDrawTableRule(IReadOnlyList<CardLine> lines,int index)
    {
        if(index<0 || index>=lines.Count || lines[index].Kind!=CardLineKind.Value)
            return false;
        for(int nextIndex=index+1;nextIndex<lines.Count;nextIndex++)
        {
            var next=lines[nextIndex];
            if(next.Kind==CardLineKind.Gap)continue;
            return next.Kind!=CardLineKind.Section &&
                !string.Equals(next.Field,lines[index].Field,StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // The checkbox must be vertically centred on the actual *ink* of the first
    // printed code line, not on a nominal line box. This layout ensures
    // the text's real bounding rectangle was used for checkbox placement.
    internal static float RecoveryCheckboxTop(string firstLine,string family,string weight,
        float fontPx,float textBaseline,float squareSide)
    {
        using var font=new SKFont(CardTextMetrics.Typeface(family,weight),Math.Max(2f,fontPx));
        font.MeasureText(firstLine,out SKRect inkBounds);
        float inkCenter = inkBounds.Bottom>inkBounds.Top
            ? textBaseline+(inkBounds.Top+inkBounds.Bottom)*.5f
            : textBaseline-fontPx*.35f;
        return inkCenter-squareSide*.5f;
    }

    private static void Recovery(ICardCanvas c,CardLine line,float x,float y,float width,CardAppearance a,
                                 SKColor ink,SKColor muted,float density)
    {
        var cells=line.Cells??[line.Text];
        float gap=Px(1.5f);
        float colWidth=(width-gap*(cells.Length-1))/cells.Length;
        float size=Pt(line.FontPt);
        // Same minimum line height as CardLayoutEngine.Height(size,a).
        float rowHeight=Math.Max(Px(2.1f),Pt(line.FontPt*(float)Math.Clamp(a.LineSpacing,.8,2)*1.35f));
        string weight=line.Weight=="inherit"?a.ValueWeight:line.Weight;
        float checkboxReserve=a.RecoveryCheckboxes?Px(2.8f):0f;
        float checkboxGap=a.RecoveryCheckboxes?Px(.45f):0f;
        for(int i=0;i<cells.Length;i++)
        {
            float start=x+i*(colWidth+gap);
            if(cells[i].Length==0)continue;
            float valueX=start;
            var segments=cells[i].Split('\n');
            float firstBaseline=y+rowHeight*.8f;
            if(a.RecoveryCheckboxes)
            {
                float side=Math.Min(Px(2.1f),Math.Max(Px(1.4f),size*.63f));
                float top=RecoveryCheckboxTop(segments[0],a.SecretFont,weight,size,firstBaseline,side);
                float radius=Math.Min(2f,side*.12f);
                using var outline=Paint(muted,true,Math.Max(1f,Pt(.3f)));
                c.DrawRoundRect(new SKRect(start,top,start+side,top+side),radius,radius,outline);
                valueX=start+checkboxReserve+checkboxGap;
            }
            float available=start+colWidth-valueX-Px(.25f);
            if(available<=0)throw new InvalidOperationException("Недостаточно места для recovery-кодов.");
            for(int j=0;j<segments.Length;j++)
            {
                float baseline=firstBaseline+j*rowHeight;
                if(line.RecoveryGrouped)
                    DrawSeparatedGroups(c,segments[j].Split(' ',StringSplitOptions.RemoveEmptyEntries),
                        valueX,baseline,available,size,ink,a.SecretFont,weight,
                        (float)(line.TrackingPt??a.ValueTrackingPt),line.RecoveryGroupGapMm,a.MarkZero);
                else
                    Fit(c,segments[j],valueX,baseline,available,size,97,ink,
                        a.SecretFont,weight,(float)(line.TrackingPt??a.ValueTrackingPt),a.MarkZero);
            }
        }
    }
    // One measuring/drawing routine for grouped OTP values in both full-width
    // and QR-side columns. Only the original group characters are drawn; a
    // fractional inter-group gap is empty space, never an inserted Unicode glyph.
    private static void DrawSeparatedGroups(ICardCanvas c,string[] groups,float x,float baseline,
        float widthPx,float fontPx,SKColor color,string family,string weight,float trackingPt,
        float gapMm,bool markZero)
    {
        float fontPt=fontPx/Pt(1);
        var widths=groups.Select(g=>CardTextMetrics.MeasureMm(g,fontPt,family,weight,trackingPt)*PixelsPerMillimetre)
            .ToArray();
        float gapPx=Px(gapMm);
        float total=widths.Sum()+Math.Max(0,groups.Length-1)*gapPx;
        if(total>widthPx+1f)
            throw new InvalidOperationException("Группы значения не помещаются по ширине карточки.");
        float dx=x;
        for(int j=0;j<groups.Length;j++)
        {
            DrawText(c,groups[j],dx,baseline,fontPx,color,family,weight,trackingPt,markZero);
            dx+=widths[j]+(j<groups.Length-1?gapPx:0);
        }
    }

    private static void Qr(ICardCanvas c,CardLine line,float x,float y,float width,
                           CardAppearance a,SKColor ink,SKColor muted,float density)
    {
        if(string.IsNullOrWhiteSpace(line.QrUri))return;
        float side=Px(line.FontPt),left;
        if(line.QrInline)left=a.TotpQrPosition=="right"?x+width-side:x;
        else left=a.TotpQrPosition switch {"left"=>x,"right"=>x+width-side,_=>x+(width-side)/2};
        if(!line.QrInline)
        {
            DrawQr(c,line.QrUri,left,y,side);
            return;
        }
        float textX=a.TotpQrPosition=="right"?x:x+side+Px(2);
        float textWidth=width-side-Px(2);
        // The first BLACK modules (not the outer white quiet-zone square)
        // share their visible top with the caption ink. Keep the original
        // white quiet zone, including at the QR's top, for scanner reliability.
        var qrLabels=string.IsNullOrEmpty(line.Label)?Array.Empty<string>():line.Label.Split('\n');
        float labelPt=Pt(a.LabelSizePt*density);
        float labelRowHeight=Math.Max(Px(2.1f),Pt(a.LabelSizePt*density*
            (float)Math.Clamp(a.LineSpacing,.8,2)*1.25f));
        float labelHeight=qrLabels.Length*labelRowHeight;
        float inkTop=qrLabels.Length>0
            ? Px(CardLayoutEngine.InlineQrLabelInkTopMm(qrLabels[0],
                (float)(a.LabelSizePt*density),labelRowHeight/PixelsPerMillimetre,a)):0;
        float blackTop=side*line.QrFirstBlackRowFraction;
        float textShift=qrLabels.Length>0?Math.Max(0,blackTop-inkTop):0;
        float qrShift=qrLabels.Length>0?Math.Max(0,inkTop-blackTop):0;
        DrawQr(c,line.QrUri,left,y+qrShift,side);
        for(int i=0;i<qrLabels.Length;i++)
            Fit(c,qrLabels[i],textX,y+textShift+labelRowHeight*i+Math.Min(labelPt,labelRowHeight*.82f),
                textWidth,labelPt,95,muted,a.LabelFont,a.LabelWeight);
        float fontPt=line.QrValuePt>0?line.QrValuePt:(float)(a.ValueSizePt*density);
        float rowHeight=Pt(fontPt*(float)Math.Clamp(a.LineSpacing,.8,2)*1.35f);
        string weight=line.Weight=="inherit"?a.ValueWeight:line.Weight;
        var rows=line.Text.Split('\n');
        if(line.QrGroupedRows is { } grouped)
        {
            float tracking=(float)(line.TrackingPt??a.ValueTrackingPt);
            foreach(var (groups,i) in grouped.Select((g,i)=>(g,i)))
            {
                float baseline=y+textShift+labelHeight+rowHeight*(i+.85f);
                DrawSeparatedGroups(c,groups,textX,baseline,textWidth,Pt(fontPt),ink,a.SecretFont,
                    weight,tracking,line.QrGroupGapMm,a.MarkZero);
            }
        }
        else for(int i=0;i<rows.Length;i++)
            Fit(c,rows[i],textX,y+textShift+labelHeight+rowHeight*(i+.85f),textWidth,Pt(fontPt),96,ink,a.SecretFont,
                weight,(float)(line.TrackingPt??a.ValueTrackingPt),a.MarkZero);
        if(!string.IsNullOrEmpty(line.QrMetadata))
        {
            float labelSize=Pt(a.LabelSizePt*density);
            float metaRowHeight=Math.Max(Px(2.1f),labelSize*(float)Math.Clamp(a.LineSpacing,.8,2)*1.25f);
            foreach(var (row,j) in line.QrMetadata.Split('\n').Select((s,j)=>(s,j)))
                Fit(c,row,textX,y+textShift+labelHeight+rowHeight*rows.Length+metaRowHeight*j+metaRowHeight*.84f,
                    textWidth,labelSize,95,muted,a.LabelFont,a.LabelWeight);
        }
    }
    internal static float QrFirstBlackRowFraction(string qrUri)
    {
        using var data=QRCodeGenerator.GenerateQrCode(qrUri,QRCodeGenerator.ECCLevel.M);
        var modules=data.ModuleMatrix;
        int count=modules.Count;
        if(count==0)throw new InvalidOperationException("QR-код пуст.");
        for(int row=0;row<count;row++)
            for(int col=0;col<count;col++)
                if(modules[row][col])return (float)row/count;
        throw new InvalidOperationException("QR-код не содержит тёмных модулей.");
    }

    private static void DrawQr(ICardCanvas c,string text,float left,float top,float side)
    {
        using var data=QRCodeGenerator.GenerateQrCode(text,QRCodeGenerator.ECCLevel.M);
        var modules=data.ModuleMatrix;int count=modules.Count;
        if(count==0)throw new InvalidOperationException("QR-код пуст.");
        float unit=side/count;
        using var white=Paint(SKColors.White);using var black=Paint(SKColors.Black);
        c.DrawRect(new SKRect(left,top,left+side,top+side),white);
        for(int row=0;row<count;row++)for(int col=0;col<count;col++)if(modules[row][col])
            c.DrawRect(new SKRect(left+col*unit,top+row*unit,
                left+(col+1)*unit+.025f,top+(row+1)*unit+.025f),black);
    }

    // Available icon modes. Never mutate the KDBX custom-icon bytes.
    // Auto darkens mostly-white glyphs; silhouette preserves alpha; outline
    // retains just the contour. All processing is in memory.
    private static SKBitmap TransformIcon(SKBitmap source,string mode)
    {
        var result=new SKBitmap(source.Width,source.Height,SKColorType.Rgba8888,SKAlphaType.Premul);
        int light=0,painted=0;
        for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
        {
            var c=source.GetPixel(x,y);
            if(c.Alpha<32)continue;
            painted++;
            if(c.Red>205&&c.Green>205&&c.Blue>205)light++;
        }
        bool autoInvert=mode=="auto" && painted>0 && light*100/painted>=80;
        for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
        {
            var c=source.GetPixel(x,y);
            if(c.Alpha<32){result.SetPixel(x,y,SKColors.Transparent);continue;}
            bool edge=x==0||y==0||x==source.Width-1||y==source.Height-1||
                source.GetPixel(Math.Max(0,x-1),y).Alpha<32 ||
                source.GetPixel(Math.Min(source.Width-1,x+1),y).Alpha<32 ||
                source.GetPixel(x,Math.Max(0,y-1)).Alpha<32 ||
                source.GetPixel(x,Math.Min(source.Height-1,y+1)).Alpha<32;
            var color=mode switch
            {
                "silhouette"=>new SKColor(32,43,56,c.Alpha),
                "outline"=>edge?new SKColor(32,43,56,c.Alpha):SKColors.Transparent,
                "auto" when autoInvert=>new SKColor((byte)(255-c.Red),(byte)(255-c.Green),(byte)(255-c.Blue),c.Alpha),
                _=>c
            };
            result.SetPixel(x,y,color);
        }
        return result;
    }

    // Inherited values reproduce old templates, including larger EN captions.
    internal static (string Font,string Weight,float SizePt) BackCaptionTypography(CardAppearance a,bool title)
    {
        string selectedFont=title ? a.BackCaptionTitleFont : a.BackCaptionSubtitleFont;
        string inheritedFont=title ? a.TitleFont : a.LabelFont;
        string selectedWeight=title ? a.BackCaptionTitleWeight : a.BackCaptionSubtitleWeight;
        string inheritedWeight=title ? a.TitleWeight : a.LabelWeight;
        double explicitPt=title ? a.BackCaptionTitleSizePt : a.BackCaptionSubtitleSizePt;
        float legacyPt=title ? (a.DuplexBackLanguage=="en" ? 6.6f : 5.8f)
                             : (a.DuplexBackLanguage=="en" ? 5f : 4.65f);
        return (string.IsNullOrWhiteSpace(selectedFont) || selectedFont=="inherit" ? inheritedFont : selectedFont,
                string.IsNullOrWhiteSpace(selectedWeight) || selectedWeight=="inherit" ? inheritedWeight : selectedWeight,
                double.IsFinite(explicitPt) && explicitPt>0 ? (float)Math.Clamp(explicitPt,2,18) : legacyPt);
    }

    /// <summary>Decorative reverse side without credential data.  Each layer is
    /// optional and independently enabled, while a blank duplex_back_fill
    /// always means an entirely blank back.</summary>
    private static void DrawBack(ICardCanvas c,int w,int h,CardAppearance a,CardPart front)
    {
        float PxMm(double mm)=>(float)(mm*PixelsPerMillimetre);
        var ink=Color(a.InkColor,new SKColor(36,43,52));
        var muted=Color(a.MutedColor,new SKColor(88,96,105));
        var accent=Color(a.AccentColor,new SKColor(54,97,141));
        // Paired dividers use neutral #777 and 0.18mm.
        var rule=new SKColor(119,119,119);
        float safeTop=a.EmptyBackShowHeader
            ? PxMm(CardHeaderLayout.Measure(a,front.Record).DecorationBottom+5)
            : PxMm(9);
        float safeBottom=a.EmptyBackShowFooter ? h-PxMm(a.FooterBottomInsetMm+a.FooterHeightMm+5) : h-PxMm(9);
        // The header and footer take priority; do not paint art over them when
        // user-selected dimensions leave too little space for an emblem.
        bool artworkBandAvailable=safeBottom>safeTop+PxMm(5);
        float centerX=w*.5f;
        bool drawCaption=artworkBandAvailable && a.EmptyBackShowArt;
        bool drawIcon=artworkBandAvailable && a.EmptyBackShowIcon && a.BackArtIcon!="none";
        string[] titles=a.DuplexBackLanguage switch
        {
            "uk"=>["КОНФІДЕНЦІЙНО","ОБЛІКОВІ ДАНІ"],
            "en"=>["PRIVATE","CREDENTIALS"],
            _=>["КОНФИДЕНЦИАЛЬНО","УЧЁТНЫЕ ДАННЫЕ"]
        };
        var titleStyle=BackCaptionTypography(a,true);
        var subStyle=BackCaptionTypography(a,false);
        float maxWidth=Math.Min(w-PxMm(14),PxMm(43));
        float titlePx=Pt(titleStyle.SizePt);
        float subPx=Pt(subStyle.SizePt);
        // Fit each line independently to the printable width. A large selected
        // point size must never make preview or printing throw an exception.
        titlePx*=Math.Min(1f,maxWidth/Math.Max(1f,Width(titles[0],titlePx,titleStyle.Font,titleStyle.Weight)));
        subPx*=Math.Min(1f,maxWidth/Math.Max(1f,Width(titles[1],subPx,subStyle.Font,subStyle.Weight)));
        using var titleFont=new SKFont(CardTextMetrics.Typeface(titleStyle.Font,titleStyle.Weight),titlePx);
        using var subFont=new SKFont(CardTextMetrics.Typeface(subStyle.Font,subStyle.Weight),subPx);
        titleFont.MeasureText(titles[0],out SKRect titleBox);
        subFont.MeasureText(titles[1],out SKRect subBox);
        float titleBaseline0=CenteredBaseline(titles[0],0,titlePx,titleStyle.Font,titleStyle.Weight);
        float subCenter=Math.Max(PxMm(4),(titleBox.Height+subBox.Height)*.5f+PxMm(.9));
        float subBaseline0=CenteredBaseline(titles[1],subCenter,subPx,subStyle.Font,subStyle.Weight);
        bool rules=drawCaption && a.DuplexBackStyle=="rules";
        float rulePadding=rules?PxMm(a.BackRulesGapMm):0;
        float captionTop0=titleBaseline0+titleBox.Top-rulePadding;
        float captionBottom0=subBaseline0+subBox.Bottom+rulePadding;
        float captionHeight=Math.Max(0,captionBottom0-captionTop0);
        if(drawCaption && captionHeight>safeBottom-safeTop-PxMm(1))
        {
            drawCaption=false;
            rules=false;
        }
        var layout=CardBackArtworkLayout.Measure(safeTop,safeBottom,
            PxMm(Math.Clamp(a.BackArtIconSizeMm,4,28)),captionHeight,
            PxMm(Math.Clamp(a.BackArtTextGapMm,2,12)),a.BackArtIconPosition,drawIcon,drawCaption);

        // Draw both text lines and their paired dividers using the same offset
        // as the emblem. Resizing the emblem moves the entire caption block.
        if(drawCaption)
        {
            float offset=layout.CaptionTopY-captionTop0;
            float titleBaseline=titleBaseline0+offset;
            float subBaseline=subBaseline0+offset;
            float titleWidth=Math.Min(maxWidth,Width(titles[0],titlePx,titleStyle.Font,titleStyle.Weight));
            float subWidth=Math.Min(maxWidth,Width(titles[1],subPx,subStyle.Font,subStyle.Weight));
            DrawText(c,titles[0],centerX-titleWidth*.5f,titleBaseline,titlePx,
                ink,titleStyle.Font,titleStyle.Weight);
            DrawText(c,titles[1],centerX-subWidth*.5f,subBaseline,subPx,
                muted,subStyle.Font,subStyle.Weight);
            if(rules)
            {
                float halfWidth=Math.Min(w-PxMm(12),PxMm(a.BackRulesWidthMm))*.5f;
                if(a.BackRulesStyle=="simple")halfWidth*=.8f;
                using var pen=Paint(rule.WithAlpha((byte)Math.Round(255*Math.Clamp(a.BackRulesOpacity,0,100)/100)),
                    true,Math.Max(.55f,PxMm(a.BackRulesThicknessMm)));
                c.DrawLine(centerX-halfWidth,titleBaseline+titleBox.Top-rulePadding,
                    centerX+halfWidth,titleBaseline+titleBox.Top-rulePadding,pen);
                c.DrawLine(centerX-halfWidth,subBaseline+subBox.Bottom+rulePadding,
                    centerX+halfWidth,subBaseline+subBox.Bottom+rulePadding,pen);
            }
        }
        if(drawIcon && layout.IconSize>=2)
        {
            var iconColor=Color(a.BackArtIconColor,ink);
            var iconInk=iconColor.WithAlpha((byte)Math.Round(255*Math.Clamp(a.BackArtIconOpacity,0,100)/100));
            CardVectorIcons.Draw(c,a.BackArtIcon,centerX,layout.IconCenterY,layout.IconSize,iconInk);
        }
        if(a.EmptyBackShowHeader)
            DrawHeader(c,w,h,a,front,ink,muted,accent,rule);
        if(a.EmptyBackShowFooter)
            DrawFooter(c,w,h,a,front);
        if(a.EmptyBackShowFrame)
            Frame(c,w,h,a);
    }
}
