// KeePassXC Card Print: Windows XPS Object Model printing bridge.
// No PDF intermediary, bitmap page, or RAW WritePrinter path.
// This bridge is compiled with the Windows SDK/MSVC by tools/build.ps1.
// Copyright (c) KeePassXC Card Print contributors.
#define UNICODE 1
#define _UNICODE 1
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#define _WIN32_WINNT 0x0A00
#include <windows.h>
#include <documenttarget.h>
#include <xpsobjectmodel.h>
#include <xpsobjectmodel_1.h>
#include <prntvpt.h>
#include <wrl/client.h>
#include <objidl.h>
#include <cstdint>
#include <cstring>
#include <string>
#include <vector>
#include <memory>
#include <algorithm>
#include <cmath>
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "prntvpt.lib")
#pragma comment(lib, "uuid.lib")

using Microsoft::WRL::ComPtr;

// documenttarget.h declares ID_DOCUMENTPACKAGETARGET_MSXPS as an external GUID.
// Depending on the Windows SDK build environment it has no linked definition,
// yielding LNK2019 even with uuid.lib. Keep a TU-local equivalent instead.
// Microsoft-defined MS-XPS target type: 9cae40a8-ded1-41c9-a9fd-d735ef33aeda.
static constexpr GUID kKpcMsXpsTargetType =
{ 0x9cae40a8, 0xded1, 0x41c9, { 0xa9, 0xfd, 0xd7, 0x35, 0xef, 0x33, 0xae, 0xda } };

struct KpcImage {const wchar_t* uri; const unsigned char* bytes; uint32_t byteCount;};
struct KpcPage {const unsigned char* markup; uint32_t markupBytes; double widthMm, heightMm;
                uint32_t imageCount; const KpcImage* images; uint32_t fontCount; const KpcImage* fonts;};

static HRESULT MemoryStream(const unsigned char* bytes,uint32_t size,ComPtr<IStream>& stream)
{
    if(size && !bytes)return E_INVALIDARG;
    HRESULT hr=CreateStreamOnHGlobal(nullptr,TRUE,&stream);
    if(FAILED(hr))return hr;
    if(size)
    {
        ULONG written=0;
        hr=stream->Write(bytes,size,&written);
        if(FAILED(hr))return hr;
        if(written!=size)return STG_E_WRITEFAULT;
    }
    LARGE_INTEGER start{};
    return stream->Seek(start,STREAM_SEEK_SET,nullptr);
}

static HRESULT PreparePrintTicket(const wchar_t* printer,const unsigned char* driverMode,uint32_t driverBytes,
                                  int duplex,ComPtr<IStream>& ticket)
{
    if(!printer || !driverMode || driverBytes < sizeof(DEVMODEW) || driverBytes>65536)return E_INVALIDARG;
    std::vector<unsigned char> bytes(driverMode,driverMode+driverBytes);
    auto* dm=reinterpret_cast<DEVMODEW*>(bytes.data());
    if(dm->dmSize < sizeof(DEVMODEW) || static_cast<uint32_t>(dm->dmSize)+dm->dmDriverExtra>driverBytes)return E_INVALIDARG;
    if(duplex>=0)
    {
        dm->dmFields |= DM_DUPLEX;
        dm->dmDuplex=duplex==0?DMDUP_SIMPLEX:duplex==1?DMDUP_VERTICAL:DMDUP_HORIZONTAL;
    }
    HPTPROVIDER provider=nullptr;
    HRESULT hr=PTOpenProvider(printer,1,&provider);
    if(FAILED(hr))return hr;
    hr=CreateStreamOnHGlobal(nullptr,TRUE,&ticket);
    if(SUCCEEDED(hr))
        hr=PTConvertDevModeToPrintTicket(provider,
            static_cast<ULONG>(dm->dmSize+dm->dmDriverExtra),
            reinterpret_cast<DEVMODEW*>(bytes.data()),kPTJobScope,ticket.Get());
    PTCloseProvider(provider);
    if(FAILED(hr))return hr;
    LARGE_INTEGER start{};
    return ticket->Seek(start,STREAM_SEEK_SET,nullptr);
}

