using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeePassXCCardPrint.Platforms.Windows.Updates;

// Explicit allow-list: a release can update application-owned files only.
// In particular it cannot write a KDBX, settings, a key file, or an arbitrary script.
internal static class CardPrintUpdatePolicy
{
    internal const string Repository = "gamba69/KeePassXC-Card-Print";
    internal const string RepositoryId = "1451452767";
    internal const string Executable = "KeePassXCCardPrint.exe";
    internal const long MaxZipBytes = 256L * 1024 * 1024;
    private const long MaxExtractedBytes = 512L * 1024 * 1024;
    private static readonly Regex VersionFormat = new(@"^v?[0-9]+\.[0-9]+\.[0-9]+$",
        RegexOptions.CultureInvariant);
    private static readonly Regex SafeSegment = new(@"\A[A-Za-z0-9_.+-]+\z",
        RegexOptions.CultureInvariant);

    internal static Version ParseVersion(string tag)
    {
        if (!VersionFormat.IsMatch(tag)) throw new InvalidDataException("Invalid release version.");
        var fields = tag.TrimStart('v').Split('.');
        if (!int.TryParse(fields[0],out var a) || !int.TryParse(fields[1],out var b) ||
            !int.TryParse(fields[2],out var c)) throw new InvalidDataException("Version overflow.");
        return new Version(a,b,c);
    }

    internal static bool IsEligible(string candidate,string current,bool reinstall)
    {
        int comparison=ParseVersion(candidate).CompareTo(ParseVersion(current));
        return comparison>0 || (reinstall && comparison==0);
    }
    internal static bool IsNewer(string candidate,string current) =>
        ParseVersion(candidate)>ParseVersion(current);
    internal static string ZipName(string tag) =>
        $"KeePassXCCardPrint-{ParseVersion(tag).ToString(3)}-win-x64.zip";
    internal static string ChecksumName(string tag) => ZipName(tag)+".sha256";

