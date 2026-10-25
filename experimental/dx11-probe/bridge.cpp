// Uses the public GOverlay IPC protocol and headers under its upstream licence.
// See ../goverlay/README.md. This adapter is experimental, synthetic data only.
#include <Windows.h>
#include <filesystem>
#include <memory>
#include <optional>
#include <vector>
#include <sstream>
#include <stdexcept>
#include "ipc/tinyipc.h"
#include "message/gmessage.hpp"
#include "bridge.h"

namespace {
constexpr int PanelWidth=600, PanelHeight=160;
constexpr char HostName[]="n_overlay_1a1y2o8l0b";
class Bridge final : public ProbeBridge, public IIpcHost {
    IIpcHostCenter* center{};
    IIpcLink* link{};
    HANDLE mutex{}, mapping{};
    void* memory{};
    HMODULE module{};
    std::string mutexName, memoryName;
    bool enabled=true, ipcConnected=false, hookReported=false, failed=false;
    unsigned updates=0;

    template<class T> void send(const T& message) {
        if (!link) return;
        overlay::OverlayIpc packet;
        packet.type=message.msgType(); packet.message=message.toJson().dump();
        if (!center->sendMessage(link,0,0,&packet)) failed=true;
    }
    overlay::Window panel() const {
        overlay::Window w;
        w.windowId=1; w.nativeHandle=0; w.name="Rift synthetic BGRA panel";
        w.transparent=true; w.resizable=false;
        w.maxWidth=w.minWidth=PanelWidth; w.maxHeight=w.minHeight=PanelHeight;
        w.bufferName=memoryName; w.rect={40,180,PanelWidth,PanelHeight};
        return w;
    }
    void onClientConnect(IIpcLink* client) override {
        // This probe only publishes pixels to its own loaded renderer.
        if (client->remoteIdentity()==GetCurrentProcessId()) link=client;
    }
    void onClientClose(IIpcLink* client) override {
        if (client==link) { link=nullptr; ipcConnected=false; }
    }
    void onMessage(IIpcLink* client,int,int,const std::string& data) override {
        if (client!=link) return;
        try {
            overlay::OverlayIpc packet; packet.upack(data);
            if (packet.type=="game.process") {
                overlay::OverlayInit init;
                init.processEnabled=enabled; init.shareMemMutex=mutexName;
                init.windows.push_back(panel()); send(init);
                ipcConnected=true;
            } else if (packet.type=="graphics.window") {
                auto state=overlay::json::parse(packet.message);
                hookReported=state.value("hooked",false);
            }
        } catch (...) { failed=true; }
    }
public:
    Bridge() = default;
    void start() {
        if (FindWindowA("STATIC",HostName))
            throw std::runtime_error("Another GOverlay host is running. Close it before starting the probe.");
        auto suffix=std::to_string(GetCurrentProcessId())+"-"+std::to_string(GetTickCount64());
        mutexName="Local\\RiftProbeMutex-"+suffix;
        memoryName="Local\\RiftProbePixels-"+suffix;
        mutex=CreateMutexA(nullptr,FALSE,mutexName.c_str());
        if (!mutex) throw std::runtime_error("Cannot create pixel mutex");
        mapping=CreateFileMappingA(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,
            sizeof(overlay::ShareMemFrameBuffer)+PanelWidth*PanelHeight*4,memoryName.c_str());
        if (!mapping) throw std::runtime_error("Cannot create pixel mapping");
        memory=MapViewOfFile(mapping,FILE_MAP_WRITE,0,0,0);
        if (!memory) throw std::runtime_error("Cannot map pixels");
        update(0);
        center=createIpcHostCenter(); center->init(HostName,this);
        wchar_t exe[32768]{};
        auto length=GetModuleFileNameW(nullptr,exe,32768);
        if (!length || length>=32768) throw std::runtime_error("Cannot resolve executable path");
        auto path=std::filesystem::path(exe).parent_path()/L"n_overlay.x64.dll";
        // Ordinary self-load only: no process enumeration, injection helper or remote PID.
        module=LoadLibraryExW(path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!module) throw std::runtime_error("GOverlay DLL could not load; Win32="+std::to_string(GetLastError()));
    }
    ~Bridge() override {
        if (center) destroyIpcHostCenter(center);
        if (memory) UnmapViewOfFile(memory);
        if (mapping) CloseHandle(mapping);
        if (mutex) CloseHandle(mutex);
        // Upstream pins its DLL and owns worker threads. Do not FreeLibrary during
        // presentation; the process exit exercises its DLL_PROCESS_DETACH path.
    }
    void update(unsigned seconds) override {
        BITMAPINFO info{}; info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth=PanelWidth; info.bmiHeader.biHeight=-PanelHeight;
        info.bmiHeader.biPlanes=1; info.bmiHeader.biBitCount=32; info.bmiHeader.biCompression=BI_RGB;
        void* pixels{};
        HDC dc=CreateCompatibleDC(nullptr);
        HBITMAP bitmap=CreateDIBSection(dc,&info,DIB_RGB_COLORS,&pixels,nullptr,0);
        if (!dc || !bitmap || !pixels) {
            if (bitmap) DeleteObject(bitmap); if (dc) DeleteDC(dc);
            throw std::runtime_error("Cannot create synthetic frame");
        }
        auto old=SelectObject(dc,bitmap);
        auto words=static_cast<unsigned*>(pixels);
        std::fill_n(words,PanelWidth*PanelHeight,0xff164a38u);
        SetBkMode(dc,TRANSPARENT); SetTextColor(dc,RGB(230,255,240));
        RECT rect{20,20,PanelWidth-20,PanelHeight-20};
        auto label=L"PANNEAU GOVERLAY — pixels fictifs\nMise à jour : "+std::to_wstring(seconds)+
            L" s\nEspace : masquer / afficher\nPowered by GOverlay (lien dans l'en-tête du banc)";
        DrawTextW(dc,label.c_str(),-1,&rect,DT_LEFT|DT_TOP|DT_NOPREFIX);
        GdiFlush();
        for (int i=0;i<PanelWidth*PanelHeight;++i) words[i]|=0xff000000u;
        DWORD wait=WaitForSingleObject(mutex,1000);
        if (wait==WAIT_OBJECT_0 || wait==WAIT_ABANDONED) {
            auto header=static_cast<overlay::ShareMemFrameBuffer*>(memory);
            header->width=PanelWidth; header->height=PanelHeight;
            memcpy(header+1,pixels,PanelWidth*PanelHeight*4);
            ReleaseMutex(mutex); ++updates;
        } else failed=true;
        SelectObject(dc,old); DeleteObject(bitmap); DeleteDC(dc);
        overlay::WindowFrameBuffer notification; notification.windowId=1; send(notification);
    }
    void toggle() override {
        // Called from WndProc: contain any allocation/protocol failure here.
        try { enabled=!enabled; overlay::OverlayEnable message; message.processEnabled=enabled; send(message); }
        catch (...) { failed=true; }
    }
    std::wstring status() const override {
        return std::wstring(L"GOverlay : IPC ")+(ipcConnected?L"connecté":L"en attente")+
            L" | hook signalé : "+(hookReported?L"oui":L"non")+L" | panneau : "+(enabled?L"actif":L"masqué")+
            L" | trames : "+std::to_wstring(updates)+(failed?L" | ERREUR IPC/pixels":L"");
    }
    bool connected() const override { return ipcConnected && !failed; }
};
}
std::unique_ptr<ProbeBridge> startBridge() {
    auto bridge=std::make_unique<Bridge>(); bridge->start(); return bridge;
}
