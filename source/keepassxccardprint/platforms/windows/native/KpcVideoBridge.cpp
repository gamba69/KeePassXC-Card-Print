// Media Foundation decoder (synchronous and event-driven CPU-readable MFTs). One owner thread per decoder; no COM
// marshalling, files, network access or third-party native dependencies.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <mferror.h>
#include <d3d11.h>
#include <d3d10.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <vector>
#include <algorithm>
#include <new>
#include <cstring>
#include <cwchar>
#include <memory>
using Microsoft::WRL::ComPtr;

namespace {
constexpr UINT32 MaxPixels = 32000000;
constexpr DWORD MaxBuffer = 192u * 1024u * 1024u;
void wipe(std::vector<BYTE>& b) { if (!b.empty()) SecureZeroMemory(b.data(), b.size()); }
struct Decoder {
    ComPtr<IMFTransform> mft;
    ComPtr<IMFMediaType> output;
    ComPtr<IMFMediaEventGenerator> events;
    ComPtr<IDXGIAdapter1> adapter;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IMFDXGIDeviceManager> deviceManager;
    ComPtr<ID3D11Texture2D> staging;
    bool gpu=false,requireGpu=false;
    bool p010=false,async=false,hardware=false,needInput=false;
    int stage=0;
    UINT32 profileId=0;
    std::vector<BYTE> packed, bgra;
    UINT32 w = 0, h = 0;
    bool com = false, mf = false, firstSample = true;
    int codecId = 0;
    ~Decoder() {
        if (mft) mft->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0);
        events.Reset(); output.Reset(); mft.Reset(); staging.Reset(); deviceManager.Reset(); context.Reset(); device.Reset(); wipe(packed); wipe(bgra);
        if (mf) MFShutdown(); if (com) CoUninitialize();
    }
    HRESULT chooseOutput() {
        stage=4;output.Reset();
        for(int pass=0;pass<(profileId==2?2:1);++pass) for (DWORD i=0; i<64; ++i) {
            ComPtr<IMFMediaType> candidate;
            HRESULT hr=mft->GetOutputAvailableType(0,i,&candidate);
            if (hr==MF_E_NO_MORE_TYPES) break;
            if (FAILED(hr)) return hr;
            GUID sub{};
            if (FAILED(candidate->GetGUID(MF_MT_SUBTYPE,&sub)) || (sub!=MFVideoFormat_NV12 && (codecId!=265 || sub!=MFVideoFormat_P010))) continue;
            if(profileId==2&&pass==0&&sub!=MFVideoFormat_P010)continue;
            UINT32 x=0,y=0;
            if (SUCCEEDED(MFGetAttributeSize(candidate.Get(),MF_MT_FRAME_SIZE,&x,&y)) &&
                (!x || !y || (x&1) || (y&1) || (UINT64)x*y>MaxPixels)) continue;
            hr=mft->SetOutputType(0,candidate.Get(),0);
            if (SUCCEEDED(hr)) { output=candidate; p010=sub==MFVideoFormat_P010; w=x; h=y; return S_OK; }
        }
        return MF_E_INVALIDMEDIATYPE;
    }
    HRESULT setupGpu() {
        D3D_FEATURE_LEVEL level{};
        HRESULT hr=D3D11CreateDevice(adapter.Get(),adapter?D3D_DRIVER_TYPE_UNKNOWN:D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_VIDEO_SUPPORT|D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,&device,&level,&context);
        if(FAILED(hr))return hr;
        ComPtr<ID3D10Multithread> multithread;
        if(SUCCEEDED(device.As(&multithread))||SUCCEEDED(context.As(&multithread)))multithread->SetMultithreadProtected(TRUE);
        UINT token=0;hr=MFCreateDXGIDeviceManager(&token,&deviceManager);
        if(SUCCEEDED(hr))hr=deviceManager->ResetDevice(device.Get(),token);
        if(SUCCEEDED(hr))hr=mft->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER,reinterpret_cast<ULONG_PTR>(deviceManager.Get()));
        return hr;
    }
    HRESULT init(int codec,UINT32 profile,int policy,int adapterIndex) {
        codecId=codec;profileId=profile;requireGpu=policy==1;stage=1;
        HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
        if (FAILED(hr)) return hr; com=true;
        hr=MFStartup(MF_VERSION,MFSTARTUP_FULL); if (FAILED(hr)) return hr; mf=true;
        MFT_REGISTER_TYPE_INFO in{MFMediaType_Video,codec==265?MFVideoFormat_HEVC:MFVideoFormat_H264};
        HRESULT result=MF_E_TOPO_CODEC_NOT_FOUND;int lastStage=2;
        // Policy 1: adapter-specific hardware MFTs, then D3D11-aware system MFTs.
        // Policy 2: software MFTs only. Policy 0 is compatibility/automatic.
        ComPtr<IMFAttributes> filter;
        if(policy==1&&adapterIndex>=0){
            ComPtr<IDXGIFactory1> factory;stage=9;
            hr=CreateDXGIFactory1(IID_PPV_ARGS(&factory));if(FAILED(hr))return hr;
            hr=factory->EnumAdapters1((UINT)adapterIndex,&adapter);if(FAILED(hr))return hr;
            DXGI_ADAPTER_DESC1 desc{};hr=adapter->GetDesc1(&desc);if(FAILED(hr))return hr;
            if(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)return E_INVALIDARG;
            hr=MFCreateAttributes(&filter,1);if(FAILED(hr))return hr;
            hr=filter->SetBlob(MFT_ENUM_ADAPTER_LUID,reinterpret_cast<const UINT8*>(&desc.AdapterLuid),sizeof(desc.AdapterLuid));if(FAILED(hr))return hr;
        }
        const UINT32 modes[]={MFT_ENUM_FLAG_SYNCMFT|MFT_ENUM_FLAG_LOCALMFT,
            MFT_ENUM_FLAG_ASYNCMFT,MFT_ENUM_FLAG_HARDWARE};
        for(int pass=0;pass<3;++pass) {
            const int mode=policy==1?(pass+2)%3:pass;
            if(policy==2&&mode==2)continue;
            IMFActivate** found=nullptr;UINT32 count=0;stage=2;
            hr=filter&&mode==2?MFTEnum2(MFT_CATEGORY_VIDEO_DECODER,modes[mode]|MFT_ENUM_FLAG_SORTANDFILTER,&in,nullptr,filter.Get(),&found,&count)
                :MFTEnumEx(MFT_CATEGORY_VIDEO_DECODER,modes[mode]|MFT_ENUM_FLAG_SORTANDFILTER,&in,nullptr,&found,&count);
            if(FAILED(hr)){if(result==MF_E_TOPO_CODEC_NOT_FOUND){result=hr;lastStage=stage;}continue;}
            for(UINT32 i=0;i<count;++i) {
                if(FAILED(result)) {
                    stage=3;events.Reset();mft.Reset();output.Reset();staging.Reset();deviceManager.Reset();context.Reset();device.Reset();gpu=false;needInput=false;async=false;hardware=mode==2;
                    hr=found[i]->ActivateObject(IID_PPV_ARGS(&mft));
                    if(SUCCEEDED(hr)) {
                        ComPtr<IMFAttributes> attrs;
                        if(SUCCEEDED(mft->GetAttributes(&attrs))) {
                            attrs->SetUINT32(MF_LOW_LATENCY,TRUE);
                            UINT32 flag=FALSE;attrs->GetUINT32(MF_TRANSFORM_ASYNC,&flag);async=flag!=0;
                            if(async){hr=attrs->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK,TRUE);if(SUCCEEDED(hr))hr=mft.As(&events);}
                            UINT32 aware=FALSE;attrs->GetUINT32(MF_SA_D3D11_AWARE,&aware);
                            if(SUCCEEDED(hr)&&(requireGpu||hardware)&&aware){stage=9;hr=setupGpu();stage=FAILED(hr)?9:3;}
                            else if(SUCCEEDED(hr)&&requireGpu){stage=9;hr=E_NOINTERFACE;}
                        }
                        if(SUCCEEDED(hr)&&requireGpu&&!deviceManager){stage=9;hr=E_NOINTERFACE;}
                        ComPtr<IMFMediaType> mt;
                        if(SUCCEEDED(hr))hr=MFCreateMediaType(&mt);
                        if(SUCCEEDED(hr))hr=mt->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video);
                        if(SUCCEEDED(hr))hr=mt->SetGUID(MF_MT_SUBTYPE,in.guidSubtype);
                        if(SUCCEEDED(hr))hr=mt->SetUINT32(MF_MT_INTERLACE_MODE,MFVideoInterlace_MixedInterlaceOrProgressive);
                        if(SUCCEEDED(hr)&&codec==265&&profile)hr=mt->SetUINT32(MF_MT_MPEG2_PROFILE,profile);
                        if(SUCCEEDED(hr))hr=mft->SetInputType(0,mt.Get(),0);
                        if(SUCCEEDED(hr))hr=chooseOutput();
                        if(SUCCEEDED(hr)){stage=5;hr=mft->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING,0);}
                        if(SUCCEEDED(hr))hr=mft->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM,0);
                    }
                    result=hr;lastStage=stage;
                    if(FAILED(hr)&&mft){mft->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH,0);events.Reset();mft.Reset();output.Reset();found[i]->ShutdownObject();}
                }
                found[i]->Release();
            }
            CoTaskMemFree(found);
            if(SUCCEEDED(result))break;
        }
        stage=lastStage;return result;
    }
    HRESULT readSurface(IMFDXGIBuffer* dxgi,UINT32 rowBytes) {
        if(!context||!device)return MF_E_INVALIDMEDIATYPE;
        ComPtr<ID3D11Texture2D> source;
        HRESULT hr=dxgi->GetResource(IID_PPV_ARGS(&source));if(FAILED(hr))return hr;
        ComPtr<ID3D11Device> owner;source->GetDevice(&owner);if(owner.Get()!=device.Get())return E_INVALIDARG;
        D3D11_TEXTURE2D_DESC desc{};source->GetDesc(&desc);UINT sub=0;hr=dxgi->GetSubresourceIndex(&sub);if(FAILED(hr))return hr;
        if(requireGpu&&!(desc.BindFlags&D3D11_BIND_DECODER)){stage=11;return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);}
        if(desc.Format!=(p010?DXGI_FORMAT_P010:DXGI_FORMAT_NV12)||desc.Width<w||desc.Height<h||
            (desc.Height&1)||desc.MipLevels!=1||sub>=desc.ArraySize||desc.SampleDesc.Count!=1||
            (UINT64)desc.Width*desc.Height>MaxPixels)return MF_E_INVALIDMEDIATYPE;
        if(staging){D3D11_TEXTURE2D_DESC old{};staging->GetDesc(&old);if(old.Width!=desc.Width||old.Height!=desc.Height||old.Format!=desc.Format)staging.Reset();}
        if(!staging){desc.ArraySize=1;desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;desc.MiscFlags=0;
            hr=device->CreateTexture2D(&desc,nullptr,&staging);if(FAILED(hr))return hr;}
        context->CopySubresourceRegion(staging.Get(),0,0,0,0,source.Get(),sub,nullptr);
        D3D11_MAPPED_SUBRESOURCE mapped{};hr=context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped);if(FAILED(hr))return hr;
        if(!mapped.pData||mapped.RowPitch<rowBytes){context->Unmap(staging.Get(),0);return MF_E_INVALIDMEDIATYPE;}
        const BYTE* src=static_cast<const BYTE*>(mapped.pData);
        for(UINT32 row=0;row<h;++row)memcpy(packed.data()+(size_t)row*rowBytes,src+(size_t)row*mapped.RowPitch,rowBytes);
        src+=(size_t)desc.Height*mapped.RowPitch;
        for(UINT32 row=0;row<h/2;++row)memcpy(packed.data()+(size_t)(h+row)*rowBytes,src+(size_t)row*mapped.RowPitch,rowBytes);
        context->Unmap(staging.Get(),0);gpu=true;return S_OK;
    }
    HRESULT convert(IMFSample* sample) {
        stage=7;
        if(!w||!h){HRESULT hr=MFGetAttributeSize(output.Get(),MF_MT_FRAME_SIZE,&w,&h);if(FAILED(hr)||!w||!h||(w&1)||(h&1)||(UINT64)w*h>MaxPixels)return MF_E_INVALIDMEDIATYPE;}
        ComPtr<IMFMediaBuffer> buf;HRESULT hr=sample->GetBufferByIndex(0,&buf);if(FAILED(hr))return hr;
        ComPtr<IMFDXGIBuffer> dxgi;buf.As(&dxgi);
        size_t n=(size_t)w*h;UINT32 rowBytes=w*(p010?2u:1u); DWORD bytes=(DWORD)((n+n/2)*(p010?2u:1u));
        wipe(packed); packed.resize(bytes);
        if(dxgi){hr=readSurface(dxgi.Get(),rowBytes);if(FAILED(hr))return hr;}
        else {
        if(requireGpu){stage=11;return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);}
        gpu=false;
        buf.Reset();hr=sample->ConvertToContiguousBuffer(&buf);if(FAILED(hr))return hr;
        ComPtr<IMF2DBuffer> two;
        if (SUCCEEDED(buf.As(&two))) {
            DWORD length=0; hr=two->GetContiguousLength(&length);
            if (FAILED(hr)||length!=bytes) return MF_E_INVALIDMEDIATYPE;
            hr=two->ContiguousCopyTo(packed.data(),bytes); if (FAILED(hr)) return hr;
        } else {
            BYTE* src=nullptr; DWORD current=0;
            hr=buf->Lock(&src,nullptr,&current); if (FAILED(hr)) return hr;
            UINT32 rawStride=0; if(FAILED(output->GetUINT32(MF_MT_DEFAULT_STRIDE,&rawStride)))rawStride=rowBytes;
            LONG stride=(LONG)rawStride;
            if (stride<(LONG)rowBytes || (UINT64)stride*(h+h/2)>current) {buf->Unlock();return MF_E_INVALIDMEDIATYPE;}
            for (UINT32 row=0;row<h+h/2;++row) memcpy(packed.data()+(size_t)row*rowBytes,src+(size_t)row*stride,rowBytes);
            buf->Unlock();
        }
        }
        wipe(bgra); bgra.resize(n*4);
        auto clip=[](int a)->BYTE {return (BYTE)std::clamp(a,0,255);};
        UINT32 nominal=MFNominalRange_16_235; output->GetUINT32(MF_MT_VIDEO_NOMINAL_RANGE,&nominal);
        UINT32 matrix=MFVideoTransferMatrix_BT601; output->GetUINT32(MF_MT_YUV_MATRIX,&matrix);
        bool full=nominal==MFNominalRange_0_255, hd=matrix==MFVideoTransferMatrix_BT709;
        for (UINT32 y=0;y<h;++y) for (UINT32 x=0;x<w;++x) {
            size_t p=(size_t)y*w+x, uv=n+(size_t)(y/2)*w+(x&~1u);
            // P010 stores the ten most significant bits in little-endian words.
            // Taking the high byte maps 10-bit limited/full range to 8-bit.
            auto sample8=[&](size_t index)->int {return p010?packed[index*2+1]:packed[index];};
            int l=sample8(p),u=sample8(uv)-128,v=sample8(uv+1)-128;
            int c=full?256*l:298*std::max(0,l-16);
            int r=full?(hd?403:359):(hd?459:409), b=full?(hd?475:454):(hd?541:516);
            int gu=full?(hd?48:88):(hd?55:100), gv=full?(hd?120:183):(hd?136:208);
            bgra[p*4]=clip((c+b*u+128)>>8); bgra[p*4+1]=clip((c-gu*u-gv*v+128)>>8);
            bgra[p*4+2]=clip((c+r*v+128)>>8); bgra[p*4+3]=255;
        }
        return S_OK;
    }
    HRESULT drain(bool& frame,bool one=false) {
        stage=6;
        for(int i=0;i<64;++i) {
            MFT_OUTPUT_STREAM_INFO info{}; HRESULT hr=mft->GetOutputStreamInfo(0,&info); if(FAILED(hr))return hr;
            ComPtr<IMFSample> supplied;
            if(!(info.dwFlags & (MFT_OUTPUT_STREAM_PROVIDES_SAMPLES|MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES))) {
                if(!info.cbSize||info.cbSize>MaxBuffer)return MF_E_INVALIDMEDIATYPE;
                ComPtr<IMFMediaBuffer> buffer;
                hr=MFCreateSample(&supplied);
                if(SUCCEEDED(hr))hr=info.cbAlignment>1
                    ?MFCreateAlignedMemoryBuffer(info.cbSize,info.cbAlignment-1,&buffer)
                    :MFCreateMemoryBuffer(info.cbSize,&buffer);
                if(SUCCEEDED(hr))hr=supplied->AddBuffer(buffer.Get()); if(FAILED(hr))return hr;
            }
            MFT_OUTPUT_DATA_BUFFER data{}; data.dwStreamID=0; data.pSample=supplied.Get(); DWORD flags=0;
            hr=mft->ProcessOutput(0,1,&data,&flags);
            ComPtr<IMFSample> owned; if(data.pSample && data.pSample!=supplied.Get())owned.Attach(data.pSample);
            if(data.pEvents)data.pEvents->Release();
            if(hr==MF_E_TRANSFORM_NEED_MORE_INPUT)return S_OK;
            if(hr==MF_E_TRANSFORM_STREAM_CHANGE){hr=chooseOutput();if(FAILED(hr))return hr;frame=false;if(one)return S_OK;continue;}
            if(FAILED(hr))return hr;
            if((data.dwStatus & MFT_OUTPUT_DATA_BUFFER_NO_SAMPLE)==MFT_OUTPUT_DATA_BUFFER_NO_SAMPLE) {
                if(!(flags & MFT_PROCESS_OUTPUT_STATUS_NEW_STREAMS) && !(data.dwStatus & MFT_OUTPUT_DATA_BUFFER_INCOMPLETE))return S_OK;
                if(one)return S_OK;continue;
            }
            if(!data.pSample)return E_UNEXPECTED;
            hr=convert(data.pSample);if(FAILED(hr))return hr;frame=true;if(one)return S_OK;
        }
        return E_UNEXPECTED;
    }
    HRESULT pump(bool& frame,bool waitForInput) {
        ULONGLONG until=GetTickCount64()+(waitForInput?1500:40);
        for(int i=0;i<2048;++i) {
            ComPtr<IMFMediaEvent> event;HRESULT hr=events->GetEvent(MF_EVENT_FLAG_NO_WAIT,&event);
            if(hr==MF_E_NO_EVENTS_AVAILABLE){
                if(needInput||GetTickCount64()>=until)return waitForInput&&!needInput?HRESULT_FROM_WIN32(WAIT_TIMEOUT):S_OK;
                Sleep(1);continue;
            }
            if(FAILED(hr))return hr;
            HRESULT status=S_OK;hr=event->GetStatus(&status);if(FAILED(hr)||FAILED(status))return FAILED(hr)?hr:status;
            MediaEventType kind=MEUnknown;hr=event->GetType(&kind);if(FAILED(hr))return hr;
            if(kind==METransformNeedInput){needInput=true;return S_OK;}
            if(kind==METransformHaveOutput){hr=drain(frame,true);if(FAILED(hr))return hr;}
            if(kind==MEError)return E_FAIL;
        }
        return waitForInput&&!needInput?HRESULT_FROM_WIN32(WAIT_TIMEOUT):S_OK;
    }
    bool randomAccess(const BYTE* p,DWORD n) const {
        // Annex B start codes (three or four bytes); emulation prevention
        // prevents this pattern occurring inside an escaped NAL payload.
        for(DWORD i=0;i+3<n;++i) {
            if(p[i]||p[i+1]||p[i+2]!=1)continue;
            unsigned type=codecId==265?(p[i+3]>>1)&63:p[i+3]&31;
            if(codecId==265?(type>=16&&type<=21):type==5)return true;
        }
        return false;
    }
    HRESULT push(const BYTE* p,DWORD n,LONGLONG time,bool& frame) {
        stage=8;
        if(async&&!needInput){HRESULT hr=pump(frame,true);if(FAILED(hr))return hr;}
        stage=8;ComPtr<IMFSample> s; ComPtr<IMFMediaBuffer> b;
        MFT_INPUT_STREAM_INFO inputInfo{};
        HRESULT hr=mft->GetInputStreamInfo(0,&inputInfo);if(FAILED(hr))return hr;
        DWORD capacity=std::max(n,inputInfo.cbSize);if(capacity>MaxBuffer)return MF_E_INVALIDMEDIATYPE;
        hr=MFCreateSample(&s);
        if(SUCCEEDED(hr))hr=inputInfo.cbAlignment>1
            ?MFCreateAlignedMemoryBuffer(capacity,inputInfo.cbAlignment-1,&b)
            :MFCreateMemoryBuffer(capacity,&b);
        if(FAILED(hr))return hr;
        BYTE* dst=nullptr;hr=b->Lock(&dst,nullptr,nullptr);if(FAILED(hr))return hr;
        memcpy(dst,p,n);b->Unlock();hr=b->SetCurrentLength(n);if(SUCCEEDED(hr))hr=s->AddBuffer(b.Get());
        if(SUCCEEDED(hr))hr=s->SetSampleTime(time);
        if(SUCCEEDED(hr)&&firstSample)hr=s->SetUINT32(MFSampleExtension_Discontinuity,TRUE);
        if(SUCCEEDED(hr)&&randomAccess(p,n))hr=s->SetUINT32(MFSampleExtension_CleanPoint,TRUE);
        if(FAILED(hr))return hr;
        hr=mft->ProcessInput(0,s.Get(),0);
        if(hr==MF_E_NOTACCEPTING&&!async){hr=drain(frame);if(FAILED(hr))return hr;hr=mft->ProcessInput(0,s.Get(),0);}
        if(FAILED(hr))return hr;
        firstSample=false;
        if(async){needInput=false;return pump(frame,false);}
        return drain(frame);
    }
};
}
extern "C" __declspec(dllexport) HRESULT __cdecl KpcVideoOpen(int codec,UINT32 profile,int policy,int adapterIndex,void** result,int* stage,int* mode) noexcept {
    if(!result || !stage || !mode || (codec!=264 && codec!=265))return E_INVALIDARG; *result=nullptr;*stage=0;*mode=0;
    try {auto d=std::make_unique<Decoder>();HRESULT hr=d->init(codec,profile,policy,adapterIndex);*stage=d->stage;*mode=d->hardware?2:d->async?1:0;if(FAILED(hr))return hr;*result=d.release();return S_OK;}catch(...){return E_OUTOFMEMORY;}
}
extern "C" __declspec(dllexport) HRESULT __cdecl KpcVideoPush(void* instance,const BYTE* data,DWORD length,LONGLONG time,const BYTE** pixels,UINT32* width,UINT32* height,int* stage,int* depth,int* mode) noexcept {
    if(!instance||!data||!length||length>24u*1024u*1024u||!pixels||!width||!height||!stage||!depth||!mode)return E_INVALIDARG;
    *pixels=nullptr;*width=*height=0;*stage=0;*depth=0;*mode=0;
    try {auto d=static_cast<Decoder*>(instance);bool frame=false;HRESULT hr=d->push(data,length,time,frame);*stage=d->stage;*depth=d->p010?10:8;*mode=d->gpu?3:d->hardware?2:d->async?1:0;if(FAILED(hr))return hr;
        if(!frame)return S_FALSE;*pixels=d->bgra.data();*width=d->w;*height=d->h;return S_OK;}catch(...){return E_OUTOFMEMORY;}
}
extern "C" __declspec(dllexport) void __cdecl KpcVideoClose(void* instance) noexcept {delete static_cast<Decoder*>(instance);}