    // Directory separators in ZIPs are '/' only. No alternate separators,
    // empty segments, dot segments, ADS, drive names, or Windows devices.
    internal static bool IsOwnedFile(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length>240 || name.Contains('\\') ||
            name.StartsWith('/') || name.EndsWith('/') || name.Contains(':') ||
            name.Any(char.IsControl)) return false;
        var parts=name.Split('/');
        if (parts.Any(p=>p.Length==0 || p is "." or ".." || p.EndsWith('.') ||
            p.EndsWith(' ') || !SafeSegment.IsMatch(p) || IsDeviceName(p))) return false;
        if (parts.Length==1)
        {
            if (new[]{Executable,"KpcXpsBridge.dll","README.md","LICENSE",
                "THIRD_PARTY_NOTICES.md"}.Contains(name,StringComparer.OrdinalIgnoreCase)) return true;
            // Native AOT may require private runtime DLLs and sidecars.
            return Regex.IsMatch(name,@"\A[A-Za-z0-9_.+-]+\.(dll|deps\.json|runtimeconfig\.json)\z",
                RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        }
        if (!parts[0].Equals("fonts",StringComparison.OrdinalIgnoreCase)) return false;
        if (parts.Length==2)
            return parts[1].EndsWith(".ttf",StringComparison.OrdinalIgnoreCase) ||
                new[]{"README.txt","FONT_SHA256.txt","PORTABLE_FONT_FILES.txt",
                    "PORTABLE_FONT_LICENSES.txt"}.Contains(parts[1],StringComparer.OrdinalIgnoreCase);
        return parts.Length==3 && parts[1].Equals("licenses",StringComparison.OrdinalIgnoreCase) &&
            parts[2].EndsWith(".txt",StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeviceName(string segment)
    {
        var baseName=segment.Split('.')[0];
        if (new[]{"CON","PRN","AUX","NUL"}.Contains(baseName,StringComparer.OrdinalIgnoreCase))return true;
        return baseName.Length==4 && baseName[3] is >= '1' and <= '9' &&
            (baseName.StartsWith("COM",StringComparison.OrdinalIgnoreCase) ||
             baseName.StartsWith("LPT",StringComparison.OrdinalIgnoreCase));
    }

    internal static string SafePath(string root,string entry)
    {
        if (!IsOwnedFile(entry)) throw new InvalidDataException("Unowned release entry: "+entry);
        var fullRoot=Path.GetFullPath(root);
        var full=Path.GetFullPath(Path.Combine(fullRoot,entry.Replace('/',Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Release path escapes installation.");
        // Do not follow symlinks/junctions at ANY nested directory, including
        // pre-existing fonts/ or fonts/licenses/ in a portable installation.
        for (var dir=Path.GetDirectoryName(full);dir is not null &&
            dir.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase);
            dir=Path.GetDirectoryName(dir))
        {
            RejectReparsePoint(dir);
            if (string.Equals(dir,fullRoot,StringComparison.OrdinalIgnoreCase))break;
        }
        RejectReparsePoint(full);
        return full;
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            // File.Exists and Directory.Exists both return false for dangling
            // symbolic links; querying attributes catches those links too.
            if ((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("Reparse-point update path: "+path);
        }
        catch(FileNotFoundException) { /* New file or directory. */ }
        catch(DirectoryNotFoundException) { /* Its parent will be created. */ }
    }

    internal static string HashFile(string file)
    {
        using var stream=File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    internal static string ParseChecksum(string contents,string expectedName)
    {
        var match=Regex.Match(contents,@"\A([a-fA-F0-9]{64})  ([^\r\n]+)\r?\n?\z",
            RegexOptions.CultureInvariant);
        if (!match.Success || !string.Equals(match.Groups[2].Value,expectedName,StringComparison.Ordinal))
            throw new InvalidDataException("Incorrect release checksum format.");
        return match.Groups[1].Value.ToLowerInvariant();
    }
    internal static void CheckChecksum(string file,string text,string name,string? apiDigest)
    {
        if (new FileInfo(file).Length is <=0 or >MaxZipBytes)
            throw new InvalidDataException("Release size outside limits.");
        var hash=HashFile(file);
        if (hash!=ParseChecksum(text,name) ||
            (apiDigest is not null && !string.Equals(apiDigest,"sha256:"+hash,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Release SHA-256 does not match.");
    }

    internal static IReadOnlyList<string> Extract(string zipFile,string destination)
    {
        using var archive=ZipFile.OpenRead(zipFile);
        var entries=new List<ZipArchiveEntry>();
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size=0;
        foreach(var entry in archive.Entries)
        {
            var name=entry.FullName;
            // ZIP symlinks, special files and duplicate/case-colliding entries
            // are rejected. Only regular files owned by Card Print are allowed.
            var mode=(entry.ExternalAttributes>>16)&0xF000;
            if (!IsOwnedFile(name) || !seen.Add(name) ||
                (mode!=0 && mode!=0x8000) || entry.Length<0)
                throw new InvalidDataException("Unsafe or duplicate ZIP entry: "+name);
            size=checked(size+entry.Length);
            if (size>MaxExtractedBytes) throw new InvalidDataException("ZIP expands beyond limit.");
            entries.Add(entry);
        }
        foreach(var required in new[]{Executable,"KpcXpsBridge.dll","README.md","LICENSE",
            "THIRD_PARTY_NOTICES.md"})
            if (!seen.Contains(required)) throw new InvalidDataException("Missing required file: "+required);
        if (seen.Count(x=>x.StartsWith("fonts/",StringComparison.OrdinalIgnoreCase) &&
            x.EndsWith(".ttf",StringComparison.OrdinalIgnoreCase))!=8 ||
            seen.Count(x=>x.StartsWith("fonts/licenses/",StringComparison.OrdinalIgnoreCase))<8)
            throw new InvalidDataException("Portable font payload is incomplete.");
        Directory.CreateDirectory(destination);
        var created=new List<string>();
        try
        {
            foreach(var entry in entries)
            {
                var path=SafePath(destination,entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var from=entry.Open();
                using var into=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                from.CopyTo(into);
                if(into.Length!=entry.Length)throw new InvalidDataException("Truncated ZIP entry.");
                created.Add(entry.FullName);
            }
        }
        catch { Directory.Delete(destination,true); throw; }
        return created;
    }

    private static string? JsonString(JsonElement obj,string name) =>
        obj.ValueKind==JsonValueKind.Object && obj.TryGetProperty(name,out var v) &&
        v.ValueKind==JsonValueKind.String ? v.GetString():null;
    private static bool ExpectedPurl(string? purl,string tag)
    {
        const string prefix="pkg:github/";
        if (purl is null || !purl.StartsWith(prefix,StringComparison.Ordinal))return false;
        int at=purl.LastIndexOf('@');
        return at>prefix.Length &&
            string.Equals(purl[prefix.Length..at],Repository,StringComparison.OrdinalIgnoreCase) &&
            string.Equals(purl[(at+1)..],tag,StringComparison.Ordinal);
    }
    internal static void VerifySignedStatement(string statement,string tag,string tagSha,
        string archive,string digest)
    {
        using var doc=JsonDocument.Parse(statement);
        var root=doc.RootElement;
        if (JsonString(root,"_type")!="https://in-toto.io/Statement/v1" ||
            JsonString(root,"predicateType") is not
            ("https://in-toto.io/attestation/release/v0.1" or
             "https://in-toto.io/attestation/release/v0.2") ||
            !root.TryGetProperty("predicate",out var pred) ||
            !string.Equals(JsonString(pred,"repository"),Repository,StringComparison.OrdinalIgnoreCase) ||
            JsonString(pred,"repositoryId")!=RepositoryId || JsonString(pred,"tag")!=tag ||
            !ExpectedPurl(JsonString(pred,"purl"),tag))
            throw new InvalidDataException("Signed release metadata does not match this repository.");
        if (!root.TryGetProperty("subject",out var subjects) || subjects.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Signed release subjects missing.");
        bool signedZip=false,signedTag=false;
        foreach(var subject in subjects.EnumerateArray())
        {
            if(!subject.TryGetProperty("digest",out var values))
                throw new InvalidDataException("Signed subject digest missing.");
            if(JsonString(subject,"name")==archive && JsonString(values,"sha256")==digest)signedZip=true;
            if(ExpectedPurl(JsonString(subject,"uri"),tag) &&
                string.Equals(JsonString(values,"sha1"),tagSha,StringComparison.OrdinalIgnoreCase))
                signedTag=true;
        }
        if(!signedZip || !signedTag)
            throw new InvalidDataException("GitHub signature does not bind release tag and archive.");
    }
}
