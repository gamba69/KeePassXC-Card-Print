namespace KeePassXCCardPrint.Rendering;

/// <summary>
/// Positions a decorative emblem and the caption+rules as one group. All inputs
/// and outputs are in raster pixels. No credentials or card data enter this plan.
/// </summary>
internal static class CardBackArtworkLayout
{
    internal readonly record struct Plan(float IconCenterY,float IconSize,float CaptionTopY,float Gap);

    internal static Plan Measure(float safeTop,float safeBottom,float requestedIconSize,
        float captionHeight,float requestedGap,string verticalPosition,bool showIcon,bool showCaption)
    {
        float height=Math.Max(0,safeBottom-safeTop);
        float margin=Math.Min(10f,height*.025f);
        float available=Math.Max(0,height-2*margin);
        float caption=showCaption?Math.Min(Math.Max(0,captionHeight),available):0;
        float icon=showIcon?Math.Min(Math.Max(0,requestedIconSize),available):0;
        float gap=showIcon && showCaption?Math.Max(0,requestedGap):0;

        // Preserve text before reducing the icon, never overlap the caption.
        if(showIcon && showCaption)
        {
            gap=Math.Min(gap,Math.Max(0,available-caption));
            icon=Math.Min(icon,Math.Max(0,available-caption-gap));
            if(icon<2f) { icon=0;gap=0; }
        }
        float blockHeight=caption+icon+(icon>0?gap:0);
        float free=Math.Max(0,available-blockHeight);
        // Top/center/bottom positions move the WHOLE composition, not just
        // the emblem. The caption cannot be hit by a repositioned emblem.
        float alignment=verticalPosition switch {"top"=>.36f,"bottom"=>.64f,_=>.5f};
        float start=safeTop+margin+free*alignment;
        float textTop=start+(icon>0?icon+gap:0);
        return new Plan(start+icon*.5f,icon,textTop,gap);
    }
}
