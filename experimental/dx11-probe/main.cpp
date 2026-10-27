#include <Windows.h>
#include <windowsx.h>
#include <shellapi.h>
#include <d3d11.h>
#include <dxgi.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
#include "bridge.h"

using Microsoft::WRL::ComPtr;
using Clock = std::chrono::steady_clock;
static void check(HRESULT hr, const char* operation) {
    if (FAILED(hr)) {
        std::ostringstream message;
        message << operation << " HRESULT=0x" << std::hex << static_cast<unsigned>(hr);
        throw std::runtime_error(message.str());
    }
}

class Probe {
    HWND window{};
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGISwapChain> chain;
    ComPtr<ID3D11RenderTargetView> view;
    ComPtr<ID2D1Factory> d2d;
    ComPtr<ID2D1RenderTarget> canvas;
    ComPtr<ID2D1SolidColorBrush> brush;
    ComPtr<IDWriteFactory> write;
    ComPtr<IDWriteTextFormat> font;
    std::unique_ptr<ProbeBridge> bridge;
    std::ofstream log;
    bool minimized = false, resizePending = false, running = true, smoke = false;
    bool togglePending = false, recreatePending = false;
    UINT width = 1100, height = 720;
    unsigned frames = 0, step = 0;
    Clock::time_point start = Clock::now(), previous = start, sample = start;
    std::vector<double> durations;
    double p95 = 0;
    bool lastFullscreen = false;
    std::wstring logPath;

