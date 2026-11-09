using KeePassXCCardPrint.Rendering;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SkiaSharp;

namespace KeePassXCCardPrint.Platforms.Windows.Printing;

/// <summary>
/// Keeps font bytes in memory only for an XPS page. Never caches decrypted card text.
/// For variable typefaces, the native HarfBuzz subset API pins every axis at
/// the exact design position used by the already-shaped Skia font. The original
/// glyph IDs are retained. If any prerequisite fails, the caller renders the
/// already-shaped glyph outlines instead, never a different font instance.
/// </summary>
internal static partial class XpsFontEmbedding
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAxis { public uint Tag; public float Value; }

    [LibraryImport("KpcXpsBridge.dll",EntryPoint="KpcFreezeFont")]
    private static partial int FreezeFontNative(byte[] fontData,uint length,int collectionIndex,
        [In] NativeAxis[] axes,uint axisCount,out IntPtr output,out uint outputLength);
    [LibraryImport("KpcXpsBridge.dll",EntryPoint="KpcFreeFont")]
    private static partial void FreeFontNative(IntPtr memory);

    private const int MaxFontBytes=32*1024*1024;
    [ThreadStatic] private static string? _lastFailure;
    internal static string LastFailure => _lastFailure ?? "Unknown font resource rejection";
    private static bool Reject(string reason) { _lastFailure=reason; return false; }

    private static bool HasSameGlyphIndices(byte[] bytes,int index,SKTypeface original)
    {
        using var data=SKData.CreateCopy(bytes);
        using var candidate=SKTypeface.FromData(data,index);
        return candidate is not null && candidate.GlyphCount==original.GlyphCount;
    }
    private static ushort Read16(byte[] bytes,int offset) =>
        (ushort)((bytes[offset]<<8)|bytes[offset+1]);
    private static uint Read32(byte[] bytes,int offset) =>
        ((uint)bytes[offset]<<24)|((uint)bytes[offset+1]<<16)|((uint)bytes[offset+2]<<8)|bytes[offset+3];

    // Validates an SFNT face, including the base offset of a TTC. This also
    // checks OS/2.fsType: restricted-license and bitmap-only fonts MUST NOT be
    // embedded as unrestricted outline resources.
    private static bool TryFindTable(byte[] bytes,int faceIndex,string name,out int offset,out int length)
    {
        offset=length=0;
        if(bytes.Length<12)return false;
        int baseOffset=0;
        if(Read32(bytes,0)==0x74746366u) // 'ttcf'
        {
            if(bytes.Length<12)return false;
            uint count=Read32(bytes,8);
            if(faceIndex<0 || (uint)faceIndex>=count || count>1024 || bytes.Length<12L+count*4)return false;
            uint position=Read32(bytes,12+faceIndex*4);
            if(position>bytes.Length-12)return false;
            baseOffset=(int)position;
        }
        if(baseOffset+12>bytes.Length)return false;
        uint signature=Read32(bytes,baseOffset);
        if(signature!=0x00010000u && signature!=0x4F54544Fu && signature!=0x74727565u)return false;
        int tables=Read16(bytes,baseOffset+4);
        if(tables>2048 || baseOffset+12L+tables*16>bytes.Length)return false;
        for(int i=0;i<tables;i++)
        {
            int entry=baseOffset+12+i*16;
            if(bytes[entry]!=(byte)name[0] || bytes[entry+1]!=(byte)name[1] ||
               bytes[entry+2]!=(byte)name[2] || bytes[entry+3]!=(byte)name[3])continue;
            uint start=Read32(bytes,entry+8), len=Read32(bytes,entry+12);
            if(start>bytes.Length || len>bytes.Length-start)return false;
            offset=(int)start; length=(int)len;
            return true;
        }
        return false;
    }

    private static bool CanEmbed(byte[] bytes,int faceIndex)
    {
        if(!TryFindTable(bytes,faceIndex,"head",out _,out _))return false;
        if(TryFindTable(bytes,faceIndex,"OS/2",out int os2,out int length) && length>=10)
        {
            ushort fsType=Read16(bytes,os2+8);
            // Only installable embedding (fsType == 0) uses an unobfuscated
            // XPS resource. Other licenses require a restricted embedding
            // policy and are retained as vector outlines for now.
            if(fsType!=0)return false;
        }
        return true;
    }

    private static bool IsVariable(byte[] bytes,int index) =>
        TryFindTable(bytes,index,"fvar",out _,out _);

    private static bool TryFreeze(byte[] original,int faceIndex,SKTypeface face,
        out byte[] instance)
    {
        instance=Array.Empty<byte>();
        var axes=face.VariationDesignPosition;
        if(axes is null || axes.Length==0)return false;
        var native=new NativeAxis[axes.Length];
        for(int i=0;i<axes.Length;i++)
        {
            string tag=axes[i].Axis.ToString();
            if(tag.Length!=4 || !float.IsFinite(axes[i].Value))return false;
            native[i]=new NativeAxis
            {
                Tag=((uint)tag[0]<<24)|((uint)tag[1]<<16)|((uint)tag[2]<<8)|tag[3],
                Value=axes[i].Value
            };
        }
        IntPtr buffer=IntPtr.Zero;
        try
        {
            int hr=FreezeFontNative(original,checked((uint)original.Length),faceIndex,
                native,checked((uint)native.Length),out buffer,out uint length);
            if(hr<0 || buffer==IntPtr.Zero || length<64 || length>MaxFontBytes)
                return Reject($"HarfBuzz variable-font instancing failed (HRESULT 0x{hr:X8}). " +
                    "Check whether libHarfBuzzSharp.dll exports its hb-subset functions.");
            instance=new byte[(int)length];
            Marshal.Copy(buffer,instance,0,instance.Length);
            // A partial variable-font instance would still pick its default
            // weight in XPS. Refuse it rather than silently changing a secret.
            if(IsVariable(instance,0) || !CanEmbed(instance,0) ||
                !HasSameGlyphIndices(instance,0,face))
            {
                CryptographicOperations.ZeroMemory(instance);
                instance=Array.Empty<byte>();
                return Reject("The static instance is invalid, retains variation axes, " +
                    "or renumbered glyph IDs; using vector outlines instead.");
            }
            return true;
        }
        catch(DllNotFoundException){return Reject("XPS native bridge is missing.");}
        catch(EntryPointNotFoundException){return Reject("XPS native bridge lacks the KpcFreezeFont export.");}
        finally { if(buffer!=IntPtr.Zero)FreeFontNative(buffer); }
    }

    internal static bool TryPrepare(SKTypeface face,out byte[] bytes,out int collectionIndex)
    {
        bytes=Array.Empty<byte>();collectionIndex=0;
        _lastFailure=null;
        try
        {
            using var stream=face.OpenStream(out int index);
            if(stream is null || !stream.HasLength || stream.Length<64 || stream.Length>MaxFontBytes)
                return Reject("No usable OpenType font stream is available.");
            // OpenStream may return a file-backed SKStreamAsset. GetData() is
            // not a reliable way to access such streams: it can return null
            // even when HasLength/Length are valid. Read the bytes explicitly.
            // Read() may return fewer bytes than requested, so never assume
            // that one call fills the entire buffer.
            if(stream.HasPosition && stream.Position!=0 && !stream.Rewind())
                return Reject("OpenType font stream could not be rewound.");
            int expectedLength=stream.Length;
            byte[] original=new byte[expectedLength];
            byte[] chunk=new byte[Math.Min(expectedLength,64*1024)];
            int received=0;
            while(received<expectedLength)
            {
                int requested=Math.Min(chunk.Length,expectedLength-received);
                int count=stream.Read(chunk,requested);
                if(count<=0 || count>requested)
                    return Reject($"OpenType font stream ended after {received} of {expectedLength} bytes.");
                Buffer.BlockCopy(chunk,0,original,received,count);
                received+=count;
            }
            // The SFNT signature and the table bounds are validated below.
            // An OpenStream that did not yield genuine font bytes must never
            // be passed to HarfBuzz or included as a native XPS font resource.
            if(!CanEmbed(original,index))
                return Reject("Font format or OS/2 embedding permissions disallow normal XPS embedding.");
            if(IsVariable(original,index))
            {
                if(!TryFreeze(original,index,face,out bytes))return false;
                collectionIndex=0; // Freezing produces one static face.
                return true;
            }
            bytes=original;
            collectionIndex=index;
            return true;
        }
        catch(Exception e) when(e is not OutOfMemoryException)
        {
            _lastFailure="Font data could not be prepared: "+e.GetType().Name;
            // Losing a font must not change the printed secret: its identical
            // Skia glyph outlines remain available as an exact vector fallback.
            return false;
        }
    }
}
