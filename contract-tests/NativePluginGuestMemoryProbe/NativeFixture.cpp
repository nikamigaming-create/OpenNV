#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdlib>
#include "../../runtime/native/plugins/opennv_plugin_domain.h"
#include "../../runtime/native/plugins/opennv_plugin_guest.h"

using namespace opennv_domain;
extern "C" __declspec(dllimport) std::uint32_t __cdecl OpenNvDependencyAdd(std::uint32_t, std::uint32_t);

namespace {
__declspec(thread) std::uint32_t tls_scalar = 0x746c7301;
std::uint32_t tls_attach = 0, dll_attach = 0, order = 0, tls_order = 0, dll_order = 0;
std::uint32_t receiver = 0x1234;
HostCallbacks callbacks{};
ModuleLifetime* receipt = nullptr;
const GuestStateView* remembered_object = nullptr;
GuestCdeclQuery remembered_query = nullptr;
bool query_on_detach = false;
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
    else if (reason == DLL_PROCESS_DETACH && receipt) {
        ++receipt->dll_detach;
        if (query_on_detach && remembered_query) remembered_query(remembered_object, 1, 0);
    }
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
GuestCdeclQuery guest_cdecl(const GuestStateView* view) {
    if (view->magic != guest_state_magic || view->version != guest_state_version || view->size != sizeof(GuestStateView))
        RaiseException(0xe04e5610U, 0, 0, nullptr);
    return reinterpret_cast<GuestCdeclQuery>(static_cast<std::uintptr_t>(view->cdecl_query));
}
}
extern "C" std::uint32_t __cdecl OpenNvGuestReadDword(std::uint32_t pointer, std::uint32_t offset) {
    entered(); return *reinterpret_cast<const std::uint32_t*>(static_cast<std::uintptr_t>(pointer) + offset);
}
extern "C" std::uint32_t __cdecl OpenNvGuestWriteDword(std::uint32_t pointer, std::uint32_t value) {
    entered(); auto* target = reinterpret_cast<std::uint32_t*>(static_cast<std::uintptr_t>(pointer));
    const auto previous = *target; *target = value; return previous;
}
extern "C" std::uint32_t __cdecl OpenNvGuestQueryCdecl(std::uint32_t pointer, std::uint32_t argument) {
    entered(); ++receipt->callbacks;
    const auto* view = reinterpret_cast<const GuestStateView*>(static_cast<std::uintptr_t>(pointer));
    return guest_cdecl(view)(view, 1, argument);
}
extern "C" std::uint32_t __stdcall OpenNvGuestDamageStdcall(std::uint32_t pointer, std::uint32_t argument) {
    entered(); ++receipt->callbacks;
    const auto* view = reinterpret_cast<const GuestStateView*>(static_cast<std::uintptr_t>(pointer));
    const auto query = reinterpret_cast<GuestStdcallQuery>(static_cast<std::uintptr_t>(view->stdcall_query));
    return query(view, 2, argument);
}
extern "C" __declspec(naked) std::uint32_t OpenNvGuestQueryThiscall() {
    __asm {
        mov edx, receipt
        inc dword ptr [edx + 32]
        inc dword ptr [edx + 36]
        mov ecx, [esp + 4]
        mov eax, [ecx + 24]
        push [esp + 8]
        push 1
        call eax
        ret
    }
}
extern "C" std::uint32_t __cdecl OpenNvGuestReenter(std::uint32_t pointer, std::uint32_t argument) {
    entered(); ++receipt->callbacks;
    const auto* view = reinterpret_cast<const GuestStateView*>(static_cast<std::uintptr_t>(pointer));
    const auto result = guest_cdecl(view)(view, 3, argument);
    return result ^ *reinterpret_cast<const std::uint32_t*>(static_cast<std::uintptr_t>(argument));
}
extern "C" std::uint32_t __cdecl OpenNvGuestRemember(std::uint32_t pointer, std::uint32_t) {
    entered(); remembered_object = reinterpret_cast<const GuestStateView*>(static_cast<std::uintptr_t>(pointer));
    remembered_query = guest_cdecl(remembered_object); return 1;
}
extern "C" std::uint32_t __cdecl OpenNvGuestUseRemembered(std::uint32_t, std::uint32_t argument) {
    entered(); ++receipt->callbacks;
    return remembered_query(remembered_object, 1, argument);
}
extern "C" std::uint32_t __cdecl OpenNvGuestQueryOnDetach(std::uint32_t pointer, std::uint32_t) {
    entered(); remembered_object = reinterpret_cast<const GuestStateView*>(static_cast<std::uintptr_t>(pointer));
    remembered_query = guest_cdecl(remembered_object); query_on_detach = true; return 1;
}
// Deliberate native ownership violations stay in this authored negative fixture.
extern "C" std::uint32_t __cdecl OpenNvGuestSetReadOnly(std::uint32_t pointer, std::uint32_t) {
    entered(); DWORD previous = 0;
    if (!VirtualProtect(reinterpret_cast<void*>(static_cast<std::uintptr_t>(pointer)), sizeof(std::uint32_t), PAGE_READONLY, &previous))
        RaiseException(0xe04e5611U, 0, 0, nullptr);
    return previous;
}
extern "C" std::uint32_t __cdecl OpenNvGuestTamperState(std::uint32_t pointer, std::uint32_t) {
    entered(); auto* view = reinterpret_cast<GuestStateView*>(static_cast<std::uintptr_t>(pointer));
    DWORD previous = 0, restored = 0;
    if (!VirtualProtect(view, sizeof(*view), PAGE_READWRITE, &previous)) RaiseException(0xe04e5612U, 0, 0, nullptr);
    view->capability ^= 1;
    if (!VirtualProtect(view, sizeof(*view), previous, &restored)) RaiseException(0xe04e5613U, 0, 0, nullptr);
    return 1;
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
    {"OpenNvGuestReadDword", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestReadDword)},
    {"OpenNvGuestWriteDword", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestWriteDword)},
    {"OpenNvGuestQueryCdecl", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestQueryCdecl)},
    {"OpenNvGuestDamageStdcall", Abi::stdcall_call, reinterpret_cast<void*>(OpenNvGuestDamageStdcall)},
    {"OpenNvGuestQueryThiscall", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestQueryThiscall)},
    {"OpenNvGuestReenter", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestReenter)},
    {"OpenNvGuestRemember", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestRemember)},
    {"OpenNvGuestUseRemembered", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestUseRemembered)},
    {"OpenNvGuestQueryOnDetach", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestQueryOnDetach)},
    {"OpenNvGuestSetReadOnly", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestSetReadOnly)},
    {"OpenNvGuestTamperState", Abi::cdecl_call, reinterpret_cast<void*>(OpenNvGuestTamperState)},
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
