#pragma once
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <map>
#include <memory>
#include <string>
#include <vector>

namespace opennv_domain {
struct PluginFindSource {
    std::wstring directory;
    HANDLE handle = INVALID_HANDLE_VALUE;
    bool attempted = false, exhausted = false;
};
struct PluginFindSearch {
    std::uint64_t id;
    std::wstring pattern;
    std::vector<PluginFindSource> sources;
    std::size_t current = 0;
    HANDLE public_handle = INVALID_HANDLE_VALUE;
    bool faulted = false;
};
struct PluginFindClose { std::uint32_t handle, result, error; };
struct PluginFindCloseEvent {
    std::uint64_t id;
    std::uint32_t public_handle;
    std::vector<PluginFindClose> handles;
};
struct PluginFindRuntime {
    std::map<std::uint64_t, std::unique_ptr<PluginFindSearch>> searches;
    std::vector<PluginFindCloseEvent> pending;
    std::vector<PluginFindClose> failures;
    ~PluginFindRuntime() {
        for (const auto& search : searches) for (const auto& source : search.second->sources)
            if (source.handle != INVALID_HANDLE_VALUE) (void)FindClose(source.handle);
    }
};
}
