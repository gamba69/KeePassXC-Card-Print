// Direct vendor decoding. The driver DLLs are loaded only on a selected adapter.
// All objects and calls belong to one decoder worker. No network or file IO.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <memory>
#include <vector>
#include <ffnvcodec/dynlink_cuda.h>
#include <ffnvcodec/dynlink_cuviddec.h>
#include <ffnvcodec/dynlink_nvcuvid.h>
#define MFX_DEPRECATED_OFF
#include <vpl/mfxvideo.h>
#include <vpl/mfxdispatcher.h>
#include <core/Factory.h>
#include <components/VideoDecoderUVD.h>
using Microsoft::WRL::ComPtr;
namespace {
constexpr uint64_t MaxPixels=32000000;
constexpr size_t MaxUnit=24u*1024*1024;
const HRESULT Unsupported=HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
const HRESULT Missing=HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND);
const HRESULT Invalid=HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
struct Module {
    HMODULE value=nullptr;
    ~Module(){if(value)FreeLibrary(value);}
    bool open(const wchar_t* name,DWORD flags=LOAD_LIBRARY_SEARCH_SYSTEM32){value=LoadLibraryExW(name,nullptr,flags);return value!=nullptr;}
    template<class T> bool get(T*& fn,const char* name){fn=reinterpret_cast<T*>(GetProcAddress(value,name));return fn!=nullptr;}
};
void wipe(std::vector<uint8_t>& b){if(!b.empty())SecureZeroMemory(b.data(),b.size());}
bool validSize(unsigned w,unsigned h){return w&&h&&uint64_t(w)*h<=MaxPixels&&w<=8192&&h<=8192;}
struct Backend {
    int stage=1,code=0,depth=8;
    unsigned w=0,h=0;bool ready=false;
    std::vector<uint8_t> bgra;
    virtual ~Backend(){wipe(bgra);}
    virtual HRESULT open(IDXGIAdapter1*,int,int,unsigned,unsigned)=0;
    virtual HRESULT push(const uint8_t*,unsigned,int64_t)=0;
    HRESULT error(int s,int c,HRESULT hr=E_FAIL){stage=s;code=c;return hr;}
    // Readback from decoder-native NV12/P010. Use limited range BT.709; no tone
    // mapping is claimed. Retain original luma detail for QR recognition.
    HRESULT convert(const uint8_t* y,const uint8_t* uv,unsigned ys,unsigned uvs,unsigned width,unsigned height,int bits){
        if(!validSize(width,height)||!y||!uv||(width&1)||(height&1)||(bits!=8&&bits!=10))return error(6,0,Invalid);
        unsigned bytes=bits==10?2:1;if(ys<width*bytes||uvs<width*bytes)return error(6,0,Invalid);
        w=width;h=height;depth=bits;bgra.resize(size_t(w)*h*4);
        auto sample=[&](const uint8_t* p,unsigned i){if(bits==8)return int(p[i]);uint16_t v;memcpy(&v,p+i*2,2);return int(v>>8);};
        auto clip=[](int x){return static_cast<uint8_t>(std::clamp(x,0,255));};
        for(unsigned row=0;row<h;row++)for(unsigned col=0;col<w;col++){
            int l=std::max(0,sample(y+size_t(row)*ys,col)-16),u=sample(uv+size_t(row/2)*uvs,col&~1u)-128,v=sample(uv+size_t(row/2)*uvs,(col&~1u)+1)-128;
            auto p=bgra.data()+(size_t(row)*w+col)*4;
            p[0]=clip((298*l+541*u+128)>>8);p[1]=clip((298*l-55*u-136*v+128)>>8);p[2]=clip((298*l+459*v+128)>>8);p[3]=255;
        }ready=true;return S_OK;
    }
};
HRESULT deviceFor(IDXGIAdapter1* adapter,ComPtr<ID3D11Device>& device){return D3D11CreateDevice(adapter,D3D_DRIVER_TYPE_UNKNOWN,nullptr,D3D11_CREATE_DEVICE_VIDEO_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,nullptr);}
struct Nv final:Backend {
    Module cuda,cuvid;CUcontext ctx=nullptr;CUvideoparser parser=nullptr;CUvideodecoder decoder=nullptr;
    tcuCtxPushCurrent_v2* pushCtx=nullptr;tcuCtxPopCurrent_v2* popCtx=nullptr;tcuCtxDestroy_v2* destroyCtx=nullptr;
    tcuvidDestroyVideoParser* destroyParser=nullptr;tcuvidDestroyDecoder* destroyDecoder=nullptr;
    tcuvidGetDecoderCaps* caps=nullptr;tcuvidCreateDecoder* createDecoder=nullptr;tcuvidDecodePicture* decode=nullptr;
    tcuvidMapVideoFrame64* map=nullptr;tcuvidUnmapVideoFrame64* unmap=nullptr;tcuMemcpy2D_v2* copy=nullptr;tcuvidParseVideoData* parse=nullptr;
    tcuvidGetDecodeStatus* decodeStatus=nullptr;unsigned surfaceW=0,surfaceH=0;int streamBits=8;HRESULT callbackError=S_OK;
    std::vector<uint8_t> raw;
    ~Nv() override {if(ctx){if(pushCtx&&pushCtx(ctx)==CUDA_SUCCESS){if(parser)destroyParser(parser);if(decoder)destroyDecoder(decoder);CUcontext old=nullptr;popCtx(&old);}destroyCtx(ctx);}wipe(raw);}
    HRESULT open(IDXGIAdapter1* adapter,int codec,int profile,unsigned,unsigned) override {
        if(!cuda.open(L"nvcuda.dll")||!cuvid.open(L"nvcuvid.dll"))return error(1,0,Missing);
        tcuInit* init=nullptr;tcuD3D11GetDevice* find=nullptr;tcuCtxCreate_v2* createCtx=nullptr;tcuvidCreateVideoParser* createParser=nullptr;
#define LOAD(m,f,n) if(!m.get(f,n))return error(1,0,Missing)
        LOAD(cuda,init,"cuInit");LOAD(cuda,find,"cuD3D11GetDevice");LOAD(cuda,createCtx,"cuCtxCreate_v2");LOAD(cuda,pushCtx,"cuCtxPushCurrent_v2");LOAD(cuda,popCtx,"cuCtxPopCurrent_v2");LOAD(cuda,destroyCtx,"cuCtxDestroy_v2");LOAD(cuda,copy,"cuMemcpy2D_v2");
        LOAD(cuvid,createParser,"cuvidCreateVideoParser");LOAD(cuvid,destroyParser,"cuvidDestroyVideoParser");LOAD(cuvid,createDecoder,"cuvidCreateDecoder");LOAD(cuvid,destroyDecoder,"cuvidDestroyDecoder");LOAD(cuvid,decode,"cuvidDecodePicture");LOAD(cuvid,map,"cuvidMapVideoFrame64");LOAD(cuvid,unmap,"cuvidUnmapVideoFrame64");LOAD(cuvid,parse,"cuvidParseVideoData");
#undef LOAD
        cuvid.get(caps,"cuvidGetDecoderCaps");cuvid.get(decodeStatus,"cuvidGetDecodeStatus");
        CUdevice dev;CUresult r=init(0);if(r!=CUDA_SUCCESS)return error(2,r);r=find(&dev,adapter);if(r!=CUDA_SUCCESS)return error(2,r);
        r=createCtx(&ctx,0,dev);if(r!=CUDA_SUCCESS)return error(2,r);
        const auto kind=codec==264?cudaVideoCodec_H264:cudaVideoCodec_HEVC;
        HRESULT hr=S_OK;
        if(caps){CUVIDDECODECAPS c{};c.eCodecType=kind;c.eChromaFormat=cudaVideoChromaFormat_420;c.nBitDepthMinus8=profile==2?2:0;r=caps(&c);if(r==CUDA_SUCCESS&&!c.bIsSupported)hr=error(3,r,Unsupported);}
        if(SUCCEEDED(hr)){
            CUVIDPARSERPARAMS p{};p.CodecType=kind;p.ulMaxNumDecodeSurfaces=1;p.ulMaxDisplayDelay=0;p.pUserData=this;
            p.pfnSequenceCallback=sequence;p.pfnDecodePicture=picture;p.pfnDisplayPicture=display;
            r=createParser(&parser,&p);if(r!=CUDA_SUCCESS)hr=error(3,r);
        }
        CUcontext old=nullptr;popCtx(&old);return hr;
    }
    static int CUDAAPI sequence(void* p,CUVIDEOFORMAT* f){auto& s=*static_cast<Nv*>(p);try{return s.onSequence(f);}catch(...){s.callbackError=E_OUTOFMEMORY;return 0;}}
    int onSequence(CUVIDEOFORMAT* f){
        unsigned width=f->display_area.right-f->display_area.left,height=f->display_area.bottom-f->display_area.top;
        if(!validSize(width,height)||!validSize(f->coded_width,f->coded_height)||(width&1)||(height&1)||f->chroma_format!=cudaVideoChromaFormat_420||(f->bit_depth_luma_minus8!=0&&f->bit_depth_luma_minus8!=2)||f->min_num_decode_surfaces>32){callbackError=error(3,0,Unsupported);return 0;}
        if(decoder){if(surfaceW==width&&surfaceH==height&&streamBits==8+f->bit_depth_luma_minus8)return std::max<int>(1,f->min_num_decode_surfaces);callbackError=error(3,0,Unsupported);return 0;}
        if(caps){CUVIDDECODECAPS c{};c.eCodecType=f->codec;c.eChromaFormat=f->chroma_format;c.nBitDepthMinus8=f->bit_depth_luma_minus8;auto r=caps(&c);if(r==CUDA_SUCCESS&&(!c.bIsSupported||f->coded_width>c.nMaxWidth||f->coded_height>c.nMaxHeight)){callbackError=error(3,r,Unsupported);return 0;}}
        CUVIDDECODECREATEINFO ci{};ci.CodecType=f->codec;ci.ChromaFormat=f->chroma_format;ci.bitDepthMinus8=f->bit_depth_luma_minus8;ci.OutputFormat=ci.bitDepthMinus8?cudaVideoSurfaceFormat_P016:cudaVideoSurfaceFormat_NV12;
        ci.ulWidth=f->coded_width;ci.ulHeight=f->coded_height;ci.ulNumDecodeSurfaces=std::max<unsigned>(1,f->min_num_decode_surfaces);ci.ulNumOutputSurfaces=2;ci.ulCreationFlags=cudaVideoCreate_PreferCUVID;ci.DeinterlaceMode=cudaVideoDeinterlaceMode_Weave;
        ci.display_area.left=static_cast<short>(f->display_area.left);ci.display_area.top=static_cast<short>(f->display_area.top);ci.display_area.right=static_cast<short>(f->display_area.right);ci.display_area.bottom=static_cast<short>(f->display_area.bottom);
        ci.ulTargetWidth=width;ci.ulTargetHeight=height;ci.target_rect.right=static_cast<short>(width);ci.target_rect.bottom=static_cast<short>(height);
        auto r=createDecoder(&decoder,&ci);if(r!=CUDA_SUCCESS){callbackError=error(3,r);return 0;}surfaceW=width;surfaceH=height;streamBits=8+ci.bitDepthMinus8;return static_cast<int>(ci.ulNumDecodeSurfaces);
    }
    static int CUDAAPI picture(void* p,CUVIDPICPARAMS* f){auto& s=*static_cast<Nv*>(p);if(!s.decoder){s.callbackError=s.error(4,0,Invalid);return 0;}auto r=s.decode(s.decoder,f);if(r!=CUDA_SUCCESS){s.callbackError=s.error(4,r);return 0;}return 1;}
    static int CUDAAPI display(void* p,CUVIDPARSERDISPINFO* f){auto& s=*static_cast<Nv*>(p);try{return s.onDisplay(f);}catch(...){s.callbackError=E_OUTOFMEMORY;return 0;}}
    int onDisplay(CUVIDPARSERDISPINFO* f){
        if(!f)return 1;
        CUVIDPROCPARAMS pp{};pp.progressive_frame=f->progressive_frame;pp.top_field_first=f->top_field_first;pp.second_field=f->repeat_first_field+1;pp.unpaired_field=f->repeat_first_field<0;
        unsigned long long ptr=0;unsigned pitch=0;auto r=map(decoder,f->picture_index,&ptr,&pitch,&pp);if(r!=CUDA_SUCCESS){callbackError=error(5,r);return 0;}
        struct Unmap{Nv* s;unsigned long long p;~Unmap(){s->unmap(s->decoder,p);}} guard{this,ptr};
        if(decodeStatus){CUVIDGETDECODESTATUS status{};r=decodeStatus(decoder,f->picture_index,&status);if(r==CUDA_SUCCESS&&(status.decodeStatus==cuvidDecodeStatus_Error||status.decodeStatus==cuvidDecodeStatus_Error_Concealed)){callbackError=error(5,status.decodeStatus,Invalid);return 0;}}
        unsigned row=surfaceW*(streamBits==10?2:1);if(pitch<row){callbackError=error(5,0,Invalid);return 0;}
        raw.resize(size_t(row)*surfaceH*3/2);CUDA_MEMCPY2D c{};c.srcMemoryType=CU_MEMORYTYPE_DEVICE;c.srcDevice=ptr;c.srcPitch=pitch;c.dstMemoryType=CU_MEMORYTYPE_HOST;c.dstHost=raw.data();c.dstPitch=row;c.WidthInBytes=row;c.Height=surfaceH*3/2;
        r=copy(&c);if(r!=CUDA_SUCCESS){callbackError=error(5,r);return 0;}
        callbackError=convert(raw.data(),raw.data()+size_t(row)*surfaceH,row,row,surfaceW,surfaceH,streamBits);return SUCCEEDED(callbackError)?1:0;
    }
    HRESULT push(const uint8_t* data,unsigned size,int64_t time) override {
        ready=false;callbackError=S_OK;auto r=pushCtx(ctx);if(r!=CUDA_SUCCESS)return error(4,r);
        struct Pop{Nv* s;~Pop(){CUcontext old=nullptr;s->popCtx(&old);}} guard{this};
        CUVIDSOURCEDATAPACKET p{};p.flags=CUVID_PKT_TIMESTAMP|CUVID_PKT_ENDOFPICTURE;p.payload=data;p.payload_size=size;p.timestamp=time;
        r=parse(parser,&p);if(FAILED(callbackError))return callbackError;if(r!=CUDA_SUCCESS)return error(4,r);return ready?S_OK:S_FALSE;
    }
};
struct Intel final:Backend {
    Module vpl;ComPtr<ID3D11Device> device;mfxLoader loader=nullptr;mfxSession session=nullptr;bool initialized=false;
    decltype(&MFXUnload) unload=nullptr;decltype(&MFXClose) closeSession=nullptr;
    decltype(&MFXVideoDECODE_Close) closeDecoder=nullptr;decltype(&MFXVideoDECODE_DecodeHeader) header=nullptr;
    decltype(&MFXVideoDECODE_QueryIOSurf) query=nullptr;decltype(&MFXVideoDECODE_Init) initDecoder=nullptr;
    decltype(&MFXVideoDECODE_DecodeFrameAsync) decode=nullptr;decltype(&MFXVideoCORE_SyncOperation) sync=nullptr;
    struct Surface {mfxFrameSurface1 frame{};std::vector<uint8_t> memory;~Surface(){wipe(memory);}};
    std::vector<std::unique_ptr<Surface>> surfaces;std::vector<uint8_t> pending;mfxVideoParam param{};
    ~Intel() override {if(session){if(initialized)closeDecoder(session);closeSession(session);}if(loader)unload(loader);wipe(pending);}
    HRESULT open(IDXGIAdapter1* adapter,int codec,int,unsigned,unsigned) override {
        if(!vpl.open(L"libvpl.dll",LOAD_LIBRARY_SEARCH_APPLICATION_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32))return error(1,0,Missing);
        decltype(&MFXLoad) load=nullptr;decltype(&MFXCreateConfig) config=nullptr;decltype(&MFXSetConfigFilterProperty) filter=nullptr;
        decltype(&MFXCreateSession) create=nullptr;decltype(&MFXVideoCORE_SetHandle) setHandle=nullptr;decltype(&MFXVideoCORE_GetHandle) getHandle=nullptr;
        decltype(&MFXInit) legacy=nullptr;decltype(&MFXQueryIMPL) impl=nullptr;
#define VPL(f,n) if(!vpl.get(f,#n))return error(1,0,Missing)
        VPL(load,MFXLoad);VPL(unload,MFXUnload);VPL(config,MFXCreateConfig);VPL(filter,MFXSetConfigFilterProperty);VPL(create,MFXCreateSession);
        VPL(closeSession,MFXClose);VPL(setHandle,MFXVideoCORE_SetHandle);VPL(getHandle,MFXVideoCORE_GetHandle);VPL(impl,MFXQueryIMPL);
        VPL(closeDecoder,MFXVideoDECODE_Close);VPL(header,MFXVideoDECODE_DecodeHeader);VPL(query,MFXVideoDECODE_QueryIOSurf);VPL(initDecoder,MFXVideoDECODE_Init);VPL(decode,MFXVideoDECODE_DecodeFrameAsync);VPL(sync,MFXVideoCORE_SyncOperation);
#undef VPL
        vpl.get(legacy,"MFXInit");HRESULT hr=deviceFor(adapter,device);if(FAILED(hr))return error(2,0,hr);
        auto bind=[&](mfxSession s){
            if(setHandle(s,MFX_HANDLE_D3D11_DEVICE,device.Get())<0)return false;
            mfxHDL actual=nullptr;return getHandle(s,MFX_HANDLE_D3D11_DEVICE,&actual)>=0&&actual==device.Get();
        };
        loader=load();if(loader){
            auto set=[&](const char* key,mfxU32 value){auto c=config(loader);if(!c)return false;mfxVariant v{};v.Version.Version=MFX_VARIANT_VERSION;v.Type=MFX_VARIANT_TYPE_U32;v.Data.U32=value;return filter(c,reinterpret_cast<const mfxU8*>(key),v)>=0;};
            if(set("mfxImplDescription.Impl",MFX_IMPL_TYPE_HARDWARE)&&set("mfxImplDescription.VendorID",0x8086)&&set("mfxImplDescription.AccelerationMode",MFX_ACCEL_MODE_VIA_D3D11)){
                for(mfxU32 i=0;i<16;i++){mfxSession s=nullptr;auto st=create(loader,i,&s);if(st==MFX_ERR_NOT_FOUND)break;if(st>=0&&s){if(bind(s)){session=s;break;}closeSession(s);}}
            }
        }
        // Legacy dispatcher path is required on older Intel graphics. Never use AUTO or SOFTWARE.
        if(!session&&legacy){mfxVersion version{};version.Major=1;version.Minor=1;
            for(mfxIMPL type:{MFX_IMPL_HARDWARE,MFX_IMPL_HARDWARE2,MFX_IMPL_HARDWARE3,MFX_IMPL_HARDWARE4}){
                mfxSession s=nullptr;if(legacy(type|MFX_IMPL_VIA_D3D11,&version,&s)<0||!s)continue;mfxIMPL actual=0;
                if(impl(s,&actual)>=0&&(MFX_IMPL_BASETYPE(actual)==MFX_IMPL_HARDWARE||MFX_IMPL_BASETYPE(actual)==MFX_IMPL_HARDWARE2||MFX_IMPL_BASETYPE(actual)==MFX_IMPL_HARDWARE3||MFX_IMPL_BASETYPE(actual)==MFX_IMPL_HARDWARE4)&&bind(s)){session=s;break;}closeSession(s);
            }
        }
        if(!session)return error(2,MFX_ERR_NOT_FOUND,Missing);
        param.mfx.CodecId=codec==264?MFX_CODEC_AVC:MFX_CODEC_HEVC;return S_OK;
    }
    HRESULT push(const uint8_t* data,unsigned size,int64_t time) override {
        ready=false;if(size>MaxUnit||pending.size()>MaxUnit-size)return error(4,0,Invalid);
        pending.insert(pending.end(),data,data+size);mfxBitstream bs{};bs.Data=pending.data();bs.DataLength=bs.MaxLength=static_cast<mfxU32>(pending.size());bs.TimeStamp=time>=0?static_cast<mfxU64>(time/10000000.0*90000):static_cast<mfxU64>(MFX_TIMESTAMP_UNKNOWN);bs.DataFlag=MFX_BITSTREAM_COMPLETE_FRAME;
        if(!initialized){
            auto copy=bs;auto st=header(session,&copy,&param);if(st==MFX_ERR_MORE_DATA)return S_FALSE;if(st<0)return error(3,st,st==MFX_ERR_UNSUPPORTED?Unsupported:E_FAIL);
            auto& info=param.mfx.FrameInfo;unsigned width=info.Width,height=info.Height;int bits=info.FourCC==MFX_FOURCC_P010?10:8;
            if(!validSize(width,height)||(width&1)||(height&1)||(info.FourCC!=MFX_FOURCC_NV12&&info.FourCC!=MFX_FOURCC_P010))return error(3,0,Unsupported);
            param.IOPattern=MFX_IOPATTERN_OUT_SYSTEM_MEMORY;param.AsyncDepth=1;if(bits==10)info.Shift=1;
            mfxFrameAllocRequest req{};st=query(session,&param,&req);if(st<0)return error(3,st,st==MFX_ERR_UNSUPPORTED?Unsupported:E_FAIL);
            unsigned count=std::max<unsigned>(req.NumFrameSuggested,req.NumFrameMin);unsigned pitch=width*(bits==10?2:1);size_t bytes=size_t(pitch)*height*3/2;
            if(!count||count>64||bytes*count>512u*1024*1024)return error(3,0,Unsupported);
            for(unsigned i=0;i<count;i++){auto s=std::make_unique<Surface>();s->memory.resize(bytes);s->frame.Info=info;s->frame.Data.Pitch=static_cast<mfxU16>(pitch);s->frame.Data.Y=s->memory.data();s->frame.Data.UV=s->memory.data()+size_t(pitch)*height;surfaces.push_back(std::move(s));}
            st=initDecoder(session,&param);if(st<0||st==MFX_WRN_PARTIAL_ACCELERATION)return error(3,st,Unsupported);initialized=true;
        }
        for(unsigned tries=0;tries<100;tries++){
            mfxFrameSurface1* work=nullptr;for(auto& s:surfaces)if(!s->frame.Data.Locked){work=&s->frame;break;}if(!work)return error(4,MFX_ERR_MORE_SURFACE);
            mfxFrameSurface1* out=nullptr;mfxSyncPoint point=nullptr;auto st=decode(session,&bs,work,&out,&point);
            if(st==MFX_WRN_DEVICE_BUSY){Sleep(2);continue;}
            if(st==MFX_WRN_PARTIAL_ACCELERATION)return error(4,st,Unsupported);
            if(st<0&&st!=MFX_ERR_MORE_DATA&&st!=MFX_ERR_MORE_SURFACE)return error(4,st);
            if(point&&out){auto rc=sync(session,point,1000);if(rc!=MFX_ERR_NONE)return error(5,rc);
                if(out->Data.Corrupted)return error(5,out->Data.Corrupted,Invalid);
                auto& i=out->Info;unsigned width=i.CropW?i.CropW:i.Width,height=i.CropH?i.CropH:i.Height;int bits=i.FourCC==MFX_FOURCC_P010?10:8;unsigned bytes=bits==10?2:1,pitch=out->Data.Pitch;
                if((i.FourCC!=MFX_FOURCC_NV12&&i.FourCC!=MFX_FOURCC_P010)||pitch<unsigned(i.Width)*bytes||!out->Data.Y||!out->Data.UV||(i.CropX&1)||(i.CropY&1)||unsigned(i.CropX)+width>i.Width||unsigned(i.CropY)+height>i.Height)return error(5,0,Invalid);
                auto hr=convert(out->Data.Y+size_t(i.CropY)*pitch+i.CropX*bytes,out->Data.UV+size_t(i.CropY/2)*pitch+i.CropX*bytes,pitch,pitch,width,height,bits);if(FAILED(hr))return hr;
            }
            if(st==MFX_ERR_MORE_SURFACE)continue;
            if(st==MFX_ERR_MORE_DATA||bs.DataLength==0||!point){
                if(bs.DataOffset>pending.size()||bs.DataLength>pending.size()-bs.DataOffset)return error(4,0,Invalid);
                std::vector<uint8_t> remaining(pending.begin()+bs.DataOffset,pending.begin()+bs.DataOffset+bs.DataLength);wipe(pending);pending.swap(remaining);return ready?S_OK:S_FALSE;
            }
        }
        return error(4,0,HRESULT_FROM_WIN32(ERROR_TIMEOUT));
    }
};
struct Amd final:Backend {
    Module runtime;ComPtr<ID3D11Device> device;amf::AMFContext* context=nullptr;amf::AMFComponent* decoder=nullptr;
    ~Amd() override {if(decoder){decoder->Terminate();decoder->Release();}if(context){context->Terminate();context->Release();}}
    HRESULT open(IDXGIAdapter1* adapter,int codec,int profile,unsigned width,unsigned height) override {
        if(!validSize(width,height))return error(3,0,Invalid);
        if(!runtime.open(AMF_DLL_NAME))return error(1,0,Missing);
        AMFInit_Fn init=nullptr;AMFQueryVersion_Fn version=nullptr;if(!runtime.get(init,AMF_INIT_FUNCTION_NAME)||!runtime.get(version,AMF_QUERY_VERSION_FUNCTION_NAME))return error(1,0,Missing);
        amf_uint64 available=0;auto st=version(&available);if(st!=AMF_OK)return error(1,st);
        amf::AMFFactory* factory=nullptr;st=init(std::min<amf_uint64>(available,AMF_FULL_VERSION),&factory);if(st!=AMF_OK||!factory)return error(1,st);
        HRESULT hr=deviceFor(adapter,device);if(FAILED(hr))return error(2,0,hr);
        st=factory->CreateContext(&context);if(st!=AMF_OK||!context)return error(2,st);
        st=context->InitDX11(device.Get());if(st!=AMF_OK)return error(2,st);
        const wchar_t* id=codec==264?AMFVideoDecoderUVD_H264_AVC:profile==2?AMFVideoDecoderHW_H265_MAIN10:AMFVideoDecoderHW_H265_HEVC;
        st=factory->CreateComponent(context,id,&decoder);
        if((st!=AMF_OK||!decoder)&&profile==2&&codec==265){if(decoder){decoder->Release();decoder=nullptr;}st=factory->CreateComponent(context,AMFVideoDecoderHW_H265_HEVC,&decoder);}
        if(st!=AMF_OK||!decoder)return error(3,st,st==AMF_NOT_SUPPORTED?Unsupported:E_FAIL);
        // Preserve reorder semantics, including cameras that use B frames.
        st=decoder->Init(profile==2?amf::AMF_SURFACE_P010:amf::AMF_SURFACE_NV12,width,height);if(st!=AMF_OK)return error(3,st,st==AMF_NOT_SUPPORTED?Unsupported:E_FAIL);return S_OK;
    }
    template<class T> struct Ref{T* ptr=nullptr;~Ref(){if(ptr)ptr->Release();}};
    HRESULT output(){
        Ref<amf::AMFData> data;auto st=decoder->QueryOutput(&data.ptr);if(st!=AMF_OK&&st!=AMF_REPEAT&&st!=AMF_NEED_MORE_INPUT)return error(5,st);if(!data.ptr)return S_FALSE;
        Ref<amf::AMFSurface> surface;st=data.ptr->QueryInterface(amf::AMFSurface::IID(),reinterpret_cast<void**>(&surface.ptr));if(st!=AMF_OK||!surface.ptr)return error(5,st);
        // The component is the actual hardware UVD/VCN decoder, not a CPU fallback.
        st=surface.ptr->Convert(amf::AMF_MEMORY_HOST);if(st!=AMF_OK)return error(5,st);
        auto format=surface.ptr->GetFormat();if(format!=amf::AMF_SURFACE_NV12&&format!=amf::AMF_SURFACE_P010)return error(6,0,Unsupported);
        auto y=surface.ptr->GetPlaneAt(0),uv=surface.ptr->GetPlaneAt(1);if(!y||!uv)return error(6,0,Invalid);
        unsigned width=y->GetWidth(),height=y->GetHeight(),pitch=y->GetHPitch(),uvpitch=uv->GetHPitch();int bits=format==amf::AMF_SURFACE_P010?10:8;unsigned bytes=bits==10?2:1;
        if(!validSize(width,height)||y->GetHPitch()<=0||uv->GetHPitch()<=0||y->GetVPitch()<=0||uv->GetVPitch()<=0||y->GetOffsetX()<0||y->GetOffsetY()<0||uv->GetOffsetX()<0||uv->GetOffsetY()<0||!y->GetNative()||!uv->GetNative()||
            (uint64_t(y->GetOffsetX())+width)*bytes>pitch||uint64_t(y->GetOffsetY())+height>unsigned(y->GetVPitch())||
            uint64_t(uv->GetOffsetX())*bytes*2+width*bytes>uvpitch||uint64_t(uv->GetOffsetY())+height/2>unsigned(uv->GetVPitch()))return error(6,0,Invalid);
        auto yp=static_cast<const uint8_t*>(y->GetNative())+size_t(y->GetOffsetY())*pitch+y->GetOffsetX()*bytes;
        auto up=static_cast<const uint8_t*>(uv->GetNative())+size_t(uv->GetOffsetY())*uvpitch+uv->GetOffsetX()*bytes*2;
        return convert(yp,up,pitch,uvpitch,width,height,bits);
    }
    HRESULT push(const uint8_t* data,unsigned size,int64_t time) override {
        ready=false;Ref<amf::AMFBuffer> buffer;auto st=context->AllocBuffer(amf::AMF_MEMORY_HOST,size,&buffer.ptr);if(st!=AMF_OK||!buffer.ptr)return error(4,st);
        memcpy(buffer.ptr->GetNative(),data,size);buffer.ptr->SetPts(time);
        bool submitted=false;for(unsigned i=0;i<100;i++){
            st=decoder->SubmitInput(buffer.ptr);if(st==AMF_OK||st==AMF_NEED_MORE_INPUT){submitted=true;break;}if(st!=AMF_INPUT_FULL)return error(4,st);auto hr=output();if(FAILED(hr))return hr;Sleep(2);
        }
        if(!submitted)return error(4,st,HRESULT_FROM_WIN32(ERROR_TIMEOUT));
        for(unsigned i=0;i<40;i++){auto hr=output();if(FAILED(hr))return hr;if(hr==S_OK){if(i>=4)break;}else if(ready)break;else Sleep(1);}
        return ready?S_OK:S_FALSE;
    }
};
} // namespace
extern "C" __declspec(dllexport) HRESULT __cdecl KpcVendorOpen(int kind,int adapterIndex,int codec,int profile,unsigned width,unsigned height,void** handle,int* stage,int* code) noexcept {
    if(!handle||!stage||!code)return E_POINTER;*handle=nullptr;*stage=1;*code=0;
    if(kind<1||kind>3||adapterIndex<0||adapterIndex>=32||(codec!=264&&codec!=265))return E_INVALIDARG;
    try {
        ComPtr<IDXGIFactory1> factory;auto hr=CreateDXGIFactory1(IID_PPV_ARGS(&factory));if(FAILED(hr))return hr;
        ComPtr<IDXGIAdapter1> adapter;hr=factory->EnumAdapters1(adapterIndex,&adapter);if(FAILED(hr))return hr;
        DXGI_ADAPTER_DESC1 desc{};hr=adapter->GetDesc1(&desc);if(FAILED(hr))return hr;
        if(desc.VendorId!=(kind==1?0x10de:kind==2?0x8086:0x1002)||(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE))return Unsupported;
        std::unique_ptr<Backend> b;if(kind==1)b=std::make_unique<Nv>();else if(kind==2)b=std::make_unique<Intel>();else b=std::make_unique<Amd>();
        hr=b->open(adapter.Get(),codec,profile,width,height);*stage=b->stage;*code=b->code;if(FAILED(hr))return hr;*handle=b.release();return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}catch(...){return E_FAIL;}
}
extern "C" __declspec(dllexport) HRESULT __cdecl KpcVendorPush(void* handle,const uint8_t* data,unsigned size,int64_t time,const uint8_t** pixels,unsigned* width,unsigned* height,int* depth,int* stage,int* code) noexcept {
    if(!handle||!data||!size||size>MaxUnit||!pixels||!width||!height||!depth||!stage||!code)return E_INVALIDARG;
    *pixels=nullptr;*width=*height=0;*depth=0;auto b=static_cast<Backend*>(handle);
    try{auto hr=b->push(data,size,time);*stage=b->stage;*code=b->code;if(hr==S_OK&&b->ready){*pixels=b->bgra.data();*width=b->w;*height=b->h;*depth=b->depth;return S_OK;}return FAILED(hr)?hr:S_FALSE;}catch(const std::bad_alloc&){return E_OUTOFMEMORY;}catch(...){return E_FAIL;}
}
extern "C" __declspec(dllexport) void __cdecl KpcVendorClose(void* handle) noexcept {delete static_cast<Backend*>(handle);}
