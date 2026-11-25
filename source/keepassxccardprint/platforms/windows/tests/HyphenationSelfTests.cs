using Meziantou.Framework;
using KeePassXCCardPrint.Services;
using KeePassXCCardPrint.Rendering;

namespace KeePassXCCardPrint.Rendering;

internal static class HyphenationSelfTests
{
    internal static void Run(Action<bool, string> check)
    {
        // The build and runtime loader must agree on every embedded name,
        // including exception files and upstream source/license notices.
        string[] expectedResources =
        [
            "Hyphenation.hyph-ru.pat.txt",
            "Hyphenation.hyph-ru.hyp.txt",
            "Hyphenation.hyph-ru.tex",
            "Hyphenation.hyph-uk.pat.txt",
            "Hyphenation.hyph-uk.tex",
            "Hyphenation.hyph-en-us.pat.txt",
            "Hyphenation.hyph-en-us.hyp.txt",
            "Hyphenation.hyph-en-us.tex",
        ];
        var bundledResources = typeof(HyphenationPackages).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Hyphenation.", StringComparison.Ordinal)).ToArray();
        check(bundledResources.Length == expectedResources.Length &&
            expectedResources.All(name => bundledResources.Contains(name, StringComparer.Ordinal)),
            "all eight bundled hyphenation resources have exact unique manifest names");
        var upstreamFiles = new Dictionary<string,string>
        {
            ["txt/hyph-ru.pat.txt"] = "pattern", ["txt/hyph-ru.hyp.txt"] = "exceptions", ["tex/hyph-ru.tex"] = "source"
        };
        check(HyphenationPackages.SameFiles("ru", upstreamFiles,"pattern","exceptions","source") &&
            !HyphenationPackages.SameFiles("ru", upstreamFiles,"pattern",null,"source") &&
            !HyphenationPackages.SameFiles("ru", upstreamFiles,"changed","exceptions","source"),
            "update detection compares actual pattern, exceptions and source blobs rather than repository revision");
        var offlineRoot = Path.Combine(Path.GetTempPath(), "CardPrint-package-table-" + Guid.NewGuid().ToString("N"));
        var bundled = HyphenationPackages.Installed(offlineRoot);
        check(bundled.Length == 3 && bundled.All(x => x.Ready && x.Size > 0 && x.Pin is null && x.UpdateAvailable is null),
            "offline installed table contains exactly three verified bundled languages and no false update status");
        check(bundled.All(x => !x.NeedsRestore && !x.CanUpdate && !x.CanRemoveOverride),
            "up-to-date bundled rows have no irrelevant actions");
        var sample = bundled[0];
        var update = sample with { UpdateAvailable = true };
        check(update.CanUpdate && !update.NeedsRestore && !update.CanRemoveOverride,
            "bundled update exposes only update action");
        var pin = new HyphenationPackages.Pin(new string('a',40), new string('b',64), null, new string('c',64));
        var missing = sample with { Pin = pin, Ready = false, UpdateAvailable = true };
        check(missing.NeedsRestore && !missing.CanUpdate && missing.CanRemoveOverride,
            "missing pinned package restores exact revision and never exposes concurrent update");
        var installed = sample with { Pin = pin, Ready = true, UpdateAvailable = false };
        check(!installed.NeedsRestore && !installed.CanUpdate && installed.CanRemoveOverride,
            "ready pinned package exposes only remove or reset action");
        check(bundled.Any(x => x.Language.Code == "ru" && x.Version == "2003/03/10") &&
              bundled.Any(x => x.Language.Code == "en-us" && x.Version == "2005-05-30"),
            "bundled author version strings remain verbatim, not synthesized from revision dates");
        // Exercise the actual CardLayoutEngine branch, not just the mapping resolver.
        var record = new KeePassXCCardPrint.Models.VaultRecord
        { Uuid = Guid.Parse("a1111111-2222-3333-4444-555555555555"), Title = "Prose", Notes = "12345 67890" };
        var appearance = new CardAppearance { NotesAlignment = "left", NotesHyphenation = false, NotesHyphenLanguage = "auto", EntryIconMode = "none", NotesMode = "normal", PrintNotes = true };
        var field = FieldMappingDefaults.Infer("NOTES");
        field.ValueHyphenation = "on"; field.ValueHyphenLanguage = "auto";
        var mapping = new Dictionary<string, FieldRule>(StringComparer.OrdinalIgnoreCase) { ["NOTES"] = field };
        CardLine[] NotesLines() => CardLayoutEngine.Build([record], appearance, mapping)
            .SelectMany(card => new[] { card.Front, card.Continuation }.Where(part => part is not null).SelectMany(part => part!.Lines))
            .Where(line => line.Kind == CardLineKind.Value && line.Field.Equals("NOTES", StringComparison.OrdinalIgnoreCase)).ToArray();
        var enabledLines = NotesLines();
        check(enabledLines.Length > 0 && enabledLines.All(line => line.ProseAlignment == "left"),
            "per-field enable reaches prose layout while template hyphenation is off");
        appearance.NotesHyphenation = true;
        field.ValueHyphenation = "off"; field.ValueHyphenLanguage = "zz-future";
        var disabledLines = NotesLines();
        check(disabledLines.Length > 0 && disabledLines.All(line => line.ProseAlignment is null),
            "per-field disable bypasses prose hyphenation while template is on");
        field.ValueAlignment = "justify";
        var alignedLines = NotesLines();
        check(alignedLines.Length > 0 && alignedLines.All(line => line.ProseAlignment == "justify"),
            "alignment remains independent of disabled hyphenation and missing language");
        var engine = Hyphenator.Parse("hy3ph he2n", "hy-phen-ation");
        var lines = ProseLayout.Break("hyphenation\nnext", 7, x => x.Length, (_, _) => engine);
        check(lines[0].Text == "hyphen-" && !lines[0].ParagraphFinal, "hyphenation fits visible hyphen within line width");
        check(lines[1].Text == "ation" && lines[1].ParagraphFinal && lines[2].ParagraphFinal, "paragraph boundaries survive wrapping");
        var technical = ProseLayout.Break("https://example.org", 7, x => x.Length, (_, _) => throw new InvalidOperationException());
        check(string.Concat(technical.Select(x => x.Text)) == "https://example.org", "technical tokens are not hyphenated");
        check(ProseLayout.Offset("center", 10, 6) == 2 && ProseLayout.Offset("right", 10, 6) == 4, "prose horizontal alignment offsets");
        check(ProseLayout.ExpandableSpaces("  one two  ") == 1, "justification ignores outer whitespace");
        check(HyphenationPackages.ResolveAuto("українська", "українська") == "uk" &&
              HyphenationPackages.ResolveAuto("объект", "объект") == "ru" &&
              HyphenationPackages.ResolveAuto("information", "information") == "en-us" &&
              HyphenationPackages.ResolveAuto("мова", "мова") is null, "automatic language selection is conservative for ambiguous Cyrillic");
        var root = Path.Combine(Path.GetTempPath(), "CardPrint-hyphenation-" + Guid.NewGuid().ToString("N"));
        foreach (var lang in HyphenationPackages.CatalogFor(root).Where(x => x.BuiltIn))
            check(HyphenationPackages.Get(root, lang.Code, new()).PatternCount > 0, "bundled rules are usable offline: " + lang.Code);
        var discovered = HyphenationPackages.Discover(new Dictionary<string, string>
        {
            ["txt/hyph-zz-future.pat.txt"] = "blob", ["tex/hyph-zz-future.tex"] = "blob",
            ["txt/hyph-zz-incomplete.pat.txt"] = "blob",
            ["txt/hyph-../escape.pat.txt"] = "blob", ["tex/hyph-../escape.tex"] = "blob"
        });
        check(discovered.Length == 1 && discovered[0].Code == "zz-future" && !discovered[0].BuiltIn,
            "upstream discovery accepts new languages without an application release and rejects incomplete or unsafe entries");
        check(HyphenationPackages.CatalogFor(root).Any(x => x.Code == "af") &&
              HyphenationPackages.CatalogFor(root).Any(x => x.Code == "zh-latn-pinyin"), "offline catalog is not limited to common European languages");
        check(!HyphenationPackages.ValidCode("../escape") && !HyphenationPackages.ValidCode("com1") && !HyphenationPackages.ValidCode("auto"),
            "portable package codes cannot be paths, Windows devices, or the auto selector");
        var unknownPin = "{\"version\":1,\"packages\":{\"zz-future\":{\"revision\":\"" + new string('a',40) +
            "\",\"pattern\":\"" + new string('b',64) + "\",\"exceptions\":null,\"source\":\"" + new string('c',64) + "\"}}}";
        using var future = System.Text.Json.JsonDocument.Parse(unknownPin);
        check(HyphenationPackages.Parse(future.RootElement).ContainsKey("zz-future"), "cloud pins accept safe future codes without a refreshed catalog");
        var options = HyphenationPackages.Options(discovered[0], "% hyphenmins:\n%     typesetting:\n%         left: 3\n%         right: 4\n");
        check(options.LeftHyphenMin == 3 && options.RightHyphenMin == 4, "language-specific typesetting minima read from upstream source");
        check(ProseLayout.IsProseWord("हिन्दी") && ProseLayout.IsProseWord("Ελληνικά") && !ProseLayout.IsProseWord("ABC123"),
            "Unicode prose includes uncased letters and combining marks, but excludes technical identifiers");
        using var json = System.Text.Json.JsonDocument.Parse("{\"version\":1,\"packages\":{}}");
        check(HyphenationPackages.Parse(json.RootElement).Count == 0, "empty portable package configuration");
    }
}
