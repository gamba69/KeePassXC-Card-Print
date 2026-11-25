using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Services;

public sealed class VaultPreferences
{
    public bool RememberPaths { get; set; } = true;
    public bool RememberSelection { get; set; } = true;
    public string DatabasePath { get; set; } = "";
    public string KeyFilePath { get; set; } = "";
    public bool UseYubiKey { get; set; }
    public int YubiSlot { get; set; } = 2;
    public bool YubiShortChallenge { get; set; }
    public string YubiSerial { get; set; } = "";
    public Dictionary<string, List<string>> Selections { get; set; } = new(StringComparer.Ordinal);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VaultPreferences))]
[JsonSerializable(typeof(Dictionary<string, FieldRule>))]
internal partial class VaultJsonContext : JsonSerializerContext { }

public static class PrivateUiState
{
    private static string Root => SettingsLocation.DirectoryPath;
    private static string StateFile => Path.Combine(Root, "vault-state.json");

    public static VaultPreferences Load()
    {
        if (File.Exists(StateFile))
        {
            try
            {
                if (new FileInfo(StateFile).Length > 2_000_000) return new();
                var prefs = JsonSerializer.Deserialize(File.ReadAllBytes(StateFile), VaultJsonContext.Default.VaultPreferences);
                if (prefs is not null) return prefs;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return new();
    }

    public static void Save(VaultPreferences prefs)
    {
        var safe = new VaultPreferences
        {
            RememberPaths = prefs.RememberPaths, RememberSelection = prefs.RememberSelection,
            DatabasePath = prefs.RememberPaths ? prefs.DatabasePath : "",
            KeyFilePath = prefs.RememberPaths ? prefs.KeyFilePath : "",
            UseYubiKey = prefs.UseYubiKey, YubiSlot = prefs.YubiSlot,
            YubiShortChallenge = prefs.YubiShortChallenge, YubiSerial = prefs.YubiSerial,
            Selections = prefs.RememberSelection ? prefs.Selections : new()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(safe, VaultJsonContext.Default.VaultPreferences);
        Directory.CreateDirectory(Root);
        var tmp = StateFile + ".tmp";
        try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, StateFile, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    // Stable privacy-preserving selection tokens; raw KDBX paths are not persisted here.
    private static string Hash(string source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    public static string DatabaseToken(string path) => Hash("database\0" + PlatformServices.Current.NormalizeDatabaseIdentityPath(path));
    public static string EntryToken(Guid id) => Hash("entry\0" + id.ToString("N"));

    public static void RestoreSelection(VaultPreferences prefs, string path, IReadOnlyList<VaultRecord> records)
    {
        if (!prefs.RememberSelection || !prefs.Selections.TryGetValue(DatabaseToken(path), out var saved)) return;
        var tokens = saved.ToHashSet(StringComparer.Ordinal);
        foreach (var r in records) r.Selected = tokens.Contains(EntryToken(r.Uuid));
    }

    public static void CaptureSelection(VaultPreferences prefs, string path, IReadOnlyList<VaultRecord> records)
    {
        if (!prefs.RememberSelection || string.IsNullOrWhiteSpace(path)) return;
        var ids = records.Where(r => r.Selected).Take(10000).Select(r => EntryToken(r.Uuid)).ToList();
        prefs.Selections[DatabaseToken(path)] = ids;
    }
}

public sealed class FieldRule
{
    public string Label { get; set; } = "";
    public string Role { get; set; } = "text";
    public string Section { get; set; } = "extra";
    public int Order { get; set; } = 500;
    public string Visibility { get; set; } = "nonempty";
    public bool ShowLabel { get; set; } = true;
    public string UrlMode { get; set; } = "full";
    public string UrlSource { get; set; } = "fallback";
    public bool PhoneFormat { get; set; } // USERNAME only, opt-in display transformation
    public string Fit { get; set; } = "wrap";
    public string ValueWeight { get; set; } = "inherit";
    public string ValueAlignment { get; set; } = "inherit";
    public string ValueHyphenation { get; set; } = "inherit";
    public string ValueHyphenLanguage { get; set; } = "inherit";
    public int ValueSizePct { get; set; } = 100;
    public double? ValueTrackingPt { get; set; }
    public string GroupMode { get; set; } = "inherit";
    public int GroupSize { get; set; }
    public double GroupSpaces { get; set; }
}

// Individual mapping.json files are obsolete. A print template is the only
// persisted mapping source; unconfigured fields use FieldMappingDefaults.
