#pragma once
#include <windows.h>
#include <cstdint>
#include <string>
#include <vector>

namespace opennv_domain {
enum class SteamSourceFileKind : std::uint32_t { provider = 1, callable = 2 };
enum class SteamSourceHashPhase : std::uint32_t { entered = 0, identified = 1, callback_entered = 2,
    managed_returned = 3, rechecked = 4, complete = 5, failed = 6 };
struct SteamSourceFileIdentity {
    std::uint32_t type = 0, attributes = 0, volume = 0;
    std::uint64_t created = 0, written = 0, index = 0, bytes = 0, extended_volume = 0, id_low = 0, id_high = 0;
};
struct SteamSourceHash {
    std::uint64_t ordinal = 0, request = 0, callback = 0;
    std::uint32_t operation = 0;
    SteamSourceFileKind kind = SteamSourceFileKind::provider;
    SteamSourceHashPhase phase = SteamSourceHashPhase::entered;
    std::wstring path;
    SteamSourceFileIdentity before{}, after{};
    bool before_copied = false, after_copied = false;
    std::string sha;
    DWORD error = ERROR_SUCCESS;
    // The actual SteamRuntime/SteamCallableSource owns the file handle. This
    // transaction retains copied public identity and managed hash receipts;
    // it creates no algorithm/hash/provider handle or original-DLL callback.
};
}
