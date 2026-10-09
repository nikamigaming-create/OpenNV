#pragma once
#include "opennv_plugin_nvse.h"
#include "opennv_plugin_guest_arena.h"
#include <cstddef>
#include <map>

namespace opennv_domain {
// The declared Array pointer is an opaque integer ID, never a game object.
struct NvseOpaqueArray;
struct alignas(8) NvseArrayElement {
    union { double number; const char* string; void* form; NvseOpaqueArray* array; std::uint64_t bits; } data;
    std::uint8_t type;
    std::uint8_t padding[7];
};
static_assert(sizeof(NvseArrayElement) == 16);
static_assert(offsetof(NvseArrayElement, type) == 8);
static_assert(alignof(NvseArrayElement) == 8);
#pragma pack(push, 4)
struct NvseStringVarInterface {
    std::uint32_t version;
    const char* (__cdecl* get)(std::uint32_t);
    void (__cdecl* set)(std::uint32_t, const char*);
    std::uint32_t (__cdecl* create)(const char*, void*);
    void (__cdecl* register_interface)(NvseStringVarInterface*);
    bool (__cdecl* assign)(const NvseParamInfo*, void*, void*, void*, void*, void*, double*, std::uint32_t*, const char*);
};
struct NvseArrayVarInterface {
    NvseOpaqueArray* (__cdecl* create_array)(const NvseArrayElement*, std::uint32_t, void*);
    NvseOpaqueArray* (__cdecl* create_string_map)(const char**, const NvseArrayElement*, std::uint32_t, void*);
    NvseOpaqueArray* (__cdecl* create_map)(const double*, const NvseArrayElement*, std::uint32_t, void*);
    bool (__cdecl* assign)(NvseOpaqueArray*, double*);
    void (__cdecl* set)(NvseOpaqueArray*, const NvseArrayElement&, const NvseArrayElement&);
    void (__cdecl* append)(NvseOpaqueArray*, const NvseArrayElement&);
    std::uint32_t (__cdecl* size)(NvseOpaqueArray*);
    NvseOpaqueArray* (__cdecl* lookup)(std::uint32_t);
    bool (__cdecl* get)(NvseOpaqueArray*, const NvseArrayElement&, NvseArrayElement&);
    bool (__cdecl* elements)(NvseOpaqueArray*, NvseArrayElement*, NvseArrayElement*);
    std::uint32_t (__cdecl* packed)(NvseOpaqueArray*);
    int (__cdecl* kind)(NvseOpaqueArray*);
    bool (__cdecl* has_key)(NvseOpaqueArray*, const NvseArrayElement&);
};
#pragma pack(pop)
static_assert(sizeof(NvseStringVarInterface) == 24);
static_assert(sizeof(NvseArrayVarInterface) == 52); // No version DWORD in this public ABI.
struct NvseValueWire {
    std::uint32_t type = 0, identity = 0;
    double number = 0;
    std::string text;
};
struct NvseValueString { GuestRegion region{}; std::string bytes; };
struct NvseValueRuntime {
    GuestArena arena;
    GuestRegion tables{};
    std::map<std::uint32_t, NvseValueString> strings;
    NvseStringVarInterface registered{};
    bool has_registered = false;
    std::uint32_t callbacks = 0;
};
}
