using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DgNet.Keepass;
using KeePassXCCardPrint.Models;
using KeePassXCCardPrint.Platforms;

namespace KeePassXCCardPrint.Services;

public sealed record VaultCredentials(string Path, string Password, string? KeyFile, bool UseYubiKey,
                                     int YubiSlot, bool YubiShortChallenge, string? YubiSerial);

public interface IKdbxReader
{
    Task<VaultSnapshot> ReadAsync(VaultCredentials input, Action<string>? status = null);
}

public sealed class KdbxVaultReader : IKdbxReader
{
    public Task<VaultSnapshot> ReadAsync(VaultCredentials input, Action<string>? status = null)
        => Task.Run(() => Read(input, status));

    private static VaultSnapshot Read(VaultCredentials input, Action<string>? status)
    {
        if (!File.Exists(input.Path))
            throw new VaultOpenException("DB_MISSING", "Файл базы KDBX не найден.");
        if (!string.IsNullOrWhiteSpace(input.KeyFile) && !File.Exists(input.KeyFile))
            throw new VaultOpenException("KEY_MISSING", "Файл-ключ не найден.");
        if (string.IsNullOrEmpty(input.Password) && string.IsNullOrWhiteSpace(input.KeyFile) && !input.UseYubiKey)
            throw new VaultOpenException("NO_KEY", "Укажите пароль, файл-ключ или YubiKey.");

        // Package version is deliberately pinned. See SECURITY.md about the internal bridge.
        var key = new CompositeKey();
        if (!string.IsNullOrEmpty(input.Password)) key.AddPassword(input.Password);
        if (!string.IsNullOrWhiteSpace(input.KeyFile))
        {
            status?.Invoke("Проверка файла-ключа…");
            // DgNet.Keepass 0.2.0 incorrectly hashes XML v2 key files in full.
            // Extract the real 32-byte key first, verifying XML v2 checksum.
            var keyFileComponent = KeePassKeyFile.Read(input.KeyFile);
            try { CompositeKeyBridge.AddKeyComponent(key, keyFileComponent); }
            finally { CryptographicOperations.ZeroMemory(keyFileComponent); }
        }

        if (input.UseYubiKey)
        {
            status?.Invoke("Ожидание YubiKey; при необходимости коснитесь ключа…");
            byte[] challenge;
            try { challenge = Kdbx4Challenge.ReadKdfSeed(input.Path); }
            catch (NotSupportedException)
            {
                throw new VaultOpenException("YUBI_V3", "YubiKey Challenge-Response для KDBX 3 пока не поддерживается.");
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or FormatException)
            {
                throw new VaultOpenException("KDBX_HEADER", "Не удалось прочитать заголовок KDBX 4 для YubiKey.");
            }
            byte[] response;
            try
            {
                response = PlatformServices.Current.SecurityKeys.ComputeChallengeResponse(challenge, input.YubiSlot,
                                                     input.YubiShortChallenge, input.YubiSerial);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or NotSupportedException)
            {
                CryptographicOperations.ZeroMemory(challenge);
                throw new VaultOpenException("YUBI_DEVICE", "YubiKey недоступен, выбран неверный слот или операция не выполнена.");
            }
            try
            {
                // KeePassXC KDBX4: SHA256(static-components || SHA256(HMAC-SHA1-response)).
                // Inject the component IN MEMORY; never create a temporary key file.
                var component = SHA256.HashData(response);
                try { CompositeKeyBridge.AddKeyComponent(key, component); }
                finally { CryptographicOperations.ZeroMemory(component); }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(response);
                CryptographicOperations.ZeroMemory(challenge);
            }
        }
        status?.Invoke("Чтение базы KDBX…");
        // Read-only: never call Save() or SaveAs(). Keep no unlocked library object after import.
        using var database = new Database(input.Path, key);
        try { database.Open(); }
        catch (CryptographicException)
        {
            // Cryptographic failures during unlock may be caused by incorrect
            // credentials; never assert that the KDBX file is damaged.
            throw KdbxOpenErrorClassifier.CredentialsRejected();
        }
        catch (EndOfStreamException)
        {
            throw new VaultOpenException("KDBX_FORMAT", "Файл KDBX обрезан, повреждён или имеет неподдерживаемый формат.");
        }
        catch (InvalidDataException ex)
        {
            // DgNet.Keepass 0.2.0 reports a wrong composite key using an
            // InvalidDataException (KDBX 4: header HMAC; KDBX 3: start bytes).
            // Do NOT turn every InvalidDataException into a credentials error:
            // header SHA256 and block integrity failures are different cases.
            throw KdbxOpenErrorClassifier.FromInvalidData(ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or NotSupportedException)
        {
            throw new VaultOpenException("KDBX_READER", "Библиотека KDBX не смогла прочитать базу. Возможна неподдерживаемая особенность формата.");
        }
        // Read display-only cipher/KDF identifiers after a successful unlock.
        // A malformed/unknown header identifier never blocks access to records.
        var security = KdbxHeaderInfo.TryRead(input.Path);
        var databaseName = CleanDisplayName(database.Metadata.Name, input.Path);
        var recordList = new List<VaultRecord>();
        var recycle = database.GetRecycleBin()?.Uuid;
        var icons = database.Metadata.CustomIcons.ToDictionary(i => i.Uuid, i => i.Data);
        foreach (var group in database.RootGroup.Groups)
            Visit(group, "", recycle, icons, recordList);
        foreach (var entry in database.RootGroup.Entries)
            AddEntry(entry, "", database.RootGroup, icons, recordList);
        status?.Invoke($"Прочитано записей: {recordList.Count}");
        return new VaultSnapshot
        {
            RootUuid = database.RootGroup.Uuid,
            DisplayName = databaseName,
            CipherName = security.Cipher,
            KdfName = security.Kdf,
            GroupCount = database.RootGroup.Groups.Sum(g => CountGroups(g, recycle)),
            FormatVersion = database.Version.ToString(),
            Records = recordList
        };
    }

    private static int CountGroups(Group group, Guid? recycle)
    {
        // The invisible root and the recycle-bin subtree are not counted.
        if (group.Uuid == recycle) return 0;
        return 1 + group.Groups.Sum(child => CountGroups(child, recycle));
    }

    private static string CleanDisplayName(string? metadataName, string filePath)
    {
        var name = string.IsNullOrWhiteSpace(metadataName)
            ? Path.GetFileNameWithoutExtension(filePath)
            : metadataName;
        // Do not let control characters from KDBX metadata disrupt the footer.
        var cleaned = new string((name ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "KDBX" : cleaned;
    }

    private static void Visit(Group group, string parent, Guid? recycle,
                              Dictionary<Guid, byte[]> icons, List<VaultRecord> output)
    {
        if (group.Uuid == recycle) return;
        var path = string.IsNullOrEmpty(parent) ? group.Name : parent + "/" + group.Name;
        foreach (var entry in group.Entries) AddEntry(entry, path, group, icons, output);
        foreach (var child in group.Groups) Visit(child, path, recycle, icons, output);
    }

    private static void AddEntry(Entry entry, string group, Group parentGroup, Dictionary<Guid, byte[]> icons,
                                 List<VaultRecord> output)
    {
        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in entry.Strings)
            if (pair.Key is not ("Title" or "UserName" or "Password" or "URL" or "Notes"))
                extra[pair.Key] = pair.Value.Value;
        // KeePassXC stores browser-integration URLs as KP2A_URL, KP2A_URL_1, etc.
        // Keep them distinct; the mapping selects the source at layout time.
        int extraUrlCandidateCount = extra.Keys.Count(UrlSourceSelector.IsAdditionalUrlAttribute);
        var extraUrls = UrlSourceSelector.ExtractAdditionalUrls(extra);
        foreach (var key in extra.Keys.Where(UrlSourceSelector.IsAdditionalUrlAttribute).ToArray())
            extra.Remove(key);
        var iconBytes = icons.GetValueOrDefault(entry.CustomIconUuid);
        var image = RecordIconImages.Get(entry.IconId, iconBytes, out var imageOwned);
        var groupBytes = icons.GetValueOrDefault(parentGroup.CustomIconUuid);
        var groupImage = RecordIconImages.Get(parentGroup.IconId == 0 ? 27 : parentGroup.IconId,
                                               groupBytes, out var groupImageOwned);
        output.Add(new VaultRecord
        {
            Uuid = entry.Uuid, Group = group, Title = entry.Title,
            UserName = entry.UserName, Password = entry.Password, Url = entry.Url, AdditionalUrls = extraUrls, AdditionalUrlCandidateCount = extraUrlCandidateCount,
            Notes = entry.Notes, IconId = entry.IconId, CustomIconUuid = entry.CustomIconUuid,
            CustomIconPng = iconBytes is null ? null : (byte[])iconBytes.Clone(),
            IconImage = image, IconImageOwned = imageOwned,
            GroupIconId = parentGroup.IconId, GroupCustomIconUuid = parentGroup.CustomIconUuid,
            GroupIconImage = groupImage, GroupIconImageOwned = groupImageOwned,
            AdditionalFields = OtpFieldParser.Normalize(extra),
            HasAttachments = entry.Binaries.Count != 0
        });
    }
}


// Error mapping is deliberately coupled to the exact diagnostics emitted by
// DgNet.Keepass 0.2.0 KdbxReader. Untrusted exception text is NEVER displayed.
internal static class KdbxOpenErrorClassifier
{
    private const string WrongCredentials =
        "Неверный мастер-пароль или другой компонент ключа (файл-ключ / YubiKey). " +
        "Если данные верны, проверьте целостность KDBX.";

    internal static VaultOpenException CredentialsRejected()
        => new("INVALID_CREDENTIALS", WrongCredentials);

    internal static VaultOpenException FromInvalidData(InvalidDataException ex)
    {
        return ex.Message switch
        {
            // DgNet.Keepass KDBX 4: derived composite key does not authenticate
            // the outer header. KDBX 3: decrypted stream start marker differs.
            // Either can ALSO arise after file corruption; don't claim certainty.
            "Header HMAC verification failed." or
            "StreamStartBytes verification failed." => CredentialsRejected(),

            // Header SHA256 is independent of credentials; when the outer
            // header HMAC already passed, block HMAC failures indicate damage.
            "Header SHA256 verification failed." or
            "Block hash verification failed." => IntegrityFailure(),

            _ when ex.Message.StartsWith("Block ", StringComparison.Ordinal) &&
                   ex.Message.EndsWith(" HMAC verification failed.", StringComparison.Ordinal) => IntegrityFailure(),

            // No guess from other InvalidDataExceptions (e.g. malformed fields).
            _ => new VaultOpenException("KDBX_FORMAT",
                "Ошибка структуры KDBX; возможны повреждение или несовместимость библиотеки чтения.")
        };
    }

    private static VaultOpenException IntegrityFailure()
        => new("KDBX_INTEGRITY", "Не совпала контрольная сумма KDBX. Файл, возможно, повреждён.");
}

// .NET 10 UnsafeAccessor is compile-time binding to a private field, not runtime reflection.
// This is intentionally isolated because DgNet.Keepass 0.2.0 exposes no in-memory
// method for contributing an externally produced key component.
internal static class CompositeKeyBridge
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_components")]
    private static extern ref List<byte[]> Components(CompositeKey key);

    internal static void AddKeyComponent(CompositeKey key, byte[] component)
    {
        if (component.Length != 32) throw new ArgumentException("Key component must be 32 bytes.");
        Components(key).Add((byte[])component.Clone());
    }
}

internal static class Kdbx4Challenge
{
    internal static byte[] ReadKdfSeed(string path)
    {
        using var s = File.OpenRead(path);
        using var br = new BinaryReader(s, Encoding.UTF8, leaveOpen: true);
        if (br.ReadUInt32() != 0x9AA2D903 || br.ReadUInt32() != 0xB54BFB67)
            throw new InvalidDataException("Неверная сигнатура KDBX.");
        var minor = br.ReadUInt16();
        var major = br.ReadUInt16();
        if (major != 4) throw new NotSupportedException("YubiKey для KDBX 3 пока не поддерживается. Откройте базу в KeePassXC и обновите до KDBX 4.");
        for (var i = 0; i < 64; i++)
        {
            var id = br.ReadByte();
            var length = br.ReadInt32();
            if (length < 0 || length > 1024 * 1024) throw new InvalidDataException("Недопустимая длина поля заголовка.");
            var bytes = br.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            if (id == 11) return ParseVariantSeed(bytes);
            if (id == 0) break;
        }
        throw new InvalidDataException("В заголовке KDBX 4 не найдены параметры KDF.");
    }

