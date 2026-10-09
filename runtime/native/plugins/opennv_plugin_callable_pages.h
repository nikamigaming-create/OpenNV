#pragma once
#include <windows.h>
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <vector>

namespace opennv_domain {
struct CallablePageFailure final : std::runtime_error {
    std::uint32_t code;
    CallablePageFailure(std::uint32_t value, const char* text) : std::runtime_error(text), code(value) { }
};
struct CallableEntry { std::uint32_t address; void* target; };

// These are parameters of our own deliberate SEH abort, retained only in the
// private exception receipt. They never authorize changing a foreign mapping.
inline thread_local std::array<ULONG_PTR, 15> first_callable_page_collision{};
inline thread_local DWORD first_callable_page_collision_count = 0;
inline void retain_callable_page_collision(const CallableEntry& call,
    std::uint32_t base, std::uint32_t granularity, SIZE_T query_result,
    DWORD query_error, const MEMORY_BASIC_INFORMATION& observed) noexcept {
    if (first_callable_page_collision_count) return;
    static_assert(EXCEPTION_MAXIMUM_PARAMETERS >= 15, "Private collision receipt requires SDK exception parameters.");
    first_callable_page_collision = {0x43414c31U, query_result, query_error,
        call.address, base, granularity, reinterpret_cast<ULONG_PTR>(call.target),
        reinterpret_cast<ULONG_PTR>(GetModuleHandleW(nullptr)),
        reinterpret_cast<ULONG_PTR>(observed.BaseAddress),
        reinterpret_cast<ULONG_PTR>(observed.AllocationBase), observed.RegionSize,
        observed.State, observed.Type, observed.Protect, observed.AllocationProtect};
    first_callable_page_collision_count = static_cast<DWORD>(first_callable_page_collision.size());
}

// One generation owns fixed callable addresses across every source family.
// Pages are shared by disjoint seven-byte entries. Removed entries become
// traps; their address reservations and identities stay retired until closure.
class OriginalCallablePages final {
    struct Page { void* base; std::uint32_t extent; bool executable; bool source_image; };
    struct Entry { std::uint64_t owner; CallableEntry call; bool live; };
    std::vector<Page> pages_;
    std::vector<Entry> entries_;
    std::uint32_t granularity_;
    std::uint64_t next_owner_ = 0;
    bool faulted_ = false;
    bool closed_ = false;
    void* source_image_base_ = nullptr;
    std::uint32_t source_image_bytes_ = 0, source_image_reserved_ = 0;

    void healthy() const {
        if (faulted_ || closed_) throw CallablePageFailure(ERROR_INVALID_STATE, "Callable generation is closed or retains a failed publication/retirement.");
    }
    void protect(Page& page, DWORD access) {
        DWORD previous = 0;
        if (!VirtualProtect(page.base, page.extent, access, &previous))
            throw CallablePageFailure(GetLastError(), "Callable page protection transition failed.");
        if (previous != static_cast<DWORD>(page.executable ? PAGE_EXECUTE_READ : PAGE_READWRITE))
            throw CallablePageFailure(ERROR_INVALID_DATA, "Callable page protection changed outside its owner.");
        page.executable = access == PAGE_EXECUTE_READ;
    }
    Page& page(std::uint32_t entry) {
        const auto base = entry & ~(granularity_ - 1);
        for (auto& retained : pages_) if (reinterpret_cast<std::uintptr_t>(retained.base) == base) return retained;
        throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Callable entry lost its exact reserved page.");
    }
    void install(const Entry& entry, bool trap) {
        auto& actual = page(entry.call.address);
        std::array<std::uint8_t, 7> bytes{};
        if (trap) bytes.fill(0xcc);
        else {
            bytes[0] = 0xb8; const auto target = reinterpret_cast<std::uintptr_t>(entry.call.target);
            if (!target || target > std::numeric_limits<std::uint32_t>::max())
                throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Callable target is not an actual x86 first-party entry.");
            const auto pointer = static_cast<std::uint32_t>(target);
            std::memcpy(bytes.data() + 1, &pointer, sizeof(pointer)); bytes[5] = 0xff; bytes[6] = 0xe0;
        }
        if (actual.executable) protect(actual, PAGE_READWRITE);
        std::memcpy(reinterpret_cast<void*>(entry.call.address), bytes.data(), bytes.size());
    }
    void publish_pages() {
        for (auto& retained : pages_) if (!retained.executable) {
            if (!FlushInstructionCache(GetCurrentProcess(), retained.base, retained.extent))
                throw CallablePageFailure(GetLastError(), "Callable publication failed instruction-cache synchronization.");
            protect(retained, PAGE_EXECUTE_READ);
        }
    }
public:
    OriginalCallablePages() {
        SYSTEM_INFO system{}; GetSystemInfo(&system); granularity_ = system.dwAllocationGranularity;
        if (!granularity_ || (granularity_ & (granularity_ - 1)) || granularity_ > 1024 * 1024)
            throw CallablePageFailure(ERROR_INVALID_DATA, "Callable mapping granularity is unowned.");
    }
    OriginalCallablePages(const OriginalCallablePages&) = delete;
    OriginalCallablePages& operator=(const OriginalCallablePages&) = delete;
    ~OriginalCallablePages() {
        for (const auto& retained : pages_) if (!retained.source_image && retained.base) VirtualFree(retained.base, 0, MEM_RELEASE);
        if (source_image_base_) VirtualFree(source_image_base_, 0, MEM_RELEASE);
    }

