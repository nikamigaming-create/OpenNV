#pragma once
#include <array>
#include <cstdint>
#include <memory>
#include <vector>

namespace opennv_domain {
constexpr std::size_t source_call_site_limit = 256;
enum class SourceCodeKind : std::uint32_t { unbound, call, suppressed, retired };
struct SourceCallSite {
    std::uint64_t generation = 0, module = 0, lease = 0;
    std::uint32_t id = 0, address = 0, target = 0, current = 0, slot = 0;
    std::uint32_t previous_protection = 0, getter_calls = 0;
    bool published = false, writable = false, restored = false, flushed = false;
    bool busy = false, retired = false, failed = false;
    bool getter = false;
    std::array<std::uint8_t, 6> flushed_code{};
    SourceCodeKind kind = SourceCodeKind::unbound;
    std::uint32_t extent() const { return getter ? 6U : 5U; }
};
struct SourceCallSites {
    std::vector<std::unique_ptr<SourceCallSite>> sites;
    bool failed = false;
};
}
