#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"
#include "../../runtime/native/plugins/opennv_plugin_command_table.h"

#ifndef OPENNV_CORESIDENT_SECONDARY
extern "C" __declspec(dllimport) unsigned int __stdcall __std_parallel_algorithms_hw_threads();
#endif

// First-party DLLs have independent static state and real public NVSE entries.
// The authored receiver is ordinary native data, never a TESForm/Actor proxy.
namespace {
using namespace opennv_domain;
const NvseCoreInterface* retained_core = nullptr;
HMODULE retained_image = nullptr;
std::uint32_t query_handle = 0, load_handle = 0, native_thread = 0, task_calls = 0, hardware_count = 0;
constexpr std::uint32_t primary_opcode = 0x3100, secondary_opcode = 0x3200;
#ifdef OPENNV_CORESIDENT_SECONDARY
constexpr auto opcode = secondary_opcode;
constexpr const char* plugin_name = "AuthoredCoResidentSecondary";
constexpr const char* command_name = "AuthoredSecondaryScalar";
constexpr auto command_value = 2.0;
#else
constexpr auto opcode = primary_opcode;
constexpr const char* plugin_name = "AuthoredCoResidentPrimary";
constexpr const char* command_name = "AuthoredPrimaryScalar";
constexpr auto command_value = 1.0;
#endif
bool __cdecl execute_scalar(const void*, void*, void*, void*, void*, void*, double* result, std::uint32_t*) {
    if (!retained_core || !result || !retained_core->query_interface(2)) return false;
    *result = command_value; return true;
}
NvseCommandInfo command{};
struct Receiver { std::uint32_t calls, value, last_loading_handle; };
static_assert(sizeof(Receiver) == 12 && sizeof(void*) == 4);
}

