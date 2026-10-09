#pragma once
#include "opennv_plugin_guest_arena.h"
#include <windows.h>
#include <cstdint>
#include <vector>

namespace opennv_domain {
struct SharedHeapRegion {
    GuestRegion region{};
    HANDLE section = nullptr;
    bool failed = false;
};

// These bytes are section-backed from birth. Existing MEM_PRIVATE allocations
// are never replaced, relocated or relabeled as shared storage.
class SharedHeap final {
    std::vector<SharedHeapRegion> regions_;
    std::uint64_t next_ = 0;
    std::uint32_t mapped_ = 0, live_ = 0, page_ = 0;
    bool failed_ = false;
    static void verify(const SharedHeapRegion& value) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!value.section || !value.region.base || value.failed ||
            !VirtualQuery(value.region.base, &memory, sizeof(memory)) ||
            memory.AllocationBase != value.region.base || memory.BaseAddress != value.region.base ||
            memory.Type != MEM_MAPPED || memory.State != MEM_COMMIT ||
            memory.RegionSize != value.region.committed ||
            memory.Protect != static_cast<DWORD>(value.region.live ? PAGE_READWRITE : PAGE_NOACCESS))
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Shared heap section/view/protection lifetime drifted.", true);
    }
public:
    SharedHeap() {
        SYSTEM_INFO info{}; GetSystemInfo(&info); page_ = info.dwPageSize;
        if (!page_ || (page_ & (page_ - 1)) || page_ > 65536)
            throw GuestArenaFailure(ERROR_INVALID_DATA, "Shared heap has no observed page granularity.", true);
        regions_.reserve(max_guest_reservations);
    }
    SharedHeap(const SharedHeap&) = delete;
    SharedHeap& operator=(const SharedHeap&) = delete;
    ~SharedHeap() {
        // Process-terminal fallback is not an orderly free receipt.
        for (const auto& value : regions_) {
            if (value.region.base) (void)UnmapViewOfFile(value.region.base);
            if (value.section) (void)CloseHandle(value.section);
        }
    }
    std::uint32_t live() const { return live_; }
    template<class Mapper> GuestRegion allocate(std::uint32_t length, Mapper&& mapper) {
        if (failed_) throw GuestArenaFailure(ERROR_INVALID_STATE, "Failed shared allocation cannot replay.", true);
        if (!length || length > max_guest_committed || live_ >= max_guest_live ||
            regions_.size() >= max_guest_reservations || next_ == UINT64_MAX)
            throw GuestArenaFailure(ERROR_NOT_ENOUGH_QUOTA, "Shared heap extent/lifetime budget is exhausted.");
        const auto extent = ((length - 1) / page_ + 1) * page_;
        if (extent > max_guest_reserved - mapped_)
            throw GuestArenaFailure(ERROR_NOT_ENOUGH_QUOTA, "Shared heap address quarantine budget is exhausted.");
        SharedHeapRegion value; value.region.id = ++next_; value.region.length = length;
        value.region.committed = extent; value.region.reserved = extent;
        value.region.access = GuestAccess::read_write; value.region.live = true;
        value.section = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, extent, nullptr);
        if (!value.section) throw GuestArenaFailure(GetLastError(), "Shared heap paging-file section allocation failed.");
        regions_.push_back(value); auto& current = regions_.back();
        DWORD error = ERROR_SUCCESS;
        current.region.base = mapper(current.region.id, current.section, length, extent, error);
        if (!current.region.base) { current.failed = failed_ = true; throw GuestArenaFailure(error, "Shared heap common placement failed with retained section.", true); }
        try { verify(current); }
        catch (...) { current.failed = failed_ = true; throw; }
        mapped_ += extent; ++live_; return current.region;
    }
    SharedHeapRegion& inspect(std::uint64_t id) {
        for (auto& value : regions_) if (value.region.id == id && value.region.live) { verify(value); return value; }
        throw GuestArenaFailure(ERROR_INVALID_HANDLE, "Shared heap capability is absent or retired.");
    }
    SharedHeapRegion* containing(const void* pointer, std::uint32_t length) {
        const auto at = reinterpret_cast<std::uintptr_t>(pointer);
        for (auto& value : regions_) {
            const auto base = reinterpret_cast<std::uintptr_t>(value.region.base);
            if (!value.region.live || at < base || at - base >= value.region.length) continue;
            if (length > value.region.length - (at - base))
                throw GuestArenaFailure(ERROR_INVALID_PARAMETER, "Shared caller range crosses its actual allocation extent.");
            verify(value); return &value;
        }
        return nullptr;
    }
    void release(std::uint64_t id) {
        auto& value = inspect(id); DWORD previous = 0;
        if (!VirtualProtect(value.region.base, value.region.committed, PAGE_NOACCESS, &previous)) {
            value.failed = failed_ = true;
            throw GuestArenaFailure(GetLastError(), "Shared heap free could not quarantine its caller address.", true);
        }
        value.region.live = false; --live_; verify(value);
        if (previous != PAGE_READWRITE) { value.failed = failed_ = true; throw GuestArenaFailure(ERROR_INVALID_DATA, "Shared heap free acquired unexpected protection.", true); }
        // The inaccessible mapped address remains occupied until generation
        // retirement. A stale pointer cannot alias another allocation.
    }
    void retire() {
        if (failed_ || live_) throw GuestArenaFailure(ERROR_BUSY, "Shared heap still has live or failed native allocations.", true);
        for (auto& value : regions_) {
            if (value.region.base) {
                verify(value); const auto old = value.region.base;
                if (!UnmapViewOfFile(old)) { value.failed = failed_ = true; throw GuestArenaFailure(GetLastError(), "Shared heap view did not retire.", true); }
                value.region.base = nullptr; MEMORY_BASIC_INFORMATION memory{};
                if (!VirtualQuery(old, &memory, sizeof(memory)) || memory.State != MEM_FREE) {
                    value.failed = failed_ = true; throw GuestArenaFailure(ERROR_INVALID_DATA, "Shared heap retired address is still mapped.", true);
                }
            }
            if (value.section) {
                if (!CloseHandle(value.section)) { value.failed = failed_ = true; throw GuestArenaFailure(GetLastError(), "Shared heap section handle did not retire.", true); }
                value.section = nullptr;
            }
        }
        regions_.clear(); mapped_ = 0;
    }
};
}
