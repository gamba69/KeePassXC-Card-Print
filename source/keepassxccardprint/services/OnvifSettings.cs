using System.Xml.Linq;

namespace KeePassXCCardPrint.Services;

// Options are returned by the device, not hard-coded to a camera family.
internal sealed record OnvifOption(string Path,string Value,double? Min,double? Max,string[] Choices)
{
    internal bool Writable=>Choices.Length>1||Min is double lo&&Max is double hi&&lo<hi;
}
internal sealed class OnvifSettings
{
    private readonly OnvifClient _client;
    private readonly OnvifStream _stream;
    private readonly bool _imaging;
    private XElement _current;
    private readonly XElement _original;
    private XElement _options;
    private OnvifSettings(OnvifClient client,OnvifStream stream,bool imaging,XElement current,XElement options)
    {_client=client;_stream=stream;_imaging=imaging;_current=new(current);_original=new(current);_options=new(options);}
    private static readonly string[] ImagePaths=["Brightness","ColorSaturation","Contrast","Sharpness","BacklightCompensation/Mode","BacklightCompensation/Level","Exposure/Mode","Exposure/Priority","Exposure/MinExposureTime","Exposure/MaxExposureTime","Exposure/ExposureTime","Exposure/MinGain","Exposure/MaxGain","Exposure/Gain","Exposure/MinIris","Exposure/MaxIris","Exposure/Iris","Focus/AutoFocusMode","Focus/DefaultSpeed","Focus/NearLimit","Focus/FarLimit","IrCutFilter","WideDynamicRange/Mode","WideDynamicRange/Level","WhiteBalance/Mode","WhiteBalance/CrGain","WhiteBalance/CbGain","Extension/NoiseReduction/Level","Extension/Extension/NoiseReduction/Level","Extension/Extension/Extension/NoiseReduction/Level"];
    private static XElement? At(XElement? node,string path)
    {foreach(var name in path.Split('/'))node=OnvifClient.Child(node,name);return node;}
    internal static async Task<OnvifSettings?> Load(OnvifClient c,OnvifStream s,bool imaging,CancellationToken ct)
    {
        if(imaging)
        {
            var ns=OnvifClient.Imaging;if(!c.Has(ns)||s.SourceToken.Length==0)return null;
            var r=await c.Call(ns,"GetImagingSettings",ct,new XElement(ns+"VideoSourceToken",s.SourceToken));
            var o=await c.Call(ns,"GetOptions",ct,new XElement(ns+"VideoSourceToken",s.SourceToken));
            return new(c,s,true,OnvifClient.Child(r,"ImagingSettings")??throw new OnvifException("Missing imaging settings"),OnvifClient.Child(o,"ImagingOptions")??throw new OnvifException("Missing imaging options"));
        }
        if(s.EncoderToken.Length==0)return null;var m=s.Service;bool v2=m==OnvifClient.Media2;
        var cfg=v2?await c.Call(m,"GetVideoEncoderConfigurations",ct,new XElement(m+"ConfigurationToken",s.EncoderToken),new XElement(m+"ProfileToken",s.Token)):await c.Call(m,"GetVideoEncoderConfiguration",ct,new XElement(m+"ConfigurationToken",s.EncoderToken));
        var opts=await c.Call(m,"GetVideoEncoderConfigurationOptions",ct,new XElement(m+"ConfigurationToken",s.EncoderToken),new XElement(m+"ProfileToken",s.Token));
        var current=cfg.Elements().FirstOrDefault(e=>e.Name.LocalName is "Configuration" or "Configurations")??throw new OnvifException("Missing encoder settings");
        // Media2 returns one option element per encoding; retain all alternatives.
        return new(c,s,false,current,new XElement("OptionsRoot",opts.Elements().Where(e=>e.Name.LocalName=="Options")));
    }
    internal IReadOnlyList<OnvifOption> Fields()
    {
        var result=new List<OnvifOption>();
        void Add(string path,XElement? option,string? value=null)
        {
            value??=At(_current,path)?.Value;if(value is null||option is null)return;
            double? min=OnvifClient.Numeric(OnvifClient.Val(option,"Min")),max=OnvifClient.Numeric(OnvifClient.Val(option,"Max"));
            if(min is not null&&max is not null&&min<=max&&OnvifClient.Numeric(value) is not null){result.Add(new(path,value,min,max,[]));return;}
            // Repeated scalar elements represent enumerated ONVIF choices.
            var choices=option.Parent?.Elements(option.Name).Where(e=>!e.HasElements).Select(e=>e.Value).Distinct().ToArray()??[];
            if(choices.Length>0&&choices.Contains(value))result.Add(new(path,value,null,null,choices));
        }
        if(_imaging){foreach(var path in ImagePaths)Add(path,At(_options,path));return result;}
        string encoding=OnvifClient.Val(_current,"Encoding");bool v2=_stream.Service==OnvifClient.Media2;
        var optionSets=_options.Elements().ToArray();var root=optionSets.FirstOrDefault();
        var codec=v2?optionSets.FirstOrDefault(x=>OnvifClient.Val(x,"Encoding")==encoding):OnvifClient.Child(root,encoding=="JPEG"?"JPEG":encoding=="H264"?"H264":encoding=="MPEG4"?"MPEG4":"H265");
        if(codec is null&&root is not null&&!v2)codec=OnvifClient.Desc(root,encoding);
        if(codec is null)return result;
        var res=At(_current,"Resolution");if(res is not null)
        {
            string current=OnvifClient.Val(res,"Width")+"×"+OnvifClient.Val(res,"Height");
            var choices=codec.Elements().Where(e=>e.Name.LocalName=="ResolutionsAvailable").Select(e=>OnvifClient.Val(e,"Width")+"×"+OnvifClient.Val(e,"Height")).Distinct().ToArray();
            if(choices.Contains(current))result.Add(new("Resolution",current,null,null,choices));
        }
        Add("Quality",OnvifClient.Child(v2?codec:root,"QualityRange"));
        Add("RateControl/FrameRateLimit",OnvifClient.Child(codec,"FrameRateRange"));
        Add("RateControl/BitrateLimit",OnvifClient.Child(codec,"BitrateRange"));
        Add("RateControl/EncodingInterval",OnvifClient.Child(codec,"EncodingIntervalRange"));
        if(v2)
        {
            var range=OnvifClient.Child(codec,"GovLengthRange");var value=_current.Attribute("GovLength")?.Value;
            if(value is not null)Add("@GovLength",range,value);
            // Media2 advertises frame rates as a space-separated attribute list.
            var rates=(codec.Attribute("FrameRatesSupported")?.Value??"").Split(' ',StringSplitOptions.RemoveEmptyEntries);
            var rate=At(_current,"RateControl/FrameRateLimit")?.Value;
            if(rate is not null&&rates.Contains(rate)&&!result.Any(x=>x.Path=="RateControl/FrameRateLimit"))result.Add(new("RateControl/FrameRateLimit",rate,null,null,rates));
        }
        else Add(encoding+"/GovLength",OnvifClient.Child(codec,"GovLengthRange"));
        return result;
    }
    internal async Task Apply(IReadOnlyDictionary<string,string> values,bool restore,CancellationToken ct)
    {
        var changed=new XElement(restore?_original:_current);
        if(!restore)
        {
            foreach(var field in Fields())
            {
                if(!field.Writable||!values.TryGetValue(field.Path,out var value)||value==field.Value)continue;
                if(field.Choices.Length>0&&!field.Choices.Contains(value))throw new FormatException();
                if(field.Min is double min&&field.Max is double max&&(OnvifClient.Numeric(value) is not double n||n<min||n>max))throw new FormatException();
                if(field.Path=="Resolution")
                {var pieces=value.Split('×');if(pieces.Length!=2)throw new FormatException();At(changed,"Resolution/Width")!.Value=pieces[0];At(changed,"Resolution/Height")!.Value=pieces[1];}
                else if(field.Path=="@GovLength")changed.SetAttributeValue("GovLength",value);
                else At(changed,field.Path)!.Value=value;
            }
        }
        if(XNode.DeepEquals(changed,_current))return;
        if(_imaging)
        {
            var ns=OnvifClient.Imaging;changed.Name=ns+"ImagingSettings";
            await _client.Call(ns,"SetImagingSettings",ct,new XElement(ns+"VideoSourceToken",_stream.SourceToken),changed,new XElement(ns+"ForcePersistence",true));
        }
        else
        {
            var ns=_stream.Service;changed.Name=ns+"Configuration";
            if(ns==OnvifClient.Media2)await _client.Call(ns,"SetVideoEncoderConfiguration",ct,changed);
            else await _client.Call(ns,"SetVideoEncoderConfiguration",ct,changed,new XElement(ns+"ForcePersistence",true));
        }
        var fresh=await Load(_client,_stream,_imaging,ct)??throw new OnvifException("Settings readback unavailable");_current=fresh._current;_options=fresh._options;
    }
}