extern "C" std::uint32_t __cdecl ModuleTaskBody(Receiver* receiver,
    std::uint32_t first, std::uint32_t second, std::uint32_t third, std::uint32_t fourth) {
    if (!receiver || !retained_core || GetCurrentThreadId() != native_thread || !retained_core->query_interface(2))
        RaiseException(ERROR_INVALID_DATA, EXCEPTION_NONCONTINUABLE, 0, nullptr);
    ++task_calls; ++receiver->calls;
    receiver->value = first ^ second ^ third ^ fourth;
    receiver->last_loading_handle = retained_core->get_handle();
    return receiver->value;
}
extern "C" __declspec(naked) std::uint32_t __stdcall ModuleTask0() {
    __asm {
        push 0
        push 0
        push 0
        push 0
        push ecx
        call ModuleTaskBody
        add esp, 20
        ret
    }
}
extern "C" __declspec(naked) std::uint32_t __stdcall ModuleTask1(std::uint32_t) {
    __asm {
        mov eax, [esp+4]
        push 0
        push 0
        push 0
        push eax
        push ecx
        call ModuleTaskBody
        add esp, 20
        ret 4
    }
}
extern "C" __declspec(naked) std::uint32_t __stdcall ModuleTask4(std::uint32_t, std::uint32_t, std::uint32_t, std::uint32_t) {
    __asm {
        push ebp
        mov ebp, esp
        push dword ptr [ebp+20]
        push dword ptr [ebp+16]
        push dword ptr [ebp+12]
        push dword ptr [ebp+8]
        push ecx
        call ModuleTaskBody
        add esp, 20
        pop ebp
        ret 16
    }
}
extern "C" std::uint32_t __stdcall ModuleRelay(std::uint32_t function, std::uint32_t receiver,
    std::uint32_t first, std::uint32_t second) {
    if (!retained_core || !retained_core->query_interface(2)) return 0;
    std::uint32_t returned = 0;
    __asm {
        push 055aa55aah
        push 0a55aa55ah
        push second
        push first
        mov ecx, receiver
        call function
        mov returned, eax
    }
    if (!retained_core->query_interface(2)) return 0;
    return returned;
}
extern "C" std::uint32_t __stdcall ModuleRead(std::uint32_t field) {
    switch (field) {
    case 0: return reinterpret_cast<std::uint32_t>(retained_core);
    case 1: return query_handle;
    case 2: return load_handle;
    case 3: return reinterpret_cast<std::uint32_t>(retained_image);
    case 4: return native_thread;
    case 5: return task_calls;
    case 6: return hardware_count;
    case 7: return retained_core ? retained_core->get_handle() : 0;
    case 8: return opcode;
    default: RaiseException(ERROR_INVALID_PARAMETER, EXCEPTION_NONCONTINUABLE, 0, nullptr); return 0;
    }
}
extern "C" std::uint32_t __cdecl ModuleCdecl4(std::uint32_t first, std::uint32_t second, std::uint32_t third, std::uint32_t fourth) {
    if (!retained_core || !retained_core->query_interface(2)) return 0;
    return first ^ second ^ third ^ fourth;
}
static void __cdecl ModulePeerListener(NvseMessage*) {
    RaiseException(ERROR_NOT_SUPPORTED, EXCEPTION_NONCONTINUABLE, 0, nullptr);
}
extern "C" std::uint32_t __stdcall ModuleListenNull() {
    const auto* messaging = retained_core ? static_cast<const NvseMessagingInterface*>(retained_core->query_interface(2)) : nullptr;
    return messaging && messaging->listen(load_handle, nullptr, ModulePeerListener) ? 1U : 0U;
}
extern "C" std::uint32_t __stdcall ModuleCallCommand(std::uint32_t requested_opcode) {
    if (!retained_core) return 0;
    const auto* table = static_cast<const NvseCommandTableInterface*>(retained_core->query_interface(3));
    const auto* declaration = table ? table->by_opcode(requested_opcode) : nullptr;
    if (!declaration || !declaration->execute || declaration->needs_parent || declaration->parameter_count) return 0;
    using Execute = bool (__cdecl*)(const void*, void*, void*, void*, void*, void*, double*, std::uint32_t*);
    double result = 0.0; std::uint32_t offset = 0;
    const auto returned = reinterpret_cast<Execute>(declaration->execute)(nullptr, nullptr, nullptr, nullptr, nullptr, nullptr, &result, &offset);
    return returned && offset == 0 ? (result == 1.0 ? 1U : result == 2.0 ? 2U : 0U) : 0U;
}
extern "C" std::uint32_t __stdcall ModuleRaise() {
    RaiseException(0xe04e5677U, EXCEPTION_NONCONTINUABLE, 0, nullptr); return 0;
}
extern "C" std::uint32_t __stdcall ModuleHardware() {
#ifdef OPENNV_CORESIDENT_SECONDARY
    RaiseException(ERROR_NOT_SUPPORTED, EXCEPTION_NONCONTINUABLE, 0, nullptr); return 0;
#else
    return __std_parallel_algorithms_hw_threads();
#endif
}
extern "C" bool __cdecl NVSEPlugin_Query(const NvseCoreInterface* core, NvsePluginInfo* info) {
    if (!core || !info || retained_core || !core->query_interface(2)) return false;
    retained_core = core; query_handle = core->get_handle();
    *info = {1, plugin_name, 1};
#ifndef OPENNV_CORESIDENT_SECONDARY
    hardware_count = __std_parallel_algorithms_hw_threads();
#endif
    return query_handle != 0 && query_handle != nvse_invalid_handle;
}
extern "C" bool __cdecl NVSEPlugin_Load(const NvseCoreInterface* core) {
    if (!core || core != retained_core || !core->query_interface(2)) return false;
    load_handle = core->get_handle(); core->set_opcode(opcode);
    return load_handle != 0 && load_handle != nvse_invalid_handle && core->register_command(&command);
}
extern "C" BOOL WINAPI DllMain(HINSTANCE image, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        retained_image = image; native_thread = GetCurrentThreadId();
        command.name = command_name; command.alias = "";
        command.help = "Authored same-process original callable";
        command.execute = reinterpret_cast<void*>(execute_scalar);
    }
    return TRUE;
}
