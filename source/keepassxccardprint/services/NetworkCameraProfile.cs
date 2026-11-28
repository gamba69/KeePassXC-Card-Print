using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeePassXCCardPrint.Services;

// All fields (including the URL, which may itself contain a token) are device-local.
internal sealed class NetworkCameraProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public bool AllowPlaintext { get; set; }
    public string Pin { get; set; } = ""; // exact SHA-256 of HTTPS / built-in RTSPS leaf DER
    public string CaFile { get; set; } = ""; // optional local PEM trust anchor for RTSPS
    public int Fps { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 10;
    public bool Reconnect { get; set; } = true;
    public string Engine { get; set; } = "sharp";
    public string Decoder { get; set; } = "auto";
    public bool DecoderFallback { get; set; } = false;
    public bool RtspUdp { get; set; } // plaintext RTSP only; never available for RTSPS
    // ONVIF is an optional, independently authenticated device-control channel.
    public string OnvifAddress { get; set; } = "";
    public string OnvifUser { get; set; } = "";
    public string OnvifPassword { get; set; } = "";
    public bool OnvifAllowHttp { get; set; }
    public string OnvifProfileToken { get; set; } = "";
    public string OnvifMediaService { get; set; } = "";
    public string OnvifIdentity { get; set; } = "";
    internal bool HasOnvif=>OnvifAddress.Length>0&&OnvifProfileToken.Length>0;
    internal void CopyOnvif(NetworkCameraProfile other)
    {
        OnvifAddress=other.OnvifAddress;OnvifUser=other.OnvifUser;OnvifPassword=other.OnvifPassword;OnvifAllowHttp=other.OnvifAllowHttp;
        OnvifProfileToken=other.OnvifProfileToken;OnvifMediaService=other.OnvifMediaService;OnvifIdentity=other.OnvifIdentity;
    }

    internal NetworkCameraProfile WithDecoder(string decoder,bool fallback)
    {
        var copy=(NetworkCameraProfile)MemberwiseClone();copy.Decoder=decoder;copy.DecoderFallback=fallback;return copy;
    }

    // Consent is scoped to the endpoint and the transport's security class,
    // not to ordinary decoding, FPS or reconnect preferences.
    internal static bool ConsentScopeChanged(string before,string beforeEngine,string after,string afterEngine)
    {
        if(!string.Equals(before.Trim(),after.Trim(),StringComparison.Ordinal))return true;
        return Uri.TryCreate(after,UriKind.Absolute,out var uri)&&uri.Scheme=="rtsps"&&beforeEngine!=afterEngine;
    }

    internal bool Validate(out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Address.Length > 2048 || User.Length > 128 || Password.Length > 512 ||
            !Uri.TryCreate(Address.Trim(), UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https" or "rtsp" or "rtsps") ||
            string.IsNullOrWhiteSpace(parsed.Host) || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0 ||
            parsed.HostNameType == UriHostNameType.Unknown || parsed.Port == 0 || Fps is < 1 or > 12 || TimeoutSeconds is < 3 or > 60 ||
            User.Any(char.IsControl) || Password.Any(char.IsControl) || (User.Length == 0 && Password.Length > 0) ||
            User.Contains(':') || (parsed.Scheme is "http" or "rtsp") && !AllowPlaintext) return false;
        if (Engine is not ("sharp" or "ffmpeg")) return false;
        if (!VideoDecoderManager.ValidSelection(Decoder)) return false;
        if (Engine=="ffmpeg" && parsed.Scheme=="rtsps" && !AllowPlaintext) return false;
        if (RtspUdp && parsed.Scheme != "rtsp") return false;
        if (Pin.Length != 0 && (!(parsed.Scheme == "https" || parsed.Scheme == "rtsps" && Engine == "sharp") || Pin.Length != 64 || !Pin.All(Uri.IsHexDigit))) return false;
        if (Pin.Length != 0 && CaFile.Length != 0) return false;
        if (CaFile.Length != 0 && (parsed.Scheme != "rtsps" || !Path.IsPathFullyQualified(CaFile) || !File.Exists(CaFile))) return false;
        uri = parsed;
        return true;
    }
    internal static string NormalizePin(string value) => value.Replace(":", "").Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
}

[JsonSerializable(typeof(List<NetworkCameraProfile>))]
internal partial class NetworkCameraJson : JsonSerializerContext { }

// Separate from the cloud-synchronized UI settings; DPAPI CurrentUser, no plaintext fallback.
internal static class NetworkCameraProfiles
{
    private static string FileName => Path.Combine(SettingsLocation.AppDataRoot, "scanner-network.dpapi");
    // Opaque profile ID only, no endpoint/name/credentials, local to this device.
    private static string SelectionFile=>Path.Combine(SettingsLocation.AppDataRoot,"scanner-network-selection.txt");
    internal static string? LoadSelectedId()
    {
        if(!File.Exists(SelectionFile)||new FileInfo(SelectionFile).Length>64)return null;
        var id=File.ReadAllText(SelectionFile);return Guid.TryParseExact(id,"N",out _)?id:null;
    }
    internal static void SaveSelectedId(string? id)
    {
        if(id is null){if(File.Exists(SelectionFile))File.Delete(SelectionFile);return;}
        if(!Guid.TryParseExact(id,"N",out _))throw new FormatException();
        Directory.CreateDirectory(SettingsLocation.AppDataRoot);
        string tmp=SelectionFile+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(tmp,id);File.Move(tmp,SelectionFile,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    internal static List<NetworkCameraProfile> Load() => LoadFrom(FileName);
    // Explicit paths allow the persistence regression test to use an isolated store.
    internal static List<NetworkCameraProfile> LoadFrom(string fileName)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(fileName)) return [];
        if (new FileInfo(fileName).Length > 256 * 1024) throw new IOException("Profile store too large.");
        var encrypted = File.ReadAllBytes(fileName);
        var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize(plain, NetworkCameraJson.Default.ListNetworkCameraProfile) ?? []; }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal static void Save(List<NetworkCameraProfile> profiles) => SaveTo(FileName, profiles);
    internal static void SaveTo(string fileName, List<NetworkCameraProfile> profiles)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (profiles.Count > 32) throw new IOException("Too many profiles.");
        var plain = JsonSerializer.SerializeToUtf8Bytes(profiles, NetworkCameraJson.Default.ListNetworkCameraProfile);
        try
        {
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(fileName)!);
            string tmp = fileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(tmp, encrypted); File.Move(tmp, fileName, true); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal static void DeleteAll() { if (File.Exists(FileName)) File.Delete(FileName);if(File.Exists(SelectionFile))File.Delete(SelectionFile);VideoDecoderManager.DeleteCache(); }
}
