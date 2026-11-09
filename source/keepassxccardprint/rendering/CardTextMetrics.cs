using KeePassXCCardPrint.Platforms;
using System.Text;
using SkiaSharp;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Single source of truth for measuring and breaking printable text in millimetres.
/// Measurements use the same Skia font family and size as the renderer, never character-count heuristics.</summary>
internal static class CardTextMetrics
{
    internal const float MmPerPoint = 25.4f / 72f;
    private static readonly Dictionary<string, SKTypeface> Families = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    internal static SKTypeface Typeface(string family,string weight="400")
    {
        int desired=FontWeights.Parse(weight);
        string key=family+":"+desired.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock(Gate)
        {
            if(Families.TryGetValue(key,out var found))return found;
            // Portable fonts use genuine variable axes; system fonts use the host resolver.
            SKTypeface font=PortableFonts.IsPortableFont(family)
                ? PortableFonts.OpenTypeface(family,desired)
                : SKTypeface.FromFamilyName(PlatformServices.Current.ResolveSystemFontFamily(family),desired,5,SKFontStyleSlant.Upright)
                    ?? SKTypeface.FromFamilyName(PlatformServices.Current.FallbackTextFont,desired,5,SKFontStyleSlant.Upright)
                    ?? SKTypeface.Default;
            Families.Add(key,font);
            return font;
        }
    }
    internal static float MeasureMm(string text,float sizePt,string family,string weight="400",float trackingPt=0)
    {
        if(text.Length==0)return 0;
        return ShapedTextEngine.Measure(text,sizePt,family,weight,trackingPt)*MmPerPoint;
    }

    // Keeps every rune and every whitespace character. Concatenating result equals normalized input.
    internal static List<string> WrapExact(string source,float widthMm,float sizePt,string family,
                                             string weight="regular",float trackingPt=0)
    {
        if (widthMm<=0) throw new InvalidOperationException("Недостаточно места для текста.");
        string normalized=source.Replace("\r\n","\n").Replace('\r','\n');
        var result=new List<string>();
        foreach (var segment in normalized.Split('\n'))
        {
            if (segment.Length==0) { result.Add(""); continue; }
            var runes=segment.EnumerateRunes().Select(r=>r.ToString()).ToArray();
            int start=0;
            while(start<runes.Length)
            {
                int end=start; int spaceBreak=-1; var part=new StringBuilder();
                while(end<runes.Length)
                {
                    string candidate = part.ToString()+runes[end];
                    if (end>start && MeasureMm(candidate,sizePt,family,weight,trackingPt)>widthMm) break;
                    if (end==start && MeasureMm(candidate,sizePt,family,weight,trackingPt)>widthMm)
                        throw new InvalidOperationException("Один символ не помещается при выбранном шрифте.");
                    part.Append(runes[end]);
                    if(char.IsWhiteSpace(runes[end][0]))spaceBreak=end+1;
                    end++;
                }
                if(end<runes.Length && spaceBreak>start+1 && spaceBreak>start+(end-start)/2)
                    end=spaceBreak;
                result.Add(string.Concat(runes[start..end]));
                start=end;
            }
        }
        return result;
    }

    internal static List<(string Text,float Points)> Fit(string source,float widthMm,float pointSize,
        string family,string weight,string mode,int shrinkFloor,int splitBoost,float trackingPt=0)
    {
        if (source.Contains('\n') || source.Contains('\r') || mode=="wrap")
            return WrapExact(source,widthMm,pointSize,family,weight,trackingPt).Select(s=>(s,pointSize)).ToList();
        if (MeasureMm(source,pointSize,family,weight,trackingPt)<=widthMm)
            return [(source,pointSize)];
        if (mode is "shrink" or "smart")
        {
            for(int percent=98;percent>=Math.Clamp(shrinkFloor,40,100);percent-=2)
            {
                float reduced=pointSize*percent/100f;
                if(MeasureMm(source,reduced,family,weight,trackingPt)<=widthMm)
                    return [(source,reduced)];
            }
        }
        if(mode is "split" or "smart")
        {
            var chars=source.EnumerateRunes().Select(r=>r.ToString()).ToArray();
            float boosted=pointSize*Math.Clamp(splitBoost,100,200)/100f;
            foreach(var middle in Enumerable.Range(1,Math.Max(0,chars.Length-1))
                         .OrderBy(x=>Math.Abs(x-chars.Length/2f)))
            {
                string left=string.Concat(chars[..middle]),right=string.Concat(chars[middle..]);
                if(MeasureMm(left,boosted,family,weight,trackingPt)<=widthMm &&
                   MeasureMm(right,boosted,family,weight,trackingPt)<=widthMm)
                    return [(left,boosted),(right,boosted)];
            }
        }
        return WrapExact(source,widthMm,pointSize,family,weight,trackingPt)
            .Select(s=>(s,pointSize)).ToList();
    }
}
