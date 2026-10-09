#pragma once
#include "opennv_plugin_values.h"
#include <array>
#include <memory>

namespace opennv_domain {
// Internal release objects, not NVSEArrayVarInterface::Array or its Element.
#pragma pack(push, 4)
struct NvseInternalArrayVar {
    std::uint32_t kind, data, count, capacity, id;
    std::uint8_t mod, key_type, packed, padding;
    std::uint32_t refs, ref_count, ref_capacity, lock_thread, lock_depth;
};
struct NvseInternalStringEntry { std::uint32_t key, element; };
#pragma pack(pop)
struct alignas(8) NvseInternalElement {
    std::uint32_t vtable, padding;
    std::uint8_t type, data_padding[3];
    std::uint32_t array;
    std::uint64_t bits;
};
struct alignas(8) NvseInternalNumericEntry { double key; std::uint32_t element, padding; };
static_assert(sizeof(NvseInternalArrayVar) == 44 && offsetof(NvseInternalArrayVar, lock_thread) == 36);
static_assert(sizeof(NvseInternalElement) == 24 && offsetof(NvseInternalElement, type) == 8 && offsetof(NvseInternalElement, bits) == 16);
static_assert(sizeof(NvseInternalNumericEntry) == 16 && sizeof(NvseInternalStringEntry) == 8);
struct NvseArrayObjectBlock { GuestRegion region{}; std::vector<std::uint8_t> bytes; };
struct NvseArrayObjectCell {
    NvseValueWire key, value;
    std::uint32_t address = 0, key_text = 0, value_text = 0;
    std::size_t pool = 0; std::uint32_t offset = 0;
};
struct NvseArrayObject {
    GuestRegion header{}; NvseInternalArrayVar image{};
    NvseArrayObjectBlock data, refs;
    std::vector<NvseArrayObjectBlock> cells, strings;
    std::vector<NvseArrayObjectBlock> retained_failures;
    std::vector<NvseArrayObjectCell> entries;
    std::uint32_t next_cell = 0;
    std::string source;
    bool published = false;
};
struct NvseArrayObjectRuntime {
    GuestArena arena;
    GuestRegion vtable{};
    std::array<std::uint32_t, 3> virtuals{};
    std::map<std::uint32_t, std::unique_ptr<NvseArrayObject>> objects;
    bool refreshing = false;
};
}