static HRESULT BuildPage(IXpsOMObjectFactory* factory,const KpcPage& src,uint32_t sequence,ComPtr<IXpsOMPage>& page)
{
    if(!factory || !src.markup || !src.markupBytes || (src.imageCount && !src.images) || src.imageCount>1024 ||
       src.markupBytes>100*1024*1024 || src.fontCount>128 ||
       (src.fontCount && !src.fonts) || !(src.widthMm>0 && src.widthMm<=594) ||
       !(src.heightMm>0 && src.heightMm<=594))return E_INVALIDARG;

    ComPtr<IXpsOMPartResources> resources;
    HRESULT hr=factory->CreatePartResources(&resources);
    if(FAILED(hr))return hr;
    ComPtr<IXpsOMImageResourceCollection> imageCollection;
    hr=resources->GetImageResources(&imageCollection);
    if(FAILED(hr))return hr;
    // Image streams are deliberately per page; no references to decrypted DB memory
    // survive after this COM invocation returns.
    for(uint32_t i=0;i<src.imageCount;i++)
    {
        const auto& image=src.images[i];
        if(!image.uri || !image.byteCount || image.byteCount>128*1024*1024)return E_INVALIDARG;
        ComPtr<IOpcPartUri> imageUri;
        hr=factory->CreatePartUri(image.uri,&imageUri);
        if(FAILED(hr))return hr;
        ComPtr<IStream> imageStream;
        hr=MemoryStream(image.bytes,image.byteCount,imageStream);
        if(FAILED(hr))return hr;
        ComPtr<IXpsOMImageResource> imageResource;
        hr=factory->CreateImageResource(imageStream.Get(),XPS_IMAGE_TYPE_PNG,imageUri.Get(),&imageResource);
        if(FAILED(hr))return hr;
        hr=imageCollection->Append(imageResource.Get());
        if(FAILED(hr))return hr;
    }
    ComPtr<IXpsOMFontResourceCollection> fontCollection;
    hr=resources->GetFontResources(&fontCollection);
    if(FAILED(hr))return hr;
    for(uint32_t i=0;i<src.fontCount;i++)
    {
        const auto& font=src.fonts[i];
        if(!font.uri || !font.bytes || font.byteCount<64 || font.byteCount>32*1024*1024)return E_INVALIDARG;
        ComPtr<IOpcPartUri> fontUri;
        hr=factory->CreatePartUri(font.uri,&fontUri);
        if(FAILED(hr))return hr;
        ComPtr<IStream> fontStream;
        hr=MemoryStream(font.bytes,font.byteCount,fontStream);
        if(FAILED(hr))return hr;
        ComPtr<IXpsOMFontResource> fontResource;
        hr=factory->CreateFontResource(fontStream.Get(),XPS_FONT_EMBEDDING_NORMAL,
                                       fontUri.Get(),FALSE,&fontResource);
        if(FAILED(hr))return hr;
        hr=fontCollection->Append(fontResource.Get());
        if(FAILED(hr))return hr;
    }
    ComPtr<IStream> pageStream;
    hr=MemoryStream(src.markup,src.markupBytes,pageStream);
    if(FAILED(hr))return hr;
    const std::wstring pageName=L"/Documents/1/Pages/"+std::to_wstring(sequence)+L".fpage";
    ComPtr<IOpcPartUri> pageUri;
    hr=factory->CreatePartUri(pageName.c_str(),&pageUri);
    if(FAILED(hr))return hr;
    return factory->CreatePageFromStream(pageStream.Get(),pageUri.Get(),resources.Get(),FALSE,&page);
}

// Statically instantiate an OpenType variable font using the HarfBuzz native
// library already shipped with HarfBuzzSharp. The API is discovered dynamically
// because some package builds omit hb-subset. No Python or extra font tools are
// needed; missing exports return E_NOTIMPL and XPS retains vector outlines.
struct KpcFontAxis {uint32_t tag; float value;};
extern "C" __declspec(dllexport) void __cdecl KpcFreeFont(void* ptr)
{ if(ptr)CoTaskMemFree(ptr); }

