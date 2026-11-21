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