    void event(const std::string& text) {
        log << std::chrono::duration_cast<std::chrono::milliseconds>(Clock::now()-start).count()
            << " " << text << std::endl;
    }
    void releaseTarget() {
        brush.Reset(); canvas.Reset(); view.Reset();
        context->OMSetRenderTargets(0, nullptr, nullptr);
        context->ClearState(); context->Flush();
    }
    void target() {
        ComPtr<ID3D11Texture2D> texture;
        check(chain->GetBuffer(0, IID_PPV_ARGS(&texture)), "GetBuffer");
        check(device->CreateRenderTargetView(texture.Get(), nullptr, &view), "CreateRenderTargetView");
        ComPtr<IDXGISurface> surface;
        check(texture.As(&surface), "Surface");
        auto properties = D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_DEFAULT,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_IGNORE), 96, 96);
        check(d2d->CreateDxgiSurfaceRenderTarget(surface.Get(), &properties, &canvas), "D2D target");
        check(canvas->CreateSolidColorBrush(D2D1::ColorF(0.8f,0.95f,0.9f), &brush), "Brush");
    }
    void makeChain() {
        ComPtr<IDXGIDevice> dxgi;
        ComPtr<IDXGIAdapter> adapter;
        ComPtr<IDXGIFactory> factory;
        check(device.As(&dxgi), "DXGI device");
        check(dxgi->GetAdapter(&adapter), "Adapter");
        check(adapter->GetParent(IID_PPV_ARGS(&factory)), "Factory");
        RECT rect{}; GetClientRect(window, &rect);
        width = (std::max)(1L, rect.right); height = (std::max)(1L, rect.bottom);
        DXGI_SWAP_CHAIN_DESC desc{};
        desc.BufferDesc.Width = width; desc.BufferDesc.Height = height;
        desc.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count = 1; desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.BufferCount = smoke ? 1 : 2; desc.OutputWindow = window; desc.Windowed = TRUE;
        // Preserve the presented image for smoke-test GPU readback after the hook.
        desc.SwapEffect = smoke ? DXGI_SWAP_EFFECT_SEQUENTIAL : DXGI_SWAP_EFFECT_DISCARD;
        desc.Flags = DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;
        check(factory->CreateSwapChain(device.Get(), &desc, &chain), "CreateSwapChain");
        check(factory->MakeWindowAssociation(window, DXGI_MWA_NO_ALT_ENTER), "WindowAssociation");
        target(); event("swap_chain_created");
    }
    void resize() {
        resizePending = false;
        if (minimized) return;
        RECT rect{}; GetClientRect(window, &rect);
        width = (std::max)(1L, rect.right); height = (std::max)(1L, rect.bottom);
        releaseTarget();
        check(chain->ResizeBuffers(0, width, height, DXGI_FORMAT_UNKNOWN,
            DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH), "ResizeBuffers");
        target(); event("resize " + std::to_string(width) + "x" + std::to_string(height));
    }
    void fullscreen() {
        BOOL active = FALSE;
        check(chain->GetFullscreenState(&active, nullptr), "GetFullscreenState");
        HRESULT hr = chain->SetFullscreenState(!active, nullptr);
        event("fullscreen_request hr=" + std::to_string(static_cast<unsigned>(hr)));
        // Fullscreen may be unavailable in Remote Desktop or without display ownership.
        // Report the actual DXGI state, never silently relabel borderless as exclusive.
        resizePending = true;
    }
    void recreate() {
        check(chain->SetFullscreenState(FALSE, nullptr), "Leave fullscreen before recreation");
        releaseTarget(); chain.Reset(); makeChain(); resizePending = true;
        event("swap_chain_recreated_windowed");
    }
    void readbackCheck() {
        // Verify an actual pixel produced by the D3D clear, away from text/overlay.
        ComPtr<ID3D11Texture2D> back, staging;
        check(chain->GetBuffer(0, IID_PPV_ARGS(&back)), "Readback buffer");
        D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
        desc.Usage = D3D11_USAGE_STAGING; desc.BindFlags = 0;
        desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ; desc.MiscFlags = 0;
        check(device->CreateTexture2D(&desc, nullptr, &staging), "Staging");
        context->CopyResource(staging.Get(), back.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        check(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped), "Map");
        auto pixel = static_cast<const unsigned char*>(mapped.pData) +
            (desc.Height-1)*mapped.RowPitch + (desc.Width-1)*4;
        const bool good = pixel[0] >= 28 && pixel[0] <= 33 && pixel[1] >= 18 && pixel[1] <= 23;
        context->Unmap(staging.Get(), 0);
        if (!good) throw std::runtime_error("Rendered background readback mismatch");
        event("readback_ok");
    }
    void overlayReadbackCheck() {
        ComPtr<ID3D11Texture2D> back, staging;
        check(chain->GetBuffer(0, IID_PPV_ARGS(&back)), "Overlay readback buffer");
        D3D11_TEXTURE2D_DESC desc{}; back->GetDesc(&desc);
        desc.Usage=D3D11_USAGE_STAGING; desc.BindFlags=0;
        desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ; desc.MiscFlags=0;
        check(device->CreateTexture2D(&desc,nullptr,&staging), "Overlay staging");
        context->CopyResource(staging.Get(),back.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};
        check(context->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped), "Overlay map");
        unsigned painted=0;
        // Below the probe's own text, inside both synthetic and Electron panels.
        for(unsigned y=200;y<300 && y<desc.Height;++y)
            for(unsigned x=60;x<400 && x<desc.Width;++x) {
                auto pixel=static_cast<const unsigned char*>(mapped.pData)+y*mapped.RowPitch+x*4;
                if(pixel[0]<28 || pixel[0]>33 || pixel[1]<18 || pixel[1]>23 || pixel[2]<8 || pixel[2]>13)
                    ++painted;
            }
        context->Unmap(staging.Get(),0);
        event("overlay_readback_nonbackground_pixels="+std::to_string(painted));
        if(painted<100)throw std::runtime_error("Overlay IPC works but presented panel pixels are absent");
    }
    void render() {
        BOOL actual = FALSE;
        check(chain->GetFullscreenState(&actual, nullptr), "GetFullscreenState");
        if (lastFullscreen != (actual != FALSE)) {
            lastFullscreen = actual != FALSE;
            event(lastFullscreen ? "dxgi_fullscreen=true" : "dxgi_fullscreen=false");
        }
        const float background[4] = {0.04f,0.08f,0.12f,1};
        context->ClearRenderTargetView(view.Get(), background);
        canvas->BeginDraw();
        std::wostringstream text;
        text << L"Rift Companion — banc DirectX 11\n"
             << L"F11 : plein écran | R : recréer la swap chain | Espace : panneau | Échap : quitter\n"
             << L"DXGI plein écran : " << (actual ? L"OUI" : L"NON")
             << L" | " << width << L" × " << height << L" | intervalle CPU p95 : "
             << std::fixed << std::setprecision(1) << p95 << L" ms\n"
             << (bridge ? bridge->status() : L"Mode DirectX seul — GOverlay non chargé");
        auto label = text.str();
        canvas->DrawText(label.c_str(), static_cast<UINT32>(label.size()), font.Get(),
            D2D1::RectF(20,20,static_cast<float>(width)-20,124), brush.Get());
        const std::wstring attribution=L"Powered by GOverlay — ouvrir le projet";
        canvas->DrawText(attribution.c_str(),static_cast<UINT32>(attribution.size()),font.Get(),
            D2D1::RectF(20,126,700,158),brush.Get());
        HRESULT draw = canvas->EndDraw();
        if (draw == D2DERR_RECREATE_TARGET) { resizePending = true; return; }
        check(draw, "EndDraw");
        // Direct2D can leave shader stages bound that the GOverlay sprite pipeline
        // does not override. Start the Present hook from a clean D3D11 state.
        context->ClearState();
        if (smoke && frames % 20 == 0) readbackCheck();
        HRESULT present = chain->Present(1,0);
        if (present == DXGI_STATUS_OCCLUDED) { Sleep(50); return; }
        check(present, "Present");
        ++frames;
        auto now = Clock::now();
        durations.push_back(std::chrono::duration<double,std::milli>(now-previous).count());
        previous = now;
        if (now-sample >= std::chrono::seconds(1)) {
            std::sort(durations.begin(), durations.end());
            p95 = durations[(durations.size()-1)*95/100];
            event("cpu_frame_interval_p95_ms="+std::to_string(p95));
            durations.clear(); sample = now;
            if (bridge) bridge->update(static_cast<unsigned>(std::chrono::duration_cast<std::chrono::seconds>(now-start).count()));
        }
    }
    static LRESULT CALLBACK wndProc(HWND hwnd, UINT msg, WPARAM w, LPARAM l) {
        auto self = reinterpret_cast<Probe*>(GetWindowLongPtr(hwnd, GWLP_USERDATA));
        if (msg == WM_NCCREATE) {
            self = static_cast<Probe*>(reinterpret_cast<CREATESTRUCT*>(l)->lpCreateParams);
            SetWindowLongPtr(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        if (!self) return DefWindowProc(hwnd,msg,w,l);
        // Never let a C++ exception cross the Win32 callback boundary.
        switch(msg) {
        case WM_SIZE:
            self->minimized = w == SIZE_MINIMIZED; self->resizePending = true; return 0;
        case WM_ACTIVATEAPP:
            self->event(w ? "focus=true" : "focus=false"); return 0;
        case WM_KEYDOWN:
            if (l & (1LL<<30)) return 0;
            if (w==VK_ESCAPE) PostMessage(hwnd,WM_CLOSE,0,0);
            if (w==VK_F11) self->togglePending = true;
            if (w=='R') self->recreatePending = true;
            if (w==VK_SPACE && self->bridge) self->bridge->toggle();
            return 0;
        case WM_LBUTTONUP:
            if (GET_X_LPARAM(l)>=20 && GET_X_LPARAM(l)<700 && GET_Y_LPARAM(l)>=126 && GET_Y_LPARAM(l)<158)
                ShellExecuteW(hwnd,L"open",L"https://github.com/hiitiger/goverlay",nullptr,nullptr,SW_SHOWNORMAL);
            return 0;
        case WM_CLOSE: self->running = false; return 0;
        case WM_DESTROY: PostQuitMessage(0); return 0;
        }
        return DefWindowProc(hwnd,msg,w,l);
    }
public:
    int run(bool smokeMode, bool withOverlay) {
        smoke = smokeMode;
        wchar_t temp[MAX_PATH]{};
        if (!GetTempPathW(MAX_PATH,temp)) throw std::runtime_error("GetTempPath failed");
        logPath = (std::filesystem::path(temp)/(L"RiftDx11Probe-"+std::to_wstring(GetCurrentProcessId())+L".log")).wstring();
        log.open(std::filesystem::path(logPath));
        if (!log) throw std::runtime_error("Cannot create diagnostic log");
        event(smoke ? "mode=warp_smoke" : "mode=hardware_manual");
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        WNDCLASS wc{}; wc.lpfnWndProc=wndProc; wc.hInstance=GetModuleHandle(nullptr);
        wc.hCursor=LoadCursor(nullptr,IDC_ARROW); wc.lpszClassName=L"RiftDx11Probe";
        if (!RegisterClass(&wc)) throw std::runtime_error("RegisterClass failed");
        window=CreateWindow(wc.lpszClassName,L"Rift DirectX 11 probe",WS_OVERLAPPEDWINDOW,
            CW_USEDEFAULT,CW_USEDEFAULT,1100,720,nullptr,nullptr,wc.hInstance,this);
        if (!window) throw std::runtime_error("CreateWindow failed");
        check(D3D11CreateDevice(nullptr,smoke?D3D_DRIVER_TYPE_WARP:D3D_DRIVER_TYPE_HARDWARE,
            nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context),"D3D11CreateDevice");
        check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.GetAddressOf()),"D2D factory");
        check(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,__uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(write.GetAddressOf())),"DWrite factory");
        check(write->CreateTextFormat(L"Segoe UI",nullptr,DWRITE_FONT_WEIGHT_NORMAL,DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH_NORMAL,18,L"fr-FR",&font),"Text format");
        makeChain(); ShowWindow(window,SW_SHOW); UpdateWindow(window);
        if (withOverlay) {
#ifdef WITH_GOVERLAY
            bridge=startBridge(); event("goverlay_loaded_self_only");
#else
            throw std::runtime_error("This build has no GOverlay bridge; use Build-WithGOverlay.ps1");
#endif
        }
        while (running) {
            MSG msg{};
            while (PeekMessage(&msg,nullptr,0,0,PM_REMOVE)) {
                if (msg.message==WM_QUIT) running=false;
                TranslateMessage(&msg); DispatchMessage(&msg);
            }
            if (!running) break;
            if (togglePending) { togglePending=false; fullscreen(); }
            if (recreatePending) { recreatePending=false; recreate(); }
            if (resizePending) resize();
            if (minimized) { WaitMessage(); continue; }
            render();
            if (smoke) {
                if (frames>=20 && step==0) { SetWindowPos(window,nullptr,0,0,800,600,SWP_NOMOVE|SWP_NOZORDER); ++step; }
                if (frames>=40 && step==1) { recreate(); ++step; }
                if (frames>=60 && (!withOverlay || Clock::now()-start>std::chrono::seconds(5))) {
                    if (withOverlay && !bridge->connected()) throw std::runtime_error("GOverlay IPC handshake failed");
                    if (withOverlay) {
                        overlayReadbackCheck();
                        event("goverlay_ipc_and_presented_pixels_pass; exclusive_fullscreen_not_tested");
                    }
                    running=false;
                }
                if (Clock::now()-start>std::chrono::seconds(20)) throw std::runtime_error("Smoke test timed out");
            }
        }
        check(chain->SetFullscreenState(FALSE,nullptr),"Leave fullscreen");
        event(smoke ? "smoke_pass: render_readback_resize_recreate; fullscreen_not_tested" : "manual_session_closed");
        return 0;
    }
    ~Probe() {
        if (chain) chain->SetFullscreenState(FALSE,nullptr);
        bridge.reset();
        if (context) releaseTarget();
        chain.Reset();
        if (window) DestroyWindow(window);
    }
};

int WINAPI wWinMain(HINSTANCE,HINSTANCE,PWSTR,int) {
    bool smoke=false, overlay=false;
    try {
        int argc{}; auto argv=CommandLineToArgvW(GetCommandLineW(),&argc);
        if (!argv) return 2;
        for (int i=1;i<argc;++i) {
            if (std::wstring(argv[i])==L"--smoke") smoke=true;
            else if (std::wstring(argv[i])==L"--bridge-check") { smoke=true; overlay=true; }
            else if (std::wstring(argv[i])==L"--goverlay") overlay=true;
            else { LocalFree(argv); return 2; }
        }
        LocalFree(argv);
        Probe probe; return probe.run(smoke,overlay);
    } catch(const std::exception& e) {
        std::fprintf(stderr,"RiftDx11Probe: %s\n",e.what());
        OutputDebugStringA(e.what());
        if (!smoke) MessageBoxA(nullptr,e.what(),"Rift DX11 probe error",MB_ICONERROR);
        return 1;
    }
}
