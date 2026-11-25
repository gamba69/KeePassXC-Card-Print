using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Single header geometry source for both pagination and Skia drawing.
/// All distances are in millimetres. No KDBX field leaves memory.</summary>
internal static class CardHeaderLayout
{
    private const float PtToMm = CardTextMetrics.MmPerPoint;

    internal sealed record TextBlock(string[] Lines, float X, float Y, float Width,
        float LineHeight, float SizePt, string Font, string Weight);

    internal sealed record InfoRow(string Text, float Y, float Height, float SizePt, string Font, string Weight);
    internal sealed record Plan(float RowY, float RowHeight, float DecorationBottom, float ContentY,
        float IconX, float IconY, float IconSize, float BadgeX, float BadgeWidth,
        TextBlock Title, TextBlock? Group, InfoRow? Page, InfoRow? Auth);

    // Inspect only credential presence, never render a secret or count individual credentials.
    internal static string AuthenticationText(VaultRecord record)
    {
        bool totp=false, hotp=false, passkey=false;
        foreach(var (key,value) in record.AdditionalFields)
        {
            if(string.IsNullOrWhiteSpace(value))continue;
            if(System.Text.RegularExpressions.Regex.IsMatch(key,
                @"^TOTP[ _-]+Secret(?: \(.+\))?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))totp=true;
            if(System.Text.RegularExpressions.Regex.IsMatch(key,
                @"^HOTP[ _-]+Secret(?: \(.+\))?$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))hotp=true;
            if(key.Equals("OTP",StringComparison.OrdinalIgnoreCase) ||
               key.Equals("TOTP",StringComparison.OrdinalIgnoreCase) || key.Equals("HOTP",StringComparison.OrdinalIgnoreCase))
            {
                if(value.StartsWith("otpauth://totp/",StringComparison.OrdinalIgnoreCase))totp=true;
                if(value.StartsWith("otpauth://hotp/",StringComparison.OrdinalIgnoreCase))hotp=true;
            }
            if(PasskeyExport.IsPasskeyAttribute(key) &&
               (key.Contains("CREDENTIAL_ID",StringComparison.OrdinalIgnoreCase) ||
                key.Contains("PRIVATE_KEY",StringComparison.OrdinalIgnoreCase)))passkey=true;
        }
        return string.Join("/",new[]{totp?"TOTP":"",hotp?"HOTP":"",passkey?"PASSKEY":""}.Where(x=>x.Length>0));
    }

    private static string Inherit(string? value,string fallback) =>
        string.IsNullOrWhiteSpace(value) || value=="inherit" ? fallback : value;
    private static float Safe(double value,double fallback,double min,double max) =>
        (float)(double.IsFinite(value)?Math.Clamp(value,min,max):fallback);

    internal static Plan Measure(CardAppearance a, VaultRecord record)
    {
        // The text row is inset from the card's top border. Its bottom margin must
        // mirror the top margin, otherwise title/icon/badge appear too high.
        const float decorationInset = 0.9f;
        float top = (float)Math.Clamp(a.HeaderTopMm, decorationInset, 20);
        float pad = (float)Math.Clamp(a.PaddingMm, 0, 15);
        float width = (float)a.WidthMm - pad * 2;
        float density = CardLayoutEngine.DensityScale(a);
        bool iconVisible = a.EntryIconMode != "none" && a.EntryIconPrintMode != "none" &&
                           !(a.EntryIconMode == "custom" && record.CustomIconPng is null);
        float icon = iconVisible ? (float)Math.Clamp(a.EntryIconSizeMm, 0, 13) : 0;
        float iconReserve = icon > 0 ? icon + 1.2f : 0;
        float titleSize = (float)Math.Clamp(a.TitleSizePt * density, 2, 25);
        float titleStep = Math.Max(1.2f, titleSize * PtToMm * 1.45f);
        float groupSize = (float)Math.Clamp(a.HeaderGroupSizePt * density, 2, 24);
        float groupStep = Math.Max(.9f, groupSize * PtToMm * 1.35f);
        string authText = a.AuthBadgeShow ? AuthenticationText(record) : "";
        float chipSize = Safe(a.FooterSizePt,5.6,2,15);
        float authSize = Safe(a.AuthBadgeSizePt,5.5,2,15);
        string pageFont=Inherit(a.PageBadgeFont,a.LabelFont), pageWeight=Inherit(a.PageBadgeWeight,a.LabelWeight);
        string authFont=Inherit(a.AuthBadgeFont,a.LabelFont), authWeight=Inherit(a.AuthBadgeWeight,a.LabelWeight);
        float chipWidth = a.PageBadgeShow ? CardTextMetrics.MeasureMm("88/88",chipSize,pageFont,pageWeight)+2f : 0;
        float authWidth = authText.Length>0 ? CardTextMetrics.MeasureMm(authText,authSize,authFont,authWeight)+2f : 0;
        float infoWidth = Math.Max(chipWidth,authWidth);
        float chipHeight = a.PageBadgeShow ? chipSize*PtToMm*1.5f : 0;
        float authHeight = authText.Length>0 ? authSize*PtToMm*1.5f : 0;
        float infoGap = chipHeight>0 && authHeight>0 ? Safe(a.HeaderInfoGapMm,.5,0,10) : 0;
        float infoHeight = chipHeight+infoGap+authHeight;
        float badgeGap = infoWidth>0 ? Safe(a.HeaderInfoTitleGapMm,1.2,0,10) : 0;
        float left = pad + iconReserve;
        float right = pad + width - infoWidth - badgeGap;
        float available = right - left;
        if (available < 7f)
            throw new InvalidOperationException("Для заголовка карточки недостаточно ширины.");

        bool hasGroup = a.HeaderGroupShow && !string.IsNullOrWhiteSpace(record.Group);
        bool leftGroup = hasGroup && a.HeaderGroupPosition == "left";
        float groupGap = (float)Math.Clamp(a.HeaderGroupGapMm, 0, 10);
        var groupFont = string.IsNullOrWhiteSpace(a.HeaderGroupFont) ? a.LabelFont : a.HeaderGroupFont;
        string title = string.IsNullOrWhiteSpace(record.Title) ? "БЕЗ НАЗВАНИЯ" : record.Title;
        string[] titleLines;
        string[] groupLines = [];
        float titleX = left, titleWidth = available;
        float groupX = left, groupWidth = available;
        if (leftGroup)
        {
            float columnPercent = (float)Math.Clamp(a.HeaderGroupColumnPct, 15, 55) / 100f;
            groupWidth = Math.Min(available * columnPercent, Math.Max(7f, available - 7f - groupGap));
            titleX += groupWidth + groupGap;
            titleWidth = available - groupWidth - groupGap;
            if (titleWidth < 7f)
                throw new InvalidOperationException("Группа оставляет слишком мало места для названия.");
            groupLines = CardTextMetrics.WrapExact(record.Group, groupWidth, groupSize,
                groupFont, a.HeaderGroupWeight).ToArray();
        }
        else if (hasGroup)
        {
            groupLines = CardTextMetrics.WrapExact(record.Group, groupWidth, groupSize,
                groupFont, a.HeaderGroupWeight).ToArray();
        }
        titleLines = CardTextMetrics.WrapExact(title, titleWidth, titleSize,
            a.TitleFont, a.TitleWeight).ToArray();
        float titleHeight = titleLines.Length * titleStep;
        float groupHeight = groupLines.Length * groupStep;
        float combinedHeight = hasGroup && !leftGroup ? groupHeight + groupGap + titleHeight : titleHeight;
        float rowHeight = Math.Max(Math.Max(icon, infoHeight), Math.Max(combinedHeight, leftGroup ? groupHeight : 0));
        // Keep the automatic height as the lower bound; a manually requested height
        // adds symmetric free space, without clipping the title/icon/badge or group.
        float autoHeight = rowHeight + 2 * (top - decorationInset);
        float selectedHeight = (float)Math.Clamp(a.HeaderHeightMm, 0, 50);
        float expandedRowTop = top + Math.Max(0, selectedHeight - autoHeight) * .5f;
        float titleTop = expandedRowTop + (rowHeight - combinedHeight) * .5f +
                         (hasGroup && !leftGroup ? groupHeight + groupGap : 0);
        float groupTop = leftGroup ? expandedRowTop + (rowHeight - groupHeight) * .5f
                                   : expandedRowTop + (rowHeight - combinedHeight) * .5f;
        float iconTop = expandedRowTop + (rowHeight - icon) * .5f;
        var titleBlock = new TextBlock(titleLines, titleX, titleTop, titleWidth, titleStep,
            titleSize, a.TitleFont, a.TitleWeight);
        TextBlock? groupBlock = hasGroup ? new TextBlock(groupLines, groupX, groupTop,
            groupWidth, groupStep, groupSize, groupFont, a.HeaderGroupWeight) : null;
        // visual header height depends ONLY on its content, not on the gap to fields.
        // The inset is symmetric: row center == decorated background center.
        float decorationBottom = decorationInset + Math.Max(autoHeight, selectedHeight);
        // The following gap is outside the background/stripe. Do not let it
        // change the header fill height or the title/icon/badge center line.
        float contentY = decorationBottom + (float)Math.Clamp(a.HeaderGapMm, 0, 40);
        // Align against the complete inner header, including any manual extra height.
        float infoY = a.HeaderInfoVertical switch
        {
            "top" => top,
            "bottom" => decorationBottom-(top-decorationInset)-infoHeight,
            _ => expandedRowTop+(rowHeight-infoHeight)*.5f
        };
        InfoRow? page = chipHeight>0 ? new("",infoY,chipHeight,chipSize,pageFont,pageWeight) : null;
        InfoRow? auth = authHeight>0 ? new(authText,infoY+chipHeight+infoGap,authHeight,authSize,authFont,authWeight) : null;
        return new Plan(expandedRowTop, rowHeight, decorationBottom, contentY, pad, iconTop, icon,
            pad + width - infoWidth, infoWidth, titleBlock, groupBlock,page,auth);
    }
}
