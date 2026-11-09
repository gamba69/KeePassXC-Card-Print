using KeePassXCCardPrint.Rendering;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using SkiaSharp;

namespace KeePassXCCardPrint.Platforms.Windows.Printing;

/// <summary>
/// Emits genuine vector FixedPage geometry, NOT a picture of a page. XPS OM
/// parses and packages this markup on Windows. Every visible card primitive
/// has an explicit representation or throws; raster page fallback is NEVER
/// hidden in this implementation. The only bitmaps are source KeePass icons.
/// Text outlines use precisely the selected Skia variable-font instance and
/// HarfBuzz glyph IDs/positions, so variable weights do not silently change.
/// </summary>
internal sealed class XpsCardCanvas : IDirectVectorCardCanvas,IDisposable
{
    private const string XpsNamespace="http://schemas.microsoft.com/xps/2005/06";
    private const float DesignUnitsPerMm=CardSkiaRenderer.PixelsPerMillimetre;
    private const float XpsUnitsPerMm=96f/25.4f;
    private readonly MemoryStream _markup=new();
    private readonly XmlWriter _xml;
    private readonly Stack<int> _savedGroupDepths=new();
    private readonly Dictionary<string,byte[]> _images=new(StringComparer.Ordinal);
    private readonly Dictionary<string,byte[]> _fonts=new(StringComparer.Ordinal);
    private readonly Dictionary<SKTypeface,string?> _fontLocations=new();
    private int _embeddedGlyphs;
    private int _outlinedGlyphs;
    private readonly string _resourcePrefix;
    private readonly string _fontResourcePrefix;
    private int _groups;
    private bool _completed;

    internal IReadOnlyDictionary<string,byte[]> Images=>_images;
    internal IReadOnlyDictionary<string,byte[]> Fonts=>_fonts;
    internal int EmbeddedGlyphCount=>_embeddedGlyphs;
    internal int OutlinedGlyphCount=>_outlinedGlyphs;
    internal long ImageBytes=>_images.Sum(kv=>kv.Value.LongLength);

    internal XpsCardCanvas(PaperGeometry paper,string resourcePrefix="/Resources/Images/")
    {
        if(!resourcePrefix.StartsWith("/Resources/",StringComparison.Ordinal) ||
            !resourcePrefix.EndsWith("/",StringComparison.Ordinal) ||
            resourcePrefix.Contains("..",StringComparison.Ordinal))
            throw new ArgumentException("Invalid XPS resource prefix.",nameof(resourcePrefix));
        _resourcePrefix=resourcePrefix;
        // Font part URIs are distinct across pages, just like icon URIs.
        // Repeating one package part name in separate page collections can
        // otherwise collide in the Windows XPS package writer.
        _fontResourcePrefix=resourcePrefix.EndsWith("/Images/",StringComparison.Ordinal)
            ? resourcePrefix[..^"Images/".Length]+"Fonts/"
            : resourcePrefix+"Fonts/";
        _xml=XmlWriter.Create(_markup,new XmlWriterSettings
        {
            Encoding=new UTF8Encoding(false),OmitXmlDeclaration=false,
            CloseOutput=false,CheckCharacters=true,Indent=false
        });
        _xml.WriteStartDocument();
        _xml.WriteStartElement("FixedPage",XpsNamespace);
        _xml.WriteAttributeString("Width",F(paper.WidthMm*XpsUnitsPerMm));
        _xml.WriteAttributeString("Height",F(paper.HeightMm*XpsUnitsPerMm));
        _xml.WriteAttributeString("xml","lang",null,"en-US");
        // The drawing engine uses 7.5 design pixels/mm; FixedPage uses 96 dpi.
        // This transformation is applied once, without rounding card placement.
        Scale(XpsUnitsPerMm/DesignUnitsPerMm);
    }

    private static string F(double value)
    {
        if(!double.IsFinite(value)||Math.Abs(value)>1_000_000)
            throw new InvalidOperationException("Nonfinite or out-of-range XPS coordinate.");
        return value.ToString("0.######",CultureInfo.InvariantCulture);
    }
    private static string Pair(double x,double y)=>F(x)+","+F(y);
    private static string Rect(SKRect r)=>string.Join(",",F(r.Left),F(r.Top),F(r.Width),F(r.Height));
    private static string Color(SKColor c)=>"#"+c.Alpha.ToString("X2",CultureInfo.InvariantCulture)+
        c.Red.ToString("X2",CultureInfo.InvariantCulture)+c.Green.ToString("X2",CultureInfo.InvariantCulture)+
        c.Blue.ToString("X2",CultureInfo.InvariantCulture);

