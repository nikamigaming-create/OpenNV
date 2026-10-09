#pragma once
#include <windows.h>
#include <cstdint>
#include <exception>
#include <vector>

namespace opennv_domain {
struct SharedPlacementRelease {
    std::uint32_t kind = 0, base = 0;
    std::uint64_t view = 0;
};
struct SharedPlacementRuntime {
    std::vector<SharedPlacementRelease> pending;
};
}

namespace {
void* shared_placement_map(std::uint32_t, std::uint64_t, std::uint64_t, HANDLE,
    std::uint64_t, std::uint64_t, std::uint32_t, void*, DWORD&);
void shared_placement_source_released(std::uint32_t, std::uint64_t, void*);
void shared_placement_flush_detach();
void shared_placement_require_retired();
void shared_placement_abort_map(std::uint64_t, void*, const std::exception&);
}
