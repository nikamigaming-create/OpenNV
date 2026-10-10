#pragma once

#include <cstdint>
#include <memory>
#include <vector>

namespace opennv_domain {
inline constexpr std::size_t source_field_method_limit = 256;
struct SourceFieldMethod {
    std::uint32_t id = 0, entry = 0, abi = 0, receiver_kind = 0, slot = 0, target = 0, callbacks = 0;
    std::uint64_t generation = 0, module = 0, lease = 0;
    bool published = false, busy = false, retired = false, failed = false;
};
struct SourceFieldMethods {
    std::vector<std::unique_ptr<SourceFieldMethod>> methods;
    bool failed = false;
};
}