    private void EnterGroup(string? attribute=null,string? value=null)
    {
        _xml.WriteStartElement("Canvas",XpsNamespace);
        if(attribute is not null)_xml.WriteAttributeString(attribute,value);
        _groups++;
    }

    public void Save()
    {
        _savedGroupDepths.Push(_groups);
        EnterGroup();
    }
    public void Restore()
    {
        if(!_savedGroupDepths.TryPop(out var level))
            throw new InvalidOperationException("Unbalanced Save/Restore in vector drawing.");
        while(_groups>level){_xml.WriteEndElement();_groups--;}
    }
    public void Translate(float x,float y)=>EnterGroup("RenderTransform","1,0,0,1,"+Pair(x,y));
    public void Scale(float x,float y)=>EnterGroup("RenderTransform",F(x)+",0,0,"+F(y)+",0,0");
    public void Scale(float factor)=>Scale(factor,factor);
    public void ClipRect(SKRect rect,SKClipOperation operation,bool aa)
    {
        if(operation!=SKClipOperation.Intersect)
            throw new NotSupportedException("Only intersect clipping has an XPS equivalent.");
        EnterGroup("Clip",BoxPath(rect));
    }
    public void ClipRoundRect(SKRoundRect round,SKClipOperation operation,bool aa)
    {
        if(operation!=SKClipOperation.Intersect)
            throw new NotSupportedException("Only intersect clipping has an XPS equivalent.");
        // Nonuniform radii are represented in path data, without raster masks.
        var rect=round.Rect;
        var radii=round.GetRadii(SKRoundRectCorner.UpperLeft);
        EnterGroup("Clip",RoundedPath(rect,radii.X,radii.Y));
    }
    private static string BoxPath(SKRect r)=>"M "+Pair(r.Left,r.Top)+" L "+Pair(r.Right,r.Top)+
        " "+Pair(r.Right,r.Bottom)+" "+Pair(r.Left,r.Bottom)+" Z";
    private static string RoundedPath(SKRect r,float rx,float ry)
    {
        rx=Math.Clamp(rx,0,r.Width/2);
        ry=Math.Clamp(ry,0,r.Height/2);
        if(rx<=0||ry<=0)return BoxPath(r);
        // Cubic segments: avoid ambiguity in SVG vs WPF elliptical-arc parsing.
        const double k=.5522847498307936;
        double x0=r.Left,y0=r.Top,x1=r.Right,y1=r.Bottom;
        return "M "+Pair(x0+rx,y0)+" L "+Pair(x1-rx,y0)+
            " C "+Pair(x1-rx+k*rx,y0)+" "+Pair(x1,y0+ry-k*ry)+" "+Pair(x1,y0+ry)+
            " L "+Pair(x1,y1-ry)+" C "+Pair(x1,y1-ry+k*ry)+" "+Pair(x1-rx+k*rx,y1)+" "+Pair(x1-rx,y1)+
            " L "+Pair(x0+rx,y1)+" C "+Pair(x0+rx-k*rx,y1)+" "+Pair(x0,y1-ry+k*ry)+" "+Pair(x0,y1-ry)+
            " L "+Pair(x0,y0+ry)+" C "+Pair(x0,y0+ry-k*ry)+" "+Pair(x0+rx-k*rx,y0)+" "+Pair(x0+rx,y0)+" Z";
    }

    private void WritePath(string data,SKPaint paint)
    {
        if(paint.Shader is not null)
            throw new NotSupportedException("An opaque Skia shader must be converted to an explicit XPS brush.");
        _xml.WriteStartElement("Path",XpsNamespace);
        _xml.WriteAttributeString("Data",data);
        if(paint.Style==SKPaintStyle.Stroke||paint.Style==SKPaintStyle.StrokeAndFill)
        {
            _xml.WriteAttributeString("Stroke",Color(paint.Color));
            _xml.WriteAttributeString("StrokeThickness",F(paint.StrokeWidth));
            if(paint.StrokeCap!=SKStrokeCap.Butt)
            {
                string cap=paint.StrokeCap==SKStrokeCap.Round?"Round":"Square";
                _xml.WriteAttributeString("StrokeStartLineCap",cap);
                _xml.WriteAttributeString("StrokeEndLineCap",cap);
            }
            if(paint.StrokeJoin!=SKStrokeJoin.Miter)
                _xml.WriteAttributeString("StrokeLineJoin",paint.StrokeJoin==SKStrokeJoin.Round?"Round":"Bevel");
        }
        if(paint.Style is SKPaintStyle.Fill or SKPaintStyle.StrokeAndFill)
            _xml.WriteAttributeString("Fill",Color(paint.Color));
        _xml.WriteEndElement();
    }
    public void DrawRect(float x,float y,float width,float height,SKPaint paint)
        => DrawRect(new SKRect(x,y,x+width,y+height),paint);
    public void DrawRect(SKRect rect,SKPaint paint)=>WritePath(BoxPath(rect),paint);
    public void DrawRoundRect(SKRect rect,float rx,float ry,SKPaint paint)
        => WritePath(RoundedPath(rect,rx,ry),paint);
    public void DrawLine(float x0,float y0,float x1,float y1,SKPaint paint)
        => WritePath("M "+Pair(x0,y0)+" L "+Pair(x1,y1),paint);
    public void DrawCircle(float cx,float cy,float r,SKPaint paint)
    {
        var rect=new SKRect(cx-r,cy-r,cx+r,cy+r);
        WritePath(RoundedPath(rect,r,r),paint);
    }
    public void DrawPath(SKPath path,SKPaint paint)
    {
        // SVG path-data and XPS abbreviated geometry have subtly different
        // number/point grammars. Serialize SKPath verbs directly to XPS.
        var data=XpsPathSerializer.Serialize(path);
        if(string.IsNullOrEmpty(data))return;
        WritePath(data,paint);
    }

