#pragma once
#include <windows.h>
#include <cstdint>
#include <map>
#include <memory>
#include <stdio.h>
#include <vector>

namespace opennv_domain {
enum class CrtSupportKind : std::uint32_t { buffer = 1, cells = 2, lock = 3, unlock = 4, position = 5, close = 6 };
enum class CrtBufferOwner : std::uint32_t { none = 0, original_module = 1, shared_heap = 2 };
struct CrtBufferLease {
    void* buffer = nullptr;
    std::uint32_t length = 0;
    CrtBufferOwner owner = CrtBufferOwner::none;
    std::uint64_t allocation = 0;
};
struct CrtSupportState {
    std::uint64_t provider = 0, route = 0;
    void* stream = nullptr;
    CrtBufferLease buffer;
    char** base_cell = nullptr;
    char** pointer_cell = nullptr;
    int* count_cell = nullptr;
    DWORD thread = 0;
    std::uint32_t lock_depth = 0;
    std::uint32_t active_calls = 0;
};
struct CrtDeferredCallback { std::uint32_t operation; std::vector<std::uint8_t> payload; bool heap = false; };
struct PluginCrtSupportRuntime {
    std::map<std::uint32_t, std::shared_ptr<CrtSupportState>> streams;
    std::vector<CrtDeferredCallback> pending;
};
}
