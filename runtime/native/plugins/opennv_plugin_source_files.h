#pragma once
#include "opennv_plugin_domain.h"
#include <algorithm>
#include <vector>

namespace opennv_domain {
enum class SourceFileMethod : std::uint32_t { next_chunk = 1, advance_chunk = 2, read_chunk = 3, read32 = 4 };
struct SourceFileThunk { std::uint32_t address; SourceFileMethod method; Abi abi; };
struct SourceFilePage { void* base; std::uint32_t length; };
struct SourceFileRuntime {
    std::vector<SourceFileThunk> thunks;
    std::vector<SourceFilePage> pages;
    std::uint32_t calls = 0, outputs = 0;
};
}