    void adopt_source_image(std::uint32_t base, std::uint32_t bytes) {
        healthy();
        if (source_image_base_ || !pages_.empty() || !entries_.empty() || !base || !bytes || base % granularity_)
            throw CallablePageFailure(ERROR_INVALID_STATE, "Source image reservation lacks a unique pre-publication owner.");
        const auto extent = (static_cast<std::uint64_t>(bytes) + granularity_ - 1) / granularity_ * granularity_;
        if (extent > std::numeric_limits<std::uint32_t>::max() || static_cast<std::uint64_t>(base) + extent > (1ULL << 32))
            throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source image reservation exceeds its actual x86 extent.");
        MEMORY_BASIC_INFORMATION observed{};
        if (!VirtualQuery(reinterpret_cast<void*>(base), &observed, sizeof(observed)) ||
            observed.BaseAddress != reinterpret_cast<void*>(base) || observed.AllocationBase != observed.BaseAddress ||
            observed.RegionSize != extent || observed.State != MEM_RESERVE || observed.Type != MEM_PRIVATE ||
            observed.Protect != 0 || observed.AllocationProtect != PAGE_NOACCESS)
            throw CallablePageFailure(ERROR_INVALID_DATA, "Source image has no actual parent-created no-access reservation.");
        source_image_base_ = reinterpret_cast<void*>(base); source_image_bytes_ = bytes;
        source_image_reserved_ = static_cast<std::uint32_t>(extent);
    }

    std::array<std::uint32_t, 3> source_image_identity() const {
        healthy();
        if (!source_image_base_)
            throw CallablePageFailure(ERROR_NOT_FOUND, "This native generation has no selected source image reservation.");
        return {static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(source_image_base_)),
            source_image_bytes_, source_image_reserved_};
    }