extern "C" __declspec(dllexport) HRESULT __cdecl KpcVideoInventory(int codec,wchar_t* text,UINT32 capacity) noexcept {
    if(!text||capacity<2||capacity>32768||(codec!=264&&codec!=265))return E_INVALIDARG;text[0]=0;
    HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);if(FAILED(hr))return hr;
    hr=MFStartup(MF_VERSION,MFSTARTUP_LITE);if(FAILED(hr)){CoUninitialize();return hr;}
    MFT_REGISTER_TYPE_INFO in{MFMediaType_Video,codec==265?MFVideoFormat_HEVC:MFVideoFormat_H264};
    const UINT32 modes[]={MFT_ENUM_FLAG_SYNCMFT|MFT_ENUM_FLAG_LOCALMFT,MFT_ENUM_FLAG_ASYNCMFT,MFT_ENUM_FLAG_HARDWARE};
    const wchar_t* labels[]={L"[sync] ",L"[async] ",L"[hardware] "};UINT32 at=0;HRESULT result=S_OK;
    for(int mode=0;mode<3;++mode) {
        IMFActivate** found=nullptr;UINT32 count=0;
        hr=MFTEnumEx(MFT_CATEGORY_VIDEO_DECODER,modes[mode]|MFT_ENUM_FLAG_SORTANDFILTER,&in,nullptr,&found,&count);
        if(FAILED(hr)){result=hr;continue;}
        auto append=[&](const wchar_t* str){while(*str&&at+1<capacity)text[at++]=*str++;text[at]=0;};
        for(UINT32 i=0;i<count;++i){wchar_t* name=nullptr;UINT32 len=0;
            if(SUCCEEDED(found[i]->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute,&name,&len))&&name){if(at)append(L"; ");append(labels[mode]);append(name);}
            CoTaskMemFree(name);found[i]->Release();
        }
        CoTaskMemFree(found);
    }
    MFShutdown();CoUninitialize();return result;
}

