#pragma once
#include <cstdint>

// First-party diagnostic module contract. This is not an NVSE or game ABI.
// The bridge never publishes a host-process pointer to a 32-bit module.
namespace opennv_domain {
constexpr std::uint32_t protocol_magic = 0x444e564f;
constexpr std::uint32_t protocol_version = 1;
constexpr std::uint32_t max_payload = 65536;
constexpr std::uint32_t max_call_depth = 8;
constexpr std::uint32_t module_magic = 0x4d4e564f;
enum class Kind : std::uint32_t { request = 1, reply = 2, callback = 3, callback_reply = 4, fault = 5 };
enum class Operation : std::uint32_t { hello = 1, load_authored = 2, resolve = 3, call = 4, unload = 5, retire = 6 };
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
