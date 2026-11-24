using KeePassXCCardPrint.Platforms;
using KeePassXCCardPrint.Platforms.Windows.Printing;
using Avalonia;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Services;
using SkiaSharp;

namespace KeePassXCCardPrint.Rendering;

/// <summary>Deterministic offline contract tests run against the *published Native AOT EXE*.
/// All fixtures are artificial. Nothing is written to disk, including rendered PNGs.</summary>
internal static class CardEngineSelfTests
{
    private static int _checks;
    private static readonly StringBuilder _testLog = new();
    private static string _lastSuccessfulCheck = "initialization";
    internal static string Report {get;private set;}="NOT RUN";
    private static void Check(bool pass,string description)
    {
        var testNumber = ++_checks;
        _testLog.AppendLine($"[{(pass ? "PASS" : "FAIL")}] layout/{testNumber:D4}: {description}");
        if(!pass)throw new InvalidOperationException("LAYOUT_TEST_FAILED: "+description);
        _lastSuccessfulCheck=description;
    }
    private static VaultRecord Record(string title="Sample") =>new()
    {
        Uuid=Guid.Parse("11111111-2222-3333-4444-555555555555"),Title=title,Group="Examples",
        UserName="example-user",Password="TEST_ONLY_000000",Url="https://login.example.invalid/auth",
        AdditionalFields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    };
    private static CardAppearance Defaults()=>new() {ShowDate=false,EntryIconMode="none"};
    private static readonly Dictionary<string,FieldRule> BasicMapping=new(StringComparer.OrdinalIgnoreCase);
    private static List<CardLine> All(IReadOnlyList<PhysicalCard> cards) =>cards.SelectMany(p=>
        new[]{p.Front,p.Continuation}.Where(part=>part is not null).SelectMany(part=>part!.Lines)).ToList();
    private static byte[] Picture(PhysicalCard c,CardAppearance a,bool back=false)
    {
        var png=CardSkiaRenderer.Render(c,a,back);
        using var sk=SKBitmap.Decode(png);
        if (sk is null) throw new InvalidOperationException("SCREEN_TEST_BITMAP_MISSING");
        Check(sk.Width==(int)Math.Round(a.WidthMm*7.5) &&
              sk.Height==(int)Math.Round(a.HeightMm*7.5),"screen dimensions");
        return png;
    }
    private static void LayoutFits(IReadOnlyList<PhysicalCard> cards,CardAppearance a)
    {
        foreach(var card in cards)
        foreach(var part in new[]{card.Front,card.Continuation}.Where(p=>p is not null))
        {
            float h=CardLayoutEngine.ContentHeight(a,part!.Record);
            Check(part.Lines.Sum(l=>l.HeightMm)<=h+.001f,"no vertical overflow");
            Check(!part.Lines.Any(l=>l.Kind==CardLineKind.Recovery && l.Cells is null),"recovery atomic cells");
        }
    }
    private static void TestPublicKdbxHeaderMetadata()
    {
        // Artificial public headers only: no keys, passwords, salts or database files.
        static byte[] Fixture(int major, string cipherHex, string? kdfHex)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(0x9AA2D903u);
            writer.Write(0xB54BFB67u);
            writer.Write((ushort)(major == 4 ? 1 : 1));
            writer.Write((ushort)major);
            void Field(byte id, byte[] data)
            {
                writer.Write(id);
                if (major == 3) writer.Write((ushort)data.Length);
                else writer.Write(data.Length);
                writer.Write(data);
            }
            Field(2, Convert.FromHexString(cipherHex));
            if (major == 4 && kdfHex is not null)
            {
                using var values = new MemoryStream();
                using (var variants = new BinaryWriter(values, Encoding.UTF8, leaveOpen: true))
                {
                    variants.Write((ushort)0x0100);
                    variants.Write((byte)0x42);
                    variants.Write(5); // "$UUID" in UTF-8.
                    variants.Write(Encoding.UTF8.GetBytes("$UUID"));
                    variants.Write(16);
                    variants.Write(Convert.FromHexString(kdfHex));
                    variants.Write((byte)0);
                }
                Field(11, values.ToArray());
            }
            Field(0, major == 4 ? new byte[] {13, 10, 13, 10} : Array.Empty<byte>());
            writer.Flush();
            return stream.ToArray();
        }
        const string aesCipher = "31C1F2E6BF714350BE5805216AFC5AFF";
        const string chachaCipher = "D6038A2B8B6F4CB5A524339A31DBB59A";
        const string argon2id = "9E298B1956DB4773B23DFC3EC6F0A1E6";
        const string argon2d = "EF636DDF8C29444B91F7A9A403E30A0C";
        const string aesKdf = "C9D9F39A628A4460BF740D08C18A4FEA";
        using var kdbx3 = new MemoryStream(Fixture(3, aesCipher, null));
        var old = KdbxHeaderInfo.Read(kdbx3);
        Check(old.Cipher == "AES-256" && old.Kdf == "AES-KDF", "public KDBX3 header identifiers");
        using var modern = new MemoryStream(Fixture(4, chachaCipher, argon2id));
        var current = KdbxHeaderInfo.Read(modern);
        Check(current.Cipher == "ChaCha20" && current.Kdf == "Argon2id", "public KDBX4 header identifiers");
        using var argonD = new MemoryStream(Fixture(4, aesCipher, argon2d));
        Check(KdbxHeaderInfo.Read(argonD).Kdf == "Argon2d", "Argon2d must not be called Argon2id");
        using var aes = new MemoryStream(Fixture(4, aesCipher, aesKdf));
        Check(KdbxHeaderInfo.Read(aes).Kdf == "AES-KDF", "KDBX4 AES-KDF label");
        using var unknown = new MemoryStream(Fixture(4, new string('A', 32), new string('F', 32)));
        var unsupported = KdbxHeaderInfo.Read(unknown);
        Check(unsupported.Cipher == "?" && unsupported.Kdf == "?", "unknown algorithms are never guessed");
    }

    private static void TestXpsPathSyntax()
    {
        // Exact XPS mini-language assertions. Unlike SVG, XPS requires x,y
        // comma-delimited points, and specifies winding/odd-even with F1/F0.
        using var pathBuilder = new SKPathBuilder { FillType = SKPathFillType.Winding };
        pathBuilder.MoveTo(1,2);
        pathBuilder.LineTo(-3,4);
        pathBuilder.QuadTo(5,6,7,8);
        pathBuilder.CubicTo(-1,2,3,4,5,6);
        pathBuilder.Close();
        using var path = pathBuilder.Detach();
        Check(XpsPathSerializer.Serialize(path) ==
              "F1 M 1,2 L -3,4 Q 5,6 7,8 C -1,2 3,4 5,6 Z",
            "Skia geometry is converted to XPS comma-paired path grammar");
        path.FillType = SKPathFillType.EvenOdd;
        Check(XpsPathSerializer.Serialize(path).StartsWith("F0 M 1,2", StringComparison.Ordinal),
            "XPS contour serializer preserves even-odd filling");
        path.FillType = SKPathFillType.InverseWinding;
        try
        {
            _ = XpsPathSerializer.Serialize(path);
            Check(false,"XPS must reject unbounded inverse-filling paths");
        }
        catch(NotSupportedException) { Check(true,"XPS rejects unsupported inverse fills"); }
    }

    private static void TestSharedGlyphRuns()
    {
        // All strings below are fabricated fixtures, never vault data.
        const string family = "Atkinson Hyperlegible Mono";
        const string secret = "0O1lA7";
        const float tracking = 1.25f;
        using var layout = ShapedTextEngine.CreateLayout(secret, 18f, family, "400",
            tracking, markZero: true);
        Check(layout.Runs.Count == secret.Length && layout.ZeroMarks.Count == 1,
            "shared glyph layout splits only at tracked grapheme boundaries");
        Check(layout.Runs.All(r => r.Glyphs.Length == r.Positions.Length &&
              r.Glyphs.Length > 0 && r.Clusters.Length == 0),
            "literal secrets have glyph positions and no HarfBuzz substitutions");
        Check(layout.Runs[0].Glyphs[0] != layout.Runs[1].Glyphs[0],
            "secret digit zero and letter O remain distinct glyph IDs");
        float measured = ShapedTextEngine.Measure(secret, 18f, family, "400", tracking);
        Check(Math.Abs(measured - layout.Width) < .0001f,
            "measurement uses the same shaped glyph advances as drawing");
        float untracked = ShapedTextEngine.Measure(secret, 18f, family, "400");
        Check(Math.Abs((layout.Width - untracked) - (secret.Length - 1) * tracking) < .01f,
            "grapheme tracking adds only the intended inter-character gaps");

        // The same shaped layout must produce the same glyph IDs and exact
        // positions in Skia and XPS. XPS prefers embedded-font Glyphs; if a
        // font cannot be embedded, it uses vector Path outlines instead.
        // Never make this test dependent on only one representation.
        using var bitmap = new SKBitmap(500, 100);
        using var skCanvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        var skia = new SkiaCardCanvas(skCanvas);
        skCanvas.Clear(SKColors.White);
        skia.DrawGlyphRuns(layout, 10, 35, paint);
        using var xps = new XpsCardCanvas(PaperGeometry.Resolve("A4"));
        xps.DrawGlyphRuns(layout, 10, 35, paint);
        byte[] xpsMarkup = xps.Complete();

        // Inspect the FixedPage semantically. One Glyphs with an explicit
        // Indices value represents a glyph, just as one transformed Path does.
        // Other paths (e.g. the optional zero marker) are not text glyphs.
        var emitted = new List<(double X, double Y, int? GlyphId, string? OutlineData)>();
        using (var markupStream = new MemoryStream(xpsMarkup, writable: false))
        using (var reader = System.Xml.XmlReader.Create(markupStream,
                   new System.Xml.XmlReaderSettings
                   {
                       DtdProcessing = System.Xml.DtdProcessing.Prohibit,
                       XmlResolver = null
                   }))
        {
            static double ParseCoordinate(string? raw, string label)
            {
                if (raw is null ||
                    !double.TryParse(raw,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsed) || !double.IsFinite(parsed))
                    throw new InvalidOperationException(
                        "LAYOUT_TEST_FAILED: invalid XPS " + label + ": " + (raw ?? "(missing)"));
                return parsed;
            }

            while (reader.Read())
            {
                if (reader.NodeType != System.Xml.XmlNodeType.Element) continue;
                if (reader.LocalName == "Glyphs")
                {
                    string? fontUri = reader.GetAttribute("FontUri");
                    Check(!string.IsNullOrWhiteSpace(fontUri), "XPS Glyphs has FontUri");
                    // The #index suffix selects a face within a collection;
                    // resource names themselves never contain '#'.
                    string fontPart = fontUri!.Split('#')[0];
                    Check(xps.Fonts.TryGetValue(fontPart, out var fontBytes) &&
                          fontBytes is { Length: >= 64 },
                        "XPS Glyphs references a nonempty embedded font resource");
                    Check(ParseCoordinate(reader.GetAttribute("FontRenderingEmSize"), "font size") > 0,
                        "XPS Glyphs has a positive font rendering size");
                    string? indices = reader.GetAttribute("Indices");
                    Check(int.TryParse(indices,
                              System.Globalization.NumberStyles.None,
                              System.Globalization.CultureInfo.InvariantCulture,
                              out int glyphId) && glyphId >= 0,
                        "XPS Glyphs contains an explicit nonnegative glyph ID");
                    emitted.Add((
                        ParseCoordinate(reader.GetAttribute("OriginX"), "glyph OriginX"),
                        ParseCoordinate(reader.GetAttribute("OriginY"), "glyph OriginY"),
                        glyphId, null));
                }
                else if (reader.LocalName == "Path")
                {
                    string? transform = reader.GetAttribute("RenderTransform");
                    if (transform is null) continue;
                    string? data = reader.GetAttribute("Data");
                    Check(!string.IsNullOrEmpty(data),
                        "XPS outlined glyph contains nonempty vector path data");
                    string[] matrix = transform.Split(',', StringSplitOptions.TrimEntries);
                    Check(matrix.Length == 6,
                        "XPS outlined glyph has a six-value transform");
                    var components = new double[6];
                    for (int i = 0; i < components.Length; i++)
                        components[i] = ParseCoordinate(matrix[i], "glyph transform");
                    Check(components[0] == 1 && components[1] == 0 &&
                          components[2] == 0 && components[3] == 1,
                        "XPS outlined glyph has an identity linear transform");
                    emitted.Add((components[4], components[5], null, data));
                }
            }
        }
        var expectedGlyphs = layout.Runs.SelectMany(run =>
            run.Positions.Select((point, i) =>
                (X: (double)(10f + run.OriginX + point.X),
                 Y: (double)(35f + point.Y),
                 GlyphId: (int)run.Glyphs[i],
                 Font: run.Font))).ToArray();
        Check(emitted.Count == expectedGlyphs.Length,
            $"XPS emits exactly one Glyphs or vector Path per synthetic glyph " +
            $"(expected {expectedGlyphs.Length}, actual {emitted.Count}; " +
            $"embedded {xps.EmbeddedGlyphCount}, outlined {xps.OutlinedGlyphCount})");
        Check(xps.EmbeddedGlyphCount + xps.OutlinedGlyphCount == expectedGlyphs.Length,
            "XPS text-renderer glyph counters match the shaped text");
        Check(xps.Images.Count == 0,
            "XPS text is never rasterized into an image resource");

        const double coordinateTolerance = 0.0001; // design units, not millimetres
        for (int i = 0; i < expectedGlyphs.Length; i++)
        {
            var expected = expectedGlyphs[i];
            var actual = emitted[i];
            Check(Math.Abs(expected.X - actual.X) <= coordinateTolerance &&
                  Math.Abs(expected.Y - actual.Y) <= coordinateTolerance,
                $"XPS shared glyph {i} positioning: " +
                $"expected ({expected.X:F6},{expected.Y:F6}), " +
                $"actual ({actual.X:F6},{actual.Y:F6})");
            if (actual.GlyphId.HasValue)
            {
                Check(actual.GlyphId.Value == expected.GlyphId,
                    $"XPS embedded glyph {i}: expected Glyph ID {expected.GlyphId}, " +
                    $"actual {actual.GlyphId.Value}");
            }
            else
            {
                using var expectedOutline = expected.Font.GetGlyphPath(
                    checked((ushort)expected.GlyphId));
                Check(expectedOutline is not null && !expectedOutline.IsEmpty,
                    $"XPS outlined glyph {i} has a valid Skia source contour");
                Check(actual.OutlineData == XpsPathSerializer.Serialize(expectedOutline!),
                    $"XPS outlined glyph {i} preserves the shaped glyph geometry");
            }
        }
        Check(!Encoding.UTF8.GetString(xpsMarkup).Contains(secret, StringComparison.Ordinal),
            "synthetic test secret is never serialized as plain Unicode page text");
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(xpsMarkup);

        using var shaped = ShapedTextEngine.CreateLayout("Office", 17f,
            "Atkinson Hyperlegible Next", "600");
        Check(shaped.Runs.Count == 1 && shaped.Runs[0].Clusters.Length ==
              shaped.Runs[0].Glyphs.Length && shaped.Width > 0,
            "HarfBuzz produces one shared glyph run with cluster mapping");
        using var cyrillic = ShapedTextEngine.CreateLayout("Пример", 17f,
            "Atkinson Hyperlegible Next", "600");
        Check(cyrillic.Runs.Count > 0 && cyrillic.Width > 0,
            "Cyrillic shaping and optional font fallback have nonzero width");
        using var fallback = ShapedTextEngine.CreateLayout("0A\u0301B", 16f,
            family, "400", tracking);
        Check(fallback.Runs.Count == 3,
            "combining marks remain attached when tracking between graphemes");
    }

    public static int Run()
    {
        _checks = 0;
        _testLog.Clear();
        try
        {
            // Host-contract smoke test: no printers, registry writes or vaults are touched.
            var platform = PlatformServices.Current;
            Check(platform.PlatformLabel == "Windows", "Windows host registered before card code");
            Check(platform.ResourceDirectory == AppContext.BaseDirectory,
                "Windows bundled-font resources preserve original folder");
            Check(platform.FileNameComparison == StringComparison.OrdinalIgnoreCase,
                "Windows path comparison preserves case-insensitive template names");
            Check(platform.WindowFrame == new WindowFrameInsets(24, 64),
                "Window chrome keeps the original Windows insets");
            string fakeDatabasePath = Path.Combine(AppContext.BaseDirectory, "__test__card-print.kdbx");
            Check(platform.NormalizeDatabaseIdentityPath(fakeDatabasePath) ==
                Path.GetFullPath(fakeDatabasePath).Replace('/', '\\').ToLowerInvariant(),
                "Windows selection-token path normalization is unchanged");
            Check(platform.Printing.VectorFormatName == "XPS", "Windows vector backend identity");
            Check(PreviewTelemetry.PercentileSorted(new double[] { 10, 20, 30, 40 }, .5) == 25,
                "preview median interpolates the middle pair for even samples");
            Check(PreviewTelemetry.PercentileSorted(new double[] { 10, 20, 30 }, .5) == 20,
                "preview median selects the middle sample for odd counts");
            Check(Math.Abs(PreviewTelemetry.PercentileSorted(new double[] { 1, 2, 3, 4, 5 }, .95) - 4.8) < 1e-10,
                "preview P95 interpolates exact sample durations instead of nearest rank");
            Check(PreviewTelemetry.PercentileSorted(new double[] { 13 }, .95) == 13,
                "preview P95 handles one collected sample");
            var demo = DemoVaultFactory.Create();
            var nextDemo = DemoVaultFactory.Create();
            Check(demo.RootUuid != Guid.Empty && demo.RootUuid != nextDemo.RootUuid,
                "demo uses a fresh in-memory database identity each time");
            Check(demo.Records.Count == 40 && demo.GroupCount >= 9 &&
                  demo.Records.Select(r => r.Uuid).Distinct().Count() == demo.Records.Count &&
                  demo.Records.Count(r => r.Selected) >= 10,
                "forty synthetic entries with representative cards preselected");
            Check(demo.Records.Any(r => r.AdditionalFields.ContainsKey("Recovery Codes")) &&
                  demo.Records.Any(r => r.AdditionalFields.ContainsKey("Backup Codes")) &&
                  demo.Records.Any(r => r.AdditionalFields.ContainsKey("TOTP Secret")) &&
                  demo.Records.Any(r => r.AdditionalFields.ContainsKey("HOTP Secret")) &&
                  demo.Records.Any(r => r.AdditionalFields.ContainsKey("Recovery Key")) &&
                  demo.Records.Any(r => r.AdditionalFields.ContainsKey("Wi-Fi Password")) &&
                  demo.Records.Any(r => r.AdditionalFields.ContainsKey("API Token")),
                "demo exercises codes, OTP, BitLocker, Wi-Fi and long credentials");
            Check(demo.Records.Any(r => r.Url.Length == 0 && r.AdditionalUrls.Length > 0) &&
                  demo.Records.Any(r => r.Url.Length == 0 && r.UserName.Length == 0) &&
                  demo.Records.Any(r => r.Notes.Contains('\n')) &&
                  demo.Records.Any(r => r.Title == "Unicode · Cyrillic & Greek"),
                "demo has fallback URLs, absent fields, multiline and printable Unicode");
            var unicode = demo.Records.Single(r => r.Title == "Unicode · Cyrillic & Greek");
            var showcaseFontSettings = CardAppearancePresets.Create("table");
            // Actual card layout splits explicit newlines before shaping. Shaping a
            // multiline value as one glyph run turns the nonprinting '\n' into
            // glyph ID 0 and incorrectly reports a missing Unicode character.
            // Test every rendered line instead, using the same measured wrapping
            // and fonts as the title and field values in the print pipeline.
            foreach (var (caption, value, fontFamily) in new[]
            {
                ("title", unicode.Title, showcaseFontSettings.TitleFont),
                ("notes", unicode.Notes, showcaseFontSettings.SecretFont),
                ("multiline description", unicode.AdditionalFields["Long Description"], showcaseFontSettings.SecretFont)
            })
            {
                var printableLines = CardTextMetrics.WrapExact(value,
                    (float)(showcaseFontSettings.WidthMm - 2 * showcaseFontSettings.PaddingMm),
                    12f, fontFamily, "400");
                Check(printableLines.Count >= (caption == "multiline description" ? 3 : 1) &&
                      printableLines.All(line => !line.Contains('\n') && !line.Contains('\r')),
                    "showcase Unicode " + caption + " separates hard line breaks before shaping");
                foreach (var line in printableLines.Where(line => line.Length > 0))
                {
                    using var shaped = ShapedTextEngine.CreateLayout(line, 12f, fontFamily, "400");
                    Check(shaped.Runs.All(run => run.Glyphs.All(glyph => glyph != 0)),
                        "showcase Unicode " + caption + " contains no missing glyphs on printed lines");
                }
            }
            var developer = demo.Records.Single(r => r.Title == "Developer Console");
            var ssh = demo.Records.Single(r => r.Title == "SSH Access / Fingerprint");
            Check(developer.AdditionalFields.ContainsKey("API Token") &&
                  !developer.AdditionalFields.ContainsKey("SSH Fingerprint") &&
                  ssh.AdditionalFields.ContainsKey("SSH Fingerprint") &&
                  !ssh.AdditionalFields.ContainsKey("API Token"),
                "ordinary demo accounts are not overloaded with unrelated secret fields");
            Check(demo.Records.All(r => r.Url.Length == 0 || r.Url.Contains(".example", StringComparison.Ordinal)) &&
                  demo.Records.All(r => r.UserName.Length == 0 ||
                      r.UserName.EndsWith("@example.com", StringComparison.Ordinal) ||
                      r.UserName.StartsWith("+380", StringComparison.Ordinal)),
                "all demo addresses and identities are fictitious");
            var demoLedger = new PrintTrackingLedger(demo.RootUuid, inMemory: true);
            demoLedger.Mark(demo.Records[0].Uuid, "printed");
            demoLedger.Mark(demo.Records[4].Uuid, "unprinted", 2);
            demoLedger.SeedDemoPartial(demo.Records[17].Uuid, 3, 1);
            demoLedger.SeedDemoHistory(demo.Records[0].Uuid, demo.Records[17].Uuid);
            Check(demoLedger.IsFullyPrinted(demo.Records[0].Uuid) &&
                  !demoLedger.IsFullyPrinted(demo.Records[17].Uuid) &&
                  demoLedger.Status(demo.Records[17].Uuid) == "Частично" &&
                  demoLedger.History.Count == 1,
                "demo-only print statuses, partial card and history use no real journal");
            var rowDividerCase = new List<CardLine>
            {
                new(CardLineKind.Value, "example", 3, 6, Field: "URL"),
                new(CardLineKind.Gap, "", 1),
                new(CardLineKind.Section, "ACCESS", 3),
                new(CardLineKind.Value, "password", 3, 6, Field: "PASSWORD"),
                new(CardLineKind.Gap, "", 1),
                new(CardLineKind.Label, "TOKEN", 3, 6, Field: "API Token"),
                new(CardLineKind.Value, "token", 3, 6, Field: "API Token"),
                new(CardLineKind.Value, "wrapped", 3, 6, Field: "API Token")
            };
            Check(!CardSkiaRenderer.ShouldDrawTableRule(rowDividerCase, 0),
                "table does not double-draw a rule immediately before a section");
            Check(CardSkiaRenderer.ShouldDrawTableRule(rowDividerCase, 3),
                "table draws one subtle rule between independent fields");
            Check(!CardSkiaRenderer.ShouldDrawTableRule(rowDividerCase, 6) &&
                  !CardSkiaRenderer.ShouldDrawTableRule(rowDividerCase, 7),
                "table avoids extra rules within wrapped values or at the end");
            foreach (var style in new[] { "archive", "technical", "table" })
            {
                var appearance = CardAppearancePresets.Create(style);
                var mapping = CardAppearancePresets.CreateMapping(style);
                Check(appearance.TitleSizePt is >= 7.5 and <= 8.5 &&
                      appearance.ValueSizePt is >= 6.2 and <= 6.9 &&
                      appearance.LabelSizePt is >= 5.3 and <= 5.8 &&
                      appearance.RecoveryMaxFontPt <= 8.2 && mapping.Count >= 20 &&
                      appearance.DuplexEnabled,
                    "built-in visual style uses compact physical type sizes, duplex and complete mapping: " + style);
                var sample = demo.Records.Where(r => r.Title is "Aurora Mail" or
                    "Disk Encryption" or "TOTP · QR Showcase" or "16 Backup Codes" or
                    "Long Multiline Notes").ToArray();
                var cards = CardLayoutEngine.Build(sample, appearance, mapping, sample);
                Check(cards.Count >= sample.Length,
                    "built-in layout includes all representative records: " + style);
                LayoutFits(cards, appearance);
                if (style == "table")
                    Check(All(cards).Any(line => line.Kind == CardLineKind.Qr),
                        "QR template displays valid demo TOTP as an actual QR");
                // One rendered card per preset catches unsupported fonts, colors
                // or background modes in both the preview and printer compositor.
                var png = CardSkiaRenderer.Render(cards[0], appearance, false);
                using (var decoded = SKBitmap.Decode(png))
                    Check(decoded is not null && decoded.Width > 100 && decoded.Height > 100,
                        "built-in preset raster renders: " + style);
                Array.Clear(png);
            }
            TestPublicKdbxHeaderMetadata();
            TestXpsPathSyntax();
            TestSharedGlyphRuns();
            // Preview animation is independent of print geometry and file data.
            // Each event type is separately controlled by the UI settings.
            var animationSettings=new UiSettings();
            Check(PreviewTransitionPolicy.ShouldAnimate(animationSettings,
                  PreviewTransitionReason.Rebuild,true),"delayed redraw triggers animation");
            Check(!PreviewTransitionPolicy.ShouldAnimate(animationSettings,
                  PreviewTransitionReason.Rebuild,false),"quick redraw stays immediate");
            Check(PreviewTransitionPolicy.ShouldAnimate(animationSettings,
                  PreviewTransitionReason.Navigation,false),"next/previous can animate");
            Check(PreviewTransitionPolicy.ShouldAnimate(animationSettings,
                  PreviewTransitionReason.SideChange,false),"front/back can animate");
            animationSettings.PreviewAnimateNavigation=false;
            Check(!PreviewTransitionPolicy.ShouldAnimate(animationSettings,
                  PreviewTransitionReason.Navigation,true),"page animation can be disabled independently");
            animationSettings.PreviewAnimation=false;
            Check(!PreviewTransitionPolicy.ShouldAnimate(animationSettings,
                  PreviewTransitionReason.SideChange,true),"global animation switch wins");
            Check(PreviewTransitionPolicy.OldOpacity(0)==1 &&
                  PreviewTransitionPolicy.OldOpacity(1)==0,"crossfade endpoint opacities");
            double previousOpacity=1;
            for(int step=1;step<=100;step++)
            {
                double oldOpacity=PreviewTransitionPolicy.OldOpacity(step/100d);
                Check(oldOpacity>=0 && oldOpacity<=previousOpacity,
                    "crossfade opacity never jumps backwards");
                previousOpacity=oldOpacity;
            }
            // Cursor-anchored zoom is independent of layout and ScrollViewer.
            // Fractional wheel deltas and multiple successive steps must never
            // change the document coordinate directly underneath the pointer.
            var origin=new Point(120.25,48.5);
            var cursor=new Point(347.8,191.2);
            double scale=1;
            double worldX=(cursor.X-origin.X)/scale;
            double worldY=(cursor.Y-origin.Y)/scale;
            foreach(double delta in new[]{.125,.25,.7,1d,2d,-.35,-1d,-.1,.01,-.45,-2.185})
            {
                double next=PreviewViewportGeometry.WheelZoom(scale,delta,8);
                origin=PreviewViewportGeometry.ZoomAtCursor(origin,cursor,scale,next);
                scale=next;
                Check(Math.Abs((cursor.X-origin.X)/scale-worldX)<1e-8 &&
                      Math.Abs((cursor.Y-origin.Y)/scale-worldY)<1e-8,
                    "fractional zoom keeps pointer anchor exact");
            }
            Check(PreviewViewportGeometry.WheelZoom(4,100,8)==6,
                "wheel zoom clamps to 600 percent");
            Check(PreviewViewportGeometry.WheelZoom(.4,-100,8)==.05,
                "wheel zoom preserves 5 percent minimum");
            Check(PreviewViewportGeometry.WheelZoom(1,1,18)>
                  PreviewViewportGeometry.WheelZoom(1,1,4),
                "wheel sensitivity changes zoom response");
            double fit=PreviewViewportGeometry.FitScale(420,370,800,1000);
            var fitOrigin=PreviewViewportGeometry.CenteredOrigin(420,370,800,1000,fit);
            Check(fit*800<=420-24+1e-8 && fit*1000<=370-24+1e-8,
                "fit command uses the complete page within viewport margins");
            Check(Math.Abs(fitOrigin.X-(420-800*fit)/2)<1e-9 &&
                  Math.Abs(fitOrigin.Y-(370-1000*fit)/2)<1e-9,
                "fit centers document without changing its geometry");
            Check(Math.Abs(PreviewViewportGeometry.FitScale(800,800,400,200)-1.94)<1e-8,
                "fit can scale small cards above 100 percent");
            // Sub-viewport and super-viewport document sizes do not enter the
            // transform equation, so crossing the fit threshold cannot snap.
            var smallOrigin=new Point(160,115);
            var smallCursor=new Point(210,180);
            var enlarged=PreviewViewportGeometry.ZoomAtCursor(smallOrigin,smallCursor,.5,3);
            Check(Math.Abs((smallCursor.X-enlarged.X)/3-
                           (smallCursor.X-smallOrigin.X)/.5)<1e-9 &&
                  Math.Abs((smallCursor.Y-enlarged.Y)/3-
                           (smallCursor.Y-smallOrigin.Y)/.5)<1e-9,
                "crossing viewport fit boundary does not recenter the document");
            // One duplex card occupies two consecutive preview stops.
            // Reversing the direction must traverse the same sequence backwards.
            var expectedOrder = new (int CardIndex,bool Back)[]
            { (0,false), (0,true), (1,false), (1,true), (2,false), (2,true) };
            Check(CardPreviewSequence.Count(3,true)==expectedOrder.Length,"duplex side count");
            for(int i=0;i<expectedOrder.Length;i++)
            {
                var item=CardPreviewSequence.At(i,true);
                Check(item==expectedOrder[i],"duplex front-back order at "+i);
                Check(CardPreviewSequence.Position(item.PhysicalIndex,item.Back,true)==i,
                    "duplex reverse navigation at "+i);
                Check(CardPreviewSequence.CanStep(3,item.PhysicalIndex,item.Back,true,1)==(i<5),
                    "duplex next boundary at "+i);
                Check(CardPreviewSequence.CanStep(3,item.PhysicalIndex,item.Back,true,-1)==(i>0),
                    "duplex previous boundary at "+i);
            }
            Check(CardPreviewSequence.Count(3,false)==3,"simplex preview count");
            Check(CardPreviewSequence.At(1,false)==(1,false),"simplex next is next front");
            Check(!CardPreviewSequence.CanStep(0,0,false,true,1),"empty duplex navigation");

            var settings=Defaults();
            var appearanceFields=AppearanceDescriptor.Create(settings);
            Check(appearanceFields.Count==175,"all 175 editable appearance parameters");
            var iconColorModel=Defaults();
            var iconColorFields=AppearanceDescriptor.Create(iconColorModel).ToDictionary(f=>f.Key,StringComparer.Ordinal);
            var iconSharedField=iconColorFields["palette_icon_color_mode"];
            var iconEntryField=iconColorFields["entry_icon_color_mode"];
            var iconTintField=iconColorFields["entry_icon_tint_color"];
            Check(CardPalette.Schemes.Select(s=>s.IconColorMode).SequenceEqual(new[]{"original","gray","palette:accent_color","palette:accent_color"}),"ready-made schemes set independent icon coloring");
            var iconLegacy=JsonSerializer.Deserialize("{}",CardAppearanceJsonContext.Default.CardAppearance)!;
            Check(iconLegacy.PaletteIconColorMode=="original" && iconLegacy.EntryIconColorMode=="inherit","old JSON preserves original icon coloring");
            iconSharedField.SelectedChoice=iconSharedField.Choices.Single(c=>c.Code=="gray");
            Check(CardIconColoring.EffectiveMode(iconColorModel)=="gray","entry icon inherits shared grayscale");
            iconEntryField.SelectedChoice=iconEntryField.Choices.Single(c=>c.Code=="original");
            Check(CardIconColoring.EffectiveMode(iconColorModel)=="original","explicit original overrides shared grayscale");
            iconEntryField.SelectedChoice=iconEntryField.Choices.Single(c=>c.Code=="palette:accent_color");
            iconColorModel.AccentColor="#AABBCC";
            Check(CardIconColoring.Resolve(iconColorModel).Tint==new SKColor(170,187,204),"explicit icon palette role follows palette edits");
            iconTintField.SetColorSource("palette:accent_color");
            iconEntryField.SelectedChoice=iconEntryField.Choices.Single(c=>c.Code=="custom");
            iconTintField.Reload();
            Check(iconColorModel.EntryIconTintColor=="#AABBCC" && iconTintField.IconCustomTintOnly,"individual mode freezes a previous color link");
            iconColorModel.AccentColor="#112233";
            iconTintField.SetColorSource("palette:accent_color");
            Check(iconColorModel.EntryIconTintColor=="#AABBCC","individual icon color rejects a new palette link");
            iconTintField.SetColorSource("#123456");
            Check(CardIconColoring.Resolve(iconColorModel).Tint==new SKColor(18,52,86),"individual icon RGB has priority");
            var iconSaved=JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(iconColorModel,CardAppearanceJsonContext.Default.CardAppearance),CardAppearanceJsonContext.Default.CardAppearance)!;
            Check(iconSaved.EntryIconColorMode=="custom" && iconSaved.PaletteIconColorMode=="gray" && iconSaved.EntryIconTintColor=="#123456","icon coloring survives template JSON round trip");
            iconEntryField.SelectedChoice=iconEntryField.Choices.Single(c=>c.Code=="inherit");
            Check(CardIconColoring.EffectiveMode(iconColorModel)=="gray" && iconColorModel.EntryIconTintColor=="#123456","return to inheritance retains individual RGB");
            iconEntryField.IsReadOnly=true;
            iconEntryField.SelectedChoice=iconEntryField.Choices.Single(c=>c.Code=="original");
            Check(iconColorModel.EntryIconColorMode=="inherit","built-in icon coloring is read-only");
            iconEntryField.IsReadOnly=false;
            iconColorModel.EntryIconMode="all";iconColorModel.EntryIconPrintMode="original";
            Check(TemplateEditorLayout.Visible("entry_icon_color_mode",iconColorModel) && !TemplateEditorLayout.Visible("entry_icon_tint_color",iconColorModel),"gray icon hides irrelevant tint editor");
            iconColorModel.EntryIconColorMode="custom";
            Check(TemplateEditorLayout.Visible("entry_icon_tint_color",iconColorModel),"individual icon shows RGB editor");
            iconColorModel.EntryIconPrintMode="none";
            Check(!TemplateEditorLayout.Visible("entry_icon_color_mode",iconColorModel) && !TemplateEditorLayout.Visible("entry_icon_tint_color",iconColorModel),"hidden icon hides coloring without resetting settings");
            using(var iconPixels=new SKBitmap(3,3,SKColorType.Rgba8888,SKAlphaType.Premul))
            {
                iconPixels.Erase(SKColors.Transparent);
                iconPixels.SetPixel(0,0,new SKColor(0,64,192));
                iconPixels.SetPixel(1,0,SKColors.Black);
                iconPixels.SetPixel(2,0,SKColors.White);
                iconPixels.SetPixel(0,1,new SKColor(120,60,200,128));
                var originalPolicy=new CardIconColoring.Resolved("original",SKColors.Black);
                var grayPolicy=new CardIconColoring.Resolved("gray",SKColors.Black);
                var tintPolicy=new CardIconColoring.Resolved("custom",new SKColor(18,52,86));
                using var originalIcon=CardSkiaRenderer.TransformIcon(iconPixels,"original",SKColors.Black,originalPolicy);
                using var grayIcon=CardSkiaRenderer.TransformIcon(iconPixels,"original",SKColors.Black,grayPolicy);
                using var tintIcon=CardSkiaRenderer.TransformIcon(iconPixels,"original",SKColors.Black,tintPolicy);
                var grayPixel=grayIcon.GetPixel(0,0);
                Check(originalIcon.GetPixel(0,0)==iconPixels.GetPixel(0,0) && grayPixel.Red==grayPixel.Green && grayPixel.Green==grayPixel.Blue,"grayscale transforms actual source pixels without mutating the original");
                Check(tintIcon.GetPixel(1,0)==tintPolicy.Tint && tintIcon.GetPixel(2,0)==SKColors.White && tintIcon.GetPixel(2,2).Alpha==0,"tint preserves tonal detail and transparent pixels");
                Check(grayIcon.GetPixel(0,1).Alpha==128 && tintIcon.GetPixel(0,1).Alpha==128,"grayscale and tint preserve partially transparent pixels");
                foreach(var processing in new[]{"auto","backdrop","outline","silhouette"})
                {
                    using var processedGray=CardSkiaRenderer.TransformIcon(iconPixels,processing,new SKColor(32,43,56),grayPolicy);
                    var processedPixel=processedGray.GetPixel(0,0);
                    Check(processedPixel.Alpha==255 && processedPixel.Red==processedPixel.Green && processedPixel.Green==processedPixel.Blue && processedGray.GetPixel(2,2).Alpha==0,"grayscale after independent processing: "+processing);
                }
                using var shapedIcon=CardSkiaRenderer.TransformIcon(iconPixels,"outline",SKColors.Black,tintPolicy);
                Check(shapedIcon.GetPixel(0,0)==tintPolicy.Tint && shapedIcon.GetPixel(2,2).Alpha==0,"outline processing stays independent of tint");
            }
            var paletteModel=Defaults();
            var paletteFields=AppearanceDescriptor.Create(paletteModel).ToDictionary(f=>f.Key,StringComparer.Ordinal);
            Check(CardPalette.RoleKeys.Length==7 && CardPalette.RoleKeys.Distinct().Count()==7,"seven distinct shared palette roles");
            Check(CardPalette.RoleKeys.All(k=>TemplateEditorLayout.Groups.Single(g=>g.Keys.Contains(k)).Id=="palette"),"palette roles have one common editor");
            var paletteTitle=paletteFields["title_color"];
            paletteTitle.SetColorSource("palette:accent_color");
            paletteFields["accent_color"].SetColorSource("#AABBCC");
            Check(paletteTitle.TextValue=="palette:accent_color" && paletteTitle.EffectiveColorHex=="#AABBCC","linked title follows accent palette edits");
            paletteTitle.SetColorSource("#123456");
            paletteFields["accent_color"].SetColorSource("#334455");
            Check(paletteTitle.TextValue=="#123456" && paletteTitle.EffectiveColorHex=="#123456","individual title overrides the palette");
            paletteTitle.SetColorSource("palette:muted_color");
            paletteFields["muted_color"].SetColorSource("#445566");
            Check(paletteTitle.EffectiveColorHex=="#445566","element can link to any shared role");
            paletteTitle.SetColorSource("palette:unknown");
            paletteTitle.SetColorSource("not-a-color");
            Check(paletteTitle.TextValue=="palette:muted_color","invalid color source does not alter the model");
            paletteFields["ink_color"].SetColorSource("palette:accent_color");
            Check(!paletteFields["ink_color"].TextValue.StartsWith("palette:",StringComparison.Ordinal),"shared palette rejects nested references");
            paletteTitle.IsReadOnly=true;
            paletteTitle.SetColorSource("#000000");
            Check(paletteTitle.TextValue=="palette:muted_color","built-in color source is read-only");
            var roundTripColors=JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(paletteModel,CardAppearanceJsonContext.Default.CardAppearance),CardAppearanceJsonContext.Default.CardAppearance)!;
            Check(roundTripColors.TitleColor=="palette:muted_color" && CardPalette.EditorHex(roundTripColors,"title_color",roundTripColors.TitleColor)=="#445566","palette reference survives JSON round-trip");
            var legacyColors=JsonSerializer.Deserialize("{\"ink_color\":\"#112233\",\"footer_uuid_color\":\"#ABCDEF\"}",CardAppearanceJsonContext.Default.CardAppearance)!;
            Check(legacyColors.TitleColor=="inherit" && CardPalette.EditorHex(legacyColors,"title_color",legacyColors.TitleColor)=="#112233" && legacyColors.FooterUuidColor=="#ABCDEF","old template retains inherited and explicit colors");
            Check(legacyColors.BackRulesColor=="#777777" && legacyColors.PageBadgeBackgroundColor=="#E5EBF2","legacy fixed decoration colors remain unchanged");
            legacyColors.FrontBgOverride=true;legacyColors.FrontBackgroundColor="palette:accent_color";
            Check(CardBackgroundRenderer.Resolve(legacyColors,"front").BaseColor=="palette:accent_color","face-specific background color source");
            legacyColors.EmptyBackBgSource="front";
            Check(CardBackgroundRenderer.Resolve(legacyColors,"empty").BaseColor=="palette:accent_color","empty reverse inherits face color source");
            legacyColors.EmptyBackBgSource="none";
            Check(CardBackgroundRenderer.Resolve(legacyColors,"empty").WhiteOnly,"white reverse ignores palette override");
            var previewSelectionHit=new PreviewHitRegion("header","title",new SKRect(1,2,30,40));
            var previewOtherHit=new PreviewHitRegion("footer","uuid",new SKRect(1,41,30,50));
            Check(PreviewHitRegion.CanCommit(previewSelectionHit,previewSelectionHit,true,false,0,0),"Ctrl-click selects the same hit");
            Check(!PreviewHitRegion.CanCommit(previewSelectionHit,previewSelectionHit,false,false,0,0),"ordinary click does not select");
            Check(!PreviewHitRegion.CanCommit(previewSelectionHit,previewOtherHit,true,false,0,0),"Ctrl-click cannot select another hit on release");
            Check(!PreviewHitRegion.CanCommit(previewSelectionHit,previewSelectionHit,true,true,0,0) && !PreviewHitRegion.CanCommit(previewSelectionHit,previewSelectionHit,true,false,4,0),"Ctrl-drag does not commit selection");
            Check(!PreviewHitRegion.CanCommit(null,null,true,false,0,0),"empty Ctrl-click does not select");
            Check(new[]{"ru","uk","en"}.All(lang=>!string.IsNullOrWhiteSpace(TemplateEditorLayout.GroupCaption("title",lang)) && TemplateEditorLayout.GroupCaption("title",lang)!="title"),"preview caption localized in all languages");
            // Every fixed enum choice must have a human-readable localized label.
            // Real font family names are not translated; inheritance still is.
            foreach (var language in new[] { "ru", "uk", "en" })
            {
                foreach (var field in appearanceFields)
                {
                    field.Relocalize(language);
                    Check(!string.IsNullOrWhiteSpace(field.Label) && field.Label != field.Key &&
                          field.Description.StartsWith(field.Label + "\n\n", StringComparison.Ordinal) &&
                          field.Description.Length > field.Label.Length + 10,
                          $"localized parameter label and help: {language}/{field.Key}");
                    if (field.Key == "notes_mode")
                        Check(field.Description.StartsWith(field.Label + "\n\n", StringComparison.Ordinal) &&
                              field.Description.Length > field.Label.Length + 40,
                            $"notes tooltip has label and detailed explanation: {language}");
                    foreach (var choice in field.Choices)
                    {
                        if (field.Key.EndsWith("_font", StringComparison.Ordinal) && choice.Code != "inherit") continue;
                        Check(!string.IsNullOrWhiteSpace(choice.Label) && choice.Label != choice.Code,
                            $"localized appearance choice: {language}/{field.Key}/{choice.Code}");
                    }
                }
            }
            var notesMode = appearanceFields.Single(f=>f.Key=="notes_mode");
            notesMode.Relocalize("ru");
            Check(notesMode.Choices.Single(c=>c.Code=="normal").Label=="Обычный размер" &&
                  notesMode.Choices.Single(c=>c.Code=="small").Label=="Отдельный размер шрифта" &&
                  notesMode.Choices.Single(c=>c.Code=="optional").Label=="Пропускать при нехватке места",
                  "notes choices explain all three modes in Russian");
            foreach (var field in appearanceFields) field.Relocalize("ru");
            TemplateEditorLayout.Validate(appearanceFields.Select(f=>f.Key));
            var ownership=TemplateEditorLayout.Groups.SelectMany(g=>g.Keys).ToArray();
            Check(ownership.Length==175 && ownership.Distinct().Count()==175,"unified editor owns every setting exactly once");
            var navigationGlyphs=TemplateEditorLayout.Sections
                .Select(s=>new TemplateEditorSection(s.Id,s.En).Glyph).ToArray();
            Check(navigationGlyphs.Length==10 && navigationGlyphs.Distinct().Count()==10,
                "template navigation has ten distinct Fluent glyphs");
            Check(new TemplateEditorSection("header", "").Glyph==FluentIcons.Common.Icon.DocumentHeader &&
                  new TemplateEditorSection("footer", "").Glyph==FluentIcons.Common.Icon.DocumentFooter &&
                  new TemplateEditorSection("fields", "").Glyph==FluentIcons.Common.Icon.TextBulletList &&
                  new TemplateEditorSection("recovery", "").Glyph==FluentIcons.Common.Icon.Key &&
                  new TemplateEditorSection("qr", "").Glyph==FluentIcons.Common.Icon.QrCode,
                  "template navigation glyphs represent their actual sections");
            var visibility=Defaults();
            visibility.FrontBgOverride=false;
            Check(!TemplateEditorLayout.Visible("front_bg_pattern",visibility),"inherited front background hides overrides");
            visibility.FrontBgOverride=true;
            Check(TemplateEditorLayout.Visible("front_bg_pattern",visibility),"explicit front background exposes overrides");
            visibility.DuplexEnabled=true;
            visibility.DuplexBackFill="blank";
            Check(!TemplateEditorLayout.Visible("back_caption_title_font",visibility),"blank reverse hides decorative caption settings");
            visibility.DuplexBackFill="design";
            visibility.EmptyBackShowArt=true;
            Check(TemplateEditorLayout.Visible("back_caption_title_font",visibility),"decorated reverse exposes caption settings");
            Check(TemplateEditorLayout.Groups.Single(g=>g.Keys.Contains("print_password")).SectionId=="mapping",
                "global field restrictions belong to mapping");
            visibility.Frame=false;
            Check(!TemplateEditorLayout.Visible("frame_width_pt",visibility),"disabled frame hides stroke width");
            visibility.HeaderFill=true;
            Check(TemplateEditorLayout.Visible("corner_radius_mm",visibility),"header can still use radius without frame");
            visibility.RecoveryAutosize=false;
            Check(!TemplateEditorLayout.Visible("recovery_min_font_pt",visibility) && !TemplateEditorLayout.Visible("recovery_max_font_pt",visibility),"disabled recovery autosize hides bounds");
            visibility.NotesMode="normal";
            Check(!TemplateEditorLayout.Visible("notes_size_pt",visibility),"normal notes hide unused size");
            visibility.NotesMode="optional";
            Check(TemplateEditorLayout.Visible("notes_size_pt",visibility),"optional notes expose size");
            visibility.CardBgPattern="clean";
            Check(!TemplateEditorLayout.Visible("card_bg_contrast",visibility),"plain background hides pattern contrast");
            visibility.CardBgPattern="gradient";
            Check(TemplateEditorLayout.Visible("card_bg_contrast",visibility),"pattern exposes contrast");
            visibility.EmptyBackShowIcon=false;
            Check(TemplateEditorLayout.Visible("back_art_icon_position",visibility),"caption alone still uses composition position");
            Check(!TemplateEditorLayout.Visible("back_art_text_gap_mm",visibility),"gap requires icon and caption");
            var autoModel=Defaults();
            var autoFields=AppearanceDescriptor.Create(autoModel);
            foreach(var autoField in autoFields.Where(f=>f.IsAutomaticSupported))
            {
                autoField.AutomaticValue=false;
                autoField.TextValue="8";
                autoField.AutomaticValue=true;
                Check(autoField.AutomaticValue && !autoField.ManualTextVisible,"automatic setting stores sentinel: "+autoField.Key);
                autoField.AutomaticValue=false;
                Check(!autoField.AutomaticValue && autoField.ManualTextVisible && autoField.TextValue=="8","manual setting restores value: "+autoField.Key);
                autoField.IsReadOnly=true;
                autoField.AutomaticValue=true;
                Check(!autoField.AutomaticValue,"built-in automatic setting is read-only: "+autoField.Key);
            }
            Check(autoFields.Count(f=>f.IsAutomaticSupported)==3,"exactly three automatic numeric modes");
            foreach (var compactLanguage in new[] { "ru", "uk", "en" })
            {
                var compactFields = AppearanceDescriptor.Create(Defaults(), compactLanguage);
                Check(compactFields.Where(f=>f.IsNumeric && !f.IsAutomaticSupported).All(f=>
                    f.TemplateFieldWidth==(f.EditorLabel.Length<=18 ? 124 : 150) &&
                    f.TemplateFieldWidth>=f.EditorWidth+18), "compact numeric editor slots: "+compactLanguage);
                Check(compactFields.Where(f=>f.IsBool).All(f=>
                    f.TemplateFieldWidth==(f.EditorLabel.Length<=18 ? 144 : 190)),
                    "compact checkbox editor slots: "+compactLanguage);
            }
            Check(autoFields.Where(f=>f.IsAutomaticSupported).All(f=>f.TemplateFieldWidth==244 && f.EditorWidth<=100),
                "automatic toggle has room beside a compact numeric editor");
            var hitAppearance=Defaults();
            hitAppearance.Frame=true; hitAppearance.ShowDate=true; hitAppearance.FooterUuidShow=true;
            var hitCards=CardLayoutEngine.Build([Record()],hitAppearance,BasicMapping);
            using(var scene=CardSkiaRenderer.Record(hitCards[0],hitAppearance,false))
            {
                Check(scene.HitTest(1,scene.Height/2)?.Group=="frame","frame has its own hit target");
                foreach(var target in scene.HitRegions.Where(h=>h.Group is "uuid" or "date"))
                    Check(scene.HitTest((target.Bounds.Left+target.Bounds.Right)/2,(target.Bounds.Top+target.Bounds.Bottom)/2)?.Group==target.Group,"footer exact hit target: "+target.Group);
                Check(scene.HitRegions.Any(h=>h.Group=="uuid") && scene.HitRegions.Any(h=>h.Group=="date"),"both footer elements have measured regions");
                Check(scene.HitRegions.Any(h=>h.Group=="page-badge"),"part number has its own editor target");
                Check(scene.HitRegions.All(h=>TemplateEditorLayout.Groups.Any(g=>g.Id==h.Group && g.SectionId==h.Section)),"preview targets resolve to actual editor groups");
            }
            var qrPart=new CardPart(Record(),[new CardLine(CardLineKind.Qr,"",15,14,QrUri:"https://example.invalid")],1,1);
            using(var qrHitScene=CardSkiaRenderer.Record(new PhysicalCard(qrPart,null),hitAppearance,false))
            {
                var qrHit=qrHitScene.HitRegions.Single(h=>h.Group=="totp");
                Check(Math.Abs(qrHit.Bounds.Width-14*CardSkiaRenderer.PixelsPerMillimetre)<.01f &&
                      Math.Abs(qrHit.Bounds.Height-qrHit.Bounds.Width)<.01f,"QR hit target uses actual square geometry");
            }
            var upperFontField=appearanceFields.Single(f=>f.Key=="back_caption_title_font");
            var lowerFontField=appearanceFields.Single(f=>f.Key=="back_caption_subtitle_font");
            Check(upperFontField.Group=="Пустой оборот" && lowerFontField.Group=="Пустой оборот" &&
                  upperFontField.Choices.Any(c=>c.Code=="Consolas") &&
                  lowerFontField.Choices.Any(c=>c.Code=="JetBrains Mono NL"),
                  "reverse caption editors include system and bundled monospace fonts");
            Check(CardSkiaRenderer.BackCaptionTypography(settings,true).Font==settings.TitleFont &&
                  CardSkiaRenderer.BackCaptionTypography(settings,false).Weight==settings.LabelWeight &&
                  Math.Abs(CardSkiaRenderer.BackCaptionTypography(settings,true).SizePt-5.8f)<.001f,
                  "legacy reverse caption fonts and sizes remain unchanged");
            settings.DuplexBackLanguage="en";
            Check(Math.Abs(CardSkiaRenderer.BackCaptionTypography(settings,true).SizePt-6.6f)<.001f &&
                  Math.Abs(CardSkiaRenderer.BackCaptionTypography(settings,false).SizePt-5f)<.001f,
                  "legacy English caption sizes remain unchanged");
            settings.DuplexBackLanguage="ru";
            // A standard template must be truly read-only, not merely greyed out.
            // These tests do not require Windows UI or any disk access.
            var lockedAppearance = new CardAppearance();
            var lockedFields = AppearanceDescriptor.Create(lockedAppearance);
            var lockedWidth = lockedFields.Single(f => f.Key == "width_mm");
            lockedWidth.IsReadOnly = true;
            lockedWidth.TextValue = "72";
            Check(Math.Abs(lockedAppearance.WidthMm - 62) < .0001,
                "read-only standard rejects numeric text edits");
            var lockedBackHeader = lockedFields.Single(f => f.Key == "empty_back_show_header");
            lockedBackHeader.IsReadOnly = true;
            lockedBackHeader.Checked = true;
            Check(!lockedAppearance.EmptyBackShowHeader,
                "read-only standard rejects reverse-side changes");
            var lockedFont = lockedFields.Single(f => f.Key == "secret_font");
            lockedFont.IsReadOnly = true;
            lockedFont.SelectedChoice = lockedFont.Choices.First(c => c.Code == "Geist Mono");
            Check(lockedAppearance.SecretFont != "Geist Mono",
                "read-only standard rejects choice changes");
            lockedWidth.IsReadOnly = false;
            lockedWidth.TextValue = "72";
            Check(Math.Abs(lockedAppearance.WidthMm - 72) < .0001,
                "personal copy enables editing");
            var fullTemplate = new CardAppearance
            {
                EmptyBackShowHeader = true,
                EmptyBackShowFooter = false,
                BackArtIcon = "vault",
                BackArtIconColor = "#223344",
                BackRulesOpacity = 55,
                DuplexBackFill = "design",
                DuplexEnabled = true,
                BackCaptionTitleFont = "JetBrains Mono NL",
                BackCaptionTitleSizePt = 9.5,
                BackCaptionTitleWeight = "800",
                BackCaptionSubtitleFont = "Cascadia Mono",
                BackCaptionSubtitleSizePt = 7.2,
                BackCaptionSubtitleWeight = "600"
            };
            var fullJson = JsonSerializer.SerializeToUtf8Bytes(fullTemplate, CardAppearanceJsonContext.Default.CardAppearance);
            var reloaded = JsonSerializer.Deserialize(fullJson, CardAppearanceJsonContext.Default.CardAppearance);
            Check(reloaded is not null && CardAppearanceStore.ContentEquals(fullTemplate, reloaded) &&
                  reloaded.BackCaptionTitleFont=="JetBrains Mono NL" &&
                  reloaded.BackCaptionSubtitleWeight=="600",
                "templates serialize complete front and reverse typography");
            Check(PortableFonts.ReleaseFiles.Length==8 &&
                  PortableFonts.ReleaseFiles.Distinct(StringComparer.Ordinal).Count()==8 &&
                  PortableFonts.ReleaseFiles.All(f=>!f.Contains("Intel",StringComparison.OrdinalIgnoreCase)),
                  "exactly eight specialist TTF files; no retired font ships");
            Check(appearanceFields.Single(f=>f.Key=="secret_font").Choices
                  .Select(c=>c.Code).SequenceEqual(PortableFonts.SecretFamilies),
                  "all seven value fonts appear in the print settings");
            Check(new[]{"title_font","label_font","header_group_font"}
                  .All(k=>appearanceFields.Single(f=>f.Key==k).Choices.Any(c=>c.Code=="Recursive")),
                  "Recursive is selectable for all printable text labels");
            Check(appearanceFields.Single(f=>f.Key=="card_bg_pattern").Group=="Фон и декор",
                "background controls have their own section");
            Check(appearanceFields.Single(f=>f.Key=="empty_back_show_header").Group=="Пустой оборот",
                "empty back header control is in its own section");
            Check(appearanceFields.Single(f=>f.Key=="back_art_icon").Group=="Фоновая иконка",
                "vector icons have their own section");
            Check(appearanceFields.Single(f=>f.Key=="back_rules_style").Group=="Линии оборота",
                "line controls have their own section");
            Check(appearanceFields.All(f=>AppearanceDescriptions.Covers(f.Key) &&
                  f.Description.Length>f.Label.Length+40 && f.Description.Contains('\n')),
                  "every appearance parameter has a meaningful label tooltip");
            Check(appearanceFields.Where(f=>f.IsNumeric).All(f=>f.EditorWidth<=100) &&
                  appearanceFields.Where(f=>!f.IsNumeric && !f.IsColor).All(f=>f.EditorWidth>=180) &&
                  appearanceFields.Where(f=>f.IsColor).All(f=>f.EditorWidth>=130),
                  "numeric editors and colour swatches remain compact");
            // Test the actual persisted JSON contract, NOT the internal
            // JsonTypeInfo.Properties collection. The source-generated serializer
            // can use an optimized fast-path in Native AOT; its metadata property
            // list is not the authoritative list of names written to disk.
            // No vault data is involved: settings is a synthetic default model.
            using var serializedAppearance = JsonDocument.Parse(
                JsonSerializer.SerializeToUtf8Bytes(settings,
                    CardAppearanceJsonContext.Default.CardAppearance));
            var serializedKeys = serializedAppearance.RootElement.EnumerateObject()
                .Select(p => p.Name).ToArray();
            var keyNames = serializedKeys.ToHashSet(StringComparer.Ordinal);
            Check(serializedKeys.Length == 175,
                $"persisted appearance JSON has 175 properties (actual {serializedKeys.Length})");
            Check(keyNames.Count==175,
                $"original and new appearance keys (actual {keyNames.Count})");
            Check(appearanceFields.Select(f=>f.Key).Distinct(StringComparer.Ordinal).Count()==appearanceFields.Count,
                "appearance editor has unique property keys");
            var descriptorKeys = appearanceFields.Select(f=>f.Key).ToHashSet(StringComparer.Ordinal);
            var missingInJson = descriptorKeys.Except(keyNames,StringComparer.Ordinal).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            var missingInEditor = keyNames.Except(descriptorKeys,StringComparer.Ordinal).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
            Check(keyNames.SetEquals(descriptorKeys),
                "JSON/editor key correspondence; missing in JSON: " + string.Join(", ",missingInJson)
                + "; missing in editor: " + string.Join(", ",missingInEditor));
            Check(keyNames.Contains("totp_qr_min_height_mm")&&keyNames.Contains("duplex_back_background"),"appearance JSON geometry keys");
            Check(keyNames.Contains("title_weight") && keyNames.Contains("label_weight"),"new typography weights");
            foreach(var key in new[]{"card_bg_pattern","card_bg_gray","card_bg_contrast",
                "front_bg_override","front_bg_pattern","front_bg_gray","front_bg_contrast",
                "back_bg_override","back_bg_gray","back_bg_contrast",
                "empty_back_bg_source","empty_back_bg_pattern","empty_back_bg_gray","empty_back_bg_contrast",
                "empty_back_show_header","empty_back_show_footer","empty_back_show_art",
                "empty_back_show_icon","empty_back_show_frame",
                "back_art_icon","back_art_icon_size_mm","back_art_icon_color","back_art_text_gap_mm",
                "back_art_icon_opacity","back_art_icon_position",
                "back_rules_style","back_rules_width_mm","back_rules_thickness_mm","back_rules_gap_mm","back_rules_opacity"})
                Check(keyNames.Contains(key),"decoration setting: "+key);
            var bgSettings=Defaults();
            Check(CardBackgroundRenderer.Resolve(bgSettings,"front").Pattern=="clean", "new front inherits clean");
            bgSettings.CardBgPattern="gradient";
            Check(CardBackgroundRenderer.Resolve(bgSettings,"front").Pattern=="gradient", "front inherits shared pattern");
            Check(CardBackgroundRenderer.Resolve(bgSettings,"back").Pattern=="gradient", "data-back inherits shared pattern");
            bgSettings.FrontBgOverride=true;bgSettings.FrontBgPattern="technical";
            bgSettings.BackBgOverride=true;bgSettings.DuplexBackBackground="diagonal";
            Check(CardBackgroundRenderer.Resolve(bgSettings,"front").Pattern=="technical", "front override");
            Check(CardBackgroundRenderer.Resolve(bgSettings,"back").Pattern=="diagonal", "data-back override");
            Check(CardBackgroundRenderer.Resolve(bgSettings,"empty").Pattern=="diagonal", "empty inherits back");
            bgSettings.EmptyBackBgSource="front";
            Check(CardBackgroundRenderer.Resolve(bgSettings,"empty").Pattern=="technical", "empty can inherit front");
            bgSettings.EmptyBackBgSource="custom";bgSettings.EmptyBackBgPattern="watermark";
            Check(CardBackgroundRenderer.Resolve(bgSettings,"empty").Pattern=="watermark", "empty can override both");
            bgSettings.EmptyBackBgSource="none";
            Check(CardBackgroundRenderer.Resolve(bgSettings,"empty").WhiteOnly, "empty background can be off");
            var inheritedPreset=CardAppearancePresets.Create("minimal");
            Check(inheritedPreset.EmptyBackBgSource==new CardAppearance().EmptyBackBgSource &&
                  inheritedPreset.CardBgPattern==new CardAppearance().CardBgPattern &&
                  !inheritedPreset.BackBgOverride,
                  "built-in templates never inherit custom face or reverse decoration");
            var anotherPreset=CardAppearancePresets.Create("minimal");
            Check(CardAppearanceStore.ContentEquals(inheritedPreset,anotherPreset),
                  "built-in template face and reverse are deterministic");
            Check(!CardAppearanceStore.ContentEquals(inheritedPreset,bgSettings),
                  "personal appearance is independent of a built-in preset");

            foreach(var key in new[]{"header_group_show","header_group_position","header_group_font",
                "header_group_size_pt","header_group_weight","header_group_color","header_group_gap_mm",
                "header_group_column_pct","header_group_align","header_accent_width_mm",
                "footer_bottom_shift_mm","footer_uuid_show","footer_uuid_length","footer_uuid_separators","footer_uuid_uppercase",
                "footer_uuid_prefix","footer_uuid_size_pt","footer_uuid_weight","footer_uuid_color",
                "footer_date_format","footer_date_size_pt","footer_date_weight","footer_date_color"})
                Check(keyNames.Contains(key),"header/footer key: "+key);

            Check(appearanceFields.Single(f=>f.Key=="footer_height_mm").Group=="Шапка и подвал", "footer height is found with header controls");
            Check(appearanceFields.Single(f=>f.Key=="header_height_mm").Group=="Шапка и подвал", "header height is found with footer controls");
            Check(appearanceFields.Single(f=>f.Key=="header_gap_mm").Group=="Шапка и подвал", "header gap is found with header controls");
            Check(appearanceFields.Single(f=>f.Key=="footer_size_pt").Group=="Шапка и подвал", "badge font is not confused with footer font");

            // Raising the footer boundary reserves exactly the requested body height.
            var footerFixture=Record("Footer height fixture");
            float oldFooterHeight=CardLayoutEngine.ContentHeight(settings,footerFixture);
            settings.FooterHeightMm+=3;
            Check(Math.Abs((oldFooterHeight-CardLayoutEngine.ContentHeight(settings,footerFixture))-3f)<.002f,
                "footer height reserves space in pagination");
            settings.FooterHeightMm-=3;
            // A zero footer shift retains the configured bottom padding.
            Check(Math.Abs(settings.FooterBottomInsetMm-settings.PaddingMm)<.002,
                "zero footer shift retains default bottom padding");
            float beforeShift=CardLayoutEngine.ContentHeight(settings,footerFixture);
            settings.FooterBottomShiftMm=1;
            Check(Math.Abs((CardLayoutEngine.ContentHeight(settings,footerFixture)-beforeShift)-1f)<.002f,
                "moving footer toward the lower edge frees precisely that much content space");
            Check(Math.Abs(settings.FooterBottomInsetMm-(settings.PaddingMm-1))<.002,
                "independent footer inset applies to footer drawing");
            settings.FooterBottomShiftMm=0;

            // Header geometry is shared by pagination and drawing. All header
            // variants should expose non-overlapping horizontal blocks.
            var headerRecord=Record("Long example title for layout measurement");
            var noGroup=CardHeaderLayout.Measure(settings,headerRecord);
            Check(noGroup.Group is null,"hidden group keeps original one-row header");
            Check(noGroup.Title.X + noGroup.Title.Width <= noGroup.BadgeX+.001f,
                "title never overlaps page-count badge");
            settings.HeaderGapMm=0;
            var noGap=CardHeaderLayout.Measure(settings,headerRecord);
            settings.HeaderGapMm=5;
            var withGap=CardHeaderLayout.Measure(settings,headerRecord);
            Check(Math.Abs(noGap.DecorationBottom-withGap.DecorationBottom)<.001f,
                "header decoration must be independent of spacing below it");
            Check(Math.Abs(noGap.Title.Y-withGap.Title.Y)<.001f &&
                  Math.Abs(noGap.IconY-withGap.IconY)<.001f,
                "header title and icon do not move when changing gap below header");
            Check(Math.Abs((withGap.ContentY-noGap.ContentY)-5f)<.001f,
                "header gap moves content by the requested amount only");
            Check(Math.Abs((noGap.RowY+noGap.RowHeight*.5f)-
                  (.9f+noGap.DecorationBottom)*.5f)<.001f,
                "text/icon/badge share the decorated header's vertical centre");
            settings.HeaderGapMm=-5;
            var negativeGap=CardHeaderLayout.Measure(settings,headerRecord);
            Check(Math.Abs(negativeGap.ContentY-negativeGap.DecorationBottom)<.001f,
                "negative gap never distorts visual header geometry");
            settings.HeaderGapMm=4.1;
            settings.HeaderHeightMm=0;
            var autoHeader=CardHeaderLayout.Measure(settings,headerRecord);
            settings.HeaderHeightMm=autoHeader.DecorationBottom-.9f+4f;
            var enlargedHeader=CardHeaderLayout.Measure(settings,headerRecord);
            Check(Math.Abs((enlargedHeader.DecorationBottom-autoHeader.DecorationBottom)-4f)<.002f,
                "manually increasing header height changes decorated area, not gap");
            Check(Math.Abs(enlargedHeader.ContentY-autoHeader.ContentY-4f)<.002f,
                "header height reduces printable body height by the same amount");
            Check(Math.Abs((enlargedHeader.RowY+enlargedHeader.RowHeight*.5f)-
                (.9f+enlargedHeader.DecorationBottom)*.5f)<.002f,
                "larger header centres icon, title and badge as a single row");
            settings.HeaderHeightMm=1;
            var tinyHeader=CardHeaderLayout.Measure(settings,headerRecord);
            Check(Math.Abs(tinyHeader.DecorationBottom-autoHeader.DecorationBottom)<.002f,
                "undersized header height must not clip the contents");
            settings.HeaderHeightMm=0;
            // Fine adjustments and boundary clamping in the UI editor.
            var smallGapField=AppearanceDescriptor.Create(settings)
                .First(f=>f.Key=="header_group_gap_mm");
            double before=settings.HeaderGroupGapMm;
            smallGapField.Nudge(1);
            Check(Math.Abs(settings.HeaderGroupGapMm-before-.05)<.0001,
                "group gap step is 0.05 mm");
            var accentField=AppearanceDescriptor.Create(settings)
                .First(f=>f.Key=="header_accent_width_mm");
            before=settings.HeaderAccentWidthMm;
            accentField.Nudge(-1);
            Check(Math.Abs(settings.HeaderAccentWidthMm-before+.05)<.0001,
                "accent width step is 0.05 mm");
            var uuidField=AppearanceDescriptor.Create(settings)
                .First(f=>f.Key=="footer_uuid_length");
            int beforeLength=settings.FooterUuidLength;
            uuidField.Nudge(1);
            Check(settings.FooterUuidLength==beforeLength+1,
                "UUID length step is one character");
            settings.FooterUuidLength=12;
            settings.HeaderGroupGapMm=.75;
            settings.HeaderAccentWidthMm=.75;
            var brokenHeader=new CardAppearance
            {
                HeaderGapMm=-10,HeaderHeightMm=-2,HeaderTopMm=-2,HeaderAccentWidthMm=-1,FooterUuidLength=40
            };
            brokenHeader.NormalizeHeaderFooter();
            Check(brokenHeader.HeaderHeightMm==0 && brokenHeader.HeaderGapMm==0 && brokenHeader.HeaderTopMm>=.9 &&
                brokenHeader.HeaderAccentWidthMm>=.2 && brokenHeader.FooterUuidLength==32,
                "invalid header/footer values normalize on load");
            settings.HeaderGroupShow=true;
            settings.HeaderGroupPosition="top";
            var above=CardHeaderLayout.Measure(settings,headerRecord);
            Check(above.Group is not null && above.Group.Y<above.Title.Y,
                "group above entry name");
            Check(above.ContentY>noGroup.ContentY,"group above contributes to header height");
            settings.HeaderGroupPosition="left";
            var beside=CardHeaderLayout.Measure(settings,headerRecord);
            Check(beside.Group is not null && beside.Group.X+beside.Group.Width<beside.Title.X,
                "group and title do not overlap when side by side");
            Check(beside.IconY>=beside.RowY-.001f,"icon centered in shared header row");
            Check(Math.Abs((beside.RowY+beside.RowHeight/2)-(beside.IconY+beside.IconSize/2))<.001f,
                "icon's centre matches header centre");
            settings.HeaderGroupShow=false;
            Check(CardHeaderLayout.Measure(settings,headerRecord).Group is null,
                "disabled group does not occupy title space");

            var firstGuid=Guid.Parse("a3f18b72-4d6e-49c2-b017-8d5e6f9042ac");
            var secondGuid=Guid.Parse("a3f18b72-4d6e-48c2-b017-8d5e6f9042ac");
            var prefixRecords=new[]{new VaultRecord{Uuid=firstGuid},new VaultRecord{Uuid=secondGuid}};
            var unique=CardUuidFormatter.UniquePrefixLengths(prefixRecords,12);
            Check(unique[firstGuid]>12 && unique[secondGuid]>12,"conflicting UUID prefixes are expanded");
            Check(unique[firstGuid]<32,"UUID prefixes do not expand to full length unnecessarily");
            Check(CardUuidFormatter.Format(firstGuid,settings,unique[firstGuid])!=
                  CardUuidFormatter.Format(secondGuid,settings,unique[secondGuid]),
                  "printed UUID prefixes are unique");
            settings.FooterUuidLength=12;
            settings.FooterUuidPrefix=false;
            settings.FooterUuidSeparators="every4";
            Check(CardUuidFormatter.Format(firstGuid,settings)=="A3F1-8B72-4D6E",
                "UUID grouping in blocks of four");
            settings.FooterUuidSeparators="standard";
            settings.FooterUuidLength=32;
            Check(CardUuidFormatter.Format(firstGuid,settings)=="A3F18B72-4D6E-49C2-B017-8D5E6F9042AC",
                "full standard UUID representation");
            settings.FooterUuidLength=12;
            settings.FooterUuidSeparators="none";
            settings.FooterUuidUppercase=false;
            Check(CardUuidFormatter.Format(firstGuid,settings)=="a3f18b724d6e",
                "lowercase UUID format without separators");
            settings.FooterUuidUppercase=true;
            settings.FooterUuidPrefix=true;
            settings.FooterUuidLength=12;
            settings.HeaderGroupShow=true;
            settings.HeaderGroupPosition="top";
            var headerCards=CardLayoutEngine.Build([headerRecord],settings,BasicMapping);
            LayoutFits(headerCards,settings);
            foreach(var card in headerCards)
            {
                var image=Picture(card,settings);
                Array.Clear(image);
            }
            settings.HeaderGroupShow=false;
            Check(FontWeights.Parse("400") == 400 && FontWeights.Parse("700") == 700,
                  "numeric font weights are parsed exactly");
            Check(FontWeights.Normalize("475") == "500" && FontWeights.Normalize("400") == "400",
                  "font weights normalize to supported instances");
            foreach(var family in new[]{"Atkinson Hyperlegible Mono","JetBrains Mono NL","Recursive Mono","IBM Plex Mono","Source Code Pro","Geist Mono","Cascadia Mono","Atkinson Hyperlegible Next","Recursive"})
            {
                foreach(var weight in FontWeights.Choices)
                {
                    var face=CardTextMetrics.Typeface(family,weight);
                    // A variable font's PostScriptName need not change with wght:
                    // verifying distinct name strings is not a valid style test.
                    Check(!string.IsNullOrWhiteSpace(face.FamilyName),"font face loaded: "+family+" "+weight);
                }
                using (var lightFont=new SKFont(CardTextMetrics.Typeface(family,"200"),52f))
                using (var darkFont=new SKFont(CardTextMetrics.Typeface(family,"800"),52f))
                using (var lightBitmap=new SKBitmap(520,90))
                using (var darkBitmap=new SKBitmap(520,90))
                using (var paint=new SKPaint { IsAntialias=true,Color=SKColors.Black })
                {
                    using(var surface=new SKCanvas(lightBitmap)) {
                        surface.Clear(SKColors.White);
                        surface.DrawText("0O8BILl1",4,65,SKTextAlign.Left,lightFont,paint);
                    }
                    using(var surface=new SKCanvas(darkBitmap)) {
                        surface.Clear(SKColors.White);
                        surface.DrawText("0O8BILl1",4,65,SKTextAlign.Left,darkFont,paint);
                    }
                    using var lightImage=SKImage.FromBitmap(lightBitmap);
                    using var darkImage=SKImage.FromBitmap(darkBitmap);
                    using var lightData=lightImage.Encode(SKEncodedImageFormat.Png,100);
                    using var darkData=darkImage.Encode(SKEncodedImageFormat.Png,100);
                    Check(lightData is not null && darkData is not null &&
                        !SHA256.HashData(lightData.ToArray()).SequenceEqual(SHA256.HashData(darkData.ToArray())),
                        "font weight changes raster rather than only a label: "+family);
                }
                float thin=CardTextMetrics.MeasureMm("0O8BILl1",8,family,"200");
                float heavy=CardTextMetrics.MeasureMm("0O8BILl1",8,family,"800");
                Check(thin>0 && heavy>0,"font text measurement: "+family);
            }

            Check(PortableFonts.SecretFamilies.Length == 7 && !PortableFonts.IsSecretFont("Recursive") &&
                PortableFonts.IsSecretFont("Cascadia Mono") && !PortableFonts.IsPortableFont("Cascadia Code"),
                "seven monospaced value families and separate proportional Recursive");
            var fontSettings = JsonSerializer.Deserialize("{\"secret_font\":\"IBM Plex Mono\"}",
                CardAppearanceJsonContext.Default.CardAppearance);
            Check(fontSettings?.SecretFont == "IBM Plex Mono", "monospace font selection survives JSON load");
            var cascadiaNarrow=CardTextMetrics.MeasureMm("iiii",9,"Cascadia Mono","400");
            var cascadiaWide=CardTextMetrics.MeasureMm("WWWW",9,"Cascadia Mono","400");
            Check(Math.Abs(cascadiaNarrow-cascadiaWide)<0.12f,
                "Cascadia Mono must preserve equal-width glyphs for secret values");
            var monoNarrow=CardTextMetrics.MeasureMm("iiii",9,"Recursive Mono","400");
            var monoWide=CardTextMetrics.MeasureMm("WWWW",9,"Recursive Mono","400");
            var textNarrow=CardTextMetrics.MeasureMm("iiii",9,"Recursive","400");
            var textWide=CardTextMetrics.MeasureMm("WWWW",9,"Recursive","400");
            Check(Math.Abs(monoNarrow-monoWide)<0.12f, "Recursive Mono MONO=1 has equal character widths");
            Check(Math.Abs(textNarrow-textWide)>0.5f, "plain Recursive MONO=0 is proportional");

            var nextTitle=CardTextMetrics.MeasureMm("Пример / Example",9,"Atkinson Hyperlegible Next","700");
            Check(nextTitle>0,"Cyrillic fallback in Atkinson Next title");
            var json=JsonSerializer.Serialize(settings,CardAppearanceJsonContext.Default.CardAppearance);
            var restored=JsonSerializer.Deserialize(json,CardAppearanceJsonContext.Default.CardAppearance);
            Check(restored is not null && restored.WidthMm==settings.WidthMm,"source-generated AOT JSON roundtrip");
            var one=CardLayoutEngine.Build([Record()],settings,BasicMapping);
            Check(one.Count==1,"single record layout");
            LayoutFits(one,settings);
            var basic=Picture(one[0],settings);
            Check(basic.Length>1000,"nonempty PNG image");
            Array.Clear(basic);

            // Every bundled value font must preserve the optional printed zero marker.
            foreach (var zeroFont in PortableFonts.SecretFamilies)
            {
                var markedAppearance = Defaults();
                markedAppearance.SecretFont = zeroFont;
                markedAppearance.MarkZero = false;
                var markedCard = CardLayoutEngine.Build([Record("Zero marker")],markedAppearance,BasicMapping)[0];
                var normalZero = Picture(markedCard,markedAppearance);
                markedAppearance.MarkZero = true;
                var dottedZero = Picture(markedCard,markedAppearance);
                Check(!SHA256.HashData(normalZero).SequenceEqual(SHA256.HashData(dottedZero)),
                      "mark_zero toggles the 0 centre dot: " + zeroFont);
                Array.Clear(normalZero);
                Array.Clear(dottedZero);
            }

            // Unbroken UTF-8 and mixed-case secret must be printed in full, not clipped or lowercased.
            string longText=string.Concat(Enumerable.Repeat("aB0J9kLm4ZéΩ",80));
            var longRecord=Record("Long value");
            longRecord.AdditionalFields["LongToken"]=longText;
            var onlySecret=new Dictionary<string,FieldRule>(StringComparer.OrdinalIgnoreCase)
            { ["LongToken"]=new FieldRule{Role="secret",Label="Long Token",Section="auth",Order=80,Fit="wrap",GroupMode="off"} };
            settings=Defaults();settings.PrintUsername=false;settings.PrintPassword=false;settings.PrintUrl=false;
            var longCards=CardLayoutEngine.Build([longRecord],settings,onlySecret);
            Check(longCards.Count>1,"long secret continues to another physical card");
            var reconstructed=string.Concat(All(longCards).Where(l=>l.Field=="LongToken" && l.Kind==CardLineKind.Value)
                .Select(l=>l.Text));
            Check(reconstructed==longText,"case-safe lossless long secret");

            // Empty mapped values must not create captions, recovery rows, or
            // continuation cards, even if visibility was explicitly "always".
            // Use the same multipage secret as the control to detect phantom pages.
            var withBlankFields=Record("Long value");
            withBlankFields.AdditionalFields["LongToken"]=longText;
            withBlankFields.AdditionalFields["RECOVER_CODES"]=" \r\n\t\n ";
            withBlankFields.AdditionalFields["EMPTY_CUSTOM"]="  \t ";
            var blankRules=new Dictionary<string,FieldRule>(onlySecret,StringComparer.OrdinalIgnoreCase)
            {
                ["RECOVER_CODES"]=new FieldRule
                {Role="codes",Label="RECOVER_CODES",Section="recovery",Order=90,Visibility="always"},
                ["EMPTY_CUSTOM"]=new FieldRule
                {Role="text",Label="EMPTY_CUSTOM",Section="extra",Order=91,Visibility="always"}
            };
            var withoutBlank=CardLayoutEngine.Build([withBlankFields],settings,blankRules);
            Check(withoutBlank.Count==longCards.Count && withoutBlank.Count>1,
                "blank mapped fields never add continuation cards");
            Check(All(withoutBlank).Select(l=>(l.Kind,l.Text,l.Field))
                .SequenceEqual(All(longCards).Select(l=>(l.Kind,l.Text,l.Field))),
                "blank mapped fields add no labels, headings, gaps, or recovery rows");
            Check(withoutBlank.All(c=>c.Front.Lines.Any(l=>l.Kind==CardLineKind.Value ||
                l.Kind==CardLineKind.Recovery || l.Kind==CardLineKind.Qr)),
                "every physical continuation contains actual values");

            var emptyEntry=new VaultRecord {Uuid=Guid.Parse("AAAAAAAA-0000-0000-0000-000000000001"),
                Title="Empty print content",AdditionalFields=new(StringComparer.OrdinalIgnoreCase)
                { ["RECOVER_CODES"]="\t\n ", ["EMPTY_CUSTOM"]=" " }};
            var noCards=CardLayoutEngine.Build([emptyEntry],Defaults(),blankRules);
            Check(noCards.Count==0,"records with no printable values generate no blank cards");
            LayoutFits(longCards,settings);
            foreach(var c in longCards)
            {
                var bytes=Picture(c,settings);
                Array.Clear(bytes);
            }

            // Recovery codes form complete cells, including internal line wraps.
            var codes=Enumerable.Range(0,11).Select(i=>"AB"+i.ToString("D2")+new string((char)('A'+i),35)).ToArray();
            var backup=Record("Codes");
            backup.AdditionalFields["RecoveryCodes"]=string.Join('\n',codes);
            settings=Defaults();settings.RecoveryColumns=3;
            settings.RecoveryAutosize=false;settings.PrintPassword=false;
            var rules=new Dictionary<string,FieldRule>{["RecoveryCodes"]=new FieldRule
                {Role="codes",Label="Codes",Section="recovery",Order=60,GroupMode="off"}};
            var rec=CardLayoutEngine.Build([backup],settings,rules);
            LayoutFits(rec,settings);
            var recoveryLines=All(rec).Where(l=>l.Kind==CardLineKind.Recovery).ToArray();
            var actual=recoveryLines
                .SelectMany(l=>l.Cells??[]).Where(s=>s.Length>0).Select(s=>s.Replace("\n", ""))
                .OrderBy(s=>s).ToArray();
            Check(actual.SequenceEqual(codes.OrderBy(s=>s)),"whole recovery codes, no missing or duplicated codes");
            Check(recoveryLines.Any(l => (l.Cells?.Length ?? 0) >= 2),"recovery uses multiple columns when it fits");
            // A checkbox is aligned to the actual glyph bounding box of its code,
            // not to an arbitrary fraction of the row height.
            const string testCode="AB01CD67";
            const float fontPx=24f, baseline=80f, checkboxSide=14f;
            using(var faceFont=new SKFont(CardTextMetrics.Typeface(settings.SecretFont,settings.ValueWeight),fontPx))
            {
                faceFont.MeasureText(testCode,out SKRect bounds);
                float checkboxTop=CardSkiaRenderer.RecoveryCheckboxTop(testCode,settings.SecretFont,
                    settings.ValueWeight,fontPx,baseline,checkboxSide);
                Check(Math.Abs((checkboxTop+checkboxSide/2)-(baseline+(bounds.Top+bounds.Bottom)/2))<.01f,
                    "checkbox centred on actual code ink");
            }
            // Compare actual rendered pixels with the same layout and only the
            // checkbox toggle changed. No real secrets or images touch disk.
            settings.RecoveryCheckboxes=true;
            var withBoxes=Picture(rec[0],settings);
            settings.RecoveryCheckboxes=false;
            var withoutBoxes=Picture(rec[0],settings);
            Check(!SHA256.HashData(withBoxes).SequenceEqual(SHA256.HashData(withoutBoxes)),
                "visible recovery checkbox toggle");
            Array.Clear(withBoxes);Array.Clear(withoutBoxes);
            settings.RecoveryCheckboxes=true;
            foreach(var c in rec)
            {
                var image=Picture(c,settings);
                Array.Clear(image);
            }

            // KeePassXC additional URLs must be resolved in stable browser-integration order.
            Check(UrlSourceSelector.IsAdditionalUrlAttribute("KP2A_URL") &&
                UrlSourceSelector.IsAdditionalUrlAttribute("KP2A_URL_1") &&
                !UrlSourceSelector.IsAdditionalUrlAttribute("URL") &&
                !UrlSourceSelector.IsAdditionalUrlAttribute("CUSTOM_URL"),
                "only KeePassXC browser-integration attributes are additional URL sources");
            Check(UrlSourceSelector.AdditionalUrlOrder("KP2A_URL") <
                UrlSourceSelector.AdditionalUrlOrder("KP2A_URL_1") &&
                UrlSourceSelector.AdditionalUrlOrder("KP2A_URL_1") <
                UrlSourceSelector.AdditionalUrlOrder("KP2A_URL_2"),
                "KeePassXC additional URL precedence follows numbered attributes");
            // URL source is a mapping decision, independent from domain display.
            var urlCase=new VaultRecord {Url="",AdditionalUrls=["*", "https://accounts.example.invalid/path"]};
            var urlRule=new Dictionary<string,FieldRule>(StringComparer.OrdinalIgnoreCase)
                {["URL"]=new FieldRule {UrlSource="fallback"}};
            Check(CardLayoutEngine.ResolveMappedUrl(urlCase,urlRule)=="https://accounts.example.invalid/path",
                "URL falls back to first valid additional KeePassXC address");
            urlRule["URL"].UrlSource="main";
            Check(CardLayoutEngine.ResolveMappedUrl(urlCase,urlRule)=="", "primary-only URL does not invent a value");
            urlRule["URL"].UrlSource="extra";
            Check(CardLayoutEngine.ResolveMappedUrl(urlCase,urlRule)=="https://accounts.example.invalid/path",
                "additional-only URL skips unusable patterns");

            // URL source regression: KeePassXC extra URLs often contain browser
            // wildcard match patterns. Visibility=nonempty must evaluate the
            // resolved URL, not the empty original main URL.
            Check(UrlSourceSelector.NormalizePrintableUrl("https://service.example.invalid/*") ==
                  "https://service.example.invalid", "wildcard path yields concrete printable host");
            Check(UrlSourceSelector.NormalizePrintableUrl("*://*.sample.example.invalid/login*") ==
                  "https://sample.example.invalid", "wildcard scheme and subdomain yield printable site");
            Check(UrlSourceSelector.NormalizePrintableUrl("service.example.invalid/login") ==
                  "https://service.example.invalid/login", "scheme-less URL is normalized for domain display");
            Check(UrlSourceSelector.NormalizePrintableUrl("service.example.invalid:8443") ==
                  "https://service.example.invalid:8443", "host with numeric port remains valid");
            // Exact-match syntax in KeePassXC extra URLs uses balanced
            // surrounding quotation marks. They must not become part of the
            // URI host, and the database's original value is never changed.
            Check(UrlSourceSelector.NormalizePrintableUrl("\"https://exact.example.invalid/login\"") ==
                  "https://exact.example.invalid/login", "quoted exact-match extra URL is printable");
            Check(UrlSourceSelector.NormalizePrintableUrl("  \"https://masked.example.invalid/*\"  ") ==
                  "https://masked.example.invalid", "quoted wildcard URL produces printable domain");
            Check(UrlSourceSelector.NormalizePrintableUrl("“https://curly.example.invalid/path”") ==
                  "https://curly.example.invalid/path", "paired typographic quotes are normalized");
            Check(!UrlSourceSelector.IsConcreteUrl("\"https://unbalanced.example.invalid/path") &&
                  !UrlSourceSelector.IsConcreteUrl("\"https://embedded.example.invalid/\"broken\""),
                  "unbalanced or embedded quotes cannot create URL values");
            Check(!UrlSourceSelector.IsConcreteUrl("*://*/*") &&
                  !UrlSourceSelector.IsConcreteUrl("javascript:alert(1)"),
                  "invalid or non-web patterns do not create printable URLs");
            var urlFields = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["kp2a_url_2"]="\"https://later.example.invalid/*\"",
                ["kp2a_url"]="\"https://first.example.invalid/*\"",
                ["kp2a_url_1"]="*://*.second.example.invalid/*",
                ["Other"]="https://other.example.invalid"
            };
            var extracted = UrlSourceSelector.ExtractAdditionalUrls(urlFields);
            Check(extracted.SequenceEqual(new[]{"https://first.example.invalid",
                "https://second.example.invalid", "https://later.example.invalid"}),
                "extra URL order and safe host extraction");
            var quotedExtraFields = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["KP2A_URL"] = "\"https://one.example.invalid/signin\"",
                ["KP2A_URL_1"] = "\"https://two.example.invalid/*\""
            };
            var quotedExtracted = UrlSourceSelector.ExtractAdditionalUrls(quotedExtraFields);
            Check(quotedExtracted.Length == 2 &&
                  quotedExtracted[0] == "https://one.example.invalid/signin" &&
                  quotedExtracted[1] == "https://two.example.invalid",
                  "both exact-match quoted additional URL fields are recognized");
            var sameHostQuoted = UrlSourceSelector.ExtractAdditionalUrls(
                new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["KP2A_URL"] = "\"https://shared.example.invalid/a/*\"",
                    ["KP2A_URL_1"] = "\"https://shared.example.invalid/b/*\""
                });
            Check(sameHostQuoted.Length == 2 && sameHostQuoted.All(x =>
                  x == "https://shared.example.invalid"),
                  "two valid quoted match patterns count as two recognized entries even on one domain");
            var quotedRecord = new VaultRecord
            {
                Url = "", AdditionalUrls = quotedExtracted, AdditionalUrlCandidateCount = 2,
                Group = "Examples", Title = "Quoted URL fixture"
            };
            var quotedResult = UrlSourceSelector.ResolveDetailed(quotedRecord, "fallback");
            Check(quotedResult.AdditionalCandidates == 2 && quotedResult.AdditionalRecognized == 2 &&
                  quotedResult.Source == "extra", "two quoted URLs resolve as recognized fallback sources");
            Check(UrlSourceSelector.ExplainForMapping(quotedRecord, "fallback", "nonempty", true, "ru")
                  .Contains("распознано: 2/2", StringComparison.Ordinal),
                  "URL diagnostic reports accepted quoted extra sources without printing their values");
            var urlOnlyExtra = new VaultRecord
            {
                Url="", AdditionalUrls=["https://first.example.invalid/*"],
                Group="Examples", Title="Extra URL fixture"
            };
            var urlVisible = new Dictionary<string,FieldRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["URL"] = new FieldRule {Role="text", Section="account", Label="Адрес",
                    UrlSource="fallback", UrlMode="smart", Visibility="nonempty", ShowLabel=false}
            };
            var urlAppearance = Defaults();
            urlAppearance.PrintUrl = true;
            var urlLines = All(CardLayoutEngine.Build([urlOnlyExtra], urlAppearance, urlVisible));
            Check(urlLines.Any(line => line.Field=="URL" && line.Kind==CardLineKind.Value &&
                line.Text.Contains("first.example.invalid", StringComparison.Ordinal)),
                "nonempty URL visibility uses fallback from wildcard additional URL");
            var quotedLines = All(CardLayoutEngine.Build([quotedRecord], urlAppearance, urlVisible));
            Check(quotedLines.Any(line => line.Field == "URL" && line.Kind == CardLineKind.Value &&
                line.Text.Contains("one.example.invalid", StringComparison.Ordinal)),
                "quoted additional URL is actually printed when primary is empty");
            urlVisible["URL"].UrlSource = "main";
            var missingMainLines = All(CardLayoutEngine.Build([urlOnlyExtra], urlAppearance, urlVisible));
            Check(!missingMainLines.Any(line=>line.Field=="URL"),
                "primary-only plus empty URL correctly hides field");
            urlVisible["URL"].UrlSource = "extra";
            var extraOnlyLines = All(CardLayoutEngine.Build([urlOnlyExtra], urlAppearance, urlVisible));
            Check(extraOnlyLines.Any(line=>line.Field=="URL" && line.Kind==CardLineKind.Value),
                "extra-only URL displays even without primary URL");

            // End-to-end URL print gates: don't incorrectly blame wildcard parsing
            // when the appearance template independently disables the URL field.
            var diagnosticRecord = new VaultRecord { Url = "", AdditionalUrls = ["https://demo.example.invalid/*"],
                AdditionalUrlCandidateCount = 2 };
            var urlDecision = UrlSourceSelector.ResolveDetailed(diagnosticRecord, "fallback");
            Check(urlDecision.Source == "extra" && urlDecision.Url == "https://demo.example.invalid" &&
                urlDecision.AdditionalCandidates == 2 && urlDecision.AdditionalRecognized == 1,
                "diagnostic and renderer share one URL source resolver");
            Check(UrlSourceSelector.ExplainForMapping(diagnosticRecord, "fallback", "nonempty", false, "ru")
                .Contains("выключена в шаблоне", StringComparison.Ordinal),
                "disabled appearance URL is reported as the actual print blocker");
            Check(UrlSourceSelector.ExplainForMapping(diagnosticRecord, "fallback", "nonempty", true, "ru")
                .Contains("дополнительный URL", StringComparison.Ordinal),
                "printable additional URL is reported without including its value");
            Check(!UrlSourceSelector.ExplainForMapping(diagnosticRecord, "fallback", "nonempty", true, "ru")
                .Contains("example.invalid", StringComparison.Ordinal),
                "URL diagnostics must never disclose actual hosts or paths");
            var appearanceWithoutUrl = Defaults(); appearanceWithoutUrl.PrintUrl = false;
            var disabledUrlLines = All(CardLayoutEngine.Build([diagnosticRecord], appearanceWithoutUrl,
                new Dictionary<string,FieldRule>(StringComparer.OrdinalIgnoreCase) {
                    ["URL"] = new FieldRule { Visibility = "nonempty", UrlSource = "fallback",
                        ShowLabel = false, UrlMode = "smart", Role="text", Section="account" }
                }));
            Check(!disabledUrlLines.Any(l => l.Field == "URL"),
                "URL print gate in appearance cannot be bypassed by mapping");

            // Printed phone groups use precisely measured spacing, not literal
            // spaces. Synthetic fixtures only: no real account data is used.
            var phoneSample=new VaultRecord
            {
                Uuid=Guid.Parse("22222222-3333-4444-5555-666666666666"),
                Title="Telephone spacing fixture", Group="Examples", UserName="+380000000000"
            };
            var phoneAppearance=Defaults();
            phoneAppearance.GroupSpaces=.2;
            var phoneRules=new Dictionary<string,FieldRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["USERNAME"]=new FieldRule {Role="secret",Section="account",Order=10,
                    Label="Login",ShowLabel=true,PhoneFormat=true,GroupSpaces=.2}
            };
            var phoneCards=CardLayoutEngine.Build([phoneSample],phoneAppearance,phoneRules);
            var phoneLines=All(phoneCards).Where(l=>l.Field=="USERNAME" && l.Kind==CardLineKind.Value).ToArray();
            Check(phoneLines.Length>0 && phoneLines.All(l=>l.GroupedValue is {Length:>0}),
                "phone login draws separate measured groups");
            Check(phoneLines.SelectMany(l=>l.GroupedValue!).SequenceEqual(
                PhoneLoginFormatter.PrintableGroups(PhoneLoginFormatter.Format(phoneSample.UserName,true))!),
                "phone groups preserve every printed character in their original order");
            var phoneFirst=phoneLines[0];
            var phoneWeight=phoneFirst.Weight=="inherit"?phoneAppearance.ValueWeight:phoneFirst.Weight;
            float expectedPhoneGap=CardTextMetrics.MeasureMm(" ",phoneFirst.FontPt,
                phoneAppearance.SecretFont,phoneWeight)*.2f;
            Check(phoneLines.All(l=>Math.Abs(l.GroupGapMm-expectedPhoneGap)<.001f),
                "phone gap uses same exact 0.2 fractional space as recovery and OTP");
            Check(phoneLines.All(l=>!l.GroupedValue!.Any(g=>g.Contains(' '))),
                "no full-space glyphs are drawn between phone groups");
            // Phone grouping has a compact default even when other fields use larger spaces.
            phoneRules["USERNAME"].GroupSpaces=0;
            phoneAppearance.GroupSpaces=1;
            var phoneDefault=All(CardLayoutEngine.Build([phoneSample],phoneAppearance,phoneRules))
                .First(l=>l.Field=="USERNAME" && l.Kind==CardLineKind.Value);
            Check(Math.Abs(phoneDefault.GroupGapMm-expectedPhoneGap)<.001f,
                "phone groups use compact 0.2 spacing by default");
            phoneRules["USERNAME"].GroupSpaces=.5;
            var phoneCustom=All(CardLayoutEngine.Build([phoneSample],phoneAppearance,phoneRules))
                .First(l=>l.Field=="USERNAME" && l.Kind==CardLineKind.Value);
            Check(Math.Abs(phoneCustom.GroupGapMm-expectedPhoneGap*2.5f)<.001f,
                "USERNAME mapping can override compact phone spacing");
            phoneRules["USERNAME"].GroupSpaces=.2;
            phoneAppearance.GroupSpaces=.2;
            foreach(var c in phoneCards){var bytes=Picture(c,phoneAppearance);Array.Clear(bytes);}
            phoneRules["USERNAME"].PhoneFormat=false;
            var plainPhone=All(CardLayoutEngine.Build([phoneSample],phoneAppearance,phoneRules))
                .Single(l=>l.Field=="USERNAME" && l.Kind==CardLineKind.Value);
            Check(plainPhone.GroupedValue is null && plainPhone.Text==phoneSample.UserName,
                "phone gap formatting is disabled when the mapping toggle is off");
            var emailSample=new VaultRecord
            {
                Uuid=Guid.Parse("33333333-4444-5555-6666-777777777777"),
                Title="Email spacing fixture", Group="Examples", UserName="sample@example.invalid"
            };
            phoneRules["USERNAME"].PhoneFormat=true;
            var emailLine=All(CardLayoutEngine.Build([emailSample],phoneAppearance,phoneRules))
                .Single(l=>l.Field=="USERNAME" && l.Kind==CardLineKind.Value);
            Check(emailLine.GroupedValue is null && emailLine.Text==emailSample.UserName,
                "ordinary login/email are never treated as phone groups");
            var shortWidth=CardTextMetrics.MeasureMm("+380 (50)",9f,phoneAppearance.SecretFont,"400")*.82f;
            var wrappedPhone=CardLayoutEngine.PackMeasuredGroups(
                ["+380","(50)","123","45","67"],.2,shortWidth,9f,
                phoneAppearance.SecretFont,"400",0,out var tinyGap);
            Check(wrappedPhone is { Length: > 1 } &&
                wrappedPhone.SelectMany(row=>row).SequenceEqual(new[]{"+380","(50)","123","45","67"}),
                "narrow phone columns wrap only between whole groups");

            // Fractional recovery spacing must use actual glyph widths, even if
            // the imported codes already contain whitespace between clusters.
            var spacedCodes=Record("Recovery spacing fixture");
            spacedCodes.AdditionalFields["BackupCodes"]="7c0f 9413\n294b b861\n5652 74cb\n644c 4c12";
            settings=Defaults();settings.RecoveryColumns=2;
            var recoveryRule=new Dictionary<string,FieldRule> { ["BackupCodes"]=new FieldRule
                {Role="codes",Section="recovery",GroupMode="on",GroupSize=4,GroupSpaces=.2} };
            var spacedLines=All(CardLayoutEngine.Build([spacedCodes],settings,recoveryRule))
                .Where(l=>l.Kind==CardLineKind.Recovery).ToArray();
            Check(spacedLines.Length>0 && spacedLines.All(l=>l.RecoveryGrouped),
                "recovery uses measured groups, not inserted hair spaces");
            var oneRecovery=spacedLines[0];
            var expectedRecoveryGap=CardTextMetrics.MeasureMm(" ",oneRecovery.FontPt,settings.SecretFont,
                oneRecovery.Weight=="inherit"?settings.ValueWeight:oneRecovery.Weight)*.2f;
            Check(Math.Abs(oneRecovery.RecoveryGroupGapMm-expectedRecoveryGap)<.001f,
                "recovery 0.2 spacing matches real space-glyph fraction");

            // Both QR layouts operate on a fictional TOTP seed, never a production secret.
            var otp=Record("QR fixture");
            otp.AdditionalFields["TOTP Secret"]="JBSWY3DPEHPK3PXP";
            otp.AdditionalFields["TOTP Digits"]="6";
            otp.AdditionalFields["TOTP Issuer"]="Example";
            var otpRules=new Dictionary<string,FieldRule>{["TOTP Secret"]=new FieldRule
                {Role="totp",Label="TOTP",Section="auth",Order=50}};
            foreach(string position in new[]{"center","left","right"})
            {
                settings=Defaults();settings.TotpQrEnabled=true;settings.TotpQrPosition=position;
                var otpCards=CardLayoutEngine.Build([otp],settings,otpRules);
                var qr=All(otpCards).Single(l=>l.Kind==CardLineKind.Qr);
                Check(qr.QrInline==(position!="center"),"QR position applied: "+position);
                Check(qr.QrUri is not null && qr.QrUri.Contains("otpauth://totp/",StringComparison.Ordinal),"QR data");
                LayoutFits(otpCards,settings);
                foreach(var c in otpCards){var bytes=Picture(c,settings);Array.Clear(bytes);}
            }

            // Regression: fractional inter-group gap must not become multiple Unicode
            // hair spaces or cause broken groups beside a QR. All data is fictional.
            var groupedOtp=Record("TOTP grouping fixture");
            const string inventedOtp="JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
            groupedOtp.AdditionalFields["TOTP Secret"]=inventedOtp;
            groupedOtp.AdditionalFields["TOTP Issuer"]="Example";
            groupedOtp.AdditionalFields["TOTP Digits"]="6";
            groupedOtp.AdditionalFields["TOTP Period"]="30";
            var groupedRules=new Dictionary<string,FieldRule>
            {
                ["TOTP Secret"]=new FieldRule
                {
                    Role="totp",Label="TOTP SECRET",Section="auth",Order=50,
                    GroupMode="on",GroupSize=4,GroupSpaces=.2
                }
            };
            foreach(var position in new[]{"left","right"})
            {
                settings=Defaults();settings.TotpQrEnabled=true;settings.TotpQrPosition=position;
                var groupedCards=CardLayoutEngine.Build([groupedOtp],settings,groupedRules);
                var qr=All(groupedCards).Single(l=>l.Kind==CardLineKind.Qr);
                Check(qr.QrInline && qr.QrGroupedRows is { Length:>0 },
                    "TOTP groups are composed atomically next to QR: "+position);
                var qrCaption=qr.Label ?? throw new InvalidOperationException("TOTP QR-side caption was not generated: "+position);
                Check(qrCaption.Contains("TOTP SECRET",StringComparison.Ordinal),
                    "side QR includes TOTP label in the same vertically aligned block: "+position);
                Check(!All(groupedCards).Any(l=>l.Kind==CardLineKind.Label && l.Field=="TOTP Secret"),
                    "side QR does not reserve a duplicate full-width label above the QR: "+position);
                float qrInkTop=CardLayoutEngine.InlineQrLabelInkTopMm(qrCaption.Split('\n')[0],
                    (float)(settings.LabelSizePt*CardLayoutEngine.DensityScale(settings)),
                    Math.Max(2.1f,(float)(settings.LabelSizePt*CardLayoutEngine.DensityScale(settings)*
                        CardLayoutEngine.MmPerPoint*Math.Clamp(settings.LineSpacing,.8,2)*1.25)),settings);
                Check(qrInkTop>=0 && qrInkTop<qr.HeightMm-qr.FontPt,
                    "QR label ink-top is measurable: "+position);
                float firstBlackRow=CardSkiaRenderer.QrFirstBlackRowFraction(qr.QrUri!);
                Check(firstBlackRow>0 && firstBlackRow<.25f &&
                      Math.Abs(firstBlackRow-qr.QrFirstBlackRowFraction)<.00001f,
                    "QR preserves original scanner quiet zone: "+position);
                // Test both branches of the same exact offsets used for paint.
                float blackTop=qr.FontPt*qr.QrFirstBlackRowFraction;
                float textShift=Math.Max(0,blackTop-qrInkTop);
                float qrShift=Math.Max(0,qrInkTop-blackTop);
                Check(Math.Abs((qrShift+blackTop)-(textShift+qrInkTop))<.00001f,
                    "visible BLACK QR top matches TOTP caption ink top: "+position);
                float labelH=Math.Max(2.1f,(float)(settings.LabelSizePt*CardLayoutEngine.DensityScale(settings)*
                    CardLayoutEngine.MmPerPoint*Math.Clamp(settings.LineSpacing,.8,2)*1.25));
                float valueH=Math.Max(2.1f,qr.QrValuePt*CardLayoutEngine.MmPerPoint*
                    (float)Math.Clamp(settings.LineSpacing,.8,2)*1.35f);
                float usedTextHeight=textShift+labelH+qr.QrGroupedRows!.Length*valueH;
                Check(qr.HeightMm>=usedTextHeight && qr.HeightMm>=qrShift+qr.FontPt,
                    "QR block reserves space for shifted text and full QR: "+position);
                var rows=qr.QrGroupedRows!;
                Check(string.Concat(rows.SelectMany(row=>row))==inventedOtp,
                    "TOTP QR-side text preserves every secret character");
                Check(rows.All(row=>row.Length>=2) &&
                    rows.Max(row=>row.Length)-rows.Min(row=>row.Length)<=1,
                    "QR-side TOTP group rows are balanced without orphans");
                var groups=rows.SelectMany(row=>row).ToArray();
                Check(groups.SkipLast(1).All(g=>g.Length==4),
                    "QR-side TOTP wrapping never breaks a configured group");
                float expectedGap=CardTextMetrics.MeasureMm(" ",qr.QrValuePt,settings.SecretFont,
                    qr.Weight=="inherit"?settings.ValueWeight:qr.Weight)*.2f;
                Check(Math.Abs(qr.QrGroupGapMm-expectedGap)<.001f,
                    "fractional 0.2 group spacing is measured in true space width");
                Check(qr.QrMetadata is {Length:>0} && qr.QrMetadata.Contains("Период: 30",StringComparison.Ordinal),
                    "TOTP metadata is directly under the secret inside QR-side column");
                Check(!All(groupedCards).Any(l=>l.Kind==CardLineKind.Meta && l.Field=="TOTP Secret"),
                    "no wasted full-width metadata row below QR");
                LayoutFits(groupedCards,settings);
                foreach(var card in groupedCards){var png=Picture(card,settings);Array.Clear(png);}
            }
            settings=Defaults();settings.TotpQrEnabled=true;settings.TotpQrPosition="right";
            settings.TotpQrHeightMm=48;
            var fallbackQrCards=CardLayoutEngine.Build([groupedOtp],settings,groupedRules);
            Check(All(fallbackQrCards).Single(l=>l.Kind==CardLineKind.Qr).QrInline==false,
                "QR moves below value if complete groups do not fit beside it");
            var belowRows=All(fallbackQrCards).Where(l=>l.Kind==CardLineKind.Value &&
                l.Field=="TOTP Secret").ToArray();
            Check(belowRows.Length>0 && belowRows.All(l=>l.GroupedValue is not null),
                "QR-below fallback retains atomic TOTP groups at exact spacing");
            Check(string.Concat(belowRows.SelectMany(l=>l.GroupedValue!))==inventedOtp,
                "QR-below fallback preserves complete secret exactly");
            Check(belowRows.All(l=>Math.Abs(l.GroupGapMm-CardTextMetrics.MeasureMm(" ",belowRows[0].FontPt,settings.SecretFont,settings.ValueWeight)*.2f)<.001f),
                "QR-below fallback preserves the fractional 0.2 space gap");
            LayoutFits(fallbackQrCards,settings);
            foreach(var card in fallbackQrCards){var png=Picture(card,settings);Array.Clear(png);}
            // With seven groups, balance rows rather than producing a lone trailing group.
            float wrapGap;
            float wideGroup=CardTextMetrics.MeasureMm("JBSW",(float)settings.ValueSizePt,settings.SecretFont,
                settings.ValueWeight);
            var balanced=CardLayoutEngine.PackOtpGroups(inventedOtp[..28],4,.2,
                wideGroup*3+1, (float)settings.ValueSizePt, settings.SecretFont,
                settings.ValueWeight,0,out wrapGap);
            Check(balanced is not null && balanced.All(row=>row.Length>=2) &&
                string.Concat(balanced.SelectMany(row=>row))==inventedOtp[..28],
                "seven OTP groups are balanced with no trailing orphan");

            settings=Defaults();settings.DuplexEnabled=true;settings.DuplexCombineDifferent=true;
            var two=CardLayoutEngine.Build([Record("First"),Record("Second")],settings,BasicMapping);
            Check(two.Count==1 && two[0].Continuation is not null,"explicit different-entry duplex pairing");
            var back=Picture(two[0],settings,true);Array.Clear(back);
            settings.DuplexCombineDifferent=false;
            Check(CardLayoutEngine.Build([Record("First"),Record("Second")],settings,BasicMapping).Count==2,
                "different records are not combined by default");
            settings.BackBgOverride=true;
            foreach(string backdrop in new[]{"clean","watermark","gradient","diagonal","technical","texture"})
            {
                settings.DuplexBackBackground=backdrop;
                var card=CardLayoutEngine.Build([Record()],settings,BasicMapping)[0];
                var bytes=Picture(card,settings,true);Array.Clear(bytes);
            }

            // Exact Microsoft Fluent (MIT) path data is shared by the themed
            // picker and the printed card. One paint pass must apply opacity
            // exactly once, including antialiased edges on complex paths.
            Check(FluentCardGlyphs.PathData("none") is null &&
                  FluentCardGlyphs.PathData("unrecognized") is null,
                  "Fluent picker accepts only supported stable emblem ids");
            foreach (string emblemId in new[] { "shield", "lock", "key", "card", "vault" })
            {
                Check(FluentCardGlyphs.PathData(emblemId) is { Length: > 80 } pathData &&
                      pathData.StartsWith('M'),
                      $"Fluent 24px Regular SVG path exists for {emblemId}");
                using var solidIcon = new SKBitmap(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
                using (var sc = new SKCanvas(solidIcon))
                {
                    sc.Clear(SKColors.Transparent);
                    CardVectorIcons.Draw(sc, emblemId, 100, 100, 145, new SKColor(40, 58, 75, 255));
                }
                foreach (byte opacity in new byte[] { 55, 120, 195 })
                {
                    using var fadedIcon = new SKBitmap(200, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using (var fc = new SKCanvas(fadedIcon))
                    {
                        fc.Clear(SKColors.Transparent);
                        CardVectorIcons.Draw(fc, emblemId, 100, 100, 145, new SKColor(40, 58, 75, opacity));
                    }
                    int maxAlphaError = 0;
                    int maximumObserved = 0;
                    int visiblePixels = 0;
                    for (int y = 0; y < 200; y++) for (int x = 0; x < 200; x++)
                    {
                        int solidAlpha = solidIcon.GetPixel(x, y).Alpha;
                        int expected = (int)Math.Round(solidAlpha * opacity / 255f);
                        int observed = fadedIcon.GetPixel(x, y).Alpha;
                        maxAlphaError = Math.Max(maxAlphaError, Math.Abs(expected - observed));
                        maximumObserved = Math.Max(maximumObserved, observed);
                        if (observed > 0) visiblePixels++;
                    }
                    Check(visiblePixels > 200 && maximumObserved >= opacity - 2,
                        $"emblem {emblemId} is visible at alpha {opacity}");
                    Check(maximumObserved <= opacity + 1,
                        $"emblem {emblemId} has no over-darkened intersections (alpha {opacity}, observed {maximumObserved})");
                    Check(maxAlphaError <= 3,
                        $"emblem opacity is applied only once: {emblemId} alpha {opacity} " +
                        $"(max alpha error {maxAlphaError}, allowed 3)");
                }
            }

            // Icon/text/rules must move as a group and never intersect, even for
            // very large emblems and non-default vertical positions.
            var smallArtwork=CardBackArtworkLayout.Measure(80,500,75,90,30,"center",true,true);
            var bigArtwork=CardBackArtworkLayout.Measure(80,500,165,90,30,"center",true,true);
            Check(bigArtwork.CaptionTopY>smallArtwork.CaptionTopY+10,
                "resizing back emblem shifts caption and both dividers");
            Check(bigArtwork.CaptionTopY>=bigArtwork.IconCenterY+bigArtwork.IconSize*.5f+29,
                "caption has guaranteed gap from emblem");
            Check(bigArtwork.CaptionTopY+90<=500,"caption stays above footer");
            var topArtwork=CardBackArtworkLayout.Measure(80,500,75,90,30,"top",true,true);
            var bottomArtwork=CardBackArtworkLayout.Measure(80,500,75,90,30,"bottom",true,true);
            Check(topArtwork.CaptionTopY<bottomArtwork.CaptionTopY,
                "vertical placement moves entire decoration group");
            var cramped=CardBackArtworkLayout.Measure(80,220,165,90,30,"center",true,true);
            Check(cramped.CaptionTopY>=cramped.IconCenterY+cramped.IconSize*.5f+cramped.Gap-.01,
                "limited room shrinks emblem before violating caption gap");
            Check(cramped.CaptionTopY+90<=220,"no overlap with footer in small area");

            // Decorative back controls must alter the raster, not merely the UI.
            settings=Defaults();settings.DuplexEnabled=true;settings.DuplexBackFill="design";
            var decorativeCard=CardLayoutEngine.Build([Record("Decor")],settings,BasicMapping)[0];
            settings.EmptyBackShowHeader=false;settings.EmptyBackShowFooter=false;
            settings.EmptyBackShowArt=false;settings.EmptyBackShowIcon=false;
            var plain=Picture(decorativeCard,settings,true);
            settings.EmptyBackShowHeader=true;
            var withHeader=Picture(decorativeCard,settings,true);
            Check(!SHA256.HashData(plain).SequenceEqual(SHA256.HashData(withHeader)),
                "empty back header toggle changes image");
            settings.EmptyBackShowHeader=false;settings.EmptyBackShowFooter=true;
            var withFooter=Picture(decorativeCard,settings,true);
            Check(!SHA256.HashData(plain).SequenceEqual(SHA256.HashData(withFooter)),
                "empty back footer toggle changes image");
            settings.EmptyBackShowFooter=false;settings.EmptyBackShowIcon=true;
            foreach(var id in new[]{"shield","lock","key","card","vault"})
            {
                settings.BackArtIcon=id;
                var art=Picture(decorativeCard,settings,true);
                Check(!SHA256.HashData(plain).SequenceEqual(SHA256.HashData(art)),
                    "vector icon renders: "+id);
                Array.Clear(art);
            }
            settings.BackArtIcon="shield";
            var originalShade=Picture(decorativeCard,settings,true);
            settings.BackArtIconColor="#C04A52";
            var customShade=Picture(decorativeCard,settings,true);
            Check(!SHA256.HashData(originalShade).SequenceEqual(SHA256.HashData(customShade)),
                "separate vector icon colour changes output");
            Array.Clear(originalShade);Array.Clear(customShade);
            settings.EmptyBackShowIcon=false;settings.EmptyBackBgSource="custom";
            foreach(var pattern in new[]{"clean","gradient","diagonal","technical","texture","watermark"})
            {
                settings.EmptyBackBgPattern=pattern;
                var image=Picture(decorativeCard,settings,true);
                if(pattern!="clean")Check(!SHA256.HashData(plain).SequenceEqual(SHA256.HashData(image)),
                    "background visible: "+pattern);
                Array.Clear(image);
            }
            settings.EmptyBackShowArt=true;settings.DuplexBackStyle="rules";
            var rulesImage=Picture(decorativeCard,settings,true);
            settings.DuplexBackStyle="minimal";
            var noRulesImage=Picture(decorativeCard,settings,true);
            Check(!SHA256.HashData(rulesImage).SequenceEqual(SHA256.HashData(noRulesImage)),
                "original paired lines actually appear");
            var baselineCaption=Picture(decorativeCard,settings,true);
            settings.BackCaptionTitleFont="JetBrains Mono NL";
            settings.BackCaptionTitleWeight="800";
            settings.BackCaptionTitleSizePt=12;
            var updatedTitle=Picture(decorativeCard,settings,true);
            Check(!SHA256.HashData(baselineCaption).SequenceEqual(SHA256.HashData(updatedTitle)),
                "independent upper caption typography alters printed pixels");
            settings.BackCaptionSubtitleFont="Cascadia Mono";
            settings.BackCaptionSubtitleWeight="700";
            settings.BackCaptionSubtitleSizePt=12;
            var updatedSubtitle=Picture(decorativeCard,settings,true);
            Check(!SHA256.HashData(updatedTitle).SequenceEqual(SHA256.HashData(updatedSubtitle)),
                "independent lower caption typography alters printed pixels");
            settings.BackCaptionTitleSizePt=18;
            settings.BackCaptionSubtitleSizePt=18;
            var fittedCaption=Picture(decorativeCard,settings,true);
            Check(fittedCaption.Length>1000,"large reverse captions fit without render errors");
            Array.Clear(baselineCaption);Array.Clear(updatedTitle);Array.Clear(updatedSubtitle);
            Array.Clear(fittedCaption);
            Array.Clear(plain);Array.Clear(withHeader);Array.Clear(withFooter);
            Array.Clear(rulesImage);Array.Clear(noRulesImage);

            settings=Defaults();settings.NotesMode="optional";
            var verbose=Record("Notes");
            // Record immutable init properties; use an added mapped multiline field instead.
            verbose.AdditionalFields["LongNotes"]=string.Join(' ',Enumerable.Repeat("placeholder",1500));
            var textRules=new Dictionary<string,FieldRule>{["LongNotes"]=new FieldRule
                {Role="multiline",Label="Notes",Section="extra",Order=900}};
            var trimmed=CardLayoutEngine.Build([verbose],settings,textRules);
            Check(trimmed.Count>=1,"optional notes layout");
            LayoutFits(trimmed,settings);

            // Built-in styles must produce different raster output from the same dummy record.
            var a=Defaults();a.Style="archive";
            var aImg=Picture(CardLayoutEngine.Build([Record()],a,BasicMapping)[0],a);
            var b=CardAppearancePresets.Create("technical");
            var bImg=Picture(CardLayoutEngine.Build([Record()],b,BasicMapping)[0],b);
            Check(!SHA256.HashData(aImg).SequenceEqual(SHA256.HashData(bImg)),"style changes printed pixels");
            Array.Clear(aImg);Array.Clear(bImg);
            // Printed sheet, preview and vector export must share one unscaled plan.
            var paperAppearance=Defaults();
            var printCard=CardLayoutEngine.Build([Record()],paperAppearance,BasicMapping)[0];
            var many=Enumerable.Repeat(printCard,10).ToArray();
            var a4=PaperGeometry.Resolve("A4");
            var sheets=PrintDocumentPipeline.Plan(many,paperAppearance,a4);
            Check(sheets.Count==2 && sheets[0].Slots.Count==9 && sheets[1].Slots.Count==1,
                "A4 nine-pocket placement and stable last-page slot");
            Check(sheets[0].Slots[0].CardIndex==0 && sheets[0].Slots[8].CardIndex==8,
                "deterministic page order");
            // Grid cells must touch on both axes, and the complete block (not
            // each card separately) must be centred on the physical sheet.
            var topLeft=sheets[0].Slots[0];
            var topMiddle=sheets[0].Slots[1];
            var below=sheets[0].Slots[3];
            Check(Math.Abs(topMiddle.Xmm-topLeft.Xmm-paperAppearance.WidthMm)<1e-6 &&
                  Math.Abs(topMiddle.Ymm-topLeft.Ymm)<1e-6,
                "no horizontal card gap");
            Check(Math.Abs(below.Ymm-topLeft.Ymm-paperAppearance.HeightMm)<1e-6 &&
                  Math.Abs(below.Xmm-topLeft.Xmm)<1e-6,
                "no vertical card gap");
            Check(Math.Abs(topLeft.Xmm-(a4.WidthMm-3*paperAppearance.WidthMm)/2)<1e-6 &&
                  Math.Abs(topLeft.Ymm-(a4.HeightMm-3*paperAppearance.HeightMm)/2)<1e-6,
                "contiguous card grid centered on sheet");
            var shifted=PrintDocumentPipeline.Plan([printCard,printCard],paperAppearance,a4,firstSheetSlot:4);
            Check(shifted.Count==1 && shifted[0].Slots.Count==2 &&
                shifted[0].Slots[0].CardIndex==0 && shifted[0].Slots[0].Xmm==sheets[0].Slots[4].Xmm,
                "partial reprint starts at the chosen physical pocket");
            Check(PrintDocumentPipeline.Plan(many,paperAppearance,PaperGeometry.Resolve("A5"))[0].Slots.Count==4,
                "A5 physical grid");
            Check(PrintDocumentPipeline.Plan(many,paperAppearance,PaperGeometry.Resolve("A6"))[0].Slots.Count==1,
                "A6 physical grid");
            paperAppearance.DuplexEnabled=true;
            var duplexPages=PrintDocumentPipeline.Plan(many,paperAppearance,a4);
            Check(duplexPages.Count==4 && !duplexPages[0].Reverse && duplexPages[1].Reverse,
                "interleaved duplex page order");
            // Ordinary one-sided printing of ALL sides must preserve the
            // physical page order and never rely on manual or driver duplex.
            var shuffledPages=new[] {duplexPages[3],duplexPages[0],duplexPages[2],duplexPages[1]};
            var sequential=SingleSidedPrintPlan.Create(shuffledPages);
            Check(sequential.Count==4 &&
                sequential[0].SheetNumber==1 && !sequential[0].Reverse &&
                sequential[1].SheetNumber==1 && sequential[1].Reverse &&
                sequential[2].SheetNumber==2 && !sequential[2].Reverse &&
                sequential[3].SheetNumber==2 && sequential[3].Reverse,
                "simplex all-pages job preserves front-back order without flipping");
            Check(SingleSidedPrintPlan.Create([duplexPages[0],duplexPages[2]])
                .All(x=>!x.Reverse),"simplex front-only job is supported");
            Check(SingleSidedPrintPlan.Create([duplexPages[1],duplexPages[3]])
                .All(x=>x.Reverse),"simplex back-only job is supported");
            bool simplexDuplicateRejected=false;
            try { _=SingleSidedPrintPlan.Create([duplexPages[0],duplexPages[0]]); }
            catch(InvalidOperationException) { simplexDuplicateRejected=true; }
            Check(simplexDuplicateRejected,"simplex job rejects duplicate page sides");
            var physicalPairs=ManualDuplexPlan.Create(duplexPages);
            Check(physicalPairs.Count==2 &&
                physicalPairs[0].Front.SheetNumber==physicalPairs[0].Back.SheetNumber &&
                physicalPairs[1].Front.SheetNumber==physicalPairs[1].Back.SheetNumber,
                "guided manual duplex matches one front and back per physical sheet");
            Check(physicalPairs[0].Front.Slots.Count==physicalPairs[0].Back.Slots.Count &&
                physicalPairs[0].Front.Slots[0].CardIndex==physicalPairs[0].Back.Slots[0].CardIndex,
                "guided duplex retains physical card identity across both sides");
            bool duplicateRejected=false;
            try
            {
                _ = ManualDuplexPlan.Create([duplexPages[0],duplexPages[0],duplexPages[1]]);
            }
            catch(InvalidOperationException) { duplicateRejected=true; }
            Check(duplicateRejected,"manual duplex refuses ambiguous face/back pairing");
            var firstFront=duplexPages[0].Slots[0];
            var firstBack=duplexPages[1].Slots[0];
            Check(Math.Abs(firstBack.Xmm-(a4.WidthMm-firstFront.Xmm-paperAppearance.WidthMm))<.0001,
                "long-edge back mirror");
            var shortPages=PrintDocumentPipeline.Plan(many,paperAppearance,a4,shortEdgeFlip:true);
            Check(Math.Abs(shortPages[1].Slots[0].Ymm-
                (a4.HeightMm-firstFront.Ymm-paperAppearance.HeightMm))<.0001,
                "short-edge back mirror");
            // The interactive preview must be a direct Skia display list,
            // independent of PNG encoding or the current screen zoom.
            using(var scene=PrintDocumentPipeline.RecordSheet(duplexPages[0],paperAppearance))
            {
                Check(Math.Abs(scene.Width-a4.WidthMm*CardSkiaRenderer.PixelsPerMillimetre)<.001 &&
                      Math.Abs(scene.Height-a4.HeightMm*CardSkiaRenderer.PixelsPerMillimetre)<.001,
                    "preview vector uses precise paper dimensions");
                using var surface=SKSurface.Create(new SKImageInfo(500,700));
                Check(surface is not null,"vector preview playback surface available");
                surface!.Canvas.Scale(500/scene.Width,700/scene.Height);
                surface.Canvas.DrawPicture(scene.Picture);
                using var snapshot=surface.Snapshot();
                Check(snapshot.Width==500 && snapshot.Height==700,
                    "Skia preview plays at arbitrary zoom without new raster source");
                using var cache=new SecurePreviewSceneCache();
                cache.Put("sample-scene",scene);
                Check(cache.TryGet("sample-scene",out var borrowed) && borrowed is not null,
                    "RAM-only vector scene cache lookup");
                borrowed!.Dispose();
                cache.Clear();
                // A second reference stays valid after cache invalidation.
                using var held=scene.Retain();
                Check(held.Width>0,"rendering scene lifetime survives cache eviction");
            }
            var raster=PrintDocumentPipeline.RenderSheetPng(duplexPages[0],paperAppearance,96);
            using(var decoded=SKBitmap.Decode(raster))
                Check(decoded is not null && decoded.Width==(int)Math.Ceiling(a4.WidthMm*96/25.4),
                    "sheet preview uses physical dimensions");
            Array.Clear(raster);
            var pdf=PrintDocumentPipeline.ExportVectorPdf([duplexPages[0]],paperAppearance);
            Check(pdf.Length>100 && Encoding.ASCII.GetString(pdf,0,5)=="%PDF-",
                "vector PDF is physically generated");
            Array.Clear(pdf);
            using(var cache=new SecurePreviewCache(2*1024*1024))
            {
                cache.Put("sample",[1,2,3,4]);
                Check(cache.TryGet("sample",out var cached) && cached.SequenceEqual(new byte[]{1,2,3,4}),
                    "memory-only preview LRU lookup");
                Array.Clear(cached);
                cache.Clear();
                Check(!cache.TryGet("sample",out _),"preview cache invalidation");
            }
            _testLog.AppendLine($"[PASS] layout/summary: {_checks} card/sheet/render assertions");
            Report = _testLog.ToString();
            return 0;
        }
        catch(Exception e)
        {
            // Only canned assertion names and exception type, no real input or KDBX data.
            // All test records are artificial, and no vault is opened in this mode.
            // Preserve the exception message and previous assertion instead of
            // hiding the useful failure behind only InvalidOperationException.
            if (!e.Message.StartsWith("LAYOUT_TEST_FAILED:", StringComparison.Ordinal))
                _testLog.AppendLine("[FAIL] layout/runtime: " + e.GetType().Name + " [after: " + _lastSuccessfulCheck + "]");
            Report = _testLog.ToString();
            return 7;
        }
    }
}
