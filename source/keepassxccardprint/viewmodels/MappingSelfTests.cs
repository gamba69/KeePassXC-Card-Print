using KeePassXCCardPrint.Services;

namespace KeePassXCCardPrint.ViewModels;

// No vault input or personal configuration. These tests run in the published
// Native AOT EXE and exercise only synthetic mapping rules/editor state.
public static class MappingSelfTests
{
    public static string Report { get; private set; } = "";
    public static int Run()
    {
        int checks = 0;
        var log = new System.Text.StringBuilder();
        void Check(bool condition, string label)
        {
            log.AppendLine($"[{(condition ? "PASS" : "FAIL")}] mapping/{++checks:D3}: {label}");
            if (!condition) throw new InvalidOperationException(label);
        }
        try
        {
            var rule=FieldMappingDefaults.Infer("PASSWORD");
            var editor=new MappingEditor("ru");
            editor.Load("PASSWORD",rule);
            editor.SetEditable(false);
            Check(!editor.Enabled, "built-in template mapping editor stays read-only");
            editor.SetEditable(true);
            Check(editor.Enabled, "personal template mapping editor becomes editable");
            Check(!editor.SupportsAlignment, "password does not offer prose alignment");
            var proseEditor = new MappingEditor("en");
            proseEditor.Load("NOTES",FieldMappingDefaults.Infer("NOTES"));
            Check(proseEditor.SupportsAlignment && proseEditor.ToRule().ValueAlignment == "inherit", "legacy notes inherit alignment");
            proseEditor.Alignment = proseEditor.Alignments.Single(x => x.Code == "justify");
            var proseRule = proseEditor.ToRule();
            Check(proseRule.ValueAlignment == "justify", "field-specific alignment is persisted");
            var appearance = new CardAppearance { NotesAlignment = "right" };
            Check(FieldMappingDefaults.ResolveAlignment(proseRule, appearance) == "justify" &&
                FieldMappingDefaults.ResolveAlignment(new FieldRule(), appearance) == "right", "explicit alignment overrides template, inherit follows template");
            var proseDraft = proseEditor.CaptureDraft();
            proseEditor.Load("NOTES",new FieldRule { Role = "multiline" });
            proseEditor.RestoreDraft(proseDraft);
            Check(proseEditor.ToRule().ValueAlignment == "justify", "alignment survives incomplete mapping drafts");
            using (var oldMapping = System.Text.Json.JsonDocument.Parse("{\"mapping\":{\"NOTES\":{\"Role\":\"multiline\"}}}"))
                Check(CardAppearanceStore.ParseMapping(oldMapping.RootElement)["notes"].ValueAlignment == "inherit", "old template mapping inherits alignment");
            using (var newMapping = System.Text.Json.JsonDocument.Parse("{\"mapping\":{\"NOTES\":{\"ValueAlignment\":\"center\"}}}"))
                Check(CardAppearanceStore.ParseMapping(newMapping.RootElement)["notes"].ValueAlignment == "center", "template JSON preserves alignment override");
            Check(proseEditor.ToRule().ValueHyphenation == "inherit" && proseEditor.ToRule().ValueHyphenLanguage == "inherit",
                "old multiline rules inherit both word hyphenation settings");
            Check(new[] { "inherit", "auto", "ru", "uk", "en-us" }.All(code=>proseEditor.HyphenLanguages.Any(x=>x.Code==code)),
                "mapping language picker includes inherited, auto and bundled choices");
            proseEditor.Hyphenation=proseEditor.HyphenationModes.Single(x=>x.Code=="on");
            proseEditor.HyphenLanguage=proseEditor.HyphenLanguages.Single(x=>x.Code=="uk");
            var hyphenRule=proseEditor.ToRule();
            appearance.NotesHyphenation=false; appearance.NotesHyphenLanguage="ru";
            Check(FieldMappingDefaults.ResolveHyphenation(hyphenRule,appearance) &&
                FieldMappingDefaults.ResolveHyphenLanguage(hyphenRule,appearance)=="uk", "field enables hyphenation and overrides language independently");
            Check(!FieldMappingDefaults.ResolveHyphenation(new FieldRule(),appearance) &&
                FieldMappingDefaults.ResolveHyphenLanguage(new FieldRule(),appearance)=="ru", "inherited field follows template");
            proseEditor.Hyphenation=proseEditor.HyphenationModes.Single(x=>x.Code=="off");
            appearance.NotesHyphenation=true;
            Check(!FieldMappingDefaults.ResolveHyphenation(proseEditor.ToRule(),appearance), "field disables template hyphenation");
            var hyphenDraft=proseEditor.CaptureDraft();
            proseEditor.Load("NOTES",new FieldRule { Role="multiline" });
            proseEditor.RestoreDraft(hyphenDraft);
            Check(proseEditor.ToRule().ValueHyphenation=="off" && proseEditor.ToRule().ValueHyphenLanguage=="uk", "hyphenation overrides survive drafts");
            proseEditor.Load("NOTES",new FieldRule { Role="multiline", ValueHyphenLanguage="zz-future" });
            proseEditor.RefreshHyphenLanguages();
            Check(proseEditor.ToRule().ValueHyphenLanguage=="zz-future", "missing future language remains selected across package refresh");
            proseEditor.Relocalize("uk");
            Check(proseEditor.HyphenationTitle=="Переноси слів" && proseEditor.HyphenLanguages.Single(x=>x.Code=="auto").Label=="Автоматично", "mapping hyphenation choices are localized");
            using(var oldHyphenMapping=System.Text.Json.JsonDocument.Parse("{\"mapping\":{\"NOTES\":{\"Role\":\"multiline\"}}}"))
            {
                var oldRule=CardAppearanceStore.ParseMapping(oldHyphenMapping.RootElement)["notes"];
                Check(oldRule.ValueHyphenation=="inherit" && oldRule.ValueHyphenLanguage=="inherit", "legacy JSON inherits hyphenation");
            }
            using(var newHyphenMapping=System.Text.Json.JsonDocument.Parse("{\"mapping\":{\"NOTES\":{\"ValueHyphenation\":\"on\",\"ValueHyphenLanguage\":\"de-1996\"}}}"))
            {
                var newRule=CardAppearanceStore.ParseMapping(newHyphenMapping.RootElement)["notes"];
                Check(newRule.ValueHyphenation=="on" && newRule.ValueHyphenLanguage=="de-1996", "portable template retains explicit hyphenation and language");
            }
            var invalidHyphen=FieldMappingDefaults.Normalize(new FieldRule { ValueHyphenation="bad",ValueHyphenLanguage="../escape" });
            Check(invalidHyphen.ValueHyphenation=="inherit" && invalidHyphen.ValueHyphenLanguage=="inherit", "unsafe override values normalized");
            Check(!FieldMappingDefaults.SupportsProseAlignment("PASSWORD","multiline") && !FieldMappingDefaults.SupportsProseAlignment("TOTP Secret","multiline"), "technical values cannot use prose overrides");
            Check(CardAppearanceStore.IsValidPersonalTemplateName("My template 2"),
                "personal template rename accepts valid names");
            Check(!CardAppearanceStore.IsValidPersonalTemplateName("../unsafe") &&
                !CardAppearanceStore.IsValidPersonalTemplateName("CON") &&
                !CardAppearanceStore.IsValidPersonalTemplateName(" ") &&
                !CardAppearanceStore.IsValidPersonalTemplateName(new string('a', 65)),
                "rename rejects traversal, reserved, empty and oversized names");
            using (var embedded = System.Text.Json.JsonDocument.Parse(
                "{\"mapping\":{\"USERNAME\":{\"Role\":\"secret\",\"Order\":7}}}"))
            {
                var map = CardAppearanceStore.ParseMapping(embedded.RootElement);
                Check(map.ContainsKey("username") && map["username"].Order == 7,
                    "personal template embeds field rules");
            }
            using (var missing = System.Text.Json.JsonDocument.Parse("{}"))
                Check(CardAppearanceStore.ParseMapping(missing.RootElement).Count == 0,
                    "missing mapping uses field inference, never a global file");
            Check(editor.ToRule().Order==rule.Order,"default field loads");
            editor.Order="27";
            Check(editor.ToRule().Order==27,"valid priority applies");
            editor.SizePct="81";
            Check(editor.ToRule().ValueSizePct==81,"valid size applies");
            editor.TrackingPt="0,2";
            Check(editor.ToRule().ValueTrackingPt==0.2,"decimal comma supported");
            editor.Order="-";
            bool invalid=false;
            try { editor.ToRule(); }catch(ArgumentException) {invalid=true;}
            Check(invalid,"partial negative number rejected");
            editor.Validate("Invalid priority");
            Check(editor.OrderInvalid && editor.HasValidationError,"invalid input flagged");
            var draft=editor.CaptureDraft();
            editor.Load("USERNAME",FieldMappingDefaults.Infer("USERNAME"));
            editor.RestoreDraft(draft);
            Check(editor.Order=="-" && editor.SizePct=="81","incomplete draft survives field switch");
            editor.Order="33";
            editor.Validate(null);
            Check(editor.ToRule().Order==33 && !editor.HasValidationError,"corrected draft valid");
            editor.GroupSize="1";
            invalid=false;
            try {editor.ToRule();}catch(ArgumentException){invalid=true;}
            Check(invalid,"invalid group size rejected");
            editor.GroupSize="4";
            editor.GroupSpaces="0.05";
            invalid=false;
            try {editor.ToRule();}catch(ArgumentException){invalid=true;}
            Check(invalid,"invalid spacing rejected");
            editor.GroupSpaces="0.2";
            Check(editor.ToRule().GroupSpaces==0.2,"corrected grouping valid");
            var usernameRule = FieldMappingDefaults.Infer("USERNAME");
            editor.Load("USERNAME", usernameRule);
            Check(!editor.PhoneFormat, "telephone formatting defaults off");
            editor.PhoneFormat = true;
            Check(editor.ToRule().PhoneFormat, "telephone option persisted in field rule");
            Check(PhoneLoginFormatter.Format("+380000000000", true) == "+380 (00) 000 00 00", "international Ukrainian phone formatted");
            Check(PhoneLoginFormatter.Format("login@example.invalid", true) == "login@example.invalid", "email remains untouched");
            Check(PhoneLoginFormatter.Format("+380000000000", false) == "+380000000000", "disabled phone formatting is non-destructive");
            Check(PhoneLoginFormatter.Format("+380 (00) 000 00 00", true) == "+380 (00) 000 00 00", "phone formatting is idempotent");
            var row=new FieldMappingRow("PASSWORD",rule,1,"ru");
            row.HasDraftError=true;
            Check(row.PositionText.StartsWith("!",StringComparison.Ordinal),"table flags invalid field");
            row.HasDraftError=false;
            Check(row.PositionText=="1","table clears invalid flag");
            editor.Load("PASSWORD",rule);
            editor.Order="33";
            row.Update(editor.ToRule(),2,"ru");
            Check(row.Position==2 && row.Order==33,"table can refresh in-place");
            editor.Validate("stale validation message");
            editor.SetEditable(false);
            editor.ValidateForEditing(false);
            Check(!editor.Enabled && !editor.HasValidationError,
                "built-in read-only editor does not show false validation errors");
            editor.SetEditable(true);
            editor.Order="-";
            editor.ValidateForEditing(true);
            Check(editor.HasValidationError && editor.OrderInvalid,
                "personal editable field still reports invalid numeric drafts");
            log.AppendLine($"[PASS] mapping/summary: {checks} assertions");
            Report = log.ToString();
            return 0;
        }
        catch(Exception e)
        {
            if (log.Length == 0 || !log.ToString().EndsWith($"[FAIL] mapping/{checks:D3}: {e.Message}{Environment.NewLine}", StringComparison.Ordinal))
                log.AppendLine("[FAIL] mapping/runtime: " + e.GetType().Name);
            Report = log.ToString();
            return 9;
        }
    }
}
