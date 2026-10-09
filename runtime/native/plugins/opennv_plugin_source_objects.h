#pragma once
#include "opennv_plugin_guest_arena.h"
#include <array>
#include <memory>
#include <vector>
#include <utility>

namespace opennv_domain {
struct SourceObject {
    std::uint64_t id = 0, image_id = 0, metadata_id = 0;
    std::uint32_t pointer = 0, form = 0, editor = 0;
    std::uint32_t kind = 0, extent = 0;
    bool live = true;
};
struct SourceObjectRuntime {
    GuestArena arena;
    GuestRegion vtable{}, quest_vtable{};
    std::array<GuestRegion, 3> components{};
    std::vector<std::unique_ptr<SourceObject>> objects;
    bool script_interface = false;
};
struct NvseScriptInterface {
    bool (__cdecl* call_function)(void*, void*, void*, void*, std::uint8_t, ...);
    std::uint32_t (__cdecl* function_parameters)(void*, std::uint8_t*);
    bool (__cdecl* extract)(const void*, void*, std::uint32_t*, void*, void*, ...);
    bool (__cdecl* extract_format)(std::uint32_t, char*, const void*, void*, std::uint32_t*, void*, void*, std::uint32_t, ...);
    bool (__cdecl* call_alternate)(void*, void*, std::uint8_t, ...);
    void* (__cdecl* compile)(const char*);
    void* (__cdecl* compile_expression)(const char*);
    std::size_t (__stdcall* decompile)(void*, void*, char*);
};
static_assert(sizeof(NvseScriptInterface) == 32);
}
