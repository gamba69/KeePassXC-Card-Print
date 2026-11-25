using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

public enum CardLineKind { Title, Label, Value, Meta, Recovery, Qr, Section, Gap }
public sealed record CardLine(CardLineKind Kind, string Text, float HeightMm, float FontPt = 0,
    string Role = "text", string Field = "", string? QrUri = null,
    string? Label = null, string Section = "", string Weight = "inherit", double? TrackingPt = null,
    string[]? Cells = null, bool QrInline = false, string? QrMetadata = null, float QrValuePt = 0,
    string[][]? QrGroupedRows = null, float QrGroupGapMm = 0,
    string[]? GroupedValue = null, float GroupGapMm = 0,
    bool RecoveryGrouped = false, float RecoveryGroupGapMm = 0,
    float QrFirstBlackRowFraction = 0, string? ProseAlignment = null, bool ParagraphFinal = true,
    bool QrStrict = false, string QrPosition = "center", string QrEcc = "Q");
public sealed record CardPart(VaultRecord Record, IReadOnlyList<CardLine> Lines, int Number, int Total, int UuidLength = 12);
public sealed record PhysicalCard(CardPart Front, CardPart? Continuation);

/// <summary>Physical card compositor. All text widths are measured with the real Skia fonts.
/// Layout and rendering use identical typefaces; only presentation strings are grouped,
/// never the original KDBX fields. Parts are RAM-only, no print spool or file writes.</summary>
public static class CardLayoutEngine
{
    public const float MmPerPoint = CardTextMetrics.MmPerPoint;
    public static float DensityScale(CardAppearance a) => a.Density switch
    { "compact"=>.90f, "large" or "spacious"=>1.12f, _=>1f };
    private sealed record Field(string Name,string Value,FieldRule Rule,int Index);
    private static readonly string[] OtpMetaNames = ["account","issuer","algorithm","digits","period","counter","encoder","parameters","settings"];

    public static float HeaderHeight(CardAppearance a,VaultRecord record) =>
        CardHeaderLayout.Measure(a,record).ContentY;

    public static float ContentHeight(CardAppearance a,VaultRecord record) =>
        (float)(a.HeightMm-a.FooterBottomInsetMm-a.FooterHeightMm-.9)-HeaderHeight(a,record);

