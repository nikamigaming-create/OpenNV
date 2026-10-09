#pragma once
#include "opennv_plugin_guest.h"
#include <windows.h>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <vector>

namespace opennv_domain {
struct GuestArenaFailure final : std::runtime_error {
    DWORD code;
    bool terminal;
    GuestArenaFailure(DWORD value, const char* reason, bool fatal = false)
        : std::runtime_error(reason), code(value), terminal(fatal) { }
};
struct GuestRegion {
    std::uint64_t id;
    void* base;
    std::uint32_t length, committed, reserved;
    GuestAccess access;
    bool state_object, live;
    GuestStateView state_view;
};
class GuestArena final {
    std::vector<GuestRegion> regions_;
    std::uint64_t next_id_ = 0, queries_ = 0;
    std::uint32_t committed_ = 0, reserved_ = 0, live_ = 0, page_size_, allocation_granularity_;

    static void verify(const GuestRegion& region) {
        MEMORY_BASIC_INFORMATION observed{};
        if (!VirtualQuery(region.base, &observed, sizeof(observed)) || observed.AllocationBase != region.base ||
            observed.BaseAddress != region.base || observed.State != MEM_COMMIT || observed.Type != MEM_PRIVATE ||
            observed.RegionSize != region.committed ||
            observed.Protect != static_cast<DWORD>(region.access == GuestAccess::read_only ? PAGE_READONLY : PAGE_READWRITE))
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Native guest committed extent/protection drifted.", true);
        if (region.committed < region.reserved) {
            const auto* tail = static_cast<const std::uint8_t*>(region.base) + region.committed;
            if (!VirtualQuery(tail, &observed, sizeof(observed)) || observed.AllocationBase != region.base ||
                observed.BaseAddress != tail || observed.State != MEM_RESERVE ||
                observed.RegionSize != region.reserved - region.committed)
                throw GuestArenaFailure(ERROR_INVALID_DATA, "Native guest reserved extent drifted.", true);
        }
    }
    static void verify_quarantine(const GuestRegion& region) {
        MEMORY_BASIC_INFORMATION observed{};
        if (!VirtualQuery(region.base, &observed, sizeof(observed)) || observed.AllocationBase != region.base ||
            observed.BaseAddress != region.base || observed.State != MEM_RESERVE || observed.RegionSize != region.reserved)
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Retired guest address lacks its exact reserved lifetime quarantine.", true);
    }

