#pragma once
#include <windows.h>
#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace opennv_domain {
struct PluginMappingObject {
    std::uint64_t id, file, maximum;
    std::uint32_t protection;
    bool writable;
    std::wstring name, declared_name;
};
struct PluginMappingHandle { std::uint64_t id, object; HANDLE handle; std::uint32_t request_flags; };
struct PluginMappingView {
    std::uint64_t id, object, offset, logical_bytes, region_bytes;
    void* base;
    std::uint32_t access, state, protection;
    HANDLE shared_handle = nullptr;
    bool common_placement = false;
};
struct PluginMappingEvent {
    std::uint32_t operation;
    std::uint64_t id;
    std::uint32_t address, requested;
    bool success;
    DWORD error;
};
struct PluginMappingRuntime {
    std::map<std::uint64_t, PluginMappingObject> objects;
    std::map<std::uint32_t, PluginMappingHandle> handles;
    std::map<std::uint32_t, PluginMappingView> views;
    std::vector<PluginMappingEvent> pending;
    ~PluginMappingRuntime() {
        // Real terminal cleanup is distinct from orderly original API calls.
        for (const auto& view : views) { (void)UnmapViewOfFile(view.second.base);
            if (view.second.shared_handle) (void)CloseHandle(view.second.shared_handle); }
        for (const auto& handle : handles) (void)CloseHandle(handle.second.handle);
    }
};
}
