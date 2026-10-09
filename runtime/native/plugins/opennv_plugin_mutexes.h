#pragma once
#include <windows.h>
#include <cstdint>
#include <map>
#include <vector>
#include <algorithm>
#include "opennv_plugin_mutex_namespace.h"

namespace opennv_domain {
enum class MutexApi : std::uint32_t {
    create_a = 1, create_w, create_ex_a, create_ex_w, open_a, open_w,
    release, close, wait, wait_ex, wait_many, wait_many_ex
};
struct PluginMutexHandle {
    std::uint64_t capability = 0;
    HANDLE handle = nullptr;
    bool live = false;
};
struct PluginMutexDetachEvent {
    std::uint64_t sequence, capability;
    std::uint32_t thread;
    MutexApi api;
    HANDLE handle;
    DWORD incoming;
    BOOL result = FALSE;
    DWORD last_error = 0;
    bool completed = false;
};
struct PluginMutexRuntime {
    std::map<std::uint64_t, PluginMutexHandle> handles;
    std::unique_ptr<PluginMutexNamespaceRuntime> namespaces;
    std::vector<PluginMutexDetachEvent> detached;
    std::size_t flushed = 0;
    std::uint64_t call = 0, sdk_call = 0;
    HMODULE image = nullptr;
    bool pending = false, detach_entered = false, in_free = false, detach_returned = false;
    BOOL unload_result = FALSE;
    DWORD unload_error = 0;
    // Fault closure belongs to the actual process. A destructor does not invent
    // successful ReleaseMutex/CloseHandle receipts for still-live native objects.
};
}