    public static IReadOnlyList<PhysicalCard> Build(IReadOnlyList<VaultRecord> records,
        CardAppearance a, IReadOnlyDictionary<string,FieldRule> mapping,
        IReadOnlyList<VaultRecord>? knownRecords = null, CancellationToken cancellationToken = default)
    {
        if(!double.IsFinite(a.WidthMm)||!double.IsFinite(a.HeightMm)||
           a.WidthMm is <35 or >205 || a.HeightMm is <45 or >292 ||
           !double.IsFinite(a.PaddingMm)||a.PaddingMm<0 || a.PaddingMm>15)
            throw new InvalidOperationException("Недопустимая геометрия карточки.");
        if(a.RecoveryColumns is <1 or >3) throw new InvalidOperationException("Недопустимое число колонок кодов.");
        var prefixLengths=CardUuidFormatter.UniquePrefixLengths(knownRecords??records,a.FooterUuidLength);
        // Independent records have no layout dependency. Compute them concurrently while
        // keeping deterministic input order; duplex pairing across records is a final pass.
        var ordered=new PhysicalCard[records.Count][];
        var options=new ParallelOptions
        {
            CancellationToken=cancellationToken,
            MaxDegreeOfParallelism=Math.Clamp(Environment.ProcessorCount-1,1,8)
        };
        Parallel.For(0,records.Count,options,index =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record=records[index];
            float height=ContentHeight(a,record);
            if(height<5)throw new InvalidOperationException("Недостаточно места для содержимого карточки.");
            var rows=BuildLines(record,a,mapping);
            var parts=Paginate(rows,height,a);
            if(a.NotesMode=="optional" && rows.Any(l=>l.Field.Equals("NOTES",StringComparison.OrdinalIgnoreCase)))
            {
                var without=Paginate(rows.Where(l=>!l.Field.Equals("NOTES",StringComparison.OrdinalIgnoreCase)).ToList(),height,a);
                if(without.Count<parts.Count)parts=without;
            }
            if(a.RecoveryAutosize && rows.Any(l=>l.Kind==CardLineKind.Recovery))
            {
                int baseline=parts.Count;
                // Enlarge only while card count is unchanged. Do not sacrifice one code.
                for(float size=(float)a.RecoveryMaxFontPt;size>Math.Max(a.RecoveryMinFontPt,a.ValueSizePt*.88)+.01;size-=.25f)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var bigger=BuildLines(record,a,mapping,size);
                        var trial=Paginate(bigger,height,a);
                        if(trial.Count<=baseline){rows=bigger;parts=trial;break;}
                    }
                    catch(InvalidOperationException) { /* test a smaller size */ }
                }
            }
            if(a.AdaptiveFieldGaps && a.AdaptiveGapMaxMm>0)
                parts=parts.Select(p=>ExpandGaps(p,height,(float)a.AdaptiveGapMaxMm)).ToList();
            // Records without printable fields do not generate header-only cards.
            if(parts.Count==0)
            {
                ordered[index]=[];
                return;
            }
            int uniqueLength=prefixLengths.TryGetValue(record.Uuid,out int calculated)
                ? calculated : Math.Clamp(a.FooterUuidLength,4,32);
            var counted=parts.Select((lines,i)=>new CardPart(record,lines,i+1,parts.Count,uniqueLength)).ToList();
            var physical=new List<PhysicalCard>();
            for(int i=0;i<counted.Count;i++)
            {
                CardPart? continuation = null;
                if(a.DuplexEnabled && a.DuplexContinuation && i+1<counted.Count)
                    continuation=counted[++i];
                physical.Add(new PhysicalCard(counted[continuation is null?i:i-1],continuation));
            }
            // Do not append from concurrent workers: iteration order must remain stable.
            ordered[index]=physical.ToArray();
        });
        var cards=new List<PhysicalCard>();
        foreach(var group in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            cards.AddRange(group);
        }
        if(a.DuplexEnabled && a.DuplexCombineDifferent)
        {
            var merged=new List<PhysicalCard>();
            for(int i=0;i<cards.Count;i++)
            {
                var current=cards[i];
                if(i+1<cards.Count && current.Continuation is null && cards[i+1].Continuation is null &&
                   current.Front.Total==1 && cards[i+1].Front.Total==1 &&
                   !ReferenceEquals(current.Front.Record,cards[i+1].Front.Record))
                    merged.Add(new PhysicalCard(current.Front,cards[++i].Front));
                else merged.Add(current);
            }
            return merged;
        }
        return cards;
    }

    private static List<CardLine> ExpandGaps(List<CardLine> lines,float allowed,float maximum)
    {
        float spare=Math.Max(0,allowed-lines.Sum(l=>l.HeightMm)-.35f);
        var indexes=Enumerable.Range(0,lines.Count).Where(i=>lines[i].Kind==CardLineKind.Gap &&
            lines.Skip(i+1).Any(l=>l.Kind!=CardLineKind.Gap)).ToArray();
        if(indexes.Length==0 || spare<=0)return lines;
        float grow=Math.Min(maximum,spare/indexes.Length);
        var set=new HashSet<int>(indexes);
        return lines.Select((l,i)=>set.Contains(i)?l with {HeightMm=l.HeightMm+grow}:l).ToList();
    }

    private static List<List<CardLine>> Paginate(IReadOnlyList<CardLine> rows,float capacity,CardAppearance a)
    {
        var result=new List<List<CardLine>>();var current=new List<CardLine>();float used=0;
        var captions=new Dictionary<string,List<CardLine>>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in rows)
            if(row.Kind==CardLineKind.Label)
            {
                if(!captions.TryGetValue(row.Field,out var list))captions[row.Field]=list=[];
                list.Add(row);
            }
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i];
            if(row.Kind==CardLineKind.Qr && !row.QrStrict)
            {
                float remaining=capacity-used;
                float smallest=(float)a.TotpQrMinHeightMm;
                // Prefer the configured size; a QR block is indivisible.
                // It may shrink toward the configured minimum, never below it.
                if(row.FontPt>smallest && row.HeightMm>remaining &&
                   remaining>=smallest+1 && !row.QrInline)
                {
                    float side=Math.Min(row.FontPt,remaining-1);
                    row=row with {FontPt=side,HeightMm=side+1};
                }
                if(row.HeightMm>capacity && !row.QrInline && smallest+1<=capacity)
                    row=row with {FontPt=smallest,HeightMm=smallest+1};
            }
            float need=row.HeightMm;
            if(row.Kind==CardLineKind.Label && i+1<rows.Count && rows[i+1].Field==row.Field)
                need+=rows[i+1].HeightMm;
            if(row.HeightMm>capacity+.001f || need>capacity+.001f)
                throw new InvalidOperationException("Поле не помещается на карточке.");
            if(current.Count>0 && used+need>capacity+.001f)
            {
                while(current.Count>0 && current[^1].Kind==CardLineKind.Gap)
                {used-=current[^1].HeightMm;current.RemoveAt(current.Count-1);}
                if(current.Count>0)result.Add(current);
                current=[];used=0;
            }
            if(row.Kind==CardLineKind.Gap && current.Count==0)continue;
            if(current.Count==0 && result.Count>0 && a.ContinuationRepeatLabel &&
               row.Kind is not (CardLineKind.Label or CardLineKind.Gap or CardLineKind.Section))
            {
                var prior=result[^1].LastOrDefault(l=>l.Kind!=CardLineKind.Gap);
                if(prior is not null && prior.Field==row.Field && captions.TryGetValue(row.Field,out var labelRows))
                {
                    float labelsHeight=labelRows.Sum(l=>l.HeightMm);
                    if(labelsHeight+row.HeightMm>capacity)
                        throw new InvalidOperationException("Заголовок продолжения не помещается.");
                    current.AddRange(labelRows);used+=labelsHeight;
                }
            }
            current.Add(row);used+=row.HeightMm;
        }
        while(current.Count>0 && current[^1].Kind==CardLineKind.Gap)current.RemoveAt(current.Count-1);
        if(current.Count>0)result.Add(current);
        // A decorative-only fragment is never a physical card. A field block
        // must contain a value, code, or QR payload to claim a page number.
        return result.Where(part=>HasPayload(part)).ToList();
    }

    private static string Family(Field f,CardAppearance a) => a.SecretFont;
    private static string Weight(Field f,CardAppearance a)=>f.Rule.ValueWeight=="inherit"?a.ValueWeight:f.Rule.ValueWeight;
    private static float Tracking(Field f,CardAppearance a)=>(float)(f.Rule.ValueTrackingPt??a.ValueTrackingPt);
    private static float Height(float pt,CardAppearance a,float multiplier=1.35f) =>
        Math.Max(2.1f,pt*MmPerPoint*(float)Math.Clamp(a.LineSpacing,.8,2)*multiplier);
    private static string Title(Field f,CardAppearance a) => a.LabelsUpper?f.Rule.Label.ToUpperInvariant():f.Rule.Label;

    // Used by BOTH card layout and drawing. Measure the actual first caption
    // glyphs, so side-aligned QR starts at the visual ink top (not the line box).
    internal static float InlineQrLabelInkTopMm(string text,float fontPt,float rowHeightMm,CardAppearance a)
    {
        if(string.IsNullOrEmpty(text))return 0;
        float fontPx=fontPt*MmPerPoint*CardSkiaRenderer.PixelsPerMillimetre;
        using var font=new SkiaSharp.SKFont(CardTextMetrics.Typeface(a.LabelFont,a.LabelWeight),fontPx);
        font.MeasureText(text,out SkiaSharp.SKRect bounds);
        float top=Math.Min(fontPt*MmPerPoint,rowHeightMm*.82f)+bounds.Top/CardSkiaRenderer.PixelsPerMillimetre;
        return Math.Clamp(top,0,rowHeightMm);
    }

    // Mapping chooses which URL to print. The same selector is used for
    // KeePassXC additional URL attributes, never duplicated in the renderer.
    internal static string ResolveMappedUrl(VaultRecord record, IReadOnlyDictionary<string,FieldRule> map) =>
        UrlSourceSelector.Resolve(record, map.TryGetValue("URL", out var rule) ? rule.UrlSource : "fallback");

    private static List<CardLine> BuildLines(VaultRecord entry,CardAppearance a,
        IReadOnlyDictionary<string,FieldRule> map,float? recoverySize=null)
    {
        var fields=new List<Field>();int stable=0;
        void Add(string name,string? value,bool enabled)
        {
            if(!enabled)return;
            var rule=map.TryGetValue(name,out var r)?r:FieldMappingDefaults.Infer(name);
            // Visibility never turns an empty value into a printable label.
            // In particular, a blank recovery-code field must not produce a
            // caption-only continuation card, even with Visibility="always".
            if(rule.Visibility=="hidden" || string.IsNullOrWhiteSpace(value))return;
            fields.Add(new Field(name,value,rule,stable++));
        }
        Add("GROUP",entry.Group,a.PrintGroup);
        Add("USERNAME",PhoneLoginFormatter.Format(entry.UserName,
            map.TryGetValue("USERNAME",out var loginRule) && loginRule.PhoneFormat),a.PrintUsername);
        Add("PASSWORD",entry.Password,a.PrintPassword);
        Add("URL", ResolveMappedUrl(entry,map),a.PrintUrl);
        if(a.PrintCustom)foreach(var (key,value) in entry.AdditionalFields)Add(key,value,true);
        // Computed field: never persist the unencrypted .passkey data in a template.
        if (a.PrintCustom && PasskeyExport.HasPasskey(entry))
        {
            var passkeyRule=map.TryGetValue(PasskeyExport.FieldName,out var configured)
                ? configured : FieldMappingDefaults.Infer(PasskeyExport.FieldName);
            if (passkeyRule.Visibility!="hidden")
                Add(PasskeyExport.FieldName,PasskeyExport.Create(entry),true);
        }
        Add("NOTES",entry.Notes,a.PrintNotes);
        fields=fields.OrderBy(x=>FieldMappingDefaults.SectionRank(x.Rule.Section)).ThenBy(x=>x.Rule.Order).ThenBy(x=>x.Index).ToList();
        var result=new List<CardLine>();
        string lastSection="";float density=DensityScale(a);
        float width=(float)(a.WidthMm-2*a.PaddingMm);
        float labelPt=(float)a.LabelSizePt*density;
        float labelH=Height(labelPt,a,1.25f);
        // Independently of custom sort order, OTP metadata follows its secret.
        // Metadata without a corresponding secret is still printed normally.
        var consumed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(a.OtpCompact)
            foreach(var secretField in fields.Where(f=>OtpKind(f.Name,f.Rule)=="secret"))
            {
                var family=secretField.Name.StartsWith("HOTP",StringComparison.OrdinalIgnoreCase)?"HOTP":"TOTP";
                foreach(var detail in fields.Where(f=>OtpKind(f.Name,f.Rule)=="meta" &&
                    f.Rule.Section==secretField.Rule.Section &&
                    f.Name.StartsWith(family,StringComparison.OrdinalIgnoreCase)))
                    consumed.Add(detail.Name);
            }
        foreach(var f in fields)
        {
            if(consumed.Contains(f.Name))continue;
            // Create labels and section headings only for fields with content.
            // When a formatting rule yields no printable payload, roll back
            // the entire field block, including its caption and spacing.
            int blockStart=result.Count;
            string previousSection=lastSection;
            string? otpType=OtpKind(f.Name,f.Rule);
            var details=new List<Field>();
            if(a.OtpCompact && otpType=="secret")
            {
                string mode=f.Name.StartsWith("HOTP",StringComparison.OrdinalIgnoreCase)?"HOTP":"TOTP";
                details=fields.Where(other=>other.Index!=f.Index && OtpKind(other.Name,other.Rule)=="meta" &&
                    other.Rule.Section==f.Rule.Section && other.Name.StartsWith(mode,StringComparison.OrdinalIgnoreCase))
                    .OrderBy(other=>other.Rule.Order).ToList();
                foreach(var detail in details)consumed.Add(detail.Name);
            }
            if(a.SectionHeadings && lastSection!=f.Rule.Section)
                result.Add(new CardLine(CardLineKind.Section,SectionLabel(f.Rule.Section),labelH+1,labelPt,Field:f.Name,Section:f.Rule.Section));
            lastSection=f.Rule.Section;
            string label=Title(f,a);
            int beforeFieldLabel=result.Count;
            if(f.Rule.ShowLabel && a.LabelsPosition!="left")
                foreach(var l in CardTextMetrics.WrapExact(label,width,labelPt,a.LabelFont,a.LabelWeight))
                    result.Add(new CardLine(CardLineKind.Label,l,labelH,labelPt,Field:f.Name,Section:f.Rule.Section));
            if (f.Rule.QrMode=="qr")
            {
                AppendGenericQr(result,f,entry,a,width);
                AddGap(result,f,a,density);
                continue;
            }
            if(f.Rule.Role=="codes")
            {
                AddRecovery(result,f,a,width,density,recoverySize);
                if (f.Rule.QrMode=="both") AppendGenericQr(result,f,entry,a,width);
                if(!HasPayload(result,blockStart))
                {
                    result.RemoveRange(blockStart,result.Count-blockStart);
                    lastSection=previousSection;
                    continue;
                }
                AddGap(result,f,a,density);
                continue;
            }
            string value=DisplayValue(f,a);
            string face=Family(f,a),weight=Weight(f,a);
            float tracking=Tracking(f,a);
            bool isNotes=f.Name.Equals("NOTES",StringComparison.OrdinalIgnoreCase)||f.Rule.Role=="multiline";
            float pt=(float)((isNotes && a.NotesMode is "small" or "optional" ? a.NotesSizePt :
                isNotes ? a.ValueSizePt*.93 : a.ValueSizePt) * density*f.Rule.ValueSizePct/100.0);
            var leftLabel=f.Rule.ShowLabel && a.LabelsPosition=="left" && !isNotes ? label:null;
            float dataWidth=leftLabel is null?width:width*(1-Math.Clamp(a.LabelColumnPct,15,55)/100f)-1.2f;
            if(dataWidth<=2)throw new InvalidOperationException("Слишком узкая колонка значений.");
            string? metadata=null;
            if(details.Count>0)
                metadata=string.Join(" · ",details.Select(d=>OtpCaption(d.Name)+": "+d.Value));
            // Unmodified OTP mappings retain legacy QR rendering. Per-field QR
            // settings opt in to the same physical policy used by other fields.
            bool mappedOtp=otpType=="secret" && (f.Rule.QrOtpOverride || f.Rule.QrMode!="text");
            bool otpQrEnabled=otpType=="secret" &&
                (mappedOtp ? f.Rule.QrMode=="both" : a.TotpQrEnabled);
            string otpQrPosition=mappedOtp ? f.Rule.QrPosition : a.TotpQrPosition;
            bool inlineQr=otpQrEnabled && otpQrPosition is "left" or "right" &&
                TryQrUri(entry,f.Name,out _);
            float qrSize=mappedOtp && otpQrEnabled && TryQrUri(entry,f.Name,out var mappedOtpPayload)
                ? QrPrintPolicy.RequiredSide(mappedOtpPayload,f.Rule,width,ContentHeight(a,entry)-1)
                : (float)Math.Clamp(a.TotpQrHeightMm,a.TotpQrMinHeightMm,
                    Math.Min(a.WidthMm-2*a.PaddingMm,a.HeightMm-18));
            float qrTextWidth=width-qrSize-2;
            string[][]? groupedQrRows=null;
            float qrGapMm=0;
            List<string>? textRows=null;
            if(inlineQr && qrTextWidth>=6)
            {
                if(ShouldGroup(f,a,f.Value) && OtpCanGroup(f.Value))
                {
                    int groupSize=f.Rule.GroupSize>=2?f.Rule.GroupSize:a.GroupSize;
                    double groupGap=f.Rule.GroupSpaces>0?f.Rule.GroupSpaces:a.GroupSpaces;
                    groupedQrRows=PackOtpGroups(f.Value,groupSize,groupGap,qrTextWidth,pt,face,weight,
                        tracking,out qrGapMm);
                    // If even two entire groups cannot fit next to QR, a below-text QR is cleaner.
                    if(groupedQrRows is null || groupedQrRows.Any(row=>row.Length<2))inlineQr=false;
                }
                else
                {
                    textRows=CardTextMetrics.WrapExact(f.Value,qrTextWidth,pt,face,weight,tracking);
                    if(textRows.Count>3)inlineQr=false;
                }
            }
            else inlineQr=false;
            // Keep the caption on one line beside the QR. If the text column
            // cannot hold it, preserve the classic full-width label + below QR.
            if(inlineQr && f.Rule.ShowLabel &&
               CardTextMetrics.MeasureMm(label,labelPt,a.LabelFont,a.LabelWeight)>qrTextWidth*1.03f)
                inlineQr=false;
            if(inlineQr && TryQrUri(entry,f.Name,out var qr))
            {
                textRows??=groupedQrRows!.Select(row=>string.Join(" ",row)).ToList();
                // QRCodeData.ModuleMatrix includes a required white scanner quiet
                // zone. Align the first BLACK QR row with the caption's visible
                // ink top: aligning the white outer box is visually incorrect.
                // Keep that white zone intact for reliable scanning.
                result.RemoveRange(beforeFieldLabel,result.Count-beforeFieldLabel);
                var labelRows=f.Rule.ShowLabel ? new List<string>{label} : new List<string>();
                float captionHeight=labelRows.Count*labelH;
                float blackRowFraction=CardSkiaRenderer.QrFirstBlackRowFraction(qr,
                    mappedOtp ? f.Rule.QrEcc : "M");
                float labelInkTop=labelRows.Count>0
                    ? InlineQrLabelInkTopMm(labelRows[0],labelPt,labelH,a):0;
                float blackTop=qrSize*blackRowFraction;
                float textShift=labelRows.Count>0 ? Math.Max(0,blackTop-labelInkTop):0;
                float qrShift=labelRows.Count>0 ? Math.Max(0,labelInkTop-blackTop):0;
                float textHeight=textRows.Count*Height(pt,a);
                var metaRows=OtpMetadataRows(details,qrTextWidth,labelPt,a).ToArray();
                float metaHeight=metaRows.Length*Height(labelPt,a,1.25f);
                float blockHeight=Math.Max(qrShift+qrSize,
                    textShift+captionHeight+textHeight+metaHeight)+1;
                result.Add(new CardLine(CardLineKind.Qr,string.Join("\n",textRows),blockHeight,
                    qrSize,"totp",f.Name,qr,labelRows.Count>0?string.Join("\n",labelRows):null,
                    f.Rule.Section,weight,tracking,null,true,
                    string.Join("\n",metaRows),pt,groupedQrRows,qrGapMm,
                    QrFirstBlackRowFraction:blackRowFraction,
                    QrStrict:mappedOtp,QrPosition:otpQrPosition,
                    QrEcc:mappedOtp ? f.Rule.QrEcc : "M"));
            }
            else
            {
                // TOTP is always grouped at complete boundaries. This also covers a
                // centered/below-value QR or an automatic fallback from an inline QR.
                float normalGapMm=0;
                string[][]? normalOtpGroups=null;
                if(otpType=="secret" && ShouldGroup(f,a,f.Value) && OtpCanGroup(f.Value))
                    normalOtpGroups=PackOtpGroups(f.Value,f.Rule.GroupSize>=2?f.Rule.GroupSize:a.GroupSize,
                        f.Rule.GroupSpaces>0?f.Rule.GroupSpaces:a.GroupSpaces,dataWidth,pt,face,weight,
                        tracking,out normalGapMm);
                // A phone-formatted USERNAME uses the SAME measured fractional gap
                // as secret/TOTP groups. Literal spaces are kept only in diagnostic
                // CardLine.Text; Skia never draws them as full-width glyphs.
                string[][]? phoneRows=null;
                float phoneGapMm=0;
                if(f.Name.Equals("USERNAME",StringComparison.OrdinalIgnoreCase) && f.Rule.PhoneFormat &&
                   PhoneLoginFormatter.PrintableGroups(value) is { Length: > 1 } phoneGroups)
                {
                    // A phone should be compact even with noncompact appearance spacing
                    // preset whose general group gap defaults to 1 full space.
                    // An explicit USERNAME group-spacing override takes priority.
                    double phoneSpacing=f.Rule.GroupSpaces>0?f.Rule.GroupSpaces:
                        Math.Min(a.GroupSpaces,.2);
                    phoneRows=PackMeasuredGroups(phoneGroups,phoneSpacing,
                        dataWidth,pt,face,weight,tracking,out phoneGapMm);
                }
                bool first=true;
                if(phoneRows is not null)
                {
                    foreach(var groups in phoneRows)
                    {
                        result.Add(new CardLine(CardLineKind.Value,string.Join(" ",groups),Height(pt,a),pt,
                            f.Rule.Role,f.Name,Label:first?leftLabel:null,Section:f.Rule.Section,
                            Weight:weight,TrackingPt:tracking,GroupedValue:groups,GroupGapMm:phoneGapMm));
                        first=false;
                    }
                }
                else if(normalOtpGroups is not null)
                {
                    foreach(var groups in normalOtpGroups)
                    {
                        result.Add(new CardLine(CardLineKind.Value,string.Join(" ",groups),Height(pt,a),pt,
                            f.Rule.Role,f.Name,Label:first?leftLabel:null,Section:f.Rule.Section,
                            Weight:weight,TrackingPt:tracking,GroupedValue:groups,GroupGapMm:normalGapMm));
                        first=false;
                    }
                }
                else if(isNotes && FieldMappingDefaults.SupportsProseAlignment(f.Name, f.Rule.Role) && otpType is null &&
                    (FieldMappingDefaults.ResolveHyphenation(f.Rule,a) || FieldMappingDefaults.ResolveAlignment(f.Rule,a) is "center" or "right" or "justify"))
                {
                    foreach(var line in ProseLayout.Wrap(value,dataWidth,pt,face,weight,tracking,
                        FieldMappingDefaults.ResolveHyphenation(f.Rule,a),FieldMappingDefaults.ResolveHyphenLanguage(f.Rule,a)))
                        result.Add(new CardLine(CardLineKind.Value,line.Text,Height(pt,a),pt,f.Rule.Role,f.Name,
                            Section:f.Rule.Section,Weight:weight,TrackingPt:tracking,
                            ProseAlignment:FieldMappingDefaults.ResolveAlignment(f.Rule,a),ParagraphFinal:line.ParagraphFinal));
                }
                else
                {
                    var pieces=isNotes?
                        CardTextMetrics.WrapExact(value,dataWidth,pt,face,weight,tracking).Select(v=>(v,pt)).ToList():
                        CardTextMetrics.Fit(value,dataWidth,pt,face,weight,f.Rule.Fit,a.ShortFitMinPct,a.SplitBoostPct,tracking);
                    foreach(var (str,size) in pieces)
                    {
                        result.Add(new CardLine(CardLineKind.Value,str,Height(size,a),size,f.Rule.Role,f.Name,
                            Label:first?leftLabel:null,Section:f.Rule.Section,Weight:weight,TrackingPt:tracking));
                        first=false;
                    }
                }
                if(metadata is not null)
                    foreach(var line in OtpMetadataRows(details,width,labelPt,a))
                        result.Add(new CardLine(CardLineKind.Meta,line,Height(labelPt,a),labelPt,Field:f.Name,Section:f.Rule.Section));
                if(otpQrEnabled && TryQrUri(entry,f.Name,out var qrUri))
                {
                    // Reuse the QR dimension calculated above for both layout paths.
                    result.Add(new CardLine(CardLineKind.Qr,"",qrSize+1,qrSize,"totp",f.Name,qrUri,
                        Section:f.Rule.Section,QrStrict:mappedOtp,QrPosition:otpQrPosition,
                        QrEcc:mappedOtp ? f.Rule.QrEcc : "M"));
                }
            }
            if (f.Rule.QrMode=="both" && otpType!="secret") AppendGenericQr(result,f,entry,a,width);
            if(!HasPayload(result,blockStart))
            {
                result.RemoveRange(blockStart,result.Count-blockStart);
                lastSection=previousSection;
                continue;
            }
            AddGap(result,f,a,density);
        }
        return result;
    }

    // Generic QR is an indivisible physical block. It never inherits the legacy
    // OTP shrink-to-fit path or its position/text layout.
    private static void AppendGenericQr(List<CardLine> result, Field field, VaultRecord record,
        CardAppearance appearance, float width)
    {
        string payload=field.Value;
        if (field.Rule.Role=="totp" && TryQrUri(record,field.Name,out var otpUri))
            payload=otpUri;
        float size=QrPrintPolicy.RequiredSide(payload,field.Rule,width,ContentHeight(appearance,record));
        result.Add(new CardLine(CardLineKind.Qr,"",size+1,size,field.Rule.Role,
            field.Name,payload,Section:field.Rule.Section,QrStrict:true,
            QrPosition:field.Rule.QrPosition,QrEcc:field.Rule.QrEcc));
    }

    // Only real values contribute to layout, pagination, and page numbering.
    // Captions, section headings, and spacing are not printable payloads.
    private static bool HasPayload(IReadOnlyList<CardLine> lines,int start=0)
    {
        for(int i=start;i<lines.Count;i++)
        {
            var line=lines[i];
            if(line.Kind==CardLineKind.Value &&
               (!string.IsNullOrWhiteSpace(line.Text) ||
                line.GroupedValue?.Any(v=>!string.IsNullOrWhiteSpace(v))==true))
                return true;
            if(line.Kind==CardLineKind.Recovery &&
               line.Cells?.Any(v=>!string.IsNullOrWhiteSpace(v))==true)
                return true;
            if(line.Kind==CardLineKind.Qr && !string.IsNullOrWhiteSpace(line.QrUri))
                return true;
            if(line.Kind==CardLineKind.Meta && !string.IsNullOrWhiteSpace(line.Text))
                return true;
        }
        return false;
    }

    // Preserve preformatted seeds verbatim instead of inserting new group delimiters.
    private static bool OtpCanGroup(string secret) => secret.Length>0 &&
        !secret.Any(c=>char.IsWhiteSpace(c) || c is '-' or '=' or '\uE000');

    // Packs *whole* groups for the narrow column beside an OTP QR.
    // Gaps are measured as a precise fraction of a real space glyph: no U+200A/U+2009
    // fallback, no wrap inside whitespace and no trailing invisible separators.
    internal static string[][]? PackOtpGroups(string secret,int groupSize,double ratio,float widthMm,
        float fontPt,string family,string weight,float trackingPt,out float gapMm)
    {
        gapMm=0;
        if(widthMm<=0 || groupSize<2 || groupSize>16 || secret.Length==0)return null;
        var chars=secret.EnumerateRunes().Select(r=>r.ToString()).ToArray();
        var groups=Enumerable.Range(0,(chars.Length+groupSize-1)/groupSize)
            .Select(i=>string.Concat(chars.Skip(i*groupSize).Take(groupSize))).ToArray();
        if(groups.Length==0)return null;
        gapMm=CardTextMetrics.MeasureMm(" ",fontPt,family,weight)*(float)Math.Clamp(ratio,0,2);
        float[] widths=groups.Select(g=>CardTextMetrics.MeasureMm(g,fontPt,family,weight,trackingPt)).ToArray();
        if(widths.Any(w=>w>widthMm+.0001f))return null;
        var rows=new List<string[]>();
        // Balance the rows: e.g. 8 groups at up to 3 per line -> 3/3/2;
        // 7 groups -> 3/2/2 (not 3/3/1 with a lonely last group).
        float widest=widths.Max();
        int perRow=Math.Max(1,(int)Math.Floor((widthMm+gapMm)/(widest+gapMm)));
        perRow=Math.Min(perRow,groups.Length);
        int rowCount=(groups.Length+perRow-1)/perRow;
        int small=groups.Length/rowCount, extra=groups.Length%rowCount;
        int offset=0;
        for(int i=0;i<rowCount;i++)
        {
            int count=small+(i<extra?1:0);
            if(widths.Skip(offset).Take(count).Sum()+gapMm*(count-1)>widthMm+.0001f)
                return null;
            rows.Add(groups.Skip(offset).Take(count).ToArray());
            offset+=count;
        }
        return rows.ToArray();
    }

    // Phone groups have unequal lengths (+country, (area), subscriber blocks).
    // Measure the glyphs themselves and insert physical gaps, not U+0020 or thin
    // Unicode spaces. For narrow cards choose the fewest rows that fit; within
    // that count, prefer balanced rows over a stranded final group.
    internal static string[][]? PackMeasuredGroups(string[] groups,double ratio,float widthMm,
        float sizePt,string family,string weight,float trackingPt,out float gapMm)
    {
        gapMm=0;
        if(groups.Length is < 2 or > 10 || widthMm<=0 || !double.IsFinite(ratio))return null;
        gapMm=CardTextMetrics.MeasureMm(" ",sizePt,family,weight)*(float)Math.Clamp(ratio,0,2);
        var widths=groups.Select(g=>CardTextMetrics.MeasureMm(g,sizePt,family,weight,trackingPt)).ToArray();
        if(widths.Any(w=>w>widthMm+.0001f))return null;
        string[][]? best=null;
        int bestRows=int.MaxValue;
        double bestScore=double.PositiveInfinity;
        for(int mask=0;mask<(1<<(groups.Length-1));mask++)
        {
            var segments=new List<string[]>();
            var start=0;
            bool fits=true;
            double score=0;
            for(int i=0;i<groups.Length;i++)
            {
                if(i<groups.Length-1 && (mask&(1<<i))==0)continue;
                int count=i-start+1;
                float lineWidth=widths.Skip(start).Take(count).Sum()+gapMm*(count-1);
                if(lineWidth>widthMm+.0001f){fits=false;break;}
                segments.Add(groups[start..(i+1)]);
                double slack=Math.Max(0,widthMm-lineWidth);
                score+=slack*slack;
                if(count==1 && groups.Length>1)score+=widthMm*widthMm;
                start=i+1;
            }
            if(!fits)continue;
            if(segments.Count<bestRows || (segments.Count==bestRows && score<bestScore))
            {
                best=segments.ToArray();bestRows=segments.Count;bestScore=score;
            }
        }
        return best;
    }

    private static IEnumerable<string> OtpMetadataRows(IReadOnlyList<Field> fields,float widthMm,float sizePt,CardAppearance a)
    {
        string current="";
        foreach(var field in fields)
        {
            string part=OtpCaption(field.Name)+": "+field.Value;
            string joined=current.Length==0?part:current+" · "+part;
            if(CardTextMetrics.MeasureMm(joined,sizePt,a.LabelFont,a.LabelWeight)<=widthMm)
            {
                current=joined;
                continue;
            }
            if(current.Length>0){yield return current;current="";}
            if(CardTextMetrics.MeasureMm(part,sizePt,a.LabelFont,a.LabelWeight)<=widthMm)
                current=part;
            else
                foreach(string fragment in CardTextMetrics.WrapExact(part,widthMm,sizePt,a.LabelFont,a.LabelWeight))
                    yield return fragment;
        }
        if(current.Length>0)yield return current;
    }

    private static void AddGap(List<CardLine> lines,Field f,CardAppearance a,float density)
    { if(a.FieldGapPt>0)lines.Add(new CardLine(CardLineKind.Gap,"",(float)a.FieldGapPt*density*MmPerPoint,Field:f.Name)); }

    private static void AddRecovery(List<CardLine> result,Field f,CardAppearance a,float width,float density,float? sizeOverride)
    {
        var codes=f.Value.Replace("\r\n","\n").Replace('\r','\n').Split('\n')
            .Where(s=>!string.IsNullOrWhiteSpace(s)).ToArray();
        if(codes.Length==0)return;

        float size=(sizeOverride??(float)(a.ValueSizePt*.88))*density*f.Rule.ValueSizePct/100f;
        // KeePassXC recovery codes sometimes already contain spaces between digit
        // clusters. Normalize those display separators before applying the user gap.
        static string NormalizeRecoveryCode(string code) =>
            string.Concat(code.Where(ch=>!char.IsWhiteSpace(ch)));
        bool grouped=codes.All(code=>
            ShouldGroup(f,a,NormalizeRecoveryCode(code)) &&
            OtpCanGroup(NormalizeRecoveryCode(code)) &&
            NormalizeRecoveryCode(code).All(char.IsLetterOrDigit));
        int groupSize=f.Rule.GroupSize>=2?f.Rule.GroupSize:a.GroupSize;
        double groupRatio=f.Rule.GroupSpaces>0?f.Rule.GroupSpaces:a.GroupSpaces;
        var display=grouped?codes.Select(NormalizeRecoveryCode).ToArray():codes;
        string weight=Weight(f,a);
        float tracking=Tracking(f,a);

        var maxColumns = Math.Max(1, Math.Min(a.RecoveryColumns, display.Length));
        RecoveryLayout? best = null;
        for (int columns = 1; columns <= maxColumns; columns++)
        {
            var candidate = BuildRecoveryLayout(display, columns, width, size, a, weight, tracking, grouped, groupSize, groupRatio);
            if (candidate is null) continue;
            if (best is null || candidate.TotalHeight < best.TotalHeight - 0.01f ||
                (Math.Abs(candidate.TotalHeight - best.TotalHeight) < 0.01f && candidate.Columns > best.Columns))
            {
                best = candidate;
            }
        }

        if (best is null)
            throw new InvalidOperationException("Коды восстановления не помещаются по ширине.");

        foreach (var row in best.Rows)
        {
            result.Add(new CardLine(CardLineKind.Recovery, "", row.HeightMm, size,
                f.Rule.Role, f.Name, Section:f.Rule.Section, Weight:weight, TrackingPt:tracking,
                Cells:row.Cells, RecoveryGrouped:grouped, RecoveryGroupGapMm:best.GroupGapMm));
        }
    }

    private sealed record RecoveryRow(string[] Cells, float HeightMm);
    private sealed record RecoveryLayout(int Columns, List<RecoveryRow> Rows, float TotalHeight, float GroupGapMm);

    private static RecoveryLayout? BuildRecoveryLayout(string[] display, int columns, float width, float size,
        CardAppearance a, string weight, float tracking, bool grouped, int groupSize, double groupRatio)
    {
        float gapMm = 1.5f;
        float checkboxMm = a.RecoveryCheckboxes ? 2.8f : 0f;
        float textGapMm = a.RecoveryCheckboxes ? 0.45f : 0f;
        float usableWidth = width - gapMm * (columns - 1);
        if (usableWidth <= 0) return null;
        float columnWidth = usableWidth / columns;
        float textWidth = columnWidth - checkboxMm - textGapMm;
        if (textWidth < 6f) return null;

        float actualGroupGap=0;
        var wrapped=new string[display.Length][];
        for(int i=0;i<display.Length;i++)
        {
            if(grouped)
            {
                var groups=PackOtpGroups(display[i],groupSize,groupRatio,textWidth,size,
                    a.SecretFont,weight,tracking,out actualGroupGap);
                if(groups is null)return null;
                wrapped[i]=groups.Select(line=>string.Join(" ",line)).ToArray();
            }
            else wrapped[i]=CardTextMetrics.WrapExact(display[i],textWidth,size,a.SecretFont,weight,tracking).ToArray();
        }
        var count = new int[columns];
        int baseCount = display.Length / columns, extra = display.Length % columns;
        for (int i = 0; i < columns; i++) count[i] = baseCount + (i < extra ? 1 : 0);
        var offsets = new int[columns];
        for (int i = 1; i < columns; i++) offsets[i] = offsets[i - 1] + count[i - 1];

        var rows = new List<RecoveryRow>();
        float totalHeight = 0;
        for (int r = 0; r < count.Max(); r++)
        {
            var rowCells = new string[columns];
            int maxLines = 1;
            for (int col = 0; col < columns; col++)
            {
                if (r < count[col])
                {
                    var lines = wrapped[offsets[col] + r];
                    rowCells[col] = string.Join("\n", lines);
                    if (lines.Length > maxLines) maxLines = lines.Length;
                }
                else rowCells[col] = string.Empty;
            }
            float rowHeight = Math.Max(1, maxLines) * Height(size, a) + .25f;
            rows.Add(new RecoveryRow(rowCells, rowHeight));
            totalHeight += rowHeight;
        }
        return new RecoveryLayout(columns, rows, totalHeight, actualGroupGap);
    }

    private static bool ShouldGroup(Field f,CardAppearance a,string value)
    {
        string mode=f.Rule.GroupMode=="inherit" ? a.PasswordGroupMode : f.Rule.GroupMode;
        if(mode is "off" or "never")return false;
        if(mode is "on" or "always")return true;
        if(f.Rule.Role=="codes")return true;
        if(!a.GroupLongValues || value.Length<a.GroupMinLength || value.Any(char.IsWhiteSpace))return false;
        if(f.Name is "USERNAME" or "URL" or "GROUP" or "NOTES")return false;
        if(mode=="auto" && f.Name=="PASSWORD")
        {
            if(!value.All(c=>c<128) || value.Contains('-') || value.Contains('_') || value.Contains('/'))return false;
            int lower=value.Count(char.IsLower),upper=value.Count(char.IsUpper),digits=value.Count(char.IsDigit);
            if(Math.Min(lower,Math.Min(upper,digits))<2)return false;
            int changes=0;
            for(int i=1;i<value.Length;i++)if(CharClass(value[i])!=CharClass(value[i-1]))changes++;
            return changes>=Math.Max(7,(int)(value.Length*.45));
        }
        return f.Rule.Role is "secret" or "totp" or "bitlocker";
    }
    private static int CharClass(char c)=>char.IsLower(c)?1:char.IsUpper(c)?2:char.IsDigit(c)?3:4;
    public static string GroupText(string value,int groupSize,int minimum,double space=1.0)
    {
        if(groupSize is <2 or >16 || value.Length<minimum || value.Length<=groupSize ||
           value.Any(c=>char.IsWhiteSpace(c)||c=='-'||c=='') ||
           Regex.IsMatch(value,@"\A[^@\s]+@[^@\s]+\.[^@\s]+\z"))return value;
        static string Sep(double s)
        {
            if (s <= 0.01) return string.Empty;
            if (s < 0.15) return " ";
            if (s < 0.30) return "  ";
            if (s < 0.45) return " ";
            if (s < 0.60) return "  ";
            if (s < 0.85) return " ";
            if (s < 1.15) return " ";
            if (s < 1.40) return "  ";
            if (s < 1.75) return "  ";
            return "  ";
        }
        var separator=Sep(space);
        var chars=value.EnumerateRunes().Select(r=>r.ToString()).ToArray();
        return string.Join(separator,Enumerable.Range(0,(chars.Length+groupSize-1)/groupSize)
            .Select(i=>string.Concat(chars.Skip(i*groupSize).Take(groupSize))));
    }
    private static string DisplayValue(Field f,CardAppearance a)
    {
        var value=f.Name.Equals("URL",StringComparison.OrdinalIgnoreCase) || f.Rule.UrlMode!="full" ?
            DisplayUrl(f.Value,f.Rule.UrlMode):f.Value;
        return ShouldGroup(f,a,value)?GroupText(value,f.Rule.GroupSize>=2?f.Rule.GroupSize:a.GroupSize,
                a.GroupMinLength,f.Rule.GroupSpaces>0?f.Rule.GroupSpaces:a.GroupSpaces):value;
    }
    public static string DisplayUrl(string input,string mode)
    {
        if(mode=="full" || !Uri.TryCreate(input,UriKind.Absolute,out var uri))return input;
        string host=uri.Host;
        if(mode=="domain"||uri.HostNameType!=UriHostNameType.Dns)return host;
        var labels=host.Split('.').ToList();
        var prefixes=DomainPrefixStore.Load();
        int suffix=(host.EndsWith(".co.uk",StringComparison.OrdinalIgnoreCase)||
                    host.EndsWith(".com.au",StringComparison.OrdinalIgnoreCase))?3:2;
        while(labels.Count>suffix && prefixes.Contains(labels[0],StringComparer.OrdinalIgnoreCase))labels.RemoveAt(0);
        return string.Join('.',labels);
    }
    private static string SectionLabel(string section)=>section switch
    {"account"=>"ОСНОВНОЕ","auth"=>"ДОСТУП","recovery"=>"ВОССТАНОВЛЕНИЕ",_=>"ДОПОЛНИТЕЛЬНО"};
    private static string? OtpKind(string name,FieldRule rule)
    {
        if(rule.Role!="totp")return null;
        var match=Regex.Match(name,@"^(TOTP|HOTP)[ _-]+(Secret(?: \(.+\))?|Account|Issuer|Algorithm|Digits|Period|Counter|Encoder|Parameters|Settings)$",RegexOptions.IgnoreCase);
        return match.Success?(match.Groups[2].Value.StartsWith("Secret",StringComparison.OrdinalIgnoreCase)?"secret":"meta"):null;
    }
    private static string OtpCaption(string key)=>key.Split(' ',2).Last() switch
    { "Account"=>"Аккаунт", "Issuer"=>"Сервис", "Algorithm"=>"Алгоритм",
      "Digits"=>"Цифр", "Period"=>"Период", "Counter"=>"Счётчик", _=>key };
    private static bool TryQrUri(VaultRecord record,string secretField,out string uri)
    {
        uri="";var f=record.AdditionalFields;
        if(!f.TryGetValue(secretField,out var secret)||string.IsNullOrWhiteSpace(secret))return false;
        secret=Regex.Replace(secret.ToUpperInvariant(),@"[\s=-]","");
        if(!Regex.IsMatch(secret,@"\A[A-Z2-7]{8,}\z"))return false;
        var prefix=secretField.StartsWith("HOTP",StringComparison.OrdinalIgnoreCase)?"HOTP":"TOTP";
        string Read(string name,string fallback)=>f.GetValueOrDefault(prefix+" "+name)??fallback;
        string digits=Read("Digits","6"),period=Read("Period","30"),algo=Read("Algorithm","SHA1");
        if(!int.TryParse(digits,out int d)||d is <4 or >10||
           !int.TryParse(period,out int p)||p is <1 or >3600)return false;
        algo=Regex.Replace(algo.ToUpperInvariant(),"[^A-Z0-9]","");
        if(algo is not ("SHA1" or "SHA256" or "SHA512"))return false;
        if(Read("Encoder","").Equals("Steam",StringComparison.OrdinalIgnoreCase))return false;
        string issuer=Read("Issuer", ""),account=Read("Account",record.UserName);
        if(account.Length==0)account=record.Title;
        string label=issuer.Length>0&&!account.StartsWith(issuer+":",StringComparison.Ordinal)?issuer+":"+account:account;
        uri="otpauth://totp/"+Uri.EscapeDataString(label)+"?secret="+secret+
             (issuer.Length>0?"&issuer="+Uri.EscapeDataString(issuer):"")+
             "&algorithm="+algo+"&digits="+d+"&period="+p;
        return true;
    }
}