    public void DrawBitmap(SKBitmap bitmap,SKRect destination,SKSamplingOptions sampling)
    {
        if(bitmap.Width<1||bitmap.Height<1)throw new InvalidOperationException("Empty icon bitmap.");
        using var image=SKImage.FromBitmap(bitmap);
        using var encoded=image.Encode(SKEncodedImageFormat.Png,100)
            ?? throw new InvalidOperationException("Cannot encode source icon for XPS.");
        var bytes=encoded.ToArray();
        string hash=Convert.ToHexString(SHA256.HashData(bytes));
        string uri=_resourcePrefix+hash+".png";
        if(!_images.TryAdd(uri,bytes))CryptographicOperations.ZeroMemory(bytes);
        _xml.WriteStartElement("Path",XpsNamespace);
        _xml.WriteAttributeString("Data",BoxPath(destination));
        _xml.WriteStartElement("Path.Fill",XpsNamespace);
        _xml.WriteStartElement("ImageBrush",XpsNamespace);
        _xml.WriteAttributeString("ImageSource",uri);
        _xml.WriteAttributeString("Viewbox","0,0,"+F(bitmap.Width)+","+F(bitmap.Height));
        _xml.WriteAttributeString("ViewboxUnits","Absolute");
        _xml.WriteAttributeString("Viewport",Rect(destination));
        _xml.WriteAttributeString("ViewportUnits","Absolute");
        _xml.WriteAttributeString("TileMode","None");
        _xml.WriteEndElement();_xml.WriteEndElement();_xml.WriteEndElement();
    }
    public void DrawPicture(SKPicture picture)
        => throw new NotSupportedException("XPS canvas must draw cards directly, not replay opaque SKPicture.");

    public void DrawGlyphRuns(ShapedTextLayout layout,float left,float baseline,SKPaint paint)
    {
        // Exactly the same layout instance is passed to the Skia backend.
        // No glyph selection, positioning or width computation happens here.
        foreach(var run in layout.Runs)
        {
            DrawGlyphRun(run,left,baseline,paint);
            if(run.ZeroMark is { } mark)
                DrawCircle(left+mark.X,baseline+mark.BaselineOffset,
                    mark.Radius,paint);
        }
    }

    private string? GetFontUri(SKTypeface face)
    {
        if(_fontLocations.TryGetValue(face,out var known))return known;
        string? uri=null;
        if(XpsFontEmbedding.TryPrepare(face,out var fontBytes,out int collectionIndex))
        {
            // A content-addressed font resource can be shared by hundreds of
            // Glyphs elements on the same page. Hash only font data, never text.
            string name=Convert.ToHexString(SHA256.HashData(fontBytes));
            string part=_fontResourcePrefix+name+".ttf";
            if(!_fonts.TryAdd(part,fontBytes))CryptographicOperations.ZeroMemory(fontBytes);
            uri=part+(collectionIndex>0?"#"+collectionIndex.ToString(CultureInfo.InvariantCulture):"");
        }
        _fontLocations[face]=uri;
        return uri;
    }

