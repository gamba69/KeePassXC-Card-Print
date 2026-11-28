using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace KeePassXCCardPrint.Services;

// Explicit SOAP messages: no generated WCF proxies, reflection or dynamic serializers.
internal sealed class OnvifClient : IDisposable
{
    internal static readonly XNamespace Device="http://www.onvif.org/ver10/device/wsdl", Media="http://www.onvif.org/ver10/media/wsdl", Media2="http://www.onvif.org/ver20/media/wsdl", Imaging="http://www.onvif.org/ver20/imaging/wsdl", Ptz="http://www.onvif.org/ver20/ptz/wsdl", Schema="http://www.onvif.org/ver10/schema";
    private static readonly XNamespace Soap="http://www.w3.org/2003/05/soap-envelope", Wsse="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd", Wsu="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private const string PasswordDigest="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest";
    private const string Base64Binary="http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
    private readonly string _user,_password;
    private readonly bool _plain;
    private readonly int _timeout;
    private TimeSpan _clockOffset;
    private readonly Dictionary<XNamespace,Uri> _services=new();
    private bool _wsSecurity=true;
    internal Uri Endpoint {get;}
    internal string DeviceName {get;private set;}="";
    internal string Identity {get;private set;}="";
    internal string Firmware {get;private set;}="";
    internal IReadOnlyList<OnvifStream> Streams {get;private set;}=[];
    internal bool Has(XNamespace service)=>_services.ContainsKey(service);
    internal OnvifClient(string endpoint,string user,string password,bool allowHttp,int timeout=10)
    {
        Endpoint=ParseEndpoint(endpoint);_plain=allowHttp;_user=user;_password=password;_timeout=Math.Clamp(timeout,3,60);
        if(user.Length>128||password.Length>512||user.Any(char.IsControl)||password.Any(char.IsControl)||user.Contains(':')||user.Length==0&&password.Length>0)throw new FormatException();
        CheckService(Endpoint);_services[Device]=Endpoint;
    }
    internal static Uri ParseEndpoint(string value)
    {
        value=value.Trim();if(!value.Contains("://",StringComparison.Ordinal))value="http://"+value;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.Host.Length==0||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||uri.Port==0||uri.AbsoluteUri.Length>2048)throw new FormatException();
        return uri.AbsolutePath=="/"?new UriBuilder(uri){Path="/onvif/device_service"}.Uri:uri;
    }
    private void CheckService(Uri uri)
    {
        if(uri.Scheme is not ("http" or "https")||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||!string.Equals(uri.IdnHost,Endpoint.IdnHost,StringComparison.OrdinalIgnoreCase)||uri.Scheme=="http"&&!_plain)throw new InvalidOperationException("ONVIF endpoint rejected");
    }
    internal static XElement? Child(XContainer? node,string name)=>node?.Elements().FirstOrDefault(e=>e.Name.LocalName==name);
    internal static XElement? Desc(XContainer? node,string name)=>node?.Descendants().FirstOrDefault(e=>e.Name.LocalName==name);
    internal static string Val(XContainer? node,string name)=>Child(node,name)?.Value??"";
    internal static string Token(XElement? node)=>node?.Attribute("token")?.Value??"";
    internal static string Number(double value)=>value.ToString("G",CultureInfo.InvariantCulture);
    internal static double? Numeric(string? text)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:null;
    internal static XDocument ParseXml(Stream data)
    {
        using var reader=XmlReader.Create(data,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2_000_000});return XDocument.Load(reader);
    }
    internal static XElement SecurityHeader(string user,string password,byte[] nonce,string created)
    {
        byte[] text=Encoding.UTF8.GetBytes(created+password);byte[] input=new byte[nonce.Length+text.Length];nonce.CopyTo(input,0);text.CopyTo(input,nonce.Length);
        try
        {
            // ONVIF UsernameToken mandates SHA-1 here (not a password-storage hash).
            return new XElement(Wsse+"Security",new XElement(Wsse+"UsernameToken",new XElement(Wsse+"Username",user),new XElement(Wsse+"Password",new XAttribute("Type",PasswordDigest),Convert.ToBase64String(SHA1.HashData(input))),new XElement(Wsse+"Nonce",new XAttribute("EncodingType",Base64Binary),Convert.ToBase64String(nonce)),new XElement(Wsu+"Created",created)));
        }
        finally{CryptographicOperations.ZeroMemory(text);CryptographicOperations.ZeroMemory(input);}
    }
    private async Task<XElement> Send(Uri uri,XNamespace ns,string operation,CancellationToken ct,bool authenticate,bool wsSecurity,params object[] args)
    {
        CheckService(uri);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(_timeout));var token=deadline.Token;
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false,UseCookies=false};
        if(authenticate&&_user.Length>0)
        {
            var cache=new CredentialCache();cache.Add(new Uri(uri.GetLeftPart(UriPartial.Authority)+"/"),"Digest",new NetworkCredential(_user,_password));handler.Credentials=cache;
        }
        using var client=new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        var envelope=new XElement(Soap+"Envelope",new XAttribute(XNamespace.Xmlns+"s",Soap.NamespaceName),new XAttribute(XNamespace.Xmlns+"tt",Schema.NamespaceName),new XAttribute(XNamespace.Xmlns+"m",ns.NamespaceName),new XElement(Soap+"Header",authenticate&&wsSecurity&&_user.Length>0?SecurityHeader(_user,_password,RandomNumberGenerator.GetBytes(20),DateTimeOffset.UtcNow.Add(_clockOffset).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture)):null),new XElement(Soap+"Body",new XElement(ns+operation,args)));
        using var request=new HttpRequestMessage(HttpMethod.Post,uri);request.Content=new StringContent(envelope.ToString(SaveOptions.DisableFormatting),Encoding.UTF8,"application/soap+xml");request.Content.Headers.ContentType!.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("action","\""+ns.NamespaceName+"/"+operation+"\""));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);
        if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new OnvifException("Authentication");
        if(response.Content.Headers.ContentLength>2_000_000)throw new OnvifException("Response limit");
        await using var stream=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var buffer=new MemoryStream();byte[] chunk=new byte[8192];int read;
        while((read=await stream.ReadAsync(chunk.AsMemory(),token).ConfigureAwait(false))>0){if(buffer.Length+read>2_000_000)throw new OnvifException("Response limit");buffer.Write(chunk,0,read);}
        buffer.Position=0;var doc=ParseXml(buffer);var body=doc.Root?.Element(Soap+"Body");var fault=body?.Element(Soap+"Fault");
        if(fault is not null)
        {
            // Never display arbitrary camera fault strings: they can contain credentials or URLs.
            var code=Desc(fault,"Subcode")?.Descendants().Where(e=>e.Name.LocalName=="Value").LastOrDefault()?.Value??"";
            throw new OnvifException(code.Contains("NotAuthorized",StringComparison.OrdinalIgnoreCase)||code.Contains("Security",StringComparison.OrdinalIgnoreCase)?"Authentication":"SOAP fault");
        }
        if(!response.IsSuccessStatusCode)throw new OnvifException("HTTP "+(int)response.StatusCode);
        return body?.Element(ns+(operation+"Response"))??throw new OnvifException("Unexpected response");
    }
    internal Task<XElement> Call(XNamespace ns,string operation,CancellationToken ct,params object[] args)=>Send(_services[ns],ns,operation,ct,true,_wsSecurity,args);
    private async Task<XElement?> Optional(XNamespace ns,string op,CancellationToken ct,params object[] args)
    {try{return await Call(ns,op,ct,args).ConfigureAwait(false);}catch(Exception) when(!ct.IsCancellationRequested){return null;}}
    internal async Task Connect(CancellationToken ct)
    {
        try
        {
            var clock=await Send(Endpoint,Device,"GetSystemDateAndTime",ct,false,false).ConfigureAwait(false);var utc=Desc(clock,"UTCDateTime");var date=Child(utc,"Date");var time=Child(utc,"Time");
            int I(XContainer? x,string n)=>int.Parse(Val(x,n),CultureInfo.InvariantCulture);
            var remote=new DateTimeOffset(I(date,"Year"),I(date,"Month"),I(date,"Day"),I(time,"Hour"),I(time,"Minute"),I(time,"Second"),TimeSpan.Zero);_clockOffset=remote-DateTimeOffset.UtcNow;
        }catch(Exception) when(!ct.IsCancellationRequested){}
        XElement info;
        try{info=await Call(Device,"GetDeviceInformation",ct).ConfigureAwait(false);}
        catch(OnvifException ex) when(ex.Message=="Authentication"&&_user.Length>0){_wsSecurity=false;info=await Call(Device,"GetDeviceInformation",ct).ConfigureAwait(false);}
        DeviceName=(Val(info,"Manufacturer")+" "+Val(info,"Model")).Trim();Identity=Val(info,"SerialNumber");Firmware=Val(info,"FirmwareVersion");
        void Register(XNamespace ns,string raw)
        {
            if(ns!=Media&&ns!=Media2&&ns!=Imaging&&ns!=Ptz)return;
            if(Uri.TryCreate(raw,UriKind.Absolute,out var uri)){try{CheckService(uri);_services[ns]=uri;}catch(InvalidOperationException){}}
        }
        var services=await Optional(Device,"GetServices",ct,new XElement(Device+"IncludeCapability",false)).ConfigureAwait(false);
        if(services is not null)foreach(var service in services.Elements().Where(e=>e.Name.LocalName=="Service"))Register(Val(service,"Namespace"),Val(service,"XAddr"));
        if(!Has(Media)||!Has(Imaging)||!Has(Ptz))
        {
            var caps=await Optional(Device,"GetCapabilities",ct,new XElement(Device+"Category","All")).ConfigureAwait(false);
            foreach(var (name,ns) in new[]{("Media",Media),("Imaging",Imaging),("PTZ",Ptz)})if(!Has(ns))Register(ns,Val(Desc(caps,name),"XAddr"));
        }
        var streams=new List<OnvifStream>();
        foreach(var ns in new[]{Media2,Media})
        {
            if(!Has(ns))continue;
            var profiles=ns==Media2?await Optional(ns,"GetProfiles",ct,new XElement(ns+"Type","All")).ConfigureAwait(false):await Optional(ns,"GetProfiles",ct).ConfigureAwait(false);
            if(profiles is null)continue;
            foreach(var p in profiles.Elements().Where(e=>e.Name.LocalName=="Profiles"))
            {
                string token=Token(p);if(token.Length==0)continue;
                var configs=ns==Media2?Child(p,"Configurations"):p;
                var source=Child(configs,ns==Media2?"VideoSource":"VideoSourceConfiguration");var encoder=Child(configs,ns==Media2?"VideoEncoder":"VideoEncoderConfiguration");var ptz=Child(configs,ns==Media2?"PTZ":"PTZConfiguration");
                var res=Child(encoder,"Resolution");string label=string.Join(" · ",new[]{Val(p,"Name"),Val(encoder,"Encoding"),res is null?"":Val(res,"Width")+"×"+Val(res,"Height"),ns==Media2?"Media2":"Media"}.Where(x=>x.Length>0));
                streams.Add(new(ns,token,label.Length>0?label:token,Val(source,"SourceToken"),Token(encoder),Token(ptz)));
            }
            // Media2 is preferred; use Media1 if the device did not return profiles.
            if(streams.Count>0)break;
        }
        Streams=streams;if(streams.Count==0)throw new OnvifException("No media profiles");
    }
    internal async Task<string> StreamUri(OnvifStream stream,CancellationToken ct)
    {
        var ns=stream.Service;var r=ns==Media2?await Call(ns,"GetStreamUri",ct,new XElement(ns+"Protocol","RTSP"),new XElement(ns+"ProfileToken",stream.Token)).ConfigureAwait(false):await Call(ns,"GetStreamUri",ct,new XElement(ns+"StreamSetup",new XElement(Schema+"Stream","RTP-Unicast"),new XElement(Schema+"Transport",new XElement(Schema+"Protocol","RTSP"))),new XElement(ns+"ProfileToken",stream.Token)).ConfigureAwait(false);
        string raw=Desc(r,"Uri")?.Value??"";
        if(!Uri.TryCreate(raw,UriKind.Absolute,out var u)||u.Scheme is not ("rtsp" or "rtsps")||u.UserInfo.Length!=0||u.Fragment.Length!=0||!string.Equals(u.IdnHost,Endpoint.IdnHost,StringComparison.OrdinalIgnoreCase))throw new OnvifException("Stream endpoint rejected");
        return u.AbsoluteUri;
    }
    internal void Dispose(){} // Each request owns its bounded HTTP connection.
    void IDisposable.Dispose()=>Dispose();
}
internal sealed record OnvifStream(XNamespace Service,string Token,string Label,string SourceToken,string EncoderToken,string PtzToken) { public override string ToString()=>Label; }
internal sealed class OnvifException(string message):IOException(message) {}

