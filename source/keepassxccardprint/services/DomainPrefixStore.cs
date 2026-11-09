using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace KeePassXCCardPrint.Services;

// The domain rules are only presentation rules; never save actual hosts or secrets.
public sealed class DomainPrefixFile
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = "KeePassCardPrint-auth-subdomains-v1";
    [JsonPropertyName("prefixes")]
    public List<string> Prefixes { get; set; } = new();
}
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DomainPrefixFile))]
internal partial class DomainPrefixContext : JsonSerializerContext { }

public static partial class DomainPrefixStore
{
    public static readonly string[] Defaults = ["accounts","account","login","log-in","signin","sign-in","sso","auth","authentication","authenticate","idp","identity"];
    private static string DomainPath => SettingsLocation.FilePath("domains.json");
    private static readonly Regex LabelRegex = new(@"\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\z",RegexOptions.CultureInvariant);
    public static List<string> Normalize(IEnumerable<string> values)
    {
        var clean = new List<string>();
        foreach (var raw in values)
        {
            if (clean.Count >= 200) throw new ArgumentException("Не более 200 служебных поддоменов.");
            var s=(raw??"").Trim().ToLowerInvariant();
            if (!LabelRegex.IsMatch(s)) throw new ArgumentException("Вводите один DNS-компонент без точек (a-z, 0-9, дефис).");
            if (!clean.Contains(s,StringComparer.Ordinal)) clean.Add(s);
        }
        return clean;
    }
    public static List<string> Load()
    {
        if (!File.Exists(DomainPath)) return Defaults.ToList();
        try
        {
            if (new FileInfo(DomainPath).Length > 128000)
                throw new InvalidDataException("Файл служебных поддоменов слишком велик.");
            var obj = JsonSerializer.Deserialize(File.ReadAllBytes(DomainPath), DomainPrefixContext.Default.DomainPrefixFile);
            if (obj is null || obj.Format != "KeePassCardPrint-auth-subdomains-v1")
                throw new InvalidDataException("Неизвестный формат списка поддоменов.");
            return Normalize(obj.Prefixes ?? []);
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException)
        {
            throw new InvalidDataException("Не удалось прочитать список служебных поддоменов.", e);
        }
    }
    public static void Save(IEnumerable<string> values)
    {
        var file = new DomainPrefixFile{Prefixes=Normalize(values)};
        var bytes=JsonSerializer.SerializeToUtf8Bytes(file,DomainPrefixContext.Default.DomainPrefixFile);
        Directory.CreateDirectory(SettingsLocation.DirectoryPath);
        var tmp=DomainPath+".tmp";
        try { File.WriteAllBytes(tmp,bytes);File.Move(tmp,DomainPath,true); }
        finally { if(File.Exists(tmp))File.Delete(tmp); }
    }
}
