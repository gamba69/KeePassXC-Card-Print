using System.Text;

namespace KeePassXCCardPrint.Services;

/// <summary>
/// The unencrypted outer KDBX header describes the payload cipher and KDF.
/// We only read well-known public identifiers, never the protected XML,
/// salts or key material. This is a display-only, fail-closed parser.
/// </summary>
internal sealed record KdbxHeaderInfo(string Cipher, string Kdf)
{
    private const string Unknown = "?";
    private const uint Signature1 = 0x9AA2D903;
    private const uint Signature2 = 0xB54BFB67;
    private const int MaxHeaderField = 1024 * 1024;

    public static KdbxHeaderInfo TryRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or NotSupportedException or ArgumentException)
        {
            // Metadata is optional. It must never prevent opening a valid vault.
            return new KdbxHeaderInfo(Unknown, Unknown);
        }
    }

    internal static KdbxHeaderInfo Read(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (br.ReadUInt32() != Signature1 || br.ReadUInt32() != Signature2)
            throw new InvalidDataException("Invalid KDBX signature.");
        _ = br.ReadUInt16(); // Minor format version.
        var major = br.ReadUInt16();
        if (major is not (3 or 4)) throw new NotSupportedException("Unknown KDBX version.");

        string cipher = Unknown;
        string kdf = major == 3 ? "AES-KDF" : Unknown;
        var terminated = false;
        for (var i = 0; i < 64; i++)
        {
            byte id = br.ReadByte();
            int length = major == 3 ? br.ReadUInt16() : br.ReadInt32();
            if (length < 0 || length > MaxHeaderField)
                throw new InvalidDataException("Invalid KDBX header field size.");
            // Limit allocations and reject truncated input, including ignored fields.
            byte[] data = br.ReadBytes(length);
            if (data.Length != length) throw new EndOfStreamException();
            if (id == 0) { terminated = true; break; }
            if (id == 2 && data.Length == 16)
                cipher = CipherName(Convert.ToHexString(data));
            else if (major == 4 && id == 11)
                kdf = ReadKdfName(data);
        }
        if (!terminated) throw new InvalidDataException("KDBX header terminator not found.");
        return new KdbxHeaderInfo(cipher, kdf);
    }

    private static string CipherName(string hex) => hex switch
    {
        "31C1F2E6BF714350BE5805216AFC5AFF" => "AES-256",
        "61AB05A1946441C38D743A563DF8DD35" => "AES-128",
        "D6038A2B8B6F4CB5A524339A31DBB59A" => "ChaCha20",
        "AD68F29F576F4BB9A36AD47AF965346C" => "Twofish-256",
        _ => Unknown
    };

    private static string ReadKdfName(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var br = new BinaryReader(stream, Encoding.UTF8);
        if (br.ReadUInt16() != 0x0100) return Unknown;
        for (var i = 0; i < 64 && stream.Position < stream.Length; i++)
        {
            byte type = br.ReadByte();
            if (type == 0) break;
            int nameLength = br.ReadInt32();
            if (nameLength is < 1 or > 128) return Unknown;
            var name = br.ReadBytes(nameLength);
            if (name.Length != nameLength) return Unknown;
            int valueLength = br.ReadInt32();
            if (valueLength < 0 || valueLength > 65536) return Unknown;
            var value = br.ReadBytes(valueLength);
            if (value.Length != valueLength) return Unknown;
            if (type != 0x42 || Encoding.UTF8.GetString(name) != "$UUID" || valueLength != 16)
                continue;
            return Convert.ToHexString(value) switch
            {
                "C9D9F39A628A4460BF740D08C18A4FEA" => "AES-KDF",
                "7C02BB8279A74AC0927D114A00648238" => "AES-KDF",
                "EF636DDF8C29444B91F7A9A403E30A0C" => "Argon2d",
                "9E298B1956DB4773B23DFC3EC6F0A1E6" => "Argon2id",
                _ => Unknown
            };
        }
        return Unknown;
    }
}
