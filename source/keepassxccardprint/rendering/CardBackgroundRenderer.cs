using SkiaSharp;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Screen-only background painter; all patterns are independent of vault data.
/// Settings for a face are resolved before rasterization and are never persisted here.</summary>
internal static class CardBackgroundRenderer
{
    internal const float PixelsPerMm = 7.5f;
    internal readonly record struct Background(string Pattern, double Gray, double Contrast, bool WhiteOnly = false);

    internal static Background Resolve(CardAppearance a, string side)
    {
        var basic = new Background(a.CardBgPattern, a.CardBgGray, a.CardBgContrast);
        var front = a.FrontBgOverride
            ? new Background(a.FrontBgPattern, a.FrontBgGray, a.FrontBgContrast) : basic;
        var back = a.BackBgOverride
            ? new Background(a.DuplexBackBackground, a.BackBgGray, a.BackBgContrast) : basic;
        return side switch
        {
            "front" => front,
            "back" => back,
            "empty" => a.EmptyBackBgSource switch
            {
                "front" => front,
                "custom" => new Background(a.EmptyBackBgPattern, a.EmptyBackBgGray, a.EmptyBackBgContrast),
                "none" => new Background("clean",0,0,true),
                _ => back
            },
            _ => basic
        };
    }

    private static SKColor GrayColor(CardAppearance a,Background spec)
    {
        if(spec.WhiteOnly) return SKColors.White;
        var baseColor=!string.IsNullOrWhiteSpace(a.BackgroundColor)
            && SKColor.TryParse(a.BackgroundColor,out var parsed) ? parsed : SKColors.White;
        int darken=(int)Math.Round(Math.Clamp(spec.Gray,0,100)*.48);
        return new SKColor(
            (byte)Math.Max(0,baseColor.Red-darken),
            (byte)Math.Max(0,baseColor.Green-darken),
            (byte)Math.Max(0,baseColor.Blue-darken));
    }

    private static SKColor Darken(SKColor color,int difference) => new(
        (byte)Math.Max(0,color.Red-difference),
        (byte)Math.Max(0,color.Green-difference),
        (byte)Math.Max(0,color.Blue-difference));

    internal static void Draw(SKCanvas canvas,int width,int height,CardAppearance appearance,Background spec)
        => Draw(new SkiaCardCanvas(canvas),width,height,appearance,spec);

    internal static void Draw(ICardCanvas canvas,int width,int height,CardAppearance appearance,Background spec)
    {
        var baseColor=GrayColor(appearance,spec);
        using (var paper = new SKPaint { Color = baseColor, Style = SKPaintStyle.Fill })
            canvas.DrawRect(0, 0, width, height, paper);
        if(spec.WhiteOnly || spec.Pattern=="clean" || spec.Contrast<=0) return;
        float contrast=(float)Math.Clamp(spec.Contrast,0,100)/100f;
        float cx=width*.5f,cy=height*.5f;
        float Px(float mm)=>mm*PixelsPerMm;
        int difference=(int)Math.Round(contrast*52);
        SKColor ink=Darken(baseColor,difference);

        if(spec.Pattern=="gradient")
        {
            // Real smooth radial shader, not a solid oval. Corners return to the
            // chosen base shade; no hard edge or gradient banding in the preview.
            var colors=new[] {Darken(baseColor,(int)Math.Round(contrast*28)),
                              Darken(baseColor,(int)Math.Round(contrast*12)),baseColor};
            float[] stops=[0f,.46f,1f];
            canvas.Save();
            try
            {
                canvas.Translate(cx,cy);
                canvas.Scale(1f,1.34f);
                canvas.Translate(-cx,-cy);
                if(canvas is IDirectVectorCardCanvas xps)
                    xps.DrawRadialGradientRect(new SKRect(0,0,width,height),new SKPoint(cx,cy),
                        Math.Min(width,height)*.63f,colors,stops);
                else
                {
                    using var shader=SKShader.CreateRadialGradient(new SKPoint(cx,cy),
                        Math.Min(width,height)*.63f,colors,stops,SKShaderTileMode.Clamp);
                    using var paint=new SKPaint{IsAntialias=true,Shader=shader};
                    canvas.DrawRect(new SKRect(0,0,width,height),paint);
                }
            }
            finally {canvas.Restore();}
            return;
        }

        // The original pattern lived inside the middle 48 x 66 mm safe area.
        // A deliberately large white margin limits any background ink near cuts.
        float halfW=Math.Min(Px(24),width*.42f);
        float halfH=Math.Min(Px(33),height*.39f);
        float left=cx-halfW,top=cy-halfH,right=cx+halfW,bottom=cy+halfH;
        canvas.Save();
        using(var safe=new SKRoundRect(new SKRect(left,top,right,bottom),Px(6),Px(6)))
            canvas.ClipRoundRect(safe,SKClipOperation.Intersect,true);
        if(spec.Pattern=="diagonal")
        {
            using var pen=new SKPaint{Color=ink,IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=Math.Max(.65f,Px(.14f))};
            float step=Px(3.9f);
            for(float x=left-(bottom-top);x<right+step;x+=step)
                canvas.DrawLine(x,top,x+(bottom-top),bottom,pen);
        }
        else if(spec.Pattern=="technical")
        {
            var light=Darken(baseColor,(int)Math.Round(difference*.55f));
            using var pen=new SKPaint{Color=light,IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=Math.Max(.55f,Px(.10f))};
            float step=Px(5.2f);
            for(float x=cx;x<=right;x+=step)canvas.DrawLine(x,top,x,bottom,pen);
            for(float x=cx-step;x>=left;x-=step)canvas.DrawLine(x,top,x,bottom,pen);
            for(float y=cy;y<=bottom;y+=step)canvas.DrawLine(left,y,right,y,pen);
            for(float y=cy-step;y>=top;y-=step)canvas.DrawLine(left,y,right,y,pen);
            pen.Color=ink;
            for(float x=cx-3*step;x<=right;x+=3*step)
            for(float y=cy-3*step;y<=bottom;y+=3*step)
            {
                canvas.DrawLine(x-Px(.6f),y,x+Px(.6f),y,pen);
                canvas.DrawLine(x,y-Px(.6f),x,y+Px(.6f),pen);
            }
        }
        else if(spec.Pattern=="texture")
        {
            using var dot=new SKPaint{Color=ink,IsAntialias=true,Style=SKPaintStyle.Fill};
            var random=new Random(430026);
            for(int i=0;i<650;i++)
            {
                dot.Color=Darken(baseColor,random.Next(6,Math.Max(8,difference+7)));
                canvas.DrawCircle((float)(left+random.NextDouble()*(right-left)),
                    (float)(top+random.NextDouble()*(bottom-top)),
                    (float)(.32+random.NextDouble()*.6),dot);
            }
        }
        else if(spec.Pattern=="watermark")
        {
            using var pen=new SKPaint{Color=Darken(baseColor,(int)Math.Round(difference*.65f)),
                IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=Px(.7f)};
            // Abstract security contours, separate from the selectable emblem.
            canvas.DrawRoundRect(new SKRect(cx-Px(17),cy-Px(21),cx+Px(17),cy+Px(21)),Px(9),Px(9),pen);
            pen.Color=Darken(baseColor,(int)Math.Round(difference*.3f));
            pen.StrokeWidth=Px(.2f);
            canvas.DrawRoundRect(new SKRect(cx-Px(14),cy-Px(18),cx+Px(14),cy+Px(18)),Px(7),Px(7),pen);
        }
        canvas.Restore();
    }
}
