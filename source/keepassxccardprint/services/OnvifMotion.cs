using System.Xml.Linq;

namespace KeePassXCCardPrint.Services;

internal sealed class OnvifMotion(OnvifClient client,OnvifStream stream)
{
    private static readonly XNamespace P=OnvifClient.Ptz,I=OnvifClient.Imaging,T=OnvifClient.Schema;
    private XElement? _ptz,_focus;
    private XElement Profile=>new(P+"ProfileToken",stream.Token);
    private XElement Source=>new(I+"VideoSourceToken",stream.SourceToken);
    internal bool Pan=>Space("RelativePanTiltTranslationSpace") is not null||Space("ContinuousPanTiltVelocitySpace") is not null;
    internal bool Zoom=>Space("RelativeZoomTranslationSpace") is not null||Space("ContinuousZoomVelocitySpace") is not null;
    internal bool Focus=>OnvifClient.Child(_focus,"Relative") is not null||OnvifClient.Child(_focus,"Absolute") is not null;
    internal bool HasPtz=>_ptz is not null;
    internal bool Presets {get;private set;}
    internal bool Home {get;private set;}
    internal async Task Load(CancellationToken ct)
    {
        if(client.Has(P)&&stream.PtzToken.Length>0)
        {
            try
            {
                _ptz=await client.Call(P,"GetConfigurationOptions",ct,new XElement(P+"ConfigurationToken",stream.PtzToken));
                var config=await client.Call(P,"GetConfiguration",ct,new XElement(P+"PTZConfigurationToken",stream.PtzToken));
                string node=OnvifClient.Desc(config,"NodeToken")?.Value??"";
                if(node.Length>0)
                {
                    var info=await client.Call(P,"GetNode",ct,new XElement(P+"NodeToken",node));
                    Presets=OnvifClient.Numeric(OnvifClient.Desc(info,"MaximumNumberOfPresets")?.Value)>0;
                    Home=OnvifClient.Desc(info,"HomeSupported")?.Value is "true" or "1";
                }
            }catch(Exception) when(!ct.IsCancellationRequested){}
        }
        if(client.Has(I)&&stream.SourceToken.Length>0)try{_focus=OnvifClient.Child(await client.Call(I,"GetMoveOptions",ct,Source),"MoveOptions");}catch(Exception) when(!ct.IsCancellationRequested){}
    }
    private XElement? Space(string name)=>OnvifClient.Desc(_ptz,name);
    private static double Axis(XElement? range,double direction,double speed)
    {
        double min=OnvifClient.Numeric(OnvifClient.Val(range,"Min"))??0,max=OnvifClient.Numeric(OnvifClient.Val(range,"Max"))??0;
        return Math.Clamp(direction<0?Math.Min(0,min)*speed:direction>0?Math.Max(0,max)*speed:0,min,max);
    }
    private static XElement Vector(XElement space,string name,double x,double y,double speed,bool two)
    {
        var vector=new XElement(T+name,new XAttribute("x",OnvifClient.Number(Axis(OnvifClient.Child(space,"XRange"),x,speed))));
        if(two)vector.Add(new XAttribute("y",OnvifClient.Number(Axis(OnvifClient.Child(space,"YRange"),y,speed))));
        string uri=OnvifClient.Val(space,"URI");if(uri.Length>0)vector.Add(new XAttribute("space",uri));return vector;
    }
    internal async Task Move(double x,double y,double zoom,double speed,CancellationToken ct)
    {
        bool pan=x!=0||y!=0;var relative=Space(pan?"RelativePanTiltTranslationSpace":"RelativeZoomTranslationSpace");
        if(relative is not null)
        {
            await client.Call(P,"RelativeMove",ct,Profile,new XElement(P+"Translation",Vector(relative,pan?"PanTilt":"Zoom",pan?x:zoom,y,speed,pan)));return;
        }
        var continuous=Space(pan?"ContinuousPanTiltVelocitySpace":"ContinuousZoomVelocitySpace");if(continuous is null)return;
        // Do not start an unbounded continuous movement. Always attempt Stop, even on cancellation.
        var timeout=OnvifClient.Desc(_ptz,"PTZTimeout");
        if(timeout is null)return;
        TimeSpan min,max;try{min=System.Xml.XmlConvert.ToTimeSpan(OnvifClient.Val(timeout,"Min"));max=System.Xml.XmlConvert.ToTimeSpan(OnvifClient.Val(timeout,"Max"));}catch(FormatException){return;}
        var duration=TimeSpan.FromMilliseconds(Math.Clamp(350,min.TotalMilliseconds,max.TotalMilliseconds));if(duration>TimeSpan.FromSeconds(2)||duration<=TimeSpan.Zero)return;
        try
        {
            await client.Call(P,"ContinuousMove",ct,Profile,new XElement(P+"Velocity",Vector(continuous,pan?"PanTilt":"Zoom",pan?x:zoom,y,speed,pan)),new XElement(P+"Timeout",System.Xml.XmlConvert.ToString(duration)));
            await Task.Delay(duration,ct);
        }
        finally{using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(3));try{await Stop(stop.Token);}catch{}}
    }
    internal Task Stop(CancellationToken ct)=>client.Call(P,"Stop",ct,Profile,new XElement(P+"PanTilt",true),new XElement(P+"Zoom",true));
    internal async Task FocusMove(double direction,double speed,CancellationToken ct)
    {
        var relative=OnvifClient.Child(_focus,"Relative");var absolute=OnvifClient.Child(_focus,"Absolute");
        XElement movement;
        if(relative is not null)movement=new XElement(T+"Relative",new XElement(T+"Distance",OnvifClient.Number(Axis(OnvifClient.Child(relative,"Distance"),direction,speed))));
        else if(absolute is not null)
        {
            var range=OnvifClient.Child(absolute,"Position");var min=OnvifClient.Numeric(OnvifClient.Val(range,"Min"));var max=OnvifClient.Numeric(OnvifClient.Val(range,"Max"));if(min is not double lo||max is not double hi||lo>=hi)return;
            var status=await client.Call(I,"GetStatus",ct,Source);var pos=OnvifClient.Numeric(OnvifClient.Desc(status,"Position")?.Value);if(pos is not double current)return;
            movement=new XElement(T+"Absolute",new XElement(T+"Position",OnvifClient.Number(Math.Clamp(current+direction*(hi-lo)*speed,lo,hi))));
        }
        else return;
        await client.Call(I,"Move",ct,Source,new XElement(I+"Focus",movement));
    }
    internal Task FocusStop(CancellationToken ct)=>client.Call(I,"Stop",ct,Source);
    internal sealed record Preset(string Token,string Name){public override string ToString()=>Name;}
    internal async Task<Preset[]> GetPresets(CancellationToken ct)
    {
        var response=await client.Call(P,"GetPresets",ct,Profile);
        return response.Elements().Where(e=>e.Name.LocalName=="Preset"&&OnvifClient.Token(e).Length>0).Select(e=>new Preset(OnvifClient.Token(e),OnvifClient.Val(e,"Name") is {Length:>0} name?name:OnvifClient.Token(e))).ToArray();
    }
    internal Task Goto(Preset preset,CancellationToken ct)=>client.Call(P,"GotoPreset",ct,Profile,new XElement(P+"PresetToken",preset.Token));
    internal Task Remove(Preset preset,CancellationToken ct)=>client.Call(P,"RemovePreset",ct,Profile,new XElement(P+"PresetToken",preset.Token));
    internal Task Save(string name,CancellationToken ct)=>client.Call(P,"SetPreset",ct,Profile,new XElement(P+"PresetName",name));
    internal Task GotoHome(CancellationToken ct)=>client.Call(P,"GotoHomePosition",ct,Profile);
}