internal static class OnvifDiscovery
{
    private static readonly XNamespace S="http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace A05="http://schemas.xmlsoap.org/ws/2004/08/addressing",D05="http://schemas.xmlsoap.org/ws/2005/04/discovery";
    private static readonly XNamespace A09="http://www.w3.org/2005/08/addressing",D09="http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01";
    private static readonly XNamespace N="http://www.onvif.org/ver10/network/wsdl";
    internal sealed record Camera(string Endpoint,string Identity){public override string ToString()=>Endpoint;}
    internal static async Task<Camera[]> Find(CancellationToken ct)
    {
        var addresses=await Task.Run(()=>NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.SupportsMulticast&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(x=>x.Address).Where(x=>x.AddressFamily==AddressFamily.InterNetwork).Distinct().ToArray(),ct).ConfigureAwait(false);
        var all=await Task.WhenAll(addresses.Select(a=>Probe(a,ct))).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
        return all.SelectMany(x=>x).GroupBy(x=>x.Endpoint,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).OrderBy(x=>x.Endpoint,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    // ONVIF uses the original 2005 discovery dialect. Also query the OASIS
    // 1.1 dialect and untyped targets for software cameras with partial discovery support.
    internal static XDocument BuildProbe(string id,bool modern,bool typed)
    {
        var a=modern?A09:A05;var d=modern?D09:D05;
        string to=modern?"urn:docs-oasis-open-org:ws-dd:ns:discovery:2009:01":"urn:schemas-xmlsoap-org:ws:2005:04:discovery";
        string anonymous=a.NamespaceName+(modern?"/anonymous":"/role/anonymous");
        return new XDocument(new XElement(S+"Envelope",new XAttribute(XNamespace.Xmlns+"s",S.NamespaceName),new XAttribute(XNamespace.Xmlns+"a",a.NamespaceName),new XAttribute(XNamespace.Xmlns+"d",d.NamespaceName),new XAttribute(XNamespace.Xmlns+"dn",N.NamespaceName),
            new XElement(S+"Header",new XElement(a+"MessageID",id),new XElement(a+"To",new XAttribute(S+"mustUnderstand","1"),to),new XElement(a+"Action",new XAttribute(S+"mustUnderstand","1"),d.NamespaceName+"/Probe"),new XElement(a+"ReplyTo",new XElement(a+"Address",anonymous))),
            new XElement(S+"Body",new XElement(d+"Probe",typed?new XElement(d+"Types","dn:NetworkVideoTransmitter"):null))));
    }
    internal static IEnumerable<Camera> ParseMatches(XDocument doc,IReadOnlySet<string> ids,IReadOnlySet<string>? typedIds=null)
    {
        var header=doc.Root?.Element(S+"Header");var body=doc.Root?.Element(S+"Body");
        if(header is null||body is null)yield break;
        string? related=(header.Element(A05+"RelatesTo")??header.Element(A09+"RelatesTo"))?.Value.Trim();
        if(related is null||!ids.Contains(related))yield break;
        foreach(var match in body.Descendants().Where(e=>e.Name==D05+"ProbeMatch"||e.Name==D09+"ProbeMatch"))
        {
            string identity=OnvifClient.Desc(match,"Address")?.Value??"";
            string types=OnvifClient.Val(match,"Types"),scopes=OnvifClient.Val(match,"Scopes");
            bool camera=typedIds?.Contains(related)==true||types.Contains("NetworkVideoTransmitter",StringComparison.OrdinalIgnoreCase)||scopes.Contains("onvif://",StringComparison.OrdinalIgnoreCase);
            foreach(var raw in OnvifClient.Val(match,"XAddrs").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))
            {
                if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||uri.AbsoluteUri.Length>2048||uri.Port==0)continue;
                // Do not list generic WSD printers etc. from the untyped probe.
                if(!camera&&!uri.AbsolutePath.Contains("onvif",StringComparison.OrdinalIgnoreCase))continue;
                yield return new(uri.AbsoluteUri,identity);
            }
        }
    }
    private static async Task<List<Camera>> Probe(IPAddress address,CancellationToken ct)
    {
        var result=new Dictionary<string,Camera>(StringComparer.OrdinalIgnoreCase);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(6));var token=deadline.Token;
        try
        {
            using var udp=new UdpClient(new IPEndPoint(address,0));udp.Client.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastInterface,address.GetAddressBytes());udp.Client.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.MulticastTimeToLive,1);
            var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var typedIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var messages=new List<byte[]>();
            foreach(bool modern in new[]{false,true})foreach(bool typed in new[]{true,false})
            {
                string id="urn:uuid:"+Guid.NewGuid();ids.Add(id);if(typed)typedIds.Add(id);messages.Add(Encoding.UTF8.GetBytes(BuildProbe(id,modern,typed).ToString(SaveOptions.DisableFormatting)));
            }
            var target=new IPEndPoint(IPAddress.Parse("239.255.255.250"),3702);
            foreach(var bytes in messages)await udp.SendAsync(bytes.AsMemory(),target,token).ConfigureAwait(false);
            await Task.Delay(250,token).ConfigureAwait(false);
            foreach(var bytes in messages)await udp.SendAsync(bytes.AsMemory(),target,token).ConfigureAwait(false);
            while(result.Count<128)
            {
                var packet=await udp.ReceiveAsync(token).ConfigureAwait(false);
                try
                {
                    using var data=new MemoryStream(packet.Buffer);var doc=OnvifClient.ParseXml(data);
                    foreach(var camera in ParseMatches(doc,ids,typedIds)){result.TryAdd(camera.Endpoint,camera);if(result.Count>=128)break;}
                }catch(XmlException){}
            }
        }catch(OperationCanceledException) when(!ct.IsCancellationRequested){}catch(SocketException) when(!ct.IsCancellationRequested){}
        return result.Values.ToList();
    }
}