// A fixed numeric DXGI ordinal is used only within the current inventory.
// No driver-specific DLL is loaded merely to advertise a decoder.
extern "C" __declspec(dllexport) HRESULT __cdecl KpcVideoAdapters(wchar_t* text,UINT32 capacity) noexcept {
    if(!text||capacity<2||capacity>32768)return E_INVALIDARG;text[0]=0;
    ComPtr<IDXGIFactory1> factory;HRESULT hr=CreateDXGIFactory1(IID_PPV_ARGS(&factory));if(FAILED(hr))return hr;
    UINT32 at=0;
    auto append=[&](const wchar_t* str){while(*str&&at+1<capacity)text[at++]=*str++;text[at]=0;};
    for(UINT i=0;i<32;++i){
        ComPtr<IDXGIAdapter1> adapter;hr=factory->EnumAdapters1(i,&adapter);
        if(hr==DXGI_ERROR_NOT_FOUND)break;if(FAILED(hr))return hr;
        DXGI_ADAPTER_DESC1 d{};if(FAILED(adapter->GetDesc1(&d))||(d.Flags&DXGI_ADAPTER_FLAG_SOFTWARE))continue;
        LARGE_INTEGER driver{};adapter->CheckInterfaceSupport(__uuidof(IDXGIDevice),&driver);
        wchar_t id[160]{};swprintf_s(id,L"%u|%04X-%04X-%08X-%02X|%08X%08X|",i,d.VendorId,d.DeviceId,d.SubSysId,d.Revision,(UINT)driver.HighPart,(UINT)driver.LowPart);
        append(id);for(auto& c:d.Description){if(c==L'|'||c==L'\r'||c==L'\n')c=L' ';}append(d.Description);append(L"\n");
    }
    return S_OK;
}

