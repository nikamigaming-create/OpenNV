#pragma once
#include "opennv_plugin_nvse.h"

namespace opennv_domain {
// Public xNVSE CommandTable v2. JIP's 36-byte client prefix ends before the
// final DLL-name lookup. No client PDB or private game layout defines this ABI.
#pragma pack(push, 4)
struct NvseCommandTableInterface {
    std::uint32_t version;
    const NvseCommandInfo* (__cdecl* start)();
    const NvseCommandInfo* (__cdecl* end)();
    const NvseCommandInfo* (__cdecl* by_opcode)(std::uint32_t);
    const NvseCommandInfo* (__cdecl* by_name)(const char*);
    std::uint32_t (__cdecl* return_type)(const NvseCommandInfo*);
    std::uint32_t (__cdecl* required_version)(const NvseCommandInfo*);
    const NvsePluginInfo* (__cdecl* parent)(const NvseCommandInfo*);
    const NvsePluginInfo* (__cdecl* plugin_name)(const char*);
    const NvsePluginInfo* (__cdecl* plugin_dll)(const char*);
};
#pragma pack(pop)
static_assert(sizeof(NvseCommandTableInterface) == 40);
enum class CommandTableCall : std::uint32_t {
    start = 1, end, by_opcode, by_name, return_type, required_version,
    parent_plugin, plugin_name, plugin_dll, publish_command, publish_plugin
};
struct NativeCommandPublication {
    std::uint64_t registration = 0;
    NvseCommandInfo descriptor{}, original{};
    std::string name, alias, help, expected_name, expected_alias, expected_help;
    std::vector<NvseRegisteredParameter> parameters;
    std::vector<NvseParamInfo> original_parameters;
};
struct NativePluginPublication {
    std::uint64_t generation = 0, module = 0;
    std::uint32_t handle = 0;
    std::string name;
    NvsePluginInfo descriptor{};
};
struct NativeCommandTableRuntime {
    std::vector<std::unique_ptr<NativeCommandPublication>> commands;
    std::vector<std::unique_ptr<NativePluginPublication>> plugins;
};
}
