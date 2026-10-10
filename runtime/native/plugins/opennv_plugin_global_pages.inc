// Include inside the shared source-image/callable owner. Data pages are
// committed only for actual declared four-byte cells and never executable.
private:
    struct GlobalPage { void* base; std::uint32_t bytes; bool read_only; };
    struct GlobalCell { std::uint64_t lease; std::uint32_t address, value; bool live; };
    std::vector<GlobalPage> global_pages_;
    std::vector<GlobalCell> global_cells_;

    void global_protect(GlobalPage& value, DWORD access) {
        DWORD previous = 0;
        if (!VirtualProtect(value.base, value.bytes, access, &previous))
            throw CallablePageFailure(GetLastError(), "Source global data page protection failed.");
        if (previous != static_cast<DWORD>(value.read_only ? PAGE_READONLY : PAGE_READWRITE))
            throw CallablePageFailure(ERROR_INVALID_DATA, "Source data page protection changed outside its actual owner.");
        value.read_only = access == PAGE_READONLY;
    }
public:
    std::uint64_t acquire_global(std::uint32_t address, std::uint32_t value) {
        healthy(); SYSTEM_INFO system{}; GetSystemInfo(&system); const auto page_bytes = system.dwPageSize;
        if (!source_image_base_ || !address || address % 4 || !page_bytes || (page_bytes & (page_bytes - 1)) ||
            address < reinterpret_cast<std::uintptr_t>(source_image_base_) ||
            static_cast<std::uint64_t>(address) + 4 > static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(source_image_base_)) + source_image_bytes_ ||
            next_owner_ == std::numeric_limits<std::uint64_t>::max() ||
            std::any_of(global_cells_.begin(), global_cells_.end(), [&](const auto& cell) { return cell.address == address; }))
            throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source global aliases a retired/live cell or leaves its exact source image.");
        global_cells_.reserve(global_cells_.size() + 1); global_pages_.reserve(global_pages_.size() + 1);
        const auto lease = ++next_owner_; global_cells_.push_back({lease, address, value, true});
        const auto base = address & ~(page_bytes - 1);
        try {
            auto found = std::find_if(global_pages_.begin(), global_pages_.end(), [&](const auto& row) { return reinterpret_cast<std::uintptr_t>(row.base) == base; });
            if (found == global_pages_.end()) {
                MEMORY_BASIC_INFORMATION memory{};
                if (!VirtualQuery(reinterpret_cast<void*>(base), &memory, sizeof(memory)) ||
                    memory.AllocationBase != source_image_base_ || memory.State != MEM_RESERVE || memory.Type != MEM_PRIVATE ||
                    memory.Protect != 0 || memory.AllocationProtect != PAGE_NOACCESS ||
                    reinterpret_cast<std::uintptr_t>(memory.BaseAddress) > base ||
                    static_cast<std::uint64_t>(base) + page_bytes > static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(memory.BaseAddress)) + memory.RegionSize)
                    throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source global data page conflicts with a committed code/foreign owner.");
                auto* actual = VirtualAlloc(reinterpret_cast<void*>(base), page_bytes, MEM_COMMIT, PAGE_READWRITE);
                if (actual != reinterpret_cast<void*>(base))
                    throw CallablePageFailure(GetLastError(), "Source global page could not commit its actual image reservation.");
                global_pages_.push_back({actual, page_bytes, false}); found = global_pages_.end() - 1;
                std::memset(actual, 0xcc, page_bytes);
                // Non-cell bytes remain unpublished allocation state, not
                // interpreted object fields or an initialized engine class.
            }
            if (found->read_only) global_protect(*found, PAGE_READWRITE);
            std::memcpy(reinterpret_cast<void*>(address), &value, sizeof(value)); global_protect(*found, PAGE_READONLY);
            return lease;
        }
        catch (...) { faulted_ = true; throw; }
    }
    void require_global(std::uint64_t lease, std::uint32_t address, std::uint32_t value, bool retired = false) const {
        healthy();
        const auto found = std::find_if(global_cells_.begin(), global_cells_.end(), [&](const auto& row) { return row.lease == lease; });
        if (!lease || found == global_cells_.end() || found->address != address || found->live == retired || found->value != value)
            throw CallablePageFailure(ERROR_INVALID_HANDLE, "Source global lost its exact value/lease/current phase.");
        MEMORY_BASIC_INFORMATION memory{}; std::uint32_t actual = 0; SIZE_T read = 0;
        if (!VirtualQuery(reinterpret_cast<void*>(address), &memory, sizeof(memory)) || memory.AllocationBase != source_image_base_ ||
            memory.State != MEM_COMMIT || memory.Type != MEM_PRIVATE || memory.Protect != PAGE_READONLY ||
            !ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(address), &actual, sizeof(actual), &read) ||
            read != sizeof(actual) || actual != (retired ? 0U : value))
            throw CallablePageFailure(ERROR_INVALID_DATA, "Source global changed outside its retained read-only publication.");
    }
    void release_global(std::uint64_t lease) {
        healthy();
        auto cell = std::find_if(global_cells_.begin(), global_cells_.end(), [&](const auto& row) { return row.lease == lease && row.live; });
        if (!lease || cell == global_cells_.end()) throw CallablePageFailure(ERROR_INVALID_HANDLE, "Source global release repeats an absent/retired lease.");
        try {
            require_global(lease, cell->address, cell->value);
            auto page = std::find_if(global_pages_.begin(), global_pages_.end(), [&](const auto& row) {
                return cell->address >= reinterpret_cast<std::uintptr_t>(row.base) &&
                    static_cast<std::uint64_t>(cell->address) + 4 <= static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(row.base)) + row.bytes;
            });
            if (page == global_pages_.end()) throw CallablePageFailure(ERROR_INVALID_DATA, "Source global lost its actual data page.");
            global_protect(*page, PAGE_READWRITE); const std::uint32_t retired = 0;
            std::memcpy(reinterpret_cast<void*>(cell->address), &retired, sizeof(retired));
            global_protect(*page, PAGE_READONLY); cell->live = false; require_global(lease, cell->address, cell->value, true);
        }
        catch (...) { faulted_ = true; throw; }
    }