    private static byte[] ParseVariantSeed(byte[] data)
    {
        using var s = new MemoryStream(data, writable: false);
        using var br = new BinaryReader(s, Encoding.UTF8);
        if (br.ReadUInt16() != 0x0100) throw new InvalidDataException("Неподдерживаемый VariantDictionary.");
        while (s.Position < s.Length)
        {
            var type = br.ReadByte();
            if (type == 0) break;
            var nameLen = br.ReadInt32();
            if (nameLen < 1 || nameLen > 128) throw new InvalidDataException("Недопустимое имя параметра KDF.");
            var key = Encoding.UTF8.GetString(br.ReadBytes(nameLen));
            var valueLen = br.ReadInt32();
            if (valueLen < 0 || valueLen > 64 * 1024) throw new InvalidDataException("Недопустимое значение параметра KDF.");
            var value = br.ReadBytes(valueLen);
            if (value.Length != valueLen) throw new EndOfStreamException();
            if (key == "S" && type == 0x42 && value.Length is >= 16 and <= 64) return value;
        }
        throw new InvalidDataException("В параметрах KDF отсутствует соль S.");
    }
}

// Self-contained icon pictograms are embedded as application resources;
// standard icons do not require external KeePassXC files or runtime network access.
internal static class RecordIconImages
{
    private static readonly Dictionary<int, Bitmap> Cache = new();
    private static readonly object CacheLock = new();

    internal static Bitmap? Get(int standardId, byte[]? customPng, out bool owned)
    {
        owned = false;
        if (customPng is { Length: > 0 and < 8_000_000 })
        {
            try
            {
                using var stream = new MemoryStream(customPng, writable: false);
                var bitmap = new Bitmap(stream);
                if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height <= 2_000_000)
                {
                    owned = true;
                    return bitmap;
                }
                bitmap.Dispose();
            }
            catch (Exception) { /* Fall back to the embedded standard icon. */ }
        }
        standardId = Math.Clamp(standardId, 0, 68);
        lock (CacheLock)
        {
            if (Cache.TryGetValue(standardId, out var existing)) return existing;
            try
            {
                var uri = new Uri($"avares://KeePassXCCardPrint/assets/icons/standard-{standardId:00}.png");
                using var stream = AssetLoader.Open(uri);
                var bitmap = new Bitmap(stream);
                Cache[standardId] = bitmap;
                return bitmap;
            }
            catch (Exception) { return null; }
        }
    }
}
