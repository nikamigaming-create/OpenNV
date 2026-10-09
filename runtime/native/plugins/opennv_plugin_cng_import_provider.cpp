#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "opennv_plugin_import_provider.h"
#include <cstring>

using namespace opennv_domain;
namespace {
CngImportProviderBridge* bridge = nullptr;
ImportProviderSnapshot counters{import_provider_abi, sizeof(ImportProviderSnapshot)};
LONG active = 0;
[[noreturn]] void fail_owner() noexcept {
    // A first-party binding failure has no genuine Windows NTSTATUS. Never
    // return zero and disguise it as STATUS_SUCCESS to an original caller.
    TerminateProcess(GetCurrentProcess(), 3); ExitProcess(3);
}
bool executable_host(const void* pointer) noexcept {
    MEMORY_BASIC_INFORMATION memory{};
    if (!pointer || VirtualQuery(pointer, &memory, sizeof(memory)) != sizeof(memory)) return false;
    const auto access = memory.Protect & 0xffU;
    return memory.State == MEM_COMMIT && memory.Type == MEM_IMAGE &&
        memory.AllocationBase == GetModuleHandleW(nullptr) &&
        (access == PAGE_EXECUTE || access == PAGE_EXECUTE_READ || access == PAGE_EXECUTE_READWRITE || access == PAGE_EXECUTE_WRITECOPY);
}
struct Invocation {
    CngImportProviderBridge* owner;
    explicit Invocation(std::uint32_t operation) : owner(bridge) {
        const auto prior = GetLastError();
        if (!owner || counters.bound != 1 || GetCurrentThreadId() != owner->thread || operation >= 7)
            fail_owner();
        if (InterlockedIncrement(&active) <= 0) fail_owner();
        if (counters.calls[operation] == UINT64_MAX) fail_owner();
        ++counters.calls[operation]; SetLastError(prior);
    }
    ~Invocation() {
        const auto prior = GetLastError();
        if (InterlockedDecrement(&active) < 0) fail_owner();
        SetLastError(prior);
    }
};
}
extern "C" BOOL WINAPI OpenNVImportProviderBind(const CngImportProviderBridge* selected) {
    if (!selected || bridge || counters.bound || active || selected->abi != import_provider_abi ||
        selected->bytes != sizeof(CngImportProviderBridge) || selected->kind != import_provider_cng ||
        !selected->generation || selected->thread != GetCurrentThreadId() ||
        !executable_host(reinterpret_cast<const void*>(selected->open)) ||
        !executable_host(reinterpret_cast<const void*>(selected->property)) ||
        !executable_host(reinterpret_cast<const void*>(selected->create)) ||
        !executable_host(reinterpret_cast<const void*>(selected->data)) ||
        !executable_host(reinterpret_cast<const void*>(selected->finish)) ||
        !executable_host(reinterpret_cast<const void*>(selected->destroy)) ||
        !executable_host(reinterpret_cast<const void*>(selected->close))) {
        SetLastError(ERROR_INVALID_DATA); return FALSE;
    }
    auto* retained = static_cast<CngImportProviderBridge*>(VirtualAlloc(nullptr, sizeof(*selected), MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!retained) return FALSE;
    std::memcpy(retained, selected, sizeof(*selected)); DWORD previous = 0;
    if (!VirtualProtect(retained, sizeof(*retained), PAGE_READONLY, &previous)) {
        const auto failure = GetLastError(); VirtualFree(retained, 0, MEM_RELEASE); SetLastError(failure); return FALSE;
    }
    bridge = retained; counters.generation = selected->generation; counters.thread = selected->thread;
    counters.bound = 1; return TRUE;
}
extern "C" BOOL WINAPI OpenNVImportProviderInspect(ImportProviderSnapshot* destination, ULONG bytes) {
    if (!destination || bytes != sizeof(ImportProviderSnapshot) || !counters.thread || counters.thread != GetCurrentThreadId()) {
        SetLastError(ERROR_INVALID_PARAMETER); return FALSE;
    }
    counters.active = static_cast<std::uint32_t>(active); *destination = counters; return TRUE;
}
extern "C" BOOL WINAPI OpenNVImportProviderUnbind(std::uint64_t generation) {
    if (!bridge || !generation || bridge->generation != generation || bridge->thread != GetCurrentThreadId() || active) {
        SetLastError(ERROR_INVALID_STATE); return FALSE;
    }
    if (!VirtualFree(bridge, 0, MEM_RELEASE)) return FALSE;
    bridge = nullptr; counters.bound = 0; return TRUE;
}
extern "C" NTSTATUS WINAPI BCryptOpenAlgorithmProvider(BCRYPT_ALG_HANDLE* target, LPCWSTR algorithm, LPCWSTR implementation, ULONG flags) {
    Invocation call(0); return call.owner->open(target, algorithm, implementation, flags);
}
extern "C" NTSTATUS WINAPI BCryptGetProperty(BCRYPT_HANDLE handle, LPCWSTR property, PUCHAR target, ULONG bytes, ULONG* copied, ULONG flags) {
    Invocation call(1); return call.owner->property(handle, property, target, bytes, copied, flags);
}
extern "C" NTSTATUS WINAPI BCryptCreateHash(BCRYPT_ALG_HANDLE algorithm, BCRYPT_HASH_HANDLE* target, PUCHAR object, ULONG object_bytes,
    PUCHAR secret, ULONG secret_bytes, ULONG flags) {
    Invocation call(2); return call.owner->create(algorithm, target, object, object_bytes, secret, secret_bytes, flags);
}
extern "C" NTSTATUS WINAPI BCryptHashData(BCRYPT_HASH_HANDLE handle, PUCHAR data, ULONG bytes, ULONG flags) {
    Invocation call(3); return call.owner->data(handle, data, bytes, flags);
}
extern "C" NTSTATUS WINAPI BCryptFinishHash(BCRYPT_HASH_HANDLE handle, PUCHAR target, ULONG bytes, ULONG flags) {
    Invocation call(4); return call.owner->finish(handle, target, bytes, flags);
}
extern "C" NTSTATUS WINAPI BCryptDestroyHash(BCRYPT_HASH_HANDLE handle) {
    Invocation call(5); return call.owner->destroy(handle);
}
extern "C" NTSTATUS WINAPI BCryptCloseAlgorithmProvider(BCRYPT_ALG_HANDLE handle, ULONG flags) {
    Invocation call(6); return call.owner->close(handle, flags);
}
extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) ++counters.process_attach;
    if (reason == DLL_PROCESS_DETACH) ++counters.process_detach;
    return TRUE;
}
