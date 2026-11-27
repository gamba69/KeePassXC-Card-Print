using SkiaSharp;

namespace KeePassXCCardPrint.Services;

internal static class HevcDecoderSelfTests
{
    internal static readonly string[] H264 = [
                        "AAAAAQkQAAAAAWdCwAraCjaEAAADAAQAAAMAUjxImoAAAAABaM4PyAAAAWWIhDomKAAJAsnJycnJigACgpJycnJ111111111111111111111111111111111111111111111111114A=", "AAAAAQkwAAABQZogPoHs", "AAAAAQkwAAABQZpAPoHs",
                        "AAAAAQkwAAABQZpgPoHs", "AAAAAQkwAAABQZqAEKB7", "AAAAAQkwAAABQZqgEKB7",
                        "AAAAAQkwAAABQZrAEKB7", "AAAAAQkwAAABQZrgEKB7", "AAAAAQkwAAABQZsAEKB7",
                        "AAAAAQkwAAABQZsgEKB7", "AAAAAQkwAAABQZtAEKB7", "AAAAAQkwAAABQZtgEKB7",
                        "AAAAAQkQAAAAAWdCwAraCjaEAAADAAQAAAMAUjxImoAAAAABaM4PyAAAAWWIggEaJigADUhJycnJyTCgAD8HJPJycnXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXgA==", "AAAAAQkwAAABQZogPoHs", "AAAAAQkwAAABQZpAPoHs",
                        "AAAAAQkwAAABQZpgPoHs", "AAAAAQkwAAABQZqAEKB7", "AAAAAQkwAAABQZqgEKB7",
                        "AAAAAQkwAAABQZrAEKB7", "AAAAAQkwAAABQZrgEKB7", "AAAAAQkwAAABQZsAEKB7",
                        "AAAAAQkwAAABQZsgEKB7", "AAAAAQkwAAABQZtAEKB7", "AAAAAQkwAAABQZtgEKB7",
                        "AAAAAQkQAAAAAWdCwAraCjaEAAADAAQAAAMAUjxImoAAAAABaM4PyAAAAWWIhASomKAAOKMnJycnJECgAD8FJEycnJ111111111111111111111111111111111111111111111111114A==", "AAAAAQkwAAABQZogPoHs", "AAAAAQkwAAABQZpAPoHs",
                        "AAAAAQkwAAABQZpgPoHs", "AAAAAQkwAAABQZqAEKB7", "AAAAAQkwAAABQZqgEKB7",
                        "AAAAAQkwAAABQZrAEKB7", "AAAAAQkwAAABQZrgEKB7", "AAAAAQkwAAABQZsAEKB7",
                        "AAAAAQkwAAABQZsgEKB7", "AAAAAQkwAAABQZtAEKB7", "AAAAAQkwAAABQZtgEKB7",
                        "AAAAAQkQAAAAAWdCwAraCjaEAAADAAQAAAMAUjxImoAAAAABaM4PyAAAAWWIggEqJigADeFJycnJyRAoAA/ByRMnJyddddddddddddddddddddddddddddddddddddddddddddddddddeA==", "AAAAAQkwAAABQZogPoHs", "AAAAAQkwAAABQZpAPoHs",
                        "AAAAAQkwAAABQZpgPoHs", "AAAAAQkwAAABQZqAEKB7", "AAAAAQkwAAABQZqgEKB7",
                        "AAAAAQkwAAABQZrAEKB7", "AAAAAQkwAAABQZrgEKB7", "AAAAAQkwAAABQZsAEKB7",
                        "AAAAAQkwAAABQZsgEKB7", "AAAAAQkwAAABQZtAEKB7", "AAAAAQkwAAABQZtgEKB7"
    ];
    internal static readonly string[] Main8 = [
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKAGvEUWU9JJm//wzcXPju8rPdu7VxoM4aKMUOGQ=",
        "AAAAAUYBMAAAAQIB0Al+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0BH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Bif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGsEE1clFnA+YIC4v2Ou+Aw2O3/7tY45QCcWoCqLy5k",
        "AAAAAUYBMAAAAQIB0Cl+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0DH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Dif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGsIE1chFnA+YJp//4Wq+Aw2O0t8Zr45QCcWoCqLy5k",
        "AAAAAUYBMAAAAQIB0El+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0FH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Fif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGsME1dEWf4gbLoPk0fJLmkef/6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Gl+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0HH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Hif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGsQE1dEWf4gbL//m9DJLmkeah6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Il+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0JH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Jif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGsUE1dEWf4gbLoPk0fJLmkef/6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Kl+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0LH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Lif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGsYE1dUWf4gbL//m9DJLmkeah6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Ml+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0NH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Nif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGscE1dUWf4gbLoPk0fJLmkef/6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Ol+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0PH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Pif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGsgE1dUWf4gbL//m9DJLmkeah6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Ql+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0RH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Rif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGskE1dEWf4gbLoPk0fJLmkef/6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Sl+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0TH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Tif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGsoE1dUWf4gbL//m9DJLmkeah6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Ul+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0VH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Vif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8BYAAAAwCQAAADAAADAB6SgJAAAAABQgEBAWAAAAMAkAAAAwAAAwAeoBQgYWWSpJMrmgIAAAMAAgAAAwAUEAAAAAFEAcFytGJAAAABKgGssE1dEWf4gbLoPk0fJLmkef/6AHzqQ4pI4KoAygI=",
        "AAAAAUYBMAAAAQIB0Wl+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0XH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Xif/cQwGIJU+S8wtlE=",
    ];
    internal static readonly string[] Main10 = [
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKAGvEUWU9JMAv/9BmFz47vKz5N21caDOGqMUOGQ=",
        "AAAAAUYBMAAAAQIB0Al+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0BH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Bif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGsEE1clFpA+YKDuL+ORL4DDY7f/zScY4O1qf+sq3JX3g==",
        "AAAAAUYBMAAAAQIB0Cl+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0DH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Dif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGsIE1chFpA+YKDv/+RGb4DDY7S3zScY4NCqf+sq3JX3g==",
        "AAAAAUYBMAAAAQIB0El+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0FH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Fif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGsME1dEWn4ggnoPotuyS5pHn/+rQNOjhubktSrEFT+",
        "AAAAAUYBMAAAAQIB0Gl+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0HH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Hif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGsQE1dEWn4ggn//pS1yS5pHmoerQNOjZAbktSrEFT+",
        "AAAAAUYBMAAAAQIB0Il+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0JH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Jif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGsUE1dEWn4ggnoPotuyS5pEE//sZOTqQ4pI4CqAMoC",
        "AAAAAUYBMAAAAQIB0Kl+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0LH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Lif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGsYE1dUWn4ggn//pS1yS5pEEqHsZOTqQ4pI4CqAMoC",
        "AAAAAUYBMAAAAQIB0Ml+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0NH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Nif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGscE1dUWn4ggnoPotuyS5pEE//sZOTqQ4pI4CqAMoC",
        "AAAAAUYBMAAAAQIB0Ol+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0PH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Pif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGsgE1dUWn4gh+//6wiskuaR5qHsZOTqQ4pI4CqAMoC",
        "AAAAAUYBMAAAAQIB0Ql+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0RH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Rif/cQwGIpU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGskE1dEWn4gh+6D6mJskuaR5//sZOTqQ4pI4CqAMoC",
        "AAAAAUYBMAAAAQIB0Sl+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0TH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Tif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGsoE1dUWn4gh+//6wiskuaR5qHsZOTqQ4pI4CqAMoC",
        "AAAAAUYBMAAAAQIB0Ul+EMYglfkvMLZR",
        "AAAAAUYBMAAAAQIB0VH/1EMGIpX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Vif/cQwGIJU+S8wtlE=",
        "AAAAAUYBEAAAAAFAAQwB//8CIAAAAwCQAAADAAADAB6SgJAAAAABQgEBAiAAAAMAkAAAAwAAAwAeoBQgYTZZKkkyuaAgAAADACAAAAMBQQAAAAFEAcFytGJAAAABKgGssE1dEWn4gh+6D6mJskuaR5//sZOTqQ4pI4CqAMoC",
        "AAAAAUYBMAAAAQIB0Wl+EMYilfkvMLZR",
        "AAAAAUYBMAAAAQIB0XH/1EMGIJX5LzC2UQ==",
        "AAAAAUYBMAAAAQIB0Xif/cQwGIJU+S8wtlE=",
    ];
    internal static string Run()
    {
        return Task.Run(()=>{
            var results=new List<string>();
            foreach(var (depth,frames) in new[]{(8,Main8),(10,Main10)})
            {
                var first=Convert.FromBase64String(frames[0]);
                var info=AdaptiveVideoDecoder.Inspect(first,true);
                if(!info.Key||info.Profile!=(depth==8?1:2))throw new InvalidOperationException("HEVC SPS profile / keyframe");
                using var software=new SoftwareHevcDecoder();
                int count=0;bool normal=false,inverted=false;
                void Check(SKBitmap? bmp)
                {
                    if(bmp is null)return;
                    try
                    {
                        if((bmp.Width,bmp.Height)!=(160,96))throw new InvalidOperationException("HEVC output dimensions");
                        var left=bmp.GetPixel(40,48).Red;var right=bmp.GetPixel(120,48).Red;
                        normal|=left<50&&right>200;inverted|=left>200&&right<50;count++;
                    }
                    finally{ScannerPixels.ClearAndDispose(bmp);}
                }
                void Verify(string backend)
                {if(count<2||!normal||!inverted)throw new InvalidOperationException($"HEVC {depth}-bit {backend}: decoded={count}, normal={normal}, inverted={inverted}");}
                for(int i=0;i<frames.Length;i++)Check(software.Decode(Convert.FromBase64String(frames[i]),i*1_000_000L,true));
                Verify("libde265");results.Add($"libde265 Main{depth}: {count}/{frames.Length}");
                // System HEVC is optional; report failure separately rather than require a paid codec.
                // The production selector must nevertheless decode both fixture profiles.
                // Simulate the actual SharpRTSP callback: every segment is already
                // Annex B prefixed; SDP carries raw parameter sets separately.
                using(var rtpSoftware=new SoftwareHevcDecoder())
                {
                    count=0;normal=inverted=false;byte[][] sdp=[];
                    for(int i=0;i<frames.Length;i++)
                    {
                        var units=AnnexBUnits.Split(Convert.FromBase64String(frames[i]));
                        var parameters=units.Where(n=>SharpNetworkCameraSource.Parameter(SharpNetworkCameraSource.NalType(n,true),true)).Select(n=>n.ToArray()).ToArray();
                        if(parameters.Length>0)sdp=parameters;
                        var segments=units.Where(n=>!SharpNetworkCameraSource.Parameter(SharpNetworkCameraSource.NalType(n,true),true)).Select(n=>{
                            var segment=new byte[n.Length+4];segment[3]=1;n.Span.CopyTo(segment.AsSpan(4));return (ReadOnlyMemory<byte>)segment;
                        }).ToArray();
                        var assembled=SharpNetworkCameraSource.Assemble("H265",segments,sdp);
                        Check(rtpSoftware.Decode(assembled,i*1_000_000L,true));
                    }
                    Verify("SharpRTSP / libde265");results.Add($"RTP Main{depth}: {count}/{frames.Length}");
                }
                using var mf=new MediaFoundationVideoDecoder();count=0;normal=inverted=false;
                try
                {
                    if(mf.Initialize("H265",info.Profile))
                    {
                        for(int i=0;i<frames.Length;i++)Check(mf.Decode("H265",Convert.FromBase64String(frames[i]),i*1_000_000L,true,info.Profile));
                        results.Add($"MF Main{depth}: {count}/{frames.Length}, pattern={normal&&inverted}, {mf.Detail}");
                    }
                    else results.Add($"MF Main{depth}: {mf.Detail}");
                }
                catch(NetworkCameraException ex){results.Add($"MF Main{depth}: {ex.Detail}");}
                using var adaptive=new AdaptiveVideoDecoder();count=0;normal=inverted=false;
                for(int i=0;i<frames.Length;i++)Check(adaptive.Decode("H265",Convert.FromBase64String(frames[i]),i*1_000_000L,true));
                Verify("adaptive");results.Add($"Adaptive Main{depth}: {count}/{frames.Length}, {adaptive.Format}");
            }
            return string.Join("; ",results);
        }).GetAwaiter().GetResult();
    }
}