extern "C" __declspec(dllexport) HRESULT __cdecl KpcFreezeFont(
    const unsigned char* data,uint32_t dataSize,int faceIndex,
    const KpcFontAxis* axes,uint32_t axisCount,void** output,uint32_t* outputSize)
{
    if(!output || !outputSize)return E_POINTER;
    *output=nullptr;*outputSize=0;
    if(!data || dataSize<64 || dataSize>32*1024*1024 || faceIndex<0 ||
       faceIndex>1024 || !axes || axisCount==0 || axisCount>32)return E_INVALIDARG;
    // Application-directory loading rules out a DLL planted on PATH.
    HMODULE dll=LoadLibraryExW(L"libHarfBuzzSharp.dll",nullptr,LOAD_LIBRARY_SEARCH_APPLICATION_DIR);
    if(!dll)return E_NOTIMPL;
    auto api=[&](const char* name){return GetProcAddress(dll,name);};
    using Ptr=void*;
    auto blobCreate=reinterpret_cast<Ptr(__cdecl*)(const char*,uint32_t,uint32_t,Ptr,Ptr)>(api("hb_blob_create"));
    auto blobDestroy=reinterpret_cast<void(__cdecl*)(Ptr)>(api("hb_blob_destroy"));
    auto faceCreate=reinterpret_cast<Ptr(__cdecl*)(Ptr,uint32_t)>(api("hb_face_create"));
    auto faceDestroy=reinterpret_cast<void(__cdecl*)(Ptr)>(api("hb_face_destroy"));
    auto inputCreate=reinterpret_cast<Ptr(__cdecl*)()>(api("hb_subset_input_create_or_fail"));
    auto inputDestroy=reinterpret_cast<void(__cdecl*)(Ptr)>(api("hb_subset_input_destroy"));
    auto keepEverything=reinterpret_cast<void(__cdecl*)(Ptr)>(api("hb_subset_input_keep_everything"));
    auto setFlags=reinterpret_cast<void(__cdecl*)(Ptr,uint32_t)>(api("hb_subset_input_set_flags"));
    auto pinDefaults=reinterpret_cast<int(__cdecl*)(Ptr,Ptr)>(api("hb_subset_input_pin_all_axes_to_default"));
    auto pinLocation=reinterpret_cast<int(__cdecl*)(Ptr,Ptr,uint32_t,float)>(api("hb_subset_input_pin_axis_location"));
    auto subset=reinterpret_cast<Ptr(__cdecl*)(Ptr,Ptr)>(api("hb_subset_or_fail"));
    auto faceBlob=reinterpret_cast<Ptr(__cdecl*)(Ptr)>(api("hb_face_reference_blob"));
    auto blobData=reinterpret_cast<const char*(__cdecl*)(Ptr,uint32_t*)>(api("hb_blob_get_data"));
    if(!blobCreate || !blobDestroy || !faceCreate || !faceDestroy || !inputCreate ||
       !inputDestroy || !keepEverything || !setFlags || !pinDefaults || !pinLocation ||
       !subset || !faceBlob || !blobData){FreeLibrary(dll);return E_NOTIMPL;}
    // HARFBUZZ hb_memory_mode_t=0 duplicates the input bytes.
    Ptr blob=blobCreate(reinterpret_cast<const char*>(data),dataSize,0,nullptr,nullptr);
    Ptr face=blob?faceCreate(blob,static_cast<uint32_t>(faceIndex)):nullptr;
    Ptr input=face?inputCreate():nullptr;
    Ptr staticFace=nullptr, staticBlob=nullptr;
    HRESULT hr=E_FAIL;
    if(input)
    {
        keepEverything(input);
        // Preserve glyph IDs: the XPS indices come from ShapedGlyphRun.
        // 0xE2: retain GIDs, notdef outline, glyph names, unrecognized tables.
        setFlags(input,0xE2u);
        bool ok=pinDefaults(input,face)!=0;
        for(uint32_t i=0;i<axisCount && ok;i++)
            ok=std::isfinite(axes[i].value) &&
               pinLocation(input,face,axes[i].tag,axes[i].value)!=0;
        if(ok)staticFace=subset(face,input);
        if(staticFace)
        {
            staticBlob=faceBlob(staticFace);
            uint32_t length=0;
            const char* bytes=staticBlob?blobData(staticBlob,&length):nullptr;
            if(bytes && length>=64 && length<=32*1024*1024)
            {
                void* memory=CoTaskMemAlloc(length);
                if(memory)
                {
                    memcpy(memory,bytes,length);
                    *output=memory;*outputSize=length;hr=S_OK;
                }
                else hr=E_OUTOFMEMORY;
            }
        }
    }
    if(staticBlob)blobDestroy(staticBlob);
    if(staticFace)faceDestroy(staticFace);
    if(input)inputDestroy(input);
    if(face)faceDestroy(face);
    if(blob)blobDestroy(blob);
    FreeLibrary(dll);
    return hr;
}

