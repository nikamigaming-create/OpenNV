#pragma once
#include "opennv_plugin_domain.h"
#include "opennv_plugin_crt.h"
#include "opennv_plugin_crt_support.h"
#include "opennv_plugin_crt_standard.h"
#include "opennv_plugin_find.h"
#include "opennv_plugin_crypto.h"
#include "opennv_plugin_cng_service.h"
#include "opennv_plugin_mappings.h"
#include "opennv_plugin_mutexes.h"
#include "opennv_plugin_shared_placement.h"
#include <windows.h>
#include <sddl.h>
#include <map>
#include <set>
#include <string>
#include <vector>
#pragma comment(lib, "advapi32.lib")

namespace opennv_domain {
struct PluginIoFile { std::uint64_t capability; HANDLE handle; bool writable; };
struct PluginIoEvent { std::uint64_t capability; std::uint32_t api, handle, count, error; };
struct PluginIoRuntime {
    std::wstring root, logical_directory;
    std::set<std::string> non_io_imports;
    std::map<std::uint32_t, PluginIoFile> files;
    std::unique_ptr<PluginCrtRuntime> crt;
    std::unique_ptr<CppRuntimeProvider> cpp;
    std::unique_ptr<PluginCrtStandardRuntime> standard;
    std::unique_ptr<PluginCrtSupportRuntime> crt_support;
    std::unique_ptr<PluginFindRuntime> find;
    std::unique_ptr<PluginCryptoRuntime> crypto;
    std::unique_ptr<CngSystemClient> cng_client;
    std::unique_ptr<PluginMappingRuntime> mappings;
    std::unique_ptr<PluginMutexRuntime> mutexes;
    std::unique_ptr<SharedPlacementRuntime> placements;
    std::vector<PluginIoEvent> pending;
    std::vector<std::pair<void**, void*>> imports;
    HANDLE stdin_file = INVALID_HANDLE_VALUE, stdout_file = INVALID_HANDLE_VALUE, stderr_file = INVALID_HANDLE_VALUE;
    bool stdout_crt_owned = false, stderr_crt_owned = false;
    bool loader_retiring = false;
    std::uint32_t callbacks = 0, bound_imports = 0;
    std::uint64_t transaction = 0;
    std::uint32_t transaction_callbacks = 0;
    ~PluginIoRuntime() {
        for (const auto& file : files) CloseHandle(file.second.handle);
        if (stdin_file != INVALID_HANDLE_VALUE) CloseHandle(stdin_file);
        if (stdout_file != INVALID_HANDLE_VALUE && !stdout_crt_owned) CloseHandle(stdout_file);
        if (stderr_file != INVALID_HANDLE_VALUE && !stderr_crt_owned) CloseHandle(stderr_file);
    }
};
}
