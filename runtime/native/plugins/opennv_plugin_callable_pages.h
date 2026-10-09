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

// One generation owns fixed callable addresses across every source family.
// Pages are shared by disjoint seven-byte entries. Removed entries become
// traps; their address reservations and identities stay retired until closure.
class OriginalCallablePages final {
    struct Page { void* base; std::uint32_t extent; bool executable; };
    struct Entry { std::uint64_t owner; CallableEntry call; bool live; };
    std::vector<Page> pages_;
    std::vector<Entry> entries_;
    std::uint32_t granularity_;
    std::uint64_t next_owner_ = 0;
    bool faulted_ = false;
    bool closed_ = false;

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
    ~OriginalCallablePages() { for (const auto& retained : pages_) VirtualFree(retained.base, 0, MEM_RELEASE); }

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
            const auto overlap = [&](std::uint32_t other) {
                return static_cast<std::uint64_t>(call.address) < static_cast<std::uint64_t>(other) + 7 &&
                    static_cast<std::uint64_t>(other) < static_cast<std::uint64_t>(call.address) + 7;
            };
            if (std::any_of(entries_.begin(), entries_.end(), [&](const auto& existing) { return overlap(existing.call.address); }) ||
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
                    MEMORY_BASIC_INFORMATION observed{};
                    if (!VirtualQuery(reinterpret_cast<void*>(base), &observed, sizeof(observed)) || observed.State != MEM_FREE ||
                        reinterpret_cast<std::uintptr_t>(observed.BaseAddress) > base ||
                        static_cast<std::uint64_t>(base) + granularity_ > reinterpret_cast<std::uintptr_t>(observed.BaseAddress) + observed.RegionSize)
                        throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source callable page overlaps a mapping outside the shared first-party owner.");
                    auto* actual = VirtualAlloc(reinterpret_cast<void*>(base), granularity_, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
                    if (actual != reinterpret_cast<void*>(base)) {
                        if (actual && !VirtualFree(actual, 0, MEM_RELEASE))
                            throw CallablePageFailure(GetLastError(), "Unexpected callable reservation could not retire.");
                        throw CallablePageFailure(GetLastError(), "Source callable reservation failed at its exact declared address.");
                    }
                    pages_.push_back({actual, granularity_, false}); std::memset(actual, 0xcc, granularity_);
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
    void close() {
        // Terminal cleanup still attempts every retained reservation after a
        // publication fault. A failed page remains in this owner for retry.
        DWORD first = ERROR_SUCCESS;
        for (auto& retained : pages_) if (retained.base) {
            const auto previous = retained.base;
            if (!VirtualFree(retained.base, 0, MEM_RELEASE)) { if (!first) first = GetLastError(); continue; }
            retained.base = nullptr; MEMORY_BASIC_INFORMATION observed{};
            if ((!VirtualQuery(previous, &observed, sizeof(observed)) || observed.State != MEM_FREE) && !first) first = ERROR_INVALID_DATA;
        }
        if (first) { faulted_ = true; throw CallablePageFailure(first, "Shared callable pages did not all retire."); }
        pages_.clear(); entries_.clear(); closed_ = true;
    }
};
}
