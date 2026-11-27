using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KeePassXCCardPrint.Services;

internal sealed record ScannerCameraProperty(int Id, int Value, int Flags, int Capabilities, int Min, int Max, int Step, bool HasRange, bool Writable)
{
    internal bool Auto => (Flags & 1) != 0;
    internal bool CanAuto => Writable && (Capabilities & 1) != 0;
    internal bool CanManual => Writable && HasRange && (Capabilities & 2) != 0;
}

// Optional legacy KS filter controls. Do not open a second capture stream or use COM RCWs.
// A driver may expose only node/extended controls or refuse a separate control handle.
internal static unsafe class ScannerCameraControls
{
    internal const int Focus=6, Exposure=4;
    private static readonly Guid CameraSet=new("C6E13370-30AC-11D0-A18C-00A0C9118956");
    private const uint Ioctl=0x002F0003, Get=1, Set=2, BasicSupport=0x200;
    [StructLayout(LayoutKind.Sequential)]
    private struct Property { internal Guid Set; internal uint Id, Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Value { internal Property Property; internal int Number, Mode, Capabilities; }
    [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle Open(string path,uint access,uint share,nint security,uint creation,uint attributes,nint template);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device,uint code,void* input,uint inputSize,void* output,uint outputSize,out uint returned,nint overlapped);
    private static SafeFileHandle OpenControl(string path) => Open(path,0xC0000000,3,0,3,0,0);
    private static Property Request(int id,uint flags) => new(){Set=CameraSet,Id=(uint)id,Flags=flags};
    internal static ScannerCameraProperty? Read(string path,int id)
    {
        if(string.IsNullOrEmpty(path))return null;
        using var handle=OpenControl(path);
        if(handle.IsInvalid)return null;
        var request=Request(id,Get);
        Value current=default;
        if(!DeviceIoControl(handle,Ioctl,&request,(uint)sizeof(Property),&current,(uint)sizeof(Value),out uint actual,0)||actual<sizeof(Value))return null;
        request=Request(id,BasicSupport);
        byte* buffer=stackalloc byte[512]; new Span<byte>(buffer,512).Clear();
        bool ok=DeviceIoControl(handle,Ioctl,&request,(uint)sizeof(Property),buffer,512,out uint size,0);
        // KS_PROPERTY_DESCRIPTION (40 bytes), KSPROPERTY_MEMBERSHEADER (16 bytes),
        // KSPROPERTY_STEPPING_LONG (16 bytes). Accept only explicit stepping ranges.
        bool writable=ok&&size>=4&&(*(uint*)buffer&Set)!=0;
        int min=0,max=0,step=1;bool range=false;
        if(ok&&size>=72&&*(uint*)(buffer+32)>0&&*(uint*)(buffer+40)==2&&*(uint*)(buffer+44)>=16&&*(uint*)(buffer+48)>0)
        {
            step=*(int*)(buffer+56);min=*(int*)(buffer+64);max=*(int*)(buffer+68);
            range=step>0&&max>=min&&(long)max-min<=1_000_000&&current.Number>=min&&current.Number<=max;
        }
        return new(id,current.Number,current.Mode,current.Capabilities,min,max,step,range,writable);
    }
    internal static bool Apply(string path,ScannerCameraProperty property,bool auto,int value)
    {
        if(auto?!property.CanAuto:!property.CanManual)return false;
        if(!auto&&(value<property.Min||value>property.Max||((long)value-property.Min)%property.Step!=0))return false;
        using var handle=OpenControl(path);
        if(handle.IsInvalid)return false;
        var request=Request(property.Id,Set);
        var data=new Value{Property=request,Number=auto?property.Value:value,Mode=auto?1:2,Capabilities=0};
        return DeviceIoControl(handle,Ioctl,&request,(uint)sizeof(Property),&data,(uint)sizeof(Value),out _,0);
    }
}
