using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// KeePass/KeePassXC key-file parser. The KDBX library reads XML v1 but does
/// not recognize the XML v2 format emitted by KeePassXC (hex + checksum).
/// No key material or file contents are ever logged or persisted.
/// </summary>
internal static class KeePassKeyFile
{
    internal static byte[] Read(string path)
    {
        // Limit how much untrusted data we read while retaining support for
        // arbitrary binary files as KeePass key sources.
        var size = new FileInfo(path).Length;
        if (size < 1 || size > 64L * 1024 * 1024)
            throw new VaultOpenException("KEY_SIZE", "Файл-ключ пустой или слишком большой (более 64 МБ).");
        var bytes = File.ReadAllBytes(path);
        try
        {
            if (LooksLikeXmlKeyFile(bytes)) return ReadXml(bytes);
            if (bytes.Length == 32) return (byte[])bytes.Clone();
            if (bytes.Length == 64 && IsHexAscii(bytes))
                return Convert.FromHexString(Encoding.ASCII.GetString(bytes));
            // KeePass treats arbitrary files as a source for SHA-256 key material.
            return SHA256.HashData(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static bool LooksLikeXmlKeyFile(byte[] bytes)
    {
        // Covers UTF-8 with/without BOM, UTF-16LE/BE. Other files remain valid
        // arbitrary-file key sources, matching KeePass behavior.
        ReadOnlySpan<byte> data = bytes;
        // Compare byte prefixes explicitly: collection expressions here can be
        // inferred as spans of int rather than byte, causing CS1929.
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            data = data[3..];
        if (data.Length >= 2 &&
            ((data[0] == 0xFF && data[1] == 0xFE) ||
             (data[0] == 0xFE && data[1] == 0xFF)))
            return true;
        int i = 0;
        while (i < data.Length && (data[i] == ' ' || data[i] == '\t' || data[i] == '\r' || data[i] == '\n')) i++;
        return i < data.Length && data[i] == '<';
    }

    private static byte[] ReadXml(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data, writable: false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 2_000_000,
                IgnoreComments = true
            });
            var document = XDocument.Load(reader);
            if (document.Root?.Name.LocalName != "KeyFile")
                throw new VaultOpenException("KEY_XML", "Некорректная структура XML-файла ключа.");
            var version = document.Root.Element("Meta")?.Element("Version")?.Value.Trim() ?? "";
            var element = document.Root.Element("Key")?.Element("Data")
                ?? throw new VaultOpenException("KEY_XML", "В XML-файле ключа отсутствует поле Data.");
            var clean = string.Concat(element.Value.Where(c => !char.IsWhiteSpace(c)));
            byte[] key;
            if (version.StartsWith("1.0", StringComparison.Ordinal))
            {
                key = Convert.FromBase64String(clean);
            }
            else if (version == "2.0")
            {
                if (clean.Length != 64 || !IsHexText(clean))
                    throw new VaultOpenException("KEY_XML", "Некорректный ключ в XML v2.");
                key = Convert.FromHexString(clean);
                var hash = element.Attribute("Hash")?.Value.Trim() ?? "";
                if (hash.Length != 8 || !IsHexText(hash))
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new VaultOpenException("KEY_CHECKSUM", "Отсутствует контрольная сумма XML v2-файла ключа.");
                }
                Span<byte> digest = stackalloc byte[32];
                SHA256.HashData(key, digest);
                var expected = Convert.FromHexString(hash);
                var equal = CryptographicOperations.FixedTimeEquals(digest[..4], expected);
                CryptographicOperations.ZeroMemory(digest);
                CryptographicOperations.ZeroMemory(expected);
                if (!equal)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new VaultOpenException("KEY_CHECKSUM", "Нарушена контрольная сумма файла-ключа XML v2.");
                }
            }
            else
                throw new VaultOpenException("KEY_VERSION", "Неподдерживаемая версия XML-файла ключа.");

            if (key.Length != 32)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new VaultOpenException("KEY_XML", "Файл-ключ должен содержать 32 байта ключа.");
            }
            return key;
        }
        catch (VaultOpenException) { throw; }
        catch (Exception ex) when (ex is XmlException or FormatException or InvalidOperationException)
        {
            throw new VaultOpenException("KEY_XML", "Некорректный XML-файл ключа.");
        }
    }

    private static bool IsHexAscii(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
            if (!IsHexChar((char)b)) return false;
        return true;
    }
    private static bool IsHexText(string value)
    {
        foreach (var c in value)
            if (!IsHexChar(c)) return false;
        return true;
    }
    private static bool IsHexChar(char c) => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}

internal sealed class VaultOpenException : Exception
{
    public string Code { get; }
    public VaultOpenException(string code, string safeMessage) : base(safeMessage) => Code = code;
}
