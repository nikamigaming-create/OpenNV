#pragma once
#include "opennv_plugin_guest_arena.h"
#include "opennv_plugin_callable_pages.h"
#include <memory>
#include <vector>

namespace opennv_domain {
enum class BinaryMethod : std::uint32_t {
    read = 1, read_inherited = 2, seek = 3, seek_current = 4, size = 5,
    cursor = 6, flush_read_buffer = 7, select_procedures = 8,
};
struct BinaryMethodEntry { std::uint32_t address; BinaryMethod method; Abi abi; std::uint32_t slot; };
struct BinaryFileBinding {
    std::uint64_t id = 0, contributor = 0, image = 0, buffer = 0;
    std::uint32_t pointer = 0, contributor_pointer = 0, extent = 0, capacity = 0;
    bool live = true, busy = false;
};
struct BinaryFileRuntime {
    GuestArena tables;
    GuestRegion table{};
    std::vector<BinaryMethodEntry> methods;
    std::vector<std::unique_ptr<BinaryFileBinding>> bindings;
    std::uint64_t callable_lease = 0, calls = 0, completed = 0;
    std::uint32_t extent = 0, slots = 0;
    std::uint32_t read = 0, write = 0, alternate_read = 0, alternate_write = 0;
};
}
