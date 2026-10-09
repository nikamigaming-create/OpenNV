#pragma once
#include <cstdint>
#include <cstddef>

namespace opennv_domain {
// First-party state bridge, not TESForm, NVSEInterface or a game vtable.
constexpr std::uint32_t guest_state_magic = 0x53564e4f;
constexpr std::uint32_t guest_state_version = 1;
constexpr std::uint32_t max_guest_committed = 8 * 1024 * 1024;
constexpr std::uint32_t max_guest_reserved = 32 * 1024 * 1024;
constexpr std::uint32_t max_guest_live = 64;
constexpr std::uint32_t max_guest_reservations = 256;
constexpr std::uint32_t max_guest_transfer = 65536 - 64;
enum class GuestAccess : std::uint32_t { read_only = 1, read_write = 2 };
#pragma pack(push, 4)
struct GuestStateView {
    std::uint32_t magic, version;
    std::uint64_t capability;
    std::uint32_t cdecl_query, stdcall_query, thiscall_query, size;
};
#pragma pack(pop)
static_assert(sizeof(GuestStateView) == 32);
static_assert(offsetof(GuestStateView, thiscall_query) == 24);
using GuestCdeclQuery = std::uint32_t(__cdecl*)(const GuestStateView*, std::uint32_t, std::uint32_t);
using GuestStdcallQuery = std::uint32_t(__stdcall*)(const GuestStateView*, std::uint32_t, std::uint32_t);
}
