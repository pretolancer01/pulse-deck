#include <windows.h>
#include <cmath>
#include <mutex>
#include <string>
#include "IPlatform.h"
#include "IDeviceManager.h"
#include "ICPUEx.h"

// Thin read-only adapter around AMD's documented Monitoring SDK interfaces.
// AMD's DLLs and driver remain in their own installation directory.
namespace {
std::mutex gate;
HMODULE platformModule = nullptr;
HMODULE deviceModule = nullptr;
IPlatform* platform = nullptr;
ICPUEx* cpu = nullptr;
}

extern "C" __declspec(dllexport) int __cdecl PulseDeckRyzenInit(const wchar_t* sdkRoot)
{
    std::lock_guard<std::mutex> lock(gate);
    if (platform != nullptr) return 0;
    if (sdkRoot == nullptr || *sdkRoot == 0) return -1;
    std::wstring path(sdkRoot);
    if (path.back() != L'\\' && path.back() != L'/') path += L'\\';
    path += L"bin\\";
    deviceModule = LoadLibraryExW((path + L"Device.dll").c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (deviceModule == nullptr) return -2000 - static_cast<int>(GetLastError());
    path += L"Platform.dll";
    platformModule = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (platformModule == nullptr) { int error=-1000-static_cast<int>(GetLastError()); FreeLibrary(deviceModule); deviceModule=nullptr; return error; }
    auto getPlatform = reinterpret_cast<IPlatform& (__stdcall*)()>(GetProcAddress(platformModule, "GetPlatform"));
    if (getPlatform == nullptr) { FreeLibrary(platformModule); platformModule = nullptr; FreeLibrary(deviceModule); deviceModule=nullptr; return -3; }
    try {
        platform = &getPlatform();
        if (!platform->Init(nullptr, true)) { platform = nullptr; FreeLibrary(platformModule); platformModule = nullptr; FreeLibrary(deviceModule); deviceModule=nullptr; return -4; }
        cpu = static_cast<ICPUEx*>(platform->GetIDeviceManager().GetDevice(dtCPU, 0));
        if (cpu == nullptr) { platform->UnInit(); platform = nullptr; FreeLibrary(platformModule); platformModule = nullptr; FreeLibrary(deviceModule); deviceModule=nullptr; return -5; }
        return 0;
    }
    catch (...) {
        if (platform != nullptr) { try { platform->UnInit(); } catch (...) {} }
        platform = nullptr; cpu = nullptr; FreeLibrary(platformModule); platformModule = nullptr; FreeLibrary(deviceModule); deviceModule=nullptr;
        return -6;
    }
}

extern "C" __declspec(dllexport) int __cdecl PulseDeckRyzenRead(double* temperatureCelsius)
{
    std::lock_guard<std::mutex> lock(gate);
    if (cpu == nullptr || temperatureCelsius == nullptr) return -1;
    try {
        CPUParameters parameters{};
        if (cpu->GetCPUParameters(parameters) != 0) return -2;
        if (!std::isfinite(parameters.dTemperature) || parameters.dTemperature <= 0 || parameters.dTemperature >= 120) return -3;
        *temperatureCelsius = parameters.dTemperature;
        return 0;
    }
    catch (...) { return -4; }
}

extern "C" __declspec(dllexport) void __cdecl PulseDeckRyzenShutdown()
{
    std::lock_guard<std::mutex> lock(gate);
    if (platform != nullptr) { try { platform->UnInit(); } catch (...) {} }
    cpu = nullptr; platform = nullptr;
    if (platformModule != nullptr) { FreeLibrary(platformModule); platformModule = nullptr; }
    if (deviceModule != nullptr) { FreeLibrary(deviceModule); deviceModule = nullptr; }
}