// submitted=1 means the spooler MAY already have received pages. Callers must
// never automatically retry a failed job after this point, even on another backend.
extern "C" __declspec(dllexport) HRESULT __cdecl KpcPrintXps(
    const wchar_t* printer,const unsigned char* driverMode,uint32_t driverBytes,
    int duplex,const KpcPage* pages,uint32_t pageCount,int* submitted)
{
    if(!submitted)return E_POINTER;
    *submitted=0;
    if(!printer || !*printer || !pages || pageCount==0 || pageCount>8192)return E_INVALIDARG;
    HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    const bool initialized=SUCCEEDED(hr);
    if(FAILED(hr) && hr!=RPC_E_CHANGED_MODE)return hr;
    auto impl=[&]()->HRESULT
    {
        ComPtr<IStream> ticket;
        HRESULT r=PreparePrintTicket(printer,driverMode,driverBytes,duplex,ticket);
        if(FAILED(r))return r;
        ComPtr<IPrintDocumentPackageTargetFactory> targetFactory;
        r=CoCreateInstance(__uuidof(PrintDocumentPackageTargetFactory),nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&targetFactory));
        if(FAILED(r))return r;
        ComPtr<IPrintDocumentPackageTarget> packageTarget;
        // The target may allocate an empty spool job. No printed page is ever
        // sent before the first AddPage; only then does automatic retry become
        // unsafe. A failed empty job is never counted as printed.
        r=targetFactory->CreateDocumentPackageTargetForPrintJob(printer,L"KeePassXC Card Print",nullptr,ticket.Get(),&packageTarget);
        if(FAILED(r))return r;
        ComPtr<IXpsDocumentPackageTarget> xpsTarget;
        r=packageTarget->GetPackageTarget(kKpcMsXpsTargetType,__uuidof(IXpsDocumentPackageTarget),
                                          reinterpret_cast<void**>(xpsTarget.GetAddressOf()));
        if(FAILED(r)){packageTarget->Cancel();return r;}
        ComPtr<IXpsOMObjectFactory> factory;
        r=xpsTarget->GetXpsOMFactory(&factory);
        if(FAILED(r)){packageTarget->Cancel();return r;}
        ComPtr<IOpcPartUri> seq,discard,doc;
        r=factory->CreatePartUri(L"/FixedDocumentSequence.fdseq",&seq);
        if(SUCCEEDED(r))r=factory->CreatePartUri(L"/DiscardControl.discard",&discard);
        if(SUCCEEDED(r))r=factory->CreatePartUri(L"/Documents/1/FixedDocument.fdoc",&doc);
        if(FAILED(r)){packageTarget->Cancel();return r;}
        ComPtr<IXpsOMPackageWriter> writer;
        r=xpsTarget->GetXpsOMPackageWriter(seq.Get(),discard.Get(),&writer);
        if(SUCCEEDED(r))r=writer->StartNewDocument(doc.Get(),nullptr,nullptr,nullptr,nullptr);
        if(FAILED(r)){packageTarget->Cancel();return r;}
        for(uint32_t i=0;i<pageCount;i++)
        {
            ComPtr<IXpsOMPage> page;
            r=BuildPage(factory.Get(),pages[i],i+1,page);
            if(FAILED(r)){packageTarget->Cancel();return r;}
            const XPS_SIZE size{static_cast<float>(pages[i].widthMm*96.0/25.4),
                                static_cast<float>(pages[i].heightMm*96.0/25.4)};
            *submitted=1; // Windows may receive part of this page even on failure.
            r=writer->AddPage(page.Get(),&size,nullptr,nullptr,nullptr,nullptr);
            if(FAILED(r)){packageTarget->Cancel();return r;}
        }
        r=writer->Close();
        if(FAILED(r))packageTarget->Cancel();
        return r;
    };
    hr=impl();
    if(initialized)CoUninitialize();
    return hr;
}

// Printer-free Windows SDK contract test. This executes the EXACT XPS OM page
// parser used by printing and rejects invalid XPS geometry before release.
extern "C" __declspec(dllexport) HRESULT __cdecl KpcValidateXps(const KpcPage* pages,uint32_t pageCount)
{
    if(!pages || pageCount==0 || pageCount>256)return E_INVALIDARG;
    HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    bool initialized=SUCCEEDED(hr);
    if(FAILED(hr)&&hr!=RPC_E_CHANGED_MODE)return hr;
    auto test=[&]()->HRESULT
    {
        ComPtr<IXpsOMObjectFactory> factory;
        HRESULT r=CoCreateInstance(CLSID_XpsOMObjectFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory));
        if(FAILED(r))return r;
        for(uint32_t i=0;i<pageCount;i++)
        {
            ComPtr<IXpsOMPage> page;
            r=BuildPage(factory.Get(),pages[i],i+1,page);
            if(FAILED(r))return r;
            ComPtr<IXpsOMVisualCollection> visuals;
            r=page->GetVisuals(&visuals);
            if(FAILED(r))return r;
            UINT32 size=0;
            r=visuals->GetCount(&size);
            if(FAILED(r)||size==0)return FAILED(r)?r:E_FAIL;
        }
        return S_OK;
    };
    hr=test();
    if(initialized)CoUninitialize();
    return hr;
}
