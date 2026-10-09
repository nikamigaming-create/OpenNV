#pragma once
#include <array>
#include <cstdint>
#include <memory>
#include <vector>

namespace opennv_domain {
constexpr std::size_t source_call_site_limit = 256;
struct SourceCallSite {
    std::uint64_t generation = 0, module = 0, lease = 0;
    std::uint32_t id = 0, address = 0, target = 0, current = 0, slot = 0;
    std::uint32_t previous_protection = 0, getter_calls = 0;
    bool published = false, writable = false, restored = false, flushed = false;
    bool busy = false, retired = false, failed = false;
};
struct SourceCallSites {
    std::vector<std::unique_ptr<SourceCallSite>> sites;
    bool failed = false;
};
}
