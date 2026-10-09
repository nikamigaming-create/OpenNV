#pragma once
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <map>
#include <memory>
#include <string>
#include <vector>
#include "opennv_plugin_steam_hash.h"


#include "opennv_plugin_steam_callbacks.h"

namespace opennv_domain {
enum class SteamOperation : std::uint32_t { load = 1, initialize = 2, is_running = 3, user = 4, statistics = 5,
    logged_on = 6, set_achievement = 7, store_statistics = 8, run_callbacks = 9, shutdown = 10, unload = 11, release_interface = 12, utilities = 13, application_id = 14, request_statistics = 15, register_callback = 16, unregister_callback = 17 };
enum class SteamPhase : std::uint32_t { loaded = 0, initialize_entered = 1, initialize_returned = 2,
    shutdown_entered = 3, shutdown_returned = 4, retired = 5, failed = 6, load_entered = 7 };
struct SteamBorrowedInterface { void* pointer; std::uint32_t kind; };
enum class SteamCallStep : std::uint32_t { source_export = 1, application_interface = 2, application_id = 3, source_method = 4 };
enum class SteamReturnKind : std::uint32_t { none = 0, boolean_byte = 1, pointer_presence = 2, application_id = 3 };
struct SteamForeignPrefix {
    SteamCallStep step;
    SteamReturnKind kind;
    bool returned = false;
    std::uint32_t result = 0;
    CallReceipt abi{};
    std::wstring callable_path;
    std::string callable_sha;
};
struct SteamCallableSource {
    HMODULE retained = nullptr;
    HANDLE input = INVALID_HANDLE_VALUE;
    std::wstring path;
    std::string sha;
    ~SteamCallableSource() { if (retained) FreeLibrary(retained); if (input != INVALID_HANDLE_VALUE) CloseHandle(input); }
};
struct SteamRuntime {
    std::uint64_t session = 1, sequence = 0, next_token = 0, next_callback = 0, next_delivery = 0, source_request = 0;
    HMODULE module = nullptr;
    HANDLE input = INVALID_HANDLE_VALUE;
    std::wstring path;
    std::string sha;
    std::uint32_t user_slot = 0, achievement_slot = 0, store_slot = 0, app_slot = 0, expected_app = 0, current_app = 0;
    SteamPhase phase = SteamPhase::load_entered;
    SteamOperation attempted = SteamOperation::load;
    std::vector<SteamForeignPrefix> prefix;
    std::vector<std::unique_ptr<SteamSourceHash>> source_hashes;
    std::map<std::uint64_t, SteamBorrowedInterface> interfaces;
    std::map<std::uint64_t, std::unique_ptr<SteamSourceCallback>> callbacks;
    std::vector<SteamSourceCallbackPrefix> callback_prefix;
    std::map<HMODULE, std::unique_ptr<SteamCallableSource>> callable_sources;
    ~SteamRuntime() {
        // Terminal cleanup owns references, never an orderly Shutdown receipt.
        callable_sources.clear();
        if (module) FreeLibrary(module);
        if (input != INVALID_HANDLE_VALUE) CloseHandle(input);
    }
};
}
