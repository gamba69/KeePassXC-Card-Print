namespace KeePassXCCardPrint.Services;

internal enum PreviewTransitionReason { Rebuild, Navigation, SideChange }

/// <summary>
/// Explicit preview-only animation rules. No print operations or render requests
/// are initiated by these methods. Keeping the policy pure makes it testable.
/// </summary>
internal static class PreviewTransitionPolicy
{
    internal static bool ShouldAnimate(UiSettings settings,PreviewTransitionReason reason,
        bool staleNoticeAppeared,bool permitted=true)
    {
        if(!permitted || !settings.PreviewAnimation)return false;
        return reason switch
        {
            PreviewTransitionReason.Rebuild => staleNoticeAppeared && settings.PreviewAnimateAfterDelay,
            PreviewTransitionReason.Navigation => settings.PreviewAnimateNavigation,
            PreviewTransitionReason.SideChange => settings.PreviewAnimateSideChange,
            _ => false
        };
    }

    // Smoothstep, with a true 0 -> 1 opacity transition over the configured
    // elapsed time, independent of render completion and display refresh rate.
    internal static double OldOpacity(double progress)
    {
        double t=Math.Clamp(progress,0,1);
        return 1 - t*t*(3-2*t);
    }
}