    private void DrawGlyphRun(ShapedGlyphRun run,float left,float baseline,SKPaint paint)
    {
        // No HarfBuzz or font positioning here: both backends consume the same
        // shaped glyph IDs and positions. For exact per-glyph placement, emit a
        // Glyphs object with one explicit Index per glyph. This avoids XPS
        // advance-width quantization drift in tiny secrets or unusual tracking.
        string? uri=GetFontUri(run.Font.Typeface);
        if(uri is not null)
        {
            for(int i=0;i<run.Glyphs.Length;i++)
            {
                _xml.WriteStartElement("Glyphs",XpsNamespace);
                _xml.WriteAttributeString("FontUri",uri);
                _xml.WriteAttributeString("FontRenderingEmSize",F(run.Font.Size));
                _xml.WriteAttributeString("OriginX",F(left+run.OriginX+run.Positions[i].X));
                _xml.WriteAttributeString("OriginY",F(baseline+run.Positions[i].Y));
                _xml.WriteAttributeString("Indices",run.Glyphs[i].ToString(CultureInfo.InvariantCulture));
                _xml.WriteAttributeString("Fill",Color(paint.Color));
                _xml.WriteEndElement();
                _embeddedGlyphs++;
            }
            return;
        }
        // Fonts that forbid embedding, cannot be extracted, or cannot be
        // statically instanced retain the previous proven vector outline path.
        // Absolutely no substitution with a wrong weight or raster text.
        var spaceGlyphs=run.Font.GetGlyphs(" ");
        var tabGlyphs=run.Font.GetGlyphs("\t");
        ushort space=spaceGlyphs.Length>0?spaceGlyphs[0]:ushort.MaxValue;
        ushort tab=tabGlyphs.Length>0?tabGlyphs[0]:ushort.MaxValue;
        for(int i=0;i<run.Glyphs.Length;i++)
        {
            ushort glyph=run.Glyphs[i];
            using var shape=run.Font.GetGlyphPath(glyph);
            if(shape is null)
            {
                if(glyph==0 || (glyph!=space && glyph!=tab))
                    throw new InvalidOperationException("Font glyph has no vector outline (glyph "+glyph+").");
                continue;
            }
            if(shape.IsEmpty)continue;
            _xml.WriteStartElement("Path",XpsNamespace);
            _xml.WriteAttributeString("Data",XpsPathSerializer.Serialize(shape));
            _xml.WriteAttributeString("Fill",Color(paint.Color));
            _xml.WriteAttributeString("RenderTransform","1,0,0,1,"+
                Pair(left+run.OriginX+run.Positions[i].X,baseline+run.Positions[i].Y));
            _xml.WriteEndElement();
            _outlinedGlyphs++;
        }
    }

    /// <summary>One-to-one representation of CardBackgroundRenderer's radial shader.
    /// It does not approximate a gradient with raster pixels.</summary>
    public void DrawRadialGradientRect(SKRect rect,SKPoint center,float radius,
        SKColor[] colors,float[] stops)
    {
        if(colors.Length!=stops.Length||colors.Length<2||radius<=0)
            throw new ArgumentOutOfRangeException(nameof(colors));
        _xml.WriteStartElement("Path",XpsNamespace);
        _xml.WriteAttributeString("Data",BoxPath(rect));
        _xml.WriteStartElement("Path.Fill",XpsNamespace);
        _xml.WriteStartElement("RadialGradientBrush",XpsNamespace);
        _xml.WriteAttributeString("Center",Pair(center.X,center.Y));
        _xml.WriteAttributeString("GradientOrigin",Pair(center.X,center.Y));
        _xml.WriteAttributeString("MappingMode","Absolute");
        _xml.WriteAttributeString("RadiusX",F(radius));
        _xml.WriteAttributeString("RadiusY",F(radius));
        // ECMA-388 XPS requires the brush property-element wrapper. Direct
        // GradientStop children can work in WPF XAML but are invalid XPS.
        _xml.WriteStartElement("RadialGradientBrush.GradientStops",XpsNamespace);
        for(int i=0;i<colors.Length;i++)
        {
            _xml.WriteStartElement("GradientStop",XpsNamespace);
            _xml.WriteAttributeString("Color",Color(colors[i]));
            _xml.WriteAttributeString("Offset",F(stops[i]));
            _xml.WriteEndElement();
        }
        _xml.WriteEndElement(); // GradientStops
        _xml.WriteEndElement(); // RadialGradientBrush
        _xml.WriteEndElement(); // Path.Fill
        _xml.WriteEndElement(); // Path
    }

    internal byte[] Complete()
    {
        if(_completed)throw new InvalidOperationException("FixedPage already completed.");
        if(_savedGroupDepths.Count!=0)throw new InvalidOperationException("Unbalanced canvas save stack.");
        while(_groups-->0)_xml.WriteEndElement();
        _xml.WriteEndElement(); // FixedPage
        _xml.WriteEndDocument();_xml.Flush();
        _completed=true;
        return _markup.ToArray();
    }
    public void Dispose()
    {
        _xml.Dispose();_markup.Dispose();
        foreach(var bytes in _images.Values)CryptographicOperations.ZeroMemory(bytes);
        _images.Clear();
        foreach(var bytes in _fonts.Values)CryptographicOperations.ZeroMemory(bytes);
        _fonts.Clear();
        _fontLocations.Clear();
    }
}
