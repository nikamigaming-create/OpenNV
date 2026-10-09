#pragma once
#include <array>
#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace opennv_domain {
constexpr std::size_t engine_command_lifetime_limit = 1024;
struct EngineCommandBinding {
    std::uint64_t generation = 0, module = 0, callable_lease = 0;
    std::uint32_t id = 0, source = 0, target = 0, slot = 0;
    bool published = false, retired = false, busy = false, failed = false;
};
struct EngineCommandRuntime {
    std::vector<std::unique_ptr<EngineCommandBinding>> bindings;
    bool failed = false;
};
}
