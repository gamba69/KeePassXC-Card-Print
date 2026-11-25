using System.Text.RegularExpressions;

namespace KeePassXCCardPrint.Services;

/// <summary>Conservative field-name inference rules.</summary>
public static class FieldMappingDefaults
{
    public static int SectionRank(string value) => value switch
    { "account"=>0,"auth"=>1,"recovery"=>2,"extra"=>3,_=>3 };
    public static FieldRule Infer(string name)
    {
        var raw=name.Trim(); var k=Regex.Replace(raw,@"[ _-]","").ToUpperInvariant();
        var rules=new Dictionary<string,(string Label,string Role,string Section,int Order)>(StringComparer.Ordinal)
        {
            ["USERNAME"]=("Логин","secret","account",10),
            ["PASSWORD"]=("Пароль","secret","auth",20),
            ["URL"]=("Адрес","text","account",30),
            ["GROUP"]=("Группа","text","account",5),
            ["NOTES"]=("Комментарий","multiline","extra",900),
            ["DEVICE"]=("Устройство","text","account",15),
            ["VOLUME"]=("Том","text","account",18),
            ["SERIALNUMBER"]=("Серийный номер","secret","account",19),
            ["STARTUPPIN"]=("PIN запуска","secret","auth",21),
            ["MEMORYPASSWORD"]=("Дополнительный пароль","secret","auth",35),
            ["RECOVERYKEY"]=("Ключ восстановления","bitlocker","recovery",40),
            ["RECOVERYKEYID"]=("ID ключа","secret","recovery",39),
            ["RECOVERYCODES"]=("Коды восстановления","codes","recovery",60),
            ["BACKUPCODES"]=("Коды восстановления","codes","recovery",60),
            ["OTP"]=("Секрет OTP","totp","auth",50)
        };
        if(rules.TryGetValue(k,out var known))return new FieldRule
        { Label=known.Label,Role=known.Role,Section=known.Section,Order=known.Order,
          ShowLabel=k!="URL",UrlMode=k=="URL"?"smart":"full",Fit=k=="URL"?"shrink":"wrap" };
        if(Regex.IsMatch(raw,@"(?:backup|recovery)[ _-]*codes?",RegexOptions.IgnoreCase))
            return new FieldRule{ Label="Коды восстановления",Role="codes",Section="recovery",Order=60 };
        var otp=Regex.Match(raw,@"^(totp|hotp)[ _-]+(secret(?: \(.+\))?|account|issuer|algorithm|digits|period|counter|encoder|parameters|settings)$",RegexOptions.IgnoreCase);
        if(otp.Success)
        {
            var category=otp.Groups[2].Value.Split(" (")[0].ToLowerInvariant();
            var ranking=new Dictionary<string,int>{{"secret",50},{"account",51},{"issuer",52},{"algorithm",53},{"digits",54},{"period",55},{"counter",56},{"encoder",57},{"parameters",58},{"settings",59}};
            return new FieldRule {Label=raw,Role="totp",Section="auth",Order=ranking.GetValueOrDefault(category,59)};
        }
        if(raw.Contains("otp",StringComparison.OrdinalIgnoreCase)) return new FieldRule {Label=raw,Role="totp",Section="auth",Order=50};
        if(new[]{"password","token","secret","passphrase","pin","парол","секрет"}.Any(part=>raw.Contains(part,StringComparison.OrdinalIgnoreCase)))
            return new FieldRule {Label=raw,Role="secret",Section="auth",Order=80};
        return new FieldRule {Label=raw,Role="text",Section="extra",Order=500};
    }
    internal static bool SupportsProseAlignment(string field, string role) =>
        (role == "multiline" || (role == "text" && field.Equals("NOTES", StringComparison.OrdinalIgnoreCase))) &&
        !field.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) &&
        !field.Equals("USERNAME", StringComparison.OrdinalIgnoreCase) &&
        !field.Equals("URL", StringComparison.OrdinalIgnoreCase) &&
        !field.StartsWith("TOTP", StringComparison.OrdinalIgnoreCase) &&
        !field.StartsWith("HOTP", StringComparison.OrdinalIgnoreCase);
    internal static string NormalizeAlignment(string? value) => value is "left" or "center" or "right" or "justify" ? value : "inherit";
    internal static string ResolveAlignment(FieldRule rule, CardAppearance appearance)
    {
        string value = NormalizeAlignment(rule.ValueAlignment);
        return value == "inherit" ? appearance.NotesAlignment : value;
    }

    internal static string NormalizeHyphenation(string? value) => value is "on" or "off" ? value : "inherit";
    internal static string NormalizeHyphenLanguage(string? value) => value == "auto" ||
        (value != "inherit" && HyphenationPackages.ValidCode(value)) ? value! : "inherit";
    internal static bool ResolveHyphenation(FieldRule rule, CardAppearance appearance) =>
        NormalizeHyphenation(rule.ValueHyphenation) switch
        { "on" => true, "off" => false, _ => appearance.NotesHyphenation };
    internal static string ResolveHyphenLanguage(FieldRule rule, CardAppearance appearance)
    {
        string value = NormalizeHyphenLanguage(rule.ValueHyphenLanguage);
        if (value == "inherit") value = NormalizeHyphenLanguage(appearance.NotesHyphenLanguage);
        return value == "inherit" ? "auto" : value;
    }

    public static FieldRule Normalize(FieldRule r)
    {
        var label = r.Label ?? string.Empty;
        r.Label = label.Length > 120 ? label[..120] : label;
        if(!new[]{"text","secret","codes","bitlocker","totp","multiline"}.Contains(r.Role))r.Role="text";
        if(!new[]{"account","auth","recovery","extra"}.Contains(r.Section))r.Section="extra";
        if(!new[]{"nonempty","always","hidden"}.Contains(r.Visibility))r.Visibility="nonempty";
        if(!new[]{"full","domain","smart"}.Contains(r.UrlMode))r.UrlMode="full";
        if(!new[]{"main","fallback","extra"}.Contains(r.UrlSource))r.UrlSource="fallback";
        if(!new[]{"wrap","shrink","split","smart"}.Contains(r.Fit))r.Fit="wrap";
        if(!FontWeights.IsRecognized(r.ValueWeight))r.ValueWeight="inherit";
        r.ValueAlignment=NormalizeAlignment(r.ValueAlignment);
        r.ValueHyphenation=NormalizeHyphenation(r.ValueHyphenation);
        r.ValueHyphenLanguage=NormalizeHyphenLanguage(r.ValueHyphenLanguage);
        if(!new[]{"inherit","on","off","auto"}.Contains(r.GroupMode))r.GroupMode="inherit";
        r.Order=Math.Clamp(r.Order,-9999,9999);
        r.ValueSizePct=Math.Clamp(r.ValueSizePct,65,160);
        r.GroupSize=r.GroupSize is >=2 and <=16?r.GroupSize:0;
        r.GroupSpaces=double.IsFinite(r.GroupSpaces) && r.GroupSpaces>=.1 && r.GroupSpaces<=2?r.GroupSpaces:0;
        r.ValueTrackingPt= r.ValueTrackingPt is double tracking && double.IsFinite(tracking)?Math.Clamp(tracking,-.5,1.5):null;
        return r;
    }
}
