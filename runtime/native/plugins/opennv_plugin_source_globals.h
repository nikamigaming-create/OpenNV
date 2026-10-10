#pragma once
#include <windows.h>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace opennv_domain {
struct EngineGlobal {
    std::uint64_t id = 0, lease = 0, object = 0;
    std::uint32_t address = 0, value = 0, kind = 0;
    bool published = false, retired = false, failed = false;
};
struct EngineStaticTls {
    HMODULE image = nullptr;
    std::wstring path;
    void* pointer = nullptr;
    std::uint32_t index = 0, extent = 0, source_extent = 0;
    bool entered = false, copied = false, retired = false, failed = false;
};
struct EngineGlobals {
    std::vector<std::unique_ptr<EngineGlobal>> cells;
    EngineStaticTls tls;
    bool failed = false;
};
}