    GuestRegion create(std::uint32_t length, GuestAccess access, const void* initial,
        std::uint32_t initial_length, bool state_object) {
        if (length == 0 || length > max_guest_committed || initial_length > length ||
            (access != GuestAccess::read_only && access != GuestAccess::read_write) ||
            (access == GuestAccess::read_only && initial_length != length))
            throw GuestArenaFailure(ERROR_INVALID_PARAMETER, "Invalid guest allocation extent, initialization or access.");
        const auto committed = ((length - 1) / page_size_ + 1) * page_size_;
        const auto reserved = ((committed - 1) / allocation_granularity_ + 1) * allocation_granularity_;
        if (live_ >= max_guest_live || regions_.size() >= max_guest_reservations ||
            committed > max_guest_committed - committed_ || reserved > max_guest_reserved - reserved_)
            throw GuestArenaFailure(ERROR_NOT_ENOUGH_QUOTA, "Guest allocation exceeds live, committed or reserved lifetime budget.");
        if (next_id_ == std::numeric_limits<std::uint64_t>::max())
            throw GuestArenaFailure(ERROR_NOT_ENOUGH_QUOTA, "Guest capability identity exhausted.");
        const auto id = next_id_ + 1;
        auto* base = VirtualAlloc(nullptr, reserved, MEM_RESERVE, PAGE_NOACCESS);
        if (!base) throw GuestArenaFailure(GetLastError(), "Native guest allocation failed.");
        try {
            if (VirtualAlloc(base, committed, MEM_COMMIT, PAGE_READWRITE) != base)
                throw GuestArenaFailure(GetLastError(), "Native guest commitment failed.");
            if (initial_length) std::memcpy(base, initial, initial_length);
            if (state_object) static_cast<GuestStateView*>(base)->capability = id;
            if (access == GuestAccess::read_only) {
                DWORD previous = 0;
                if (!VirtualProtect(base, committed, PAGE_READONLY, &previous))
                    throw GuestArenaFailure(GetLastError(), "Native guest read-only protection failed.");
            }
            GuestRegion region{id, base, length, committed, reserved, access, state_object, true, {}};
            verify(region);
            if (state_object) region.state_view = *static_cast<const GuestStateView*>(base);
            regions_.push_back(region);
            next_id_ = id; committed_ += committed; reserved_ += reserved; ++live_;
            return region;
        }
        catch (...) {
            if (!VirtualFree(base, 0, MEM_RELEASE))
                throw GuestArenaFailure(GetLastError(), "Rejected guest allocation could not retire.", true);
            throw;
        }
    }
    GuestRegion& find(std::uint64_t id) {
        for (auto& region : regions_) if (region.id == id && region.live) { verify(region); return region; }
        throw GuestArenaFailure(ERROR_INVALID_HANDLE, "Guest capability is absent or retired.");
    }
    static void extent(const GuestRegion& region, std::uint32_t offset, std::uint32_t count) {
        if (count == 0 || count > max_guest_transfer || offset > region.length || count > region.length - offset)
            throw GuestArenaFailure(ERROR_INVALID_PARAMETER, "Guest access crosses its owned extent or transfer budget.");
    }
public:
#include "opennv_plugin_guest_replace_range.inc"
    GuestArena() {
        SYSTEM_INFO info{}; GetSystemInfo(&info); page_size_ = info.dwPageSize;
        allocation_granularity_ = info.dwAllocationGranularity;
        if (!page_size_ || (page_size_ & (page_size_ - 1)) || page_size_ > 65536 ||
            allocation_granularity_ < page_size_ || (allocation_granularity_ & (allocation_granularity_ - 1)) ||
            allocation_granularity_ > max_guest_committed)
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Native guest page size/allocation granularity is unowned.", true);
        regions_.reserve(max_guest_reservations);
    }
    GuestArena(const GuestArena&) = delete;
    GuestArena& operator=(const GuestArena&) = delete;
    ~GuestArena() { for (const auto& region : regions_) if (region.base) VirtualFree(region.base, 0, MEM_RELEASE); }
    std::uint32_t page_size() const { return page_size_; }
    std::uint32_t allocation_granularity() const { return allocation_granularity_; }
    std::uint32_t live() const { return live_; }
    std::uint32_t retired() const { return static_cast<std::uint32_t>(regions_.size()) - live_; }
    std::uint32_t committed() const { return committed_; }
    std::uint32_t reserved() const { return reserved_; }
    std::uint64_t queries() const { return queries_; }
    GuestRegion allocate(std::uint32_t length, GuestAccess access, const void* initial, std::uint32_t count) {
        return create(length, access, initial, count, false);
    }
    GuestRegion bind(std::uint32_t cdecl_query, std::uint32_t stdcall_query, std::uint32_t thiscall_query) {
        const GuestStateView view{guest_state_magic, guest_state_version, 0,
            cdecl_query, stdcall_query, thiscall_query, sizeof(GuestStateView)};
        return create(sizeof(view), GuestAccess::read_only, &view, sizeof(view), true);
    }
    GuestRegion inspect(std::uint64_t id) { return find(id); }
    const void* read(std::uint64_t id, std::uint32_t offset, std::uint32_t count) {
        const auto& region = find(id); extent(region, offset, count);
        return static_cast<const std::uint8_t*>(region.base) + offset;
    }
    void write(std::uint64_t id, std::uint32_t offset, const void* bytes, std::uint32_t count) {
        auto& region = find(id); extent(region, offset, count);
        if (region.access != GuestAccess::read_write)
            throw GuestArenaFailure(ERROR_ACCESS_DENIED, "Guest write targets a read-only capability.");
        std::memcpy(static_cast<std::uint8_t*>(region.base) + offset, bytes, count);
    }
    void seal(std::uint64_t id) {
        auto& region = find(id);
        if (region.access != GuestAccess::read_write || region.state_object)
            throw GuestArenaFailure(ERROR_INVALID_STATE, "Only a live writable data capability can publish immutable metadata.");
        DWORD previous = 0;
        if (!VirtualProtect(region.base, region.committed, PAGE_READONLY, &previous))
            throw GuestArenaFailure(GetLastError(), "Guest metadata could not publish its exact read-only extent.", true);
        if (previous != PAGE_READWRITE)
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Guest metadata protection changed during immutable publication.", true);
        region.access = GuestAccess::read_only; verify(region);
    }
    void replace_read_only(std::uint64_t id, const void* before, const void* after, std::uint32_t count) {
        auto& region = find(id); extent(region, 0, count);
        if (region.state_object || region.access != GuestAccess::read_only || count != region.length ||
            std::memcmp(region.base, before, count))
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Native class old image/owner changed before idle refresh.", true);
        DWORD previous = 0;
        if (!VirtualProtect(region.base, region.committed, PAGE_READWRITE, &previous))
            throw GuestArenaFailure(GetLastError(), "Native class refresh could not acquire its writable publication extent.", true);
        if (previous != PAGE_READONLY) {
            DWORD restored = 0;
            if (!VirtualProtect(region.base, region.committed, PAGE_READONLY, &restored))
                throw GuestArenaFailure(GetLastError(), "Native class refresh could not restore protection after an unexpected acquisition.", true);
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Native class refresh acquired unexpected protection.", true);
        }
        std::memcpy(region.base, after, count);
        DWORD writable = 0;
        if (!VirtualProtect(region.base, region.committed, PAGE_READONLY, &writable)) {
            const auto failure = GetLastError(); DWORD restored = 0;
            if (!VirtualProtect(region.base, region.committed, PAGE_READONLY, &restored))
                throw GuestArenaFailure(GetLastError(), "Native class refresh could not restore protection after a failed publication.", true);
            throw GuestArenaFailure(failure, "Native class refresh initially failed to retire its writable publication extent.", true);
        }
        if (writable != PAGE_READWRITE)
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Native class refresh retired unexpected protection.", true);
        verify(region);
        if (std::memcmp(region.base, after, count))
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Native class refresh bytes differ from the authoritative image.", true);
    }
    GuestRegion object(const void* pointer) {
        for (const auto& region : regions_) if (region.base == pointer && region.live && region.state_object) {
            verify(region);
            const auto& view = *static_cast<const GuestStateView*>(region.base);
            if (view.magic != guest_state_magic || view.version != guest_state_version ||
                view.size != sizeof(GuestStateView) || view.capability != region.id ||
                view.cdecl_query != region.state_view.cdecl_query || view.stdcall_query != region.state_view.stdcall_query ||
                view.thiscall_query != region.state_view.thiscall_query)
                throw GuestArenaFailure(ERROR_INVALID_DATA, "Guest state object layout/identity drifted.", true);
            return region;
        }
        throw GuestArenaFailure(ERROR_INVALID_HANDLE, "Native state query targets an unknown or retired object.");
    }
    void note_query() {
        if (queries_ == std::numeric_limits<std::uint64_t>::max())
            throw GuestArenaFailure(ERROR_NOT_ENOUGH_QUOTA, "Native state query identity exhausted.", true);
        ++queries_;
    }
    void release(std::uint64_t id) {
        auto& region = find(id);
        if (!VirtualFree(region.base, region.committed, MEM_DECOMMIT))
            throw GuestArenaFailure(GetLastError(), "Guest capability decommit failed.", true);
        verify_quarantine(region);
        region.live = false; committed_ -= region.committed; --live_;
        // Keep the reservation until generation retirement. Cached native pointers
        // must never alias a later object at the same address in this generation.
    }
    void retire() {
        if (live_) throw GuestArenaFailure(ERROR_BUSY, "Live guest capabilities prevent generation retirement.");
        for (auto& region : regions_) {
            verify_quarantine(region);
            if (!VirtualFree(region.base, 0, MEM_RELEASE))
                throw GuestArenaFailure(GetLastError(), "Guest lifetime quarantine could not retire.", true);
            const auto previous = region.base; region.base = nullptr;
            MEMORY_BASIC_INFORMATION observed{};
            if (!VirtualQuery(previous, &observed, sizeof(observed)) || observed.State != MEM_FREE)
                throw GuestArenaFailure(ERROR_INVALID_DATA, "Retired guest reservation still maps native pages.", true);
        }
        regions_.clear(); reserved_ = 0;
    }
};
}
