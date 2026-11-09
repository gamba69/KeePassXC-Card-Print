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

    internal sealed record Plan(float RowY, float RowHeight, float DecorationBottom, float ContentY,
        float IconX, float IconY, float IconSize, float BadgeX, float BadgeWidth,
        TextBlock Title, TextBlock? Group);

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
        float chipSize = (float)Math.Clamp(a.FooterSizePt, 2, 15);
        float chipWidth = CardTextMetrics.MeasureMm("88/88", chipSize, a.LabelFont, a.LabelWeight) + 2.0f;
        float badgeGap = 1.2f;
        float left = pad + iconReserve;
        float right = pad + width - chipWidth - badgeGap;
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
        float chipHeight = chipSize * PtToMm * 1.5f;
        float rowHeight = Math.Max(Math.Max(icon, chipHeight), Math.Max(combinedHeight, leftGroup ? groupHeight : 0));
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
        return new Plan(expandedRowTop, rowHeight, decorationBottom, contentY, pad, iconTop, icon,
            pad + width - chipWidth, chipWidth, titleBlock, groupBlock);
    }
}
