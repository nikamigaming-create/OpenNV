#pragma once
#include <cstdint>

// First-party diagnostic module contract. This is not an NVSE or game ABI.
// The bridge never publishes a host-process pointer to a 32-bit module.
namespace opennv_domain {
constexpr std::uint32_t protocol_magic = 0x444e564f;
constexpr std::uint32_t protocol_version = 12;
constexpr std::uint32_t max_payload = 65536;
constexpr std::uint32_t max_call_depth = 8;
constexpr std::uint32_t module_magic = 0x4d4e564f;
enum class Kind : std::uint32_t { request = 1, reply = 2, callback = 3, callback_reply = 4, fault = 5, state_query = 6, state_reply = 7, nvse_callback = 8, nvse_reply = 9, io_callback = 10, io_reply = 11 };
enum class Operation : std::uint32_t { hello = 1, load_authored = 2, resolve = 3, call = 4, unload = 5, retire = 6,
    guest_caps = 7, guest_allocate = 8, guest_read = 9, guest_write = 10, guest_release = 11, guest_bind_state = 12, guest_stats = 13, load_nvse = 14, nvse_query = 15, nvse_load = 16,
    nvse_message = 17, nvse_serialization = 18, unload_nvse = 19, nvse_expression_abi = 20, nvse_command = 21, nvse_expression_statistics = 22, nvse_values_attach = 23, nvse_values_statistics = 24, nvse_value_heap = 25, private_io_prepare = 26, nvse_local_create = 27, nvse_local_fill = 28, nvse_local_seal = 29, nvse_local_retire = 30, nvse_local_statistics = 31, nvse_object_bind = 32, guest_seal = 33, nvse_script_interface = 34, nvse_object_retire = 35, nvse_object_refresh = 36, nvse_local_attach_script = 37, nvse_file_methods = 38, nvse_binary_methods = 39, nvse_binary_bind = 40, nvse_binary_retire = 41 };
enum class Abi : std::uint32_t { cdecl_call = 1, stdcall_call = 2, thiscall_call = 3 };

#pragma pack(push, 8)
struct FrameHeader {
    std::uint32_t magic, version, kind, operation;
    std::uint64_t generation, id, parent;
    std::uint32_t length, reserved;
};
#pragma pack(pop)
static_assert(sizeof(FrameHeader) == 48);

using CdeclCallback = std::uint32_t(__cdecl*)(void*, std::uint32_t, std::uint32_t, std::uint32_t);
using StdcallCallback = std::uint32_t(__stdcall*)(void*, std::uint32_t, std::uint32_t, std::uint32_t);
struct HostCallbacks {
    std::uint32_t size, version;
    void* context;
    CdeclCallback cdecl_callback;
    StdcallCallback stdcall_callback;
};
static_assert(sizeof(HostCallbacks) == 20, "The companion must be compiled for x86.");

// Storage belongs to the companion and outlives FreeLibrary. DllMain/TLS may
// update these scalar fields, but may never call IPC or another loader API.
struct ModuleLifetime {
    std::uint32_t tls_attach, dll_attach, tls_order, dll_order;
    std::uint32_t imported_value, tls_value, tls_detach, dll_detach;
    std::uint32_t calls, callbacks;
};
struct ExportContract {
    const char* name;
    Abi abi;
    void* function;
};
struct AuthoredModuleContract {
    std::uint32_t size, version, magic, export_count;
    const ExportContract* exports;
    void* receiver;
};
using DescribeAuthoredModule = const AuthoredModuleContract*(__cdecl*)();
using BindAuthoredModule = bool(__cdecl*)(const HostCallbacks*, ModuleLifetime*);

struct CallReceipt {
    std::uint32_t result;
    std::int32_t stack_delta;
    std::uint32_t preserved_registers;
    std::uint32_t exception_code;
};
}