    std::uint64_t acquire(const std::vector<CallableEntry>& calls) {
        healthy();
        if (calls.empty() || calls.size() > 128 || next_owner_ == std::numeric_limits<std::uint64_t>::max() ||
            entries_.size() > 16384 - calls.size())
            throw CallablePageFailure(ERROR_NOT_ENOUGH_QUOTA, "Callable lease has an absent or exhausted entry budget.");
        for (std::size_t index = 0; index < calls.size(); ++index) {
            const auto& call = calls[index];
            if (!call.address || call.address > std::numeric_limits<std::uint32_t>::max() - 7 || !call.target ||
                call.address - (call.address & ~(granularity_ - 1)) > granularity_ - 7)
                throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Callable entry lacks a complete contained seven-byte extent.");
            const auto image_begin = static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(source_image_base_));
            if (source_image_base_ && call.address >= image_begin &&
                static_cast<std::uint64_t>(call.address) < image_begin + source_image_reserved_ &&
                static_cast<std::uint64_t>(call.address) + 7 > image_begin + source_image_bytes_)
                throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Callable entry leaves its selected source image extent.");
            const auto overlap = [&](std::uint32_t other) {
                return static_cast<std::uint64_t>(call.address) < static_cast<std::uint64_t>(other) + 7 &&
                    static_cast<std::uint64_t>(other) < static_cast<std::uint64_t>(call.address) + 7;
            };
            if (site_overlap(call.address, 7) ||
                std::any_of(entries_.begin(), entries_.end(), [&](const auto& existing) { return overlap(existing.call.address); }) ||
                std::any_of(calls.begin(), calls.begin() + static_cast<std::ptrdiff_t>(index), [&](const auto& earlier) { return overlap(earlier.address); }))
                throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Callable entry aliases a live or retired source entry.");
        }
        const auto owner = ++next_owner_;
        // Reserve host containers before native allocation: no throwing vector
        // growth may lose an OS reservation or unpublished entry identity.
        entries_.reserve(entries_.size() + calls.size()); pages_.reserve(pages_.size() + calls.size());
        try {
            for (const auto& call : calls) {
                const auto base = call.address & ~(granularity_ - 1);
                if (!std::any_of(pages_.begin(), pages_.end(), [&](const auto& retained) { return reinterpret_cast<std::uintptr_t>(retained.base) == base; })) {
                    const auto image_begin = static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(source_image_base_));
                    const auto source_image = source_image_base_ && base >= image_begin &&
                        static_cast<std::uint64_t>(base) + granularity_ <= image_begin + source_image_reserved_;
                    MEMORY_BASIC_INFORMATION observed{};
                    const auto queried = VirtualQuery(reinterpret_cast<void*>(base), &observed, sizeof(observed));
                    const auto query_error = queried ? ERROR_SUCCESS : GetLastError();
                    const auto owned_reservation = source_image && observed.AllocationBase == source_image_base_ &&
                        observed.State == MEM_RESERVE && observed.Type == MEM_PRIVATE && observed.Protect == 0 &&
                        observed.AllocationProtect == PAGE_NOACCESS;
                    if (!queried || (source_image ? !owned_reservation : observed.State != MEM_FREE) ||
                        reinterpret_cast<std::uintptr_t>(observed.BaseAddress) > base ||
                        static_cast<std::uint64_t>(base) + granularity_ > reinterpret_cast<std::uintptr_t>(observed.BaseAddress) + observed.RegionSize) {
                        retain_callable_page_collision(call, base, granularity_, queried, query_error, observed);
                        throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source callable page overlaps a mapping outside the shared first-party owner.");
                    }
                    auto* actual = VirtualAlloc(reinterpret_cast<void*>(base), granularity_,
                        source_image ? MEM_COMMIT : MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
                    if (actual != reinterpret_cast<void*>(base)) {
                        if (actual && !VirtualFree(actual, 0, MEM_RELEASE))
                            throw CallablePageFailure(GetLastError(), "Unexpected callable reservation could not retire.");
                        throw CallablePageFailure(GetLastError(), "Source callable reservation failed at its exact declared address.");
                    }
                    pages_.push_back({actual, granularity_, false, source_image}); std::memset(actual, 0xcc, granularity_);
                }
                entries_.push_back({owner, call, true}); install(entries_.back(), false);
            }
            publish_pages(); return owner;
        }
        catch (...) { faulted_ = true; throw; }
    }
    void release(std::uint64_t owner) {
        healthy();
        if (!owner || !std::any_of(entries_.begin(), entries_.end(), [&](const auto& entry) { return entry.owner == owner && entry.live; }))
            throw CallablePageFailure(ERROR_INVALID_HANDLE, "Callable source lease is absent or already retired.");
        try {
            for (auto& entry : entries_) if (entry.owner == owner && entry.live) { install(entry, true); entry.live = false; }
            publish_pages();
        }
        catch (...) { faulted_ = true; throw; }
    }
    #include "opennv_plugin_callable_sites.inc"

    void close() {
        // Terminal cleanup still attempts every retained reservation after a
        // publication fault. A failed page remains in this owner for retry.
        DWORD first = ERROR_SUCCESS;
        for (auto& retained : pages_) if (retained.base && !retained.source_image) {
            const auto previous = retained.base;
            if (!VirtualFree(retained.base, 0, MEM_RELEASE)) { if (!first) first = GetLastError(); continue; }
            retained.base = nullptr; MEMORY_BASIC_INFORMATION observed{};
            if ((!VirtualQuery(previous, &observed, sizeof(observed)) || observed.State != MEM_FREE) && !first) first = ERROR_INVALID_DATA;
        }
        if (source_image_base_) {
            const auto previous = source_image_base_;
            if (!VirtualFree(previous, 0, MEM_RELEASE)) { if (!first) first = GetLastError(); }
            else {
                source_image_base_ = nullptr;
                MEMORY_BASIC_INFORMATION observed{};
                if ((!VirtualQuery(previous, &observed, sizeof(observed)) || observed.State != MEM_FREE) && !first) first = ERROR_INVALID_DATA;
            }
        }
        if (first) { faulted_ = true; throw CallablePageFailure(first, "Shared callable pages did not all retire."); }
        pages_.clear(); entries_.clear(); site_entries_.clear(); closed_ = true;
    }
};
}
