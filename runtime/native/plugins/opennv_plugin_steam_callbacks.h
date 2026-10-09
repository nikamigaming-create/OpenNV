#pragma once
#include <cstddef>

namespace opennv_domain {
// Implementation-neutral x86 callback ABI: original overloaded slot order,
// one byte of flags and aligned signed callback ID. No copied SDK template,
// proprietary game receiver or member-function address is an input.
struct SteamSourceCallback {
    void** table = nullptr;
    std::uint8_t flags = 0;
    std::uint8_t padding[3]{};
    std::int32_t callback_id = 0;
    std::uint32_t source_id = 0, payload_size = 0;
    std::uint64_t token = 0;
    bool live = true;
};
static_assert(offsetof(SteamSourceCallback, flags) == 4);
static_assert(offsetof(SteamSourceCallback, callback_id) == 8);
struct SteamSourceCallbackPrefix {
    std::uint64_t sequence = 0, token = 0;
    std::uint32_t id = 0, flags = 0;
    bool alternate = false, io_failure = false;
    std::uint64_t api_call = 0;
    bool payload_copied = false, handler_returned = false;
    std::vector<unsigned char> payload;
};
}
