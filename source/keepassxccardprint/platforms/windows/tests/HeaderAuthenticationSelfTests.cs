using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.Rendering;

internal static class HeaderAuthenticationSelfTests
{
    internal static void Run(Action<bool,string> check)
    {
        var a=new CardAppearance { WidthMm=86,HeightMm=90,EntryIconMode="none",HeaderHeightMm=22 };
        var record=new VaultRecord { Title="Synthetic header", Group="Examples" };
        var empty=CardHeaderLayout.Measure(a,record);
        check(empty.Page is not null && empty.Auth is null,"empty record shows only its page number");
        record.AdditionalFields["TOTP Account"]="fixture";
        check(CardHeaderLayout.AuthenticationText(record)=="","OTP metadata alone does not indicate a credential");
        record.AdditionalFields["TOTP Secret"]="fixture";
        record.AdditionalFields["TOTP Secret (2)"]="fixture";
        record.AdditionalFields["KPEX_PASSKEY_CREDENTIAL_ID"]="fixture";
        record.AdditionalFields["KPEX_PASSKEY_CREDENTIAL_ID (2)"]="fixture";
        check(CardHeaderLayout.AuthenticationText(record)=="TOTP/PASSKEY","credential types are deduplicated");
        record.AdditionalFields["HOTP Secret"]="fixture";
        check(CardHeaderLayout.AuthenticationText(record)=="TOTP/HOTP/PASSKEY","authentication order is deterministic");
        var both=CardHeaderLayout.Measure(a,record);
        var page=both.Page ?? throw new InvalidOperationException("missing page row");
        var auth=both.Auth ?? throw new InvalidOperationException("missing auth row");
        check(auth.Text=="TOTP/HOTP/PASSKEY" && auth.Y>=page.Y+page.Height,
            "one authentication row below page number");
        check(Math.Abs(auth.Y-page.Y-page.Height-a.HeaderInfoGapMm)<.002,
            "configured gap between indicator rows");
        check(Math.Abs((page.Y+auth.Y+auth.Height)*.5-(.9+both.DecorationBottom)*.5)<.002,
            "combined indicator rows are centered");
        check(both.Title.X+both.Title.Width<=both.BadgeX+.001 && both.BadgeWidth>empty.BadgeWidth,
            "title does not overlap wider authentication row");
        a.HeaderInfoVertical="top";
        var top=CardHeaderLayout.Measure(a,record);
        a.HeaderInfoVertical="bottom";
        var bottom=CardHeaderLayout.Measure(a,record);
        check(top.Page!.Y<page.Y && bottom.Page!.Y>page.Y,"manual header height supports top and bottom alignment");
        a.HeaderInfoVertical="center";
        a.PageBadgeShow=false;
        var authOnly=CardHeaderLayout.Measure(a,record);
        check(authOnly.Page is null && authOnly.Auth is not null &&
            Math.Abs(authOnly.Auth.Y+authOnly.Auth.Height*.5-(.9+authOnly.DecorationBottom)*.5)<.002,
            "auth-only row is independently centered");
        a.AuthBadgeShow=false;
        var none=CardHeaderLayout.Measure(a,record);
        check(none.Page is null && none.Auth is null && none.BadgeWidth==0 && none.Title.Width>authOnly.Title.Width,
            "disabling both rows releases their entire width");
        a.PageBadgeShow=true;
        var pageOnly=CardHeaderLayout.Measure(a,record);
        check(pageOnly.Auth is null && pageOnly.Page is not null && Math.Abs(pageOnly.BadgeWidth-empty.BadgeWidth)<.001,
            "auth disabled restores original page column");
        a.AuthBadgeShow=true;
        a.WidthMm=42;a.PaddingMm=2;
        var narrow=CardHeaderLayout.Measure(a,record);
        check(narrow.Auth is not null && narrow.Title.X+narrow.Title.Width<=narrow.BadgeX+.001,
            "narrow header preserves single authentication line and title bounds");
        // The default values must remain independent of mapping and old template files.
        var defaults=new CardAppearance();
        check(defaults.PageBadgeShow && defaults.AuthBadgeShow && defaults.HeaderInfoVertical=="center",
            "new indicator defaults preserve page number and show authentication");
        var keys=AppearanceDescriptor.Create(a).Select(f=>f.Key).ToArray();
        TemplateEditorLayout.Validate(keys);
        check(keys.Contains("auth_badge_show") && keys.Contains("page_badge_show"),"both switches are registered in template editor");
    }
}
