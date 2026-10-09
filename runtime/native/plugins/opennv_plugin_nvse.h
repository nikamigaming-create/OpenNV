#pragma once
#include <cstdint>
#include <array>
#include <string>
#include <vector>
#include <memory>

// First-party declarations of the public xNVSE 6.4.9 Windows/x86 ABI. These
// structures describe callable interfaces, not game executable/object layouts.
namespace opennv_domain {
enum class NvseHostCall : std::uint32_t {
    query_interface = 1, set_opcode = 2, register_command = 3,
    register_listener = 4, serialization_callback = 5, unsupported = 6,
    dispatch_message = 7, delivered_message = 8, delivered_serialization = 9, begin_serialization = 10
};
enum class NvsePhase : std::uint32_t { mapped, querying, queried_true, queried_false, loading, loaded_true, loaded_false };
enum class NvseSerializationEvent : std::uint32_t { save, load, new_game, pre_load };
constexpr std::uint32_t nvse_invalid_handle = 0xffffffffU;
constexpr std::uint32_t nvse_expression_parser = 0x08000000U;

#pragma pack(push, 4)
struct NvseParamInfo { const char* name; std::uint32_t type, optional; };
struct NvseCommandInfo {
    const char* name;
    const char* alias;
    std::uint32_t opcode;
    const char* help;
    std::uint16_t needs_parent, parameter_count;
    const NvseParamInfo* parameters;
    void* execute;
    void* parse;
    void* evaluate;
    std::uint32_t flags;
};
struct NvsePluginInfo { std::uint32_t info_version; const char* name; std::uint32_t version; };
struct NvseCoreInterface {
    std::uint32_t nvse_version, runtime_version, editor_version, is_editor;
    bool (__cdecl* register_command)(NvseCommandInfo*);
    void (__cdecl* set_opcode)(std::uint32_t);
    void* (__cdecl* query_interface)(std::uint32_t);
    std::uint32_t (__cdecl* get_handle)();
    bool (__cdecl* register_typed)(NvseCommandInfo*, std::uint8_t);
    const char* (__cdecl* runtime_directory)();
    std::uint32_t no_gore;
    void (__cdecl* expression_utils)(void*);
    bool (__cdecl* register_versioned)(NvseCommandInfo*, std::uint8_t, std::uint32_t);
};
struct NvseMessage { const char* sender; std::uint32_t type, length; void* data; };
using NvseMessageCallback = void(__cdecl*)(NvseMessage*);
using NvseSerializationCallback = void(__cdecl*)(void*);
struct NvseMessagingInterface {
    std::uint32_t version;
    bool (__cdecl* listen)(std::uint32_t, const char*, NvseMessageCallback);
    bool (__cdecl* dispatch)(std::uint32_t, std::uint32_t, void*, std::uint32_t, const char*);
};
struct NvseSerializationInterface {
    std::uint32_t version;
    void (__cdecl* set_save)(std::uint32_t, NvseSerializationCallback);
    void (__cdecl* set_load)(std::uint32_t, NvseSerializationCallback);
    void (__cdecl* set_new_game)(std::uint32_t, NvseSerializationCallback);
    bool (__cdecl* write_record)(std::uint32_t, std::uint32_t, const void*, std::uint32_t);
    bool (__cdecl* open_record)(std::uint32_t, std::uint32_t);
    bool (__cdecl* write_data)(const void*, std::uint32_t);
    bool (__cdecl* next_record)(std::uint32_t*, std::uint32_t*, std::uint32_t*);
    std::uint32_t (__cdecl* read_data)(void*, std::uint32_t);
    bool (__cdecl* resolve_form)(std::uint32_t, std::uint32_t*);
    void (__cdecl* set_pre_load)(std::uint32_t, NvseSerializationCallback);
    const char* (__cdecl* save_path)();
    std::uint32_t (__cdecl* peek_data)(void*, std::uint32_t);
    void (__cdecl* write8)(std::uint8_t);
    void (__cdecl* write16)(std::uint16_t);
    void (__cdecl* write32)(std::uint32_t);
    void (__cdecl* write64)(const void*);
    std::uint8_t (__cdecl* read8)();
    std::uint16_t (__cdecl* read16)();
    std::uint32_t (__cdecl* read32)();
    void (__cdecl* read64)(void*);
    void (__cdecl* skip)(std::uint32_t);
};
struct NvseTables { NvseCoreInterface core; NvseMessagingInterface messaging; NvseSerializationInterface serialization; };
#pragma pack(pop)
static_assert(sizeof(NvseParamInfo) == 12);
static_assert(sizeof(NvseCommandInfo) == 40);
static_assert(sizeof(NvsePluginInfo) == 12);
static_assert(sizeof(NvseCoreInterface) == 52);
static_assert(sizeof(NvseMessage) == 16);
static_assert(sizeof(NvseMessagingInterface) == 12);
static_assert(sizeof(NvseSerializationInterface) == 88);

struct NvseListener { std::uint32_t sender, function; };
struct NvseRegisteredParameter {
    std::uint32_t name_address, type, optional;
    std::string name;
};
struct NvseRegisteredCommand {
    std::uint32_t original, opcode, execute, parse, evaluate, parameter_count, needs_parent, return_type;
    std::vector<NvseRegisteredParameter> parameter_declarations;
    std::unique_ptr<NvseParamInfo[]> owned_parameters;
    NvseCommandInfo source_descriptor{};
    std::string source_name, source_alias, source_help;
};
struct NvseRuntime {
    NvsePhase phase = NvsePhase::mapped;
    std::uint32_t handle = 0, query = 0, load = 0;
    std::uint32_t interface_budget = 0, interface_calls = 0;
    std::uint32_t invocation_depth = 0;
    std::uint32_t command_events = 0, listener_events = 0, serialization_events = 0;
    std::uint64_t next_dispatch = 0;
    NvseTables* tables = nullptr;
    std::uint32_t tables_extent = 0;
    const char* directory = nullptr;
    NvsePluginInfo info{};
    std::string name;
    std::vector<NvseListener> listeners;
    std::vector<NvseRegisteredCommand> commands;
    std::array<std::uint32_t, 4> serialization{};
    ~NvseRuntime();
};
}
