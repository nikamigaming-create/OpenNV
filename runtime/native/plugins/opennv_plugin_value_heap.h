#pragma once
#include "opennv_plugin_domain.h"
#include "opennv_plugin_values.h"

namespace opennv_domain {
enum class NvseHeapOperation : std::uint32_t { allocate = 1, release = 2 };
struct NvseHeapThunkDeclaration { std::uint32_t address; NvseHeapOperation operation; Abi abi; std::uint32_t receiver; };
struct NvseHeapEvent { std::uint64_t capability, caller; std::uint32_t pointer, length, operation, abi, receiver; };
struct NvseHeapAllocation { GuestRegion region; std::uint64_t caller; };
struct NvseHeapThunkPage { void* base; std::uint32_t length; };
struct NvseValueHeap {
    GuestArena arena;
    std::vector<NvseHeapThunkDeclaration> declarations;
    std::vector<NvseHeapThunkPage> pages;
    std::map<std::uint32_t, NvseHeapAllocation> allocations;
    std::vector<NvseHeapEvent> pending;
    bool loader_retiring = false;
    std::uint32_t created = 0, destroyed = 0;
    ~NvseValueHeap() { for (const auto& page : pages) if (page.base) VirtualFree(page.base, 0, MEM_RELEASE); }
};
}
