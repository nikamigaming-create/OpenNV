#pragma once
#include <windows.h>
#include <bcrypt.h>
#include <cstdint>

// This is OpenNV's first-party provider ABI, not a Windows private layout or
// an original-plugin interface. Every public SDK call still executes in the
// separately owned Windows system-provider process.
namespace opennv_domain {
constexpr std::uint32_t import_provider_abi = 1, import_provider_cng = 1;
#pragma pack(push, 4)
struct CngImportProviderBridge {
    std::uint32_t abi, bytes;
    std::uint64_t generation;
    DWORD thread;
    std::uint32_t kind;
    decltype(&BCryptOpenAlgorithmProvider) open;
    decltype(&BCryptGetProperty) property;
    decltype(&BCryptCreateHash) create;
    decltype(&BCryptHashData) data;
    decltype(&BCryptFinishHash) finish;
    decltype(&BCryptDestroyHash) destroy;
    decltype(&BCryptCloseAlgorithmProvider) close;
};
struct ImportProviderSnapshot {
    std::uint32_t abi, bytes;
    std::uint64_t generation;
    DWORD thread;
    std::uint32_t active, bound, process_attach, process_detach;
    std::uint64_t calls[7];
};
#pragma pack(pop)
static_assert(sizeof(void*) == 4 && sizeof(CngImportProviderBridge) == 52);
static_assert(sizeof(ImportProviderSnapshot) == 92);
using ImportProviderBind = BOOL (WINAPI*)(const CngImportProviderBridge*);
using ImportProviderInspect = BOOL (WINAPI*)(ImportProviderSnapshot*, ULONG);
using ImportProviderUnbind = BOOL (WINAPI*)(std::uint64_t);
}
