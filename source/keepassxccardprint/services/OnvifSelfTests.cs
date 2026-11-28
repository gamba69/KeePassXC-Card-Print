using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace KeePassXCCardPrint.Services;

internal static class OnvifSelfTests
{
    internal static void Run()
    {
        void Require(bool condition,string label){if(!condition)throw new InvalidOperationException("ONVIF: "+label);}
        foreach(bool modern in new[]{false,true})foreach(bool typed in new[]{false,true})
        {
            var probe=OnvifDiscovery.BuildProbe("urn:uuid:probe",modern,typed);
            Require(OnvifClient.Desc(probe,"ReplyTo") is not null&&OnvifClient.Desc(probe,"MessageID")?.Value=="urn:uuid:probe","discovery addressing");
            Require((OnvifClient.Desc(probe,"Types") is not null)==typed,"typed/untyped discovery");
            var d=OnvifClient.Desc(probe,"Probe")!.Name.Namespace;
            var a=OnvifClient.Desc(probe,"MessageID")!.Name.Namespace;
            var soap=probe.Root!.Name.Namespace;
            var response=new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement(soap+"Envelope",
                new System.Xml.Linq.XElement(soap+"Header",new System.Xml.Linq.XElement(a+"RelatesTo","urn:uuid:probe")),
                new System.Xml.Linq.XElement(soap+"Body",new System.Xml.Linq.XElement(d+"ProbeMatches",new System.Xml.Linq.XElement(d+"ProbeMatch",new System.Xml.Linq.XElement(d+"XAddrs","http://192.0.2.1:8080/onvif/device_service"))))));
            Require(OnvifDiscovery.ParseMatches(response,new HashSet<string>{"urn:uuid:probe"}).Count()==1,"discovery response");
            Require(!OnvifDiscovery.ParseMatches(response,new HashSet<string>{"urn:uuid:other"}).Any(),"unrelated discovery response");
        }
        Require(OnvifClient.ParseEndpoint("192.0.2.1:8080").AbsoluteUri=="http://192.0.2.1:8080/onvif/device_service","manual address");
        Require(OnvifClient.ParseEndpoint("https://192.0.2.1/custom").AbsolutePath=="/custom","custom endpoint");
        // Build a synthetic user-info URI at runtime; never store a credential-bearing URL in source.
        var embeddedCredentials=new UriBuilder("http", "192.0.2.1"){UserName="user",Password="test"}.Uri.AbsoluteUri;
        bool rejected=false;try{OnvifClient.ParseEndpoint(embeddedCredentials);}catch(FormatException){rejected=true;}Require(rejected,"embedded credentials");
        rejected=false;try{using var client=new OnvifClient("http://192.0.2.1","user","test",false);}catch(InvalidOperationException){rejected=true;}Require(rejected,"plaintext consent");
        // Independent public fixture: SHA1(UTF8("noncecreatedpassword")).
        var header=OnvifClient.SecurityHeader("user","password",Encoding.UTF8.GetBytes("nonce"),"created");
        Require(OnvifClient.Desc(header,"Password")?.Value=="BfDGkUDkls7Y/w6yIu+ZG3vOsmU=","WS-Security digest");
        using var xml=new MemoryStream(Encoding.UTF8.GetBytes("<a><b>42</b></a>"));Require(OnvifClient.Desc(OnvifClient.ParseXml(xml),"b")?.Value=="42","XML parsing");
        rejected=false;using var dtd=new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///secret'>]><a>&x;</a>"));try{OnvifClient.ParseXml(dtd);}catch(XmlException){rejected=true;}Require(rejected,"DTD rejection");
        Require(OnvifClient.Numeric("1.25")==1.25&&OnvifClient.Numeric("NaN") is null,"culture-invariant ranges");
        Require(!new OnvifOption("Brightness","1",1,1,[]).Writable&&new OnvifOption("Brightness","1",0,2,[]).Writable,"read-only ranges");
    }
}