// Query driver-advertised profiles independently of registered Media Foundation transforms.
// S_OK: profile and output format advertised; S_FALSE: not advertised; failed HRESULT: unknown.
extern "C" __declspec(dllexport) HRESULT __cdecl KpcVideoCapability(int adapterIndex,int codec,UINT32 profile,UINT32* at1080) noexcept {
    if(!at1080||adapterIndex<0||adapterIndex>=32||(codec!=264&&codec!=265))return E_INVALIDARG;
    *at1080=0;
    ComPtr<IDXGIFactory1> factory;HRESULT hr=CreateDXGIFactory1(IID_PPV_ARGS(&factory));if(FAILED(hr))return hr;
    ComPtr<IDXGIAdapter1> adapter;hr=factory->EnumAdapters1((UINT)adapterIndex,&adapter);if(FAILED(hr))return hr;
    ComPtr<ID3D11Device> device;D3D_FEATURE_LEVEL level{};
    hr=D3D11CreateDevice(adapter.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,D3D11_CREATE_DEVICE_VIDEO_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,&level,nullptr);
    if(FAILED(hr))return hr;
    ComPtr<ID3D11VideoDevice> video;hr=device.As(&video);if(FAILED(hr))return hr;
    // Values from the Windows SDK d3d11.h, kept local so no additional GUID import library is needed.
    constexpr GUID h264E{0x1b81be68,0xa0c7,0x11d3,{0xb9,0x84,0x00,0xc0,0x4f,0x2e,0x73,0xc5}};
    constexpr GUID h264F{0x1b81be69,0xa0c7,0x11d3,{0xb9,0x84,0x00,0xc0,0x4f,0x2e,0x73,0xc5}};
    constexpr GUID hevcMain{0x5b11d51b,0x2f4c,0x4452,{0xbc,0xc3,0x09,0xf2,0xa1,0x16,0x0c,0xc0}};
    constexpr GUID hevcMain10{0x107af0e0,0xef1a,0x4d19,{0xab,0xa8,0x67,0xa1,0x63,0x07,0x3d,0x13}};
    bool supported=false;
    for(UINT i=0;i<video->GetVideoDecoderProfileCount();++i){
        GUID id{};hr=video->GetVideoDecoderProfile(i,&id);if(FAILED(hr))return hr;
        bool match=codec==264?(id==h264E||id==h264F):
            profile==2?id==hevcMain10:id==hevcMain;
        if(!match)continue;
        DXGI_FORMAT format=codec==265&&profile==2?DXGI_FORMAT_P010:DXGI_FORMAT_NV12;
        BOOL ok=FALSE;hr=video->CheckVideoDecoderFormat(&id,format,&ok);if(FAILED(hr))return hr;
        if(!ok)continue;supported=true;
        D3D11_VIDEO_DECODER_DESC desc{id,1920,1080,format};UINT count=0;
        if(SUCCEEDED(video->GetVideoDecoderConfigCount(&desc,&count))&&count)*at1080=1;
    }
    return supported?S_OK:S_FALSE;
}
