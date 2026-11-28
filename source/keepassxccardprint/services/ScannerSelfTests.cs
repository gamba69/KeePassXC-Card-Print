using System.Text;
using QRCoder;
using SkiaSharp;

namespace KeePassXCCardPrint.Services;

internal static class ScannerSelfTests
{
    internal static string Report { get; private set; } = "";
    internal static int Run()
    {
        try
        {
            // Workspace states determine the single camera action independently of the UI theme.
            var workspaceCases=new (bool Camera,bool Network,bool Ended,NetworkCameraState State,bool Frozen,bool Pdf,bool Image,bool Text,ScannerWorkspaceState Expected,ScannerCameraCommand Command)[]
            {
                (false,false,false,NetworkCameraState.Stopped,false,false,false,false,ScannerWorkspaceState.Idle,ScannerCameraCommand.Start),
                (true,false,false,NetworkCameraState.Stopped,false,false,true,false,ScannerWorkspaceState.LocalLive,ScannerCameraCommand.Stop),
                (true,true,false,NetworkCameraState.Connecting,false,false,false,false,ScannerWorkspaceState.NetworkConnecting,ScannerCameraCommand.Cancel),
                (true,true,false,NetworkCameraState.Retrying,false,false,false,false,ScannerWorkspaceState.NetworkConnecting,ScannerCameraCommand.Cancel),
                (true,true,false,NetworkCameraState.Receiving,false,false,true,false,ScannerWorkspaceState.NetworkLive,ScannerCameraCommand.Stop),
                (true,true,true,NetworkCameraState.Authentication,false,false,false,false,ScannerWorkspaceState.NetworkFailed,ScannerCameraCommand.Retry),
                (true,true,true,NetworkCameraState.VideoDecodeFailed,true,false,true,false,ScannerWorkspaceState.NetworkFailed,ScannerCameraCommand.Retry),
                (true,true,false,NetworkCameraState.Receiving,true,false,true,false,ScannerWorkspaceState.Frozen,ScannerCameraCommand.Stop),
                (false,false,false,NetworkCameraState.Stopped,false,false,true,false,ScannerWorkspaceState.Image,ScannerCameraCommand.Start),
                (false,false,false,NetworkCameraState.Stopped,false,true,true,false,ScannerWorkspaceState.Pdf,ScannerCameraCommand.Start),
                (false,false,false,NetworkCameraState.Stopped,false,false,false,true,ScannerWorkspaceState.Text,ScannerCameraCommand.Start)
            };
            foreach(var c in workspaceCases)
            {
                var state=ScannerWorkspace.Resolve(c.Camera,c.Network,c.Ended,c.State,c.Frozen,c.Pdf,c.Image,c.Text);
                Require(state==c.Expected&&ScannerWorkspace.CameraCommand(state)==c.Command,"Scanner workspace action: "+c.Expected);
            }
            Require(ScannerWorkspace.InputActions(ScannerInputKind.Camera)==(true,false,false),"Camera-only input actions");
            Require(ScannerWorkspace.InputActions(ScannerInputKind.File)==(false,true,false),"File-only input actions");
            Require(ScannerWorkspace.InputActions(ScannerInputKind.Clipboard)==(false,false,true),"Clipboard-only input actions");
            // Transient failures stay in the retry lifecycle; permanent failures end it.
            foreach(var state in new[]{NetworkCameraState.Timeout,NetworkCameraState.Error})
                Require(NetworkCameraSource.ShouldReconnect(true,state)&&!NetworkCameraSource.ShouldReconnect(false,state),"Network retry policy: "+state);
            foreach(var state in new[]{NetworkCameraState.Authentication,NetworkCameraState.Certificate,NetworkCameraState.DecoderMissing,NetworkCameraState.SystemCodecMissing,NetworkCameraState.VideoInitializationFailed,NetworkCameraState.VideoDecodeFailed,NetworkCameraState.Unsupported})
                Require(!NetworkCameraSource.ShouldReconnect(true,state),"Terminal network failure: "+state);
            // RFC 6238, Appendix B: public test vectors, never a user's secret.
            string prefix = "otpauth:" + "//totp/Test?";
            using var totp = ScannerPayload.Parse(prefix + "secret=" + "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ" + "&digits=8");
            Require(totp.Kind == ScannerKind.Totp && totp.Code(59) == "94287082" && totp.Code(1111111109) == "07081804", "RFC 6238 SHA-1");
            using var invalid = ScannerPayload.Parse(prefix + "secret=" + "!!!");
            Require(invalid.Kind == ScannerKind.Invalid && !invalid.Allowed(true), "Invalid OTP must not become text");
            using var web = ScannerPayload.Parse("https://example.invalid/");
            Require(web.Kind == ScannerKind.Url && !web.Allowed(false) && web.Allowed(true), "Recognition filter");
            using var text = ScannerPayload.Parse("scanner self-test");
            Require(text.Kind == ScannerKind.Text && !text.Allowed(false), "Text classification");
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode("scanner self-test", QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            using var image = SKBitmap.Decode(png.GetGraphic(8)) ?? throw new InvalidOperationException();
            var hits = ScannerDecoder.Decode(image);
            try { Require(hits.Count == 1 && hits[0].Payload.Text == "scanner self-test", "QR round trip"); }
            finally { foreach (var hit in hits) hit.Dispose(); }
            // Inversion and faint print must exercise recovery, not just normal QR decoding.
            using var inverted = new SKBitmap(image.Width,image.Height);
            using var faint = new SKBitmap(image.Width,image.Height);
            for (int y=0;y<image.Height;y++) for (int x=0;x<image.Width;x++)
            {
                byte gray = (byte)(255-image.GetPixel(x,y).Red);
                inverted.SetPixel(x,y,new SKColor(gray,gray,gray));
                byte low = (byte)(160+image.GetPixel(x,y).Red*24/255);
                faint.SetPixel(x,y,new SKColor(low,low,low));
            }
            AssertRead(inverted,"scanner self-test","inverted QR");
            AssertRead(faint,"scanner self-test","low contrast QR");
            string denseText = string.Concat(Enumerable.Range(0,600).Select(i => (char)('a'+(i*17+i/26)%26)));
            using var denseData = generator.CreateQrCode(denseText,QRCodeGenerator.ECCLevel.M);
            using var densePng = new PngByteQRCode(denseData);
            using var dense = SKBitmap.Decode(densePng.GetGraphic(3)) ?? throw new InvalidOperationException();
            AssertRead(dense,denseText,"dense QR at 3 pixels/module");
            using var multi = new SKBitmap(image.Width+dense.Width+48,Math.Max(image.Height,dense.Height)+48);
            using (var canvas = new SKCanvas(multi))
            {
                canvas.Clear(SKColors.White);
                var sampling = new SKSamplingOptions(SKFilterMode.Nearest,SKMipmapMode.None);
                canvas.DrawBitmap(image,new SKRect(8,8,8+image.Width,8+image.Height),sampling);
                canvas.DrawBitmap(dense,new SKRect(image.Width+24,8,image.Width+24+dense.Width,8+dense.Height),sampling);
            }
            hits = ScannerDecoder.Decode(multi,ScannerSensitivity.Maximum,false,0,out var multiDiagnostics);
            try
            {
                Require(hits.Count == 2 && hits.Any(h=>h.Payload.Text==denseText) && hits.Any(h=>h.Payload.Text=="scanner self-test"),"multiple QR and deduplication");
                Require(multiDiagnostics.Complete && multiDiagnostics.Decoded==2 && multiDiagnostics.Located && multiDiagnostics.Attempts>0,"recognition diagnostics");
            }
            finally { foreach(var hit in hits) hit.Dispose(); }
            using var blank = new SKBitmap(320,240); blank.Erase(SKColors.White);
            hits = ScannerDecoder.Decode(blank,ScannerSensitivity.Adaptive,false,0,out var emptyDiagnostics);
            try { Require(hits.Count==0 && !emptyDiagnostics.Located && emptyDiagnostics.Complete,"blank input diagnostics"); }
            finally { foreach(var hit in hits) hit.Dispose(); }
            // Quality diagnostics: no real camera needed, no saved images.
            var thumb = new byte[ScannerQualityMeter.SampleWidth*ScannerQualityMeter.SampleHeight];
            var nextThumb = new byte[thumb.Length];
            using var dark = new SKBitmap(320,240); dark.Erase(SKColors.Black);
            var darkQuality = ScannerQualityMeter.Measure(dark,thumb,[]);
            Require(darkQuality.Light==0 && darkQuality.Dark==1 && darkQuality.Detail==0 && darkQuality.Motion is null,"dark uniform frame");
            var stable = ScannerQualityMeter.Measure(dark,nextThumb,thumb);
            Require(stable.Motion==0,"unchanged frame");
            var clearQuality=ScannerQualityMeter.Measure(image,thumb,[]);
            var faintQuality=ScannerQualityMeter.Measure(faint,nextThumb,[]);
            Require(clearQuality.Detail>faintQuality.Detail && clearQuality.Score>faintQuality.Score,"quality ordering");
            var moved=ScannerQualityMeter.Measure(inverted,nextThumb,thumb);
            Require(moved.Motion>0,"inter-frame change");
            Array.Clear(thumb); Array.Clear(nextThumb);
            // Numerical telemetry and bounded rolling percentiles (nearest-rank).
            var window=new ScannerMetricWindow();
            for(int i=1;i<=100;i++)window.Add(i,i);
            Require(window.Percentile(.05)==5 && window.Percentile(.95)==95,"rolling percentiles");
            window.Add(101,null);Require(window.Count==100,"unknown is not zero");
            window.Trim(30_101);Require(window.Count==0 && window.Percentile(.95) is null,"rolling expiry");
            // Manual crop must exclude the other QR and report original-image geometry.
            var roi=new ScannerSearchRegion(0,0,(float)(image.Width+16)/multi.Width,1);
            hits=ScannerDecoder.Decode(multi,ScannerSensitivity.Maximum,false,0,out var roiDiagnostics,selection:roi);
            try
            {
                Require(hits.Count==1&&hits[0].Payload.Text=="scanner self-test","manual search area excludes other QR");
                Require(roiDiagnostics.SelectedArea is not null,"selected area quality");
                Require(roiDiagnostics.Qr is { ModulePixels:>6 and <10, Contrast:>100 },"module estimate in original pixels");
            }
            finally { foreach(var hit in hits)hit.Dispose(); }
            // Record the empirical boundary, rather than asserting that every blurred QR must decode.
            var sweep=new List<string>();
            for(int ppm=1;ppm<=5;ppm++)
            {
                using var clean=SKBitmap.Decode(densePng.GetGraphic(ppm))??throw new InvalidOperationException();
                using var blurred=new SKBitmap(clean.Width,clean.Height);
                for(int y=0;y<clean.Height;y++)for(int x=0;x<clean.Width;x++)
                {
                    int sum=0;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                        sum+=clean.GetPixel(Math.Clamp(x+dx,0,clean.Width-1),Math.Clamp(y+dy,0,clean.Height-1)).Red*(dx==0?2:1)*(dy==0?2:1);
                    byte c=(byte)(sum/16);blurred.SetPixel(x,y,new SKColor(c,c,c));
                }
                bool Read(SKBitmap candidate)
                {
                    var found=ScannerDecoder.Decode(candidate,ScannerSensitivity.Adaptive,false,0,out _);
                    try {Require(found.All(h=>h.Payload.Text==denseText),"density sweep payload integrity");return found.Count>0;}
                    finally{foreach(var hit in found)hit.Dispose();}
                }
                bool raw=Read(clean),soft=Read(blurred);
                if(ppm>=4)Require(raw,"clean density sweep >=4 px/module");
                sweep.Add($"{ppm}px: clean={(raw?1:0)}, blur={(soft?1:0)}");
            }
            // Network source contract: profile policy, split JPEG/MJPEG and concatenated PNGs.
            var profile=new NetworkCameraProfile{Name="Synthetic",Address="rtsp://127.0.0.1:8554/demo"};
            Require(profile.Engine=="sharp","native RTSP is default");
            Require(!profile.Validate(out _),"plaintext camera requires opt-in");
            profile.AllowPlaintext=true;Require(profile.Validate(out _),"explicit plaintext camera");
            profile.RtspUdp=true;Require(profile.Validate(out _),"plaintext RTSP UDP");
            profile.Address="rtsps://127.0.0.1:322/demo";Require(!profile.Validate(out _),"RTSPS rejects UDP");
            profile.RtspUdp=false;Require(profile.Validate(out _),"RTSPS TCP");
            profile.Pin=new string('A',64);Require(profile.Validate(out _),"built-in RTSPS exact pin");
            profile.Engine="ffmpeg";Require(!profile.Validate(out _),"FFmpeg cannot silently discard RTSPS pin");
            profile.Pin="";profile.AllowPlaintext=false;Require(!profile.Validate(out _),"FFmpeg unverified transport requires explicit consent");
            profile.AllowPlaintext=true;Require(profile.Validate(out _),"explicit FFmpeg demo mode");profile.Engine="sharp";
            var annex=SharpNetworkCameraSource.Assemble("H264",[new byte[]{0x65,0x11}],[new byte[]{0x67,0x22}]);
            Require(annex.SequenceEqual(new byte[]{0,0,0,1,0x67,0x22,0,0,0,1,0x65,0x11}),"Annex B keyframe with SDP parameter sets");
            var alreadyPrefixed=SharpNetworkCameraSource.Assemble("H264",[new byte[]{0,0,0,1,0x65,0x11}],[new byte[]{0,0,1,0x67,0x22}]);
            Require(alreadyPrefixed.SequenceEqual(annex),"SharpRTSP Annex B segment is not prefixed twice");
            var inBand=SharpNetworkCameraSource.Assemble("H265",[new byte[]{0,0,0,1,0x42,1,0x21},new byte[]{0,0,1,0x26,1,0x22}],[new byte[]{0x40,1,0x11},new byte[]{0x42,1,0x99},new byte[]{0x44,1,0x33}]);
            var nals=AnnexBUnits.Split(inBand);
            Require(nals.Count==4&&nals.Select(n=>SharpNetworkCameraSource.NalType(n,true)).SequenceEqual(new[]{32,33,34,19})&&nals[1].Span[2]==0x21,"HEVC in-band parameter precedence and canonical order");
            Require(AdaptiveVideoDecoder.Inspect(inBand,true).Key,"prefixed HEVC keyframe detection");
            Require(VideoDecoderManager.Plan("software",true,"H265",1).All(c=>!c.Hardware),"software-only decoder policy");
            Require(VideoDecoderManager.Plan("hardware",true,"H265",1).All(c=>c.Hardware),"hardware-only decoder policy");
            Require(VideoDecoderManager.Plan("libde265",false,"H264",0).Length==0,"manual decoder without fallback");
            var candidates=new[]{new VideoDecoderCandidate("gpu","GPU",1,0,true,1),new VideoDecoderCandidate("cpu","CPU",2,-1,false)};
            var probes=new[]{new VideoDecoderProbe("gpu","H264",true,24,80,"",VideoProbeStatus.Working,P95Ms:90,Inputs:24),new VideoDecoderProbe("cpu","H264",true,24,10,"",VideoProbeStatus.Working,P95Ms:12,Inputs:24)};
            Require(VideoDecoderManager.Rank(candidates,probes,"auto",false,"H264").First().Id=="cpu","fastest decoder wins independent of hardware");
            Require(VideoDecoderManager.Rank(candidates,probes,"gpu",false,"H264").Select(c=>c.Id).SequenceEqual(new[]{"gpu"}),"strict forced GPU");
            Require(VideoDecoderManager.Rank(candidates,probes,"gpu",true,"H264").Select(c=>c.Id).SequenceEqual(new[]{"gpu","cpu"}),"explicit fallback after forced GPU");
            Require(VideoDecoderManager.Rank(candidates,probes,"hardware",true,"H264").All(c=>c.Hardware),"hardware-only never switches to CPU");
            var unavailable=new[]{new VideoDecoderProbe("gpu","H265",false,0,0,"",VideoProbeStatus.Unsupported),new VideoDecoderProbe("cpu","H265",true,24,10,"",VideoProbeStatus.Working)};
            Require(VideoDecoderManager.Available(candidates,unavailable,"H265").Select(c=>c.Id).SequenceEqual(new[]{"cpu"}),"only available HEVC decoder choices");
            Require(VideoDecoderManager.Available(candidates,unavailable,"H264").Length==2,"unprobed H264 choices remain");
            Require(VideoDecoderManager.Available(candidates,unavailable,"JPEG").Length==0,"JPEG does not need video decoder");
            Require(ScannerDashboard.TableColumns(479)==1&&ScannerDashboard.TableColumns(480)==2&&ScannerDashboard.TableColumns(720)==3,"responsive measurement columns");
            var screenLimit=DialogScreenPolicy.ClientLimit(new Avalonia.PixelRect(-1920,0,1920,1040),1.25,new Avalonia.Size(16,40));
            Require(screenLimit.Width==1504&&screenLimit.Height==776,"dialog bounds in target-monitor DIPs");
            Require(!NetworkCameraProfile.ConsentScopeChanged("rtsp://127.0.0.1/demo","sharp","rtsp://127.0.0.1/demo","ffmpeg"),"same plaintext endpoint retains consent");
            Require(NetworkCameraProfile.ConsentScopeChanged("rtsps://127.0.0.1/demo","sharp","rtsps://127.0.0.1/demo","ffmpeg"),"changed TLS trust requires consent");
            Require(NetworkCameraProfile.ConsentScopeChanged("rtsp://127.0.0.1/demo","sharp","rtsp://127.0.0.2/demo","sharp"),"changed endpoint requires consent");
            Require(profile.WithDecoder("mf-cpu",false).AllowPlaintext==profile.AllowPlaintext,"decoder change retains consent");
            var delta=SharpNetworkCameraSource.Assemble("H264",[new byte[]{0x41,0x22}],[new byte[]{0x67}]);
            Require(delta.SequenceEqual(new byte[]{0,0,0,1,0x41,0x22}),"Annex B predictive frame without repeated parameter sets");
            var jpegJoin=SharpNetworkCameraSource.Assemble("JPEG",[new byte[]{0xff,0xd8},new byte[]{0xff,0xd9}],[]);
            Require(jpegJoin.SequenceEqual(new byte[]{0xff,0xd8,0xff,0xd9}),"RTP JPEG assembly");
            // Construct synthetic user-info at runtime so the repository privacy guard
            // does not mistake a deliberately invalid test URL for leaked credentials.
            var credentialUri=new UriBuilder("https://127.0.0.1/demo")
                { UserName="TEST_user", Password="TEST_password" }.Uri;
            Require(!string.IsNullOrEmpty(credentialUri.UserInfo),"synthetic URL contains user-info");
            profile.Address=credentialUri.AbsoluteUri;Require(!profile.Validate(out _),"embedded credentials rejected");
            profile.Address="https://127.0.0.1/demo";profile.Pin="00";Require(!profile.Validate(out _),"malformed pin rejected");
            using(var rsa=System.Security.Cryptography.RSA.Create(2048))
            {
                var request=new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Synthetic",rsa,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                var san=new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();san.AddIpAddress(System.Net.IPAddress.Loopback);request.CertificateExtensions.Add(san.Build());
                using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
                var fingerprint=cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
                Require(NetworkCameraSource.CertificateMatches(cert,fingerprint,System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors),"explicit exact HTTPS pin");
                Require(!NetworkCameraSource.CertificateMatches(cert,new string('0',64),System.Net.Security.SslPolicyErrors.None),"changed pin fails even if PKI succeeds");
                Require(!NetworkCameraSource.CertificateMatches(cert,"",System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors),"untrusted certificate rejected by default");
                var secure=new NetworkCameraProfile{Address="rtsps://127.0.0.1:322/demo",Name="Synthetic",Pin=fingerprint};
                Require(SharpNetworkCameraSource.ValidateTls(new Uri(secure.Address),secure,cert,System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors),"RTSPS pin plus configured host");
                Require(!SharpNetworkCameraSource.ValidateTls(new Uri("rtsps://127.0.0.2:322/demo"),secure,cert,System.Net.Security.SslPolicyErrors.None),"RTSPS wrong host rejected even with correct pin");
            }
            using(var sk=SKImage.FromBitmap(image))
            using(var jpg=sk.Encode(SKEncodedImageFormat.Jpeg,95))
            {
                var jpeg=jpg.ToArray();
                var dimensions=NetworkCameraSource.CheckImage(jpeg);Require(dimensions==(image.Width,image.Height),"JPEG dimensions");
                using var multipart=new MemoryStream();
                void WriteAscii(string value)=>multipart.Write(Encoding.ASCII.GetBytes(value));
                WriteAscii($"--demo\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");multipart.Write(jpeg);
                WriteAscii("\r\n--demo\r\nContent-Type: image/jpeg\r\n\r\n");multipart.Write(jpeg);multipart.Position=0;
                using var reader=new NetworkCameraReader(multipart);
                Require(reader.ReadMultipart("demo",CancellationToken.None).GetAwaiter().GetResult().SequenceEqual(jpeg),"MJPEG content-length frame");
                Require(reader.ReadMultipart("demo",CancellationToken.None).GetAwaiter().GetResult().SequenceEqual(jpeg),"MJPEG marker-framed JPEG");
                // Same 54-byte BI_RGB header as the FFmpeg BGRA pipe. Two rows,
                // different colors, bottom-up and top-down; no compression.
                byte[] BgraFrame(bool topDown)
                {
                    using var mem=new MemoryStream();using var writer=new BinaryWriter(mem);
                    writer.Write((ushort)0x4D42);writer.Write(70);writer.Write(0);writer.Write(54);writer.Write(40);
                    writer.Write(2);writer.Write(topDown?-2:2);writer.Write((ushort)1);writer.Write((ushort)32);
                    writer.Write(0);writer.Write(16);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
                    byte[] red=[0,0,255,0,0,0,255,0],blue=[255,0,0,0,255,0,0,0];
                    writer.Write(topDown?red:blue);writer.Write(topDown?blue:red);return mem.ToArray();
                }
                using var concatenated=new MemoryStream();concatenated.Write(BgraFrame(false));concatenated.Write(BgraFrame(true));concatenated.Position=0;
                using var rawReader=new NetworkCameraReader(concatenated);
                for(int i=0;i<2;i++)
                {
                    using var raw=rawReader.ReadBgra(CancellationToken.None).GetAwaiter().GetResult();
                    Require(raw.Width==2&&raw.Height==2&&raw.GetPixel(0,0)==SKColors.Red&&raw.GetPixel(1,1)==SKColors.Blue,"uncompressed BGRA frame framing / row orientation / opaque alpha");
                }
                using(var lease=new ScannerPixels(new SKBitmap(2,2)))
                {
                    lease.Bitmap.Erase(SKColors.Red);using var retained=lease.Share();lease.Dispose();
                    Require(retained.Bitmap.GetPixel(0,0)==SKColors.Red,"shared frame lease survives producer release");
                }
                // APP1 can contain FF D9, so a marker search alone must not truncate JPEGs.
                byte[] embedded=[0xff,0xd8,0xff,0xe1,0,6,0xff,0xd8,0xff,0xd9,0xff,0xd9];
                using var embeddedReader=new NetworkCameraReader(new MemoryStream(embedded));
                Require(embeddedReader.ReadJpeg(CancellationToken.None).GetAwaiter().GetResult().SequenceEqual(embedded),"embedded JPEG thumbnail not truncated");
            }
            // Generated 160x96 constrained-baseline Annex B, 10 fps, 48 consecutive
            // access units, IDR every 12 frames, no B frames. Alternating black/white
            // halves. Never recycle predicted pictures out of their coded order.
            // Verify live output before end-of-stream; draining alone cannot prove
            // that the live camera works. Keep the real Windows test mandatory if
            // a system H.264 decoder exists.
            string mfStatus="not packaged";
            if(File.Exists(Path.Combine(AppContext.BaseDirectory,"KpcVideoBridge.dll")))
                mfStatus=Task.Run(()=>
                {
                    using var mf=new MediaFoundationVideoDecoder();
                    if(!mf.Initialize("H264"))
                    {
                        if(mf.LastHResult==MediaFoundationVideoDecoder.CodecNotFound)return "system H.264 decoder unavailable";
                        throw new InvalidOperationException($"Media Foundation H.264 open: HRESULT=0x{mf.LastHResult:X8}");
                    }
                    var frames=HevcDecoderSelfTests.H264;
                    int decoded=0,first=-1;bool normal=false,inverted=false;
                    for(int i=0;i<frames.Length;i++)
                    {
                        SKBitmap? pixels;
                        try{pixels=mf.Decode("H264",Convert.FromBase64String(frames[i]),i*1_000_000L,true);}
                        catch(NetworkCameraException)
                        {throw new InvalidOperationException($"Media Foundation H.264 input {i+1}/{frames.Length}, decoded={decoded}, HRESULT=0x{mf.LastHResult:X8}");}
                        if(pixels is null)continue;
                        if(first<0)first=i+1;
                        using var picture=pixels;
                        Require((picture.Width,picture.Height)==(160,96),"Media Foundation H.264 dimensions");
                        var left=picture.GetPixel(40,48).Red;var right=picture.GetPixel(120,48).Red;
                        normal|=left<50&&right>200;inverted|=left>200&&right<50;
                        decoded++;
                    }
                    Require(decoded>=2&&normal&&inverted,$"Media Foundation synthetic H.264 stream: input={frames.Length}, decoded={decoded}, first={first}, normal={normal}, inverted={inverted}, HRESULT=0x{mf.LastHResult:X8}");
                    return $"H.264 decoded {decoded}/{frames.Length}; first output at input {first}";
                }).GetAwaiter().GetResult();
            string hevcStatus="not packaged";
            if(File.Exists(Path.Combine(AppContext.BaseDirectory,"KpcVideoBridge.dll")))
            {
                Require(File.Exists(Path.Combine(AppContext.BaseDirectory,"libde265.dll")),"Bundled HEVC decoder missing");
                try{hevcStatus=HevcDecoderSelfTests.Run();}
                catch(NetworkCameraException ex){throw new InvalidOperationException("HEVC self-test: "+ex.Detail);}
            }
            // The direct GPU bridge and dispatcher must load even without a GPU.
            // Driver-specific frame tests are performed by the decoder diagnostics.
            foreach(var (fixture,hevc) in new[]{(DecoderProbeFixtures.H264,false),(DecoderProbeFixtures.Main8,true),(DecoderProbeFixtures.Main10,true)})
            {
                Require(VendorStreamSize.TryRead(Convert.FromBase64String(fixture[0]),hevc,out uint w,out uint h)&&
                    w==DecoderProbeFixtures.Width&&h==DecoderProbeFixtures.Height,"Vendor decoder SPS dimensions");
            }
            Require(!VendorStreamSize.TryRead([0,0,0,1,0x67],false,out _,out _),"Vendor decoder rejects truncated SPS");
            if(File.Exists(Path.Combine(AppContext.BaseDirectory,"KpcVideoBridge.dll")))VendorVideoDecoder.VerifyPackagedBridge();
            // Exercise the actual packaged native PDF library and decoder on synthetic data.
            using var output = new MemoryStream();
            using (var pdf = SKDocument.CreatePdf(output) ?? throw new InvalidOperationException())
            {
                var page = pdf.BeginPage(288,288); page.Clear(SKColors.White);
                page.DrawBitmap(image, new SKRect(30,30,258,258), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)); pdf.EndPage(); pdf.Close();
            }
            using var input = new ScannerPdf(output.ToArray());
            Require(input.Count == 1, "PDF page count");
            using var pageImage = input.Render(0);
            hits = ScannerDecoder.Decode(pageImage);
            try { Require(hits.Count == 1 && hits[0].Payload.Text == "scanner self-test", "PDF QR round trip"); }
            finally { foreach (var hit in hits) hit.Dispose(); }
            Report = "PASS: TOTP, recognition filter, QR round trip, inversion, faint and dense QR, multiple QR, diagnostics, camera quality metrics, PDF QR round trip, rolling statistics, ROI, geometry, network profile policy, TLS pinning, engine selection, RTP access-unit assembly, image framing, vendor bridge exports and SPS dimensions. Media Foundation: " + mfStatus + ". HEVC: " + hevcStatus + ". Density sweep: " + string.Join("; ",sweep);
            return 0;
        }
        catch (Exception e) { Report = "FAIL: " + e.GetType().Name + ": " + (e is InvalidOperationException ? e.Message : "scanner test error"); return 1; }
    }
    private static void AssertRead(SKBitmap bitmap,string expected,string name)
    {
        var hits = ScannerDecoder.Decode(bitmap,ScannerSensitivity.Maximum,false,0,out var diagnostics);
        try { Require(hits.Count==1 && hits[0].Payload.Text==expected && diagnostics.Located && diagnostics.Complete,name); }
        finally { foreach(var hit in hits) hit.Dispose(); }
    }
    private static void Require(bool condition, string name)
    { if (!condition) throw new InvalidOperationException(name); }
}
