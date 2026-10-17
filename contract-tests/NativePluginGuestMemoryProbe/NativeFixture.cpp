#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdlib>
#include "../../runtime/native/plugins/opennv_plugin_domain.h"

using namespace opennv_domain;
extern "C" __declspec(dllimport) std::uint32_t __cdecl OpenNvDependencyAdd(std::uint32_t, std::uint32_t);

namespace {
__declspec(thread) std::uint32_t tls_scalar = 0x746c7301;
std::uint32_t tls_attach = 0, dll_attach = 0, order = 0, tls_order = 0, dll_order = 0;
std::uint32_t receiver = 0x1234;
HostCallbacks callbacks{};
ModuleLifetime* receipt = nullptr;
void entered() { if (receipt) ++receipt->calls; }
}

extern "C" void NTAPI OpenNvTlsCallback(PVOID, DWORD reason, PVOID) {
    if (reason == DLL_PROCESS_ATTACH) { ++tls_attach; tls_order = ++order; }
    else if (reason == DLL_PROCESS_DETACH && receipt) ++receipt->tls_detach;
}
#pragma section(".CRT$XLB", read)
extern "C" __declspec(allocate(".CRT$XLB")) PIMAGE_TLS_CALLBACK opennv_fixture_tls = OpenNvTlsCallback;
#pragma comment(linker, "/INCLUDE:__tls_used")
#pragma comment(linker, "/INCLUDE:_opennv_fixture_tls")

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        ++dll_attach; dll_order = ++order;
#ifdef OPENNV_FIXTURE_REJECT_ENTRY
        return FALSE;
#endif
    }
    else if (reason == DLL_PROCESS_DETACH && receipt) ++receipt->dll_detach;
    return TRUE;
}

extern "C" std::uint32_t __cdecl OpenNvScalarCdecl(std::uint32_t first, std::uint32_t second) {
    entered(); return (OpenNvDependencyAdd(first, second) + first * 5) ^ 0xcdec0000U;
}
extern "C" std::uint32_t __stdcall OpenNvScalarStdcall(std::uint32_t first, std::uint32_t second) {
    entered(); return (first * 3 + second) ^ 0x5dca0000U;
}
// A genuinely ECX-owned receiver and ret 8; no C wrapper consumes "this".
// All bytes below are authored for this fixture, never copied from a game.
extern "C" __declspec(naked) std::uint32_t OpenNvThiscall() {
    __asm {
        mov edx, receipt
        test edx, edx
        jz no_receipt
        inc dword ptr [edx + 32]
    no_receipt:
        mov eax, [ecx]
        add eax, [esp + 4]
        mov edx, [esp + 8]
        lea eax, [eax + edx * 2]
        inc dword ptr [ecx]
        ret 8
    }
}
extern "C" std::uint32_t __cdecl OpenNvCallbackCdecl(std::uint32_t first, std::uint32_t second) {
    entered(); if (!receipt || !callbacks.cdecl_callback) return 0;
    ++receipt->callbacks; return callbacks.cdecl_callback(callbacks.context, 11, first, second) + 0x100;
}
extern "C" std::uint32_t __stdcall OpenNvCallbackStdcall(std::uint32_t first, std::uint32_t second) {
    entered(); if (!receipt || !callbacks.stdcall_callback) return 0;
    ++receipt->callbacks; return callbacks.stdcall_callback(callbacks.context, 12, first, second) + 0x200;
}
extern "C" __declspec(naked) std::uint32_t OpenNvBadCleanup() {
    __asm {
        mov eax, 1
        ret 8
    }
}
extern "C" __declspec(naked) std::uint32_t OpenNvBadPreservation() {
    __asm {
        xor ebx, ebx
        mov eax, 1
        ret
    }
}
extern "C" __declspec(naked) std::uint32_t OpenNvBadCalleeCleanup() {
    __asm {
        mov eax, 1
        ret
    }
}
extern "C" std::uint32_t __cdecl OpenNvRaise(std::uint32_t, std::uint32_t) {
    RaiseException(0xe04e5601U, 0, 0, nullptr); return 0;
}
extern "C" std::uint32_t __cdecl OpenNvHang(std::uint32_t, std::uint32_t) {
    Sleep(INFINITE); return 0;
}
namespace {
struct ForeignCall { std::uint32_t first, second, result; };
DWORD WINAPI foreign_call(void* context) {
    auto* call = static_cast<ForeignCall*>(context);
    call->result = callbacks.cdecl_callback(callbacks.context, 13, call->first, call->second); return 0;
}
}
extern "C" std::uint32_t __cdecl OpenNvForeignCallback(std::uint32_t first, std::uint32_t second) {
    entered(); if (!receipt || !callbacks.cdecl_callback) return 0;
    ++receipt->callbacks; ForeignCall call{first, second, 0};
    const auto thread = CreateThread(nullptr, 0, foreign_call, &call, 0, nullptr);
    if (!thread) { RaiseException(0xe04e5602U, 0, 0, nullptr); return 0; }
    WaitForSingleObject(thread, INFINITE); CloseHandle(thread); return call.result;
}

namespace {
const ExportContract exports[] = {
    {"OpenNvScalarCdecl", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvScalarCdecl)},
    {"OpenNvScalarStdcall", Abi::stdcall_call, reinterpret_cast<void*>(OpenNvScalarStdcall)},
    {"OpenNvThiscall", Abi::thiscall_call, reinterpret_cast<void*>(OpenNvThiscall)},
    {"OpenNvCallbackCdecl", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvCallbackCdecl)},
    {"OpenNvCallbackStdcall", Abi::stdcall_call, reinterpret_cast<void*>(OpenNvCallbackStdcall)},
    // Deliberate ABI violations live only in the authored fixture.
    {"OpenNvBadCleanup", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvBadCleanup)},
    {"OpenNvBadPreservation", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvBadPreservation)},
    {"OpenNvBadStdcallCleanup", Abi::stdcall_call, reinterpret_cast<void*>(OpenNvBadCalleeCleanup)},
    {"OpenNvBadThiscallCleanup", Abi::thiscall_call, reinterpret_cast<void*>(OpenNvBadCalleeCleanup)},
    {"OpenNvRaise", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvRaise)},
    {"OpenNvHang", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvHang)},
    {"OpenNvForeignCallback", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvForeignCallback)},
};
const AuthoredModuleContract contract{sizeof(AuthoredModuleContract), 1, module_magic, _countof(exports), exports, &receiver};
}
extern "C" const AuthoredModuleContract* __cdecl OpenNvDomainDescribe() { return &contract; }
extern "C" bool __cdecl OpenNvDomainBind(const HostCallbacks* host, ModuleLifetime* lifetime) {
    if (!host || !lifetime || host->size != sizeof(HostCallbacks) || host->version != 1 ||
        !host->cdecl_callback || !host->stdcall_callback || receipt) return false;
    callbacks = *host; receipt = lifetime;
    *receipt = {tls_attach, dll_attach, tls_order, dll_order, OpenNvDependencyAdd(1, 2), tls_scalar, 0, 0, 0, 0};
    return true;
}
