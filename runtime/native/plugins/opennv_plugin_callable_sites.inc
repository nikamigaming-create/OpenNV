// Include inside OriginalCallablePages' public section. Getter sites own
// authored CALL/RET producers; mutation-only sites own five traps before the
// original module writes. Neither copies an original code body.
private:
    struct SiteEntry { std::uint64_t owner; std::uint32_t address, extent; bool live; };
    std::vector<SiteEntry> site_entries_;
    bool site_overlap(std::uint32_t address, std::uint32_t extent) const {
        return std::any_of(site_entries_.begin(), site_entries_.end(), [&](const auto& site) {
            return static_cast<std::uint64_t>(address) < static_cast<std::uint64_t>(site.address) + site.extent &&
                static_cast<std::uint64_t>(site.address) < static_cast<std::uint64_t>(address) + extent;
        });
    }
public:
    std::array<std::uint32_t, 2> source_code_range(std::uint32_t address, std::uint32_t extent, bool publication) const {
        const auto alignment = publication ? granularity_ : page_bytes_;
        const auto first = address & ~(alignment - 1);
        const auto end = (static_cast<std::uint64_t>(address) + extent + alignment - 1) & ~static_cast<std::uint64_t>(alignment - 1);
        if (!extent || end > (1ULL << 32))
            throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source code SDK-page range overflowed its owned extent.");
        return {first, static_cast<std::uint32_t>(end - first)};
    }
    DWORD source_code_protection(std::uint32_t address, std::uint32_t extent) {
        healthy(); const auto range = source_code_range(address, extent, false); DWORD access = 0;
        for (std::uint64_t cursor = range[0]; cursor < static_cast<std::uint64_t>(range[0]) + range[1]; cursor += page_bytes_) {
            const auto& retained = page(static_cast<std::uint32_t>(cursor)); require_page(retained);
            if (!retained.source_image) throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source code protection left its selected image owner.");
            const auto observed = retained.protections[static_cast<std::size_t>((cursor - reinterpret_cast<std::uintptr_t>(retained.base)) / page_bytes_)];
            if (observed != PAGE_EXECUTE_READ && observed != PAGE_EXECUTE_READWRITE)
                throw CallablePageFailure(ERROR_INVALID_DATA, "Source code has no recorded executable SDK-page protection.");
            if (access && access != observed) throw CallablePageFailure(ERROR_INVALID_DATA, "Source code spans different SDK-page protections.");
            access = observed;
        }
        return access;
    }
    void source_code_protection_changed(std::uint32_t address, std::uint32_t extent, DWORD previous, DWORD access) {
        healthy(); const auto range = source_code_range(address, extent, false);
        if (access != PAGE_EXECUTE_READ && access != PAGE_EXECUTE_READWRITE)
            throw CallablePageFailure(ERROR_NOT_SUPPORTED, "Source code requested an unowned executable SDK protection.");
        auto& first = page(range[0]);
        if (first.protections[(range[0] - reinterpret_cast<std::uintptr_t>(first.base)) / page_bytes_] != previous)
            throw CallablePageFailure(ERROR_INVALID_DATA, "Source SDK result differs from its recorded first-page protection.");
        for (std::uint64_t cursor = range[0]; cursor < static_cast<std::uint64_t>(range[0]) + range[1]; cursor += page_bytes_) {
            auto& retained = page(static_cast<std::uint32_t>(cursor));
            if (!retained.source_image) throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source SDK protection escaped its owned image mapping.");
            retained.protections[static_cast<std::size_t>((cursor - reinterpret_cast<std::uintptr_t>(retained.base)) / page_bytes_)] = access;
        }
        (void)source_code_protection(address, extent);
    }
    std::uint64_t acquire_site(std::uint32_t address, std::uint32_t target, bool getter) {
        healthy();
        const auto extent = getter ? 6U : 5U;
        const auto image = reinterpret_cast<std::uintptr_t>(source_image_base_);
        if (!source_image_base_ || !address || (getter != (target != 0)) || address < image ||
            static_cast<std::uint64_t>(address) + extent > static_cast<std::uint64_t>(image) + source_image_bytes_ ||
            next_owner_ == std::numeric_limits<std::uint64_t>::max() || site_overlap(address, extent) ||
            std::any_of(entries_.begin(), entries_.end(), [&](const auto& entry) {
                return static_cast<std::uint64_t>(address) < static_cast<std::uint64_t>(entry.call.address) + 7 &&
                    static_cast<std::uint64_t>(entry.call.address) < static_cast<std::uint64_t>(address) + extent;
            }))
            throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source CALL site overlaps another lease or leaves its actual reserved image.");
        pages_.reserve(pages_.size() + 2); site_entries_.reserve(site_entries_.size() + 1);
        const auto owner = ++next_owner_; site_entries_.push_back({owner, address, extent, true});
        try {
            const auto first = address & ~(granularity_ - 1);
            const auto last = (address + extent - 1) & ~(granularity_ - 1);
            for (std::uint64_t base = first; base <= last; base += granularity_) {
                auto found = std::find_if(pages_.begin(), pages_.end(), [&](const auto& page) {
                    return reinterpret_cast<std::uintptr_t>(page.base) == base;
                });
                if (found != pages_.end()) {
                    if (!found->source_image || found->extent != granularity_)
                        throw CallablePageFailure(ERROR_INVALID_DATA, "Source CALL site lost its shared source-page owner.");
                    if (found->executable) protect(*found, PAGE_READWRITE);
                    continue;
                }
                MEMORY_BASIC_INFORMATION memory{};
                if (!VirtualQuery(reinterpret_cast<void*>(static_cast<std::uintptr_t>(base)), &memory, sizeof(memory)) ||
                    memory.AllocationBase != source_image_base_ || memory.State != MEM_RESERVE || memory.Type != MEM_PRIVATE ||
                    memory.Protect != 0 || memory.AllocationProtect != PAGE_NOACCESS ||
                    reinterpret_cast<std::uintptr_t>(memory.BaseAddress) > base ||
                    base + granularity_ > static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(memory.BaseAddress)) + memory.RegionSize)
                    throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source CALL page is occupied outside its original generation reservation.");
                std::vector<DWORD> protections(granularity_ / page_bytes_, PAGE_READWRITE);
                auto* actual = VirtualAlloc(reinterpret_cast<void*>(static_cast<std::uintptr_t>(base)), granularity_, MEM_COMMIT, PAGE_READWRITE);
                if (actual != reinterpret_cast<void*>(static_cast<std::uintptr_t>(base)))
                    throw CallablePageFailure(GetLastError(), "Source CALL page failed its exact source reservation commitment.");
                pages_.push_back({actual, granularity_, false, true, std::move(protections)}); std::memset(actual, 0xcc, granularity_);
            }
            if (getter) {
                std::array<std::uint8_t, 6> body{0xe8, 0, 0, 0, 0, 0xc3};
                const auto displacement = target - address - 5U;
                std::memcpy(body.data() + 1, &displacement, 4);
                std::memcpy(reinterpret_cast<void*>(address), body.data(), body.size());
            }
            else {
                // A write lease owns five traps until the original module's
                // actual mutation. No copied target, fake getter or RET exists.
                std::memset(reinterpret_cast<void*>(address), 0xcc, extent);
            }
            publish_pages(); return owner;
        }
        catch (...) { faulted_ = true; throw; }
    }
    void release_site(std::uint64_t owner) {
        healthy();
        auto found = std::find_if(site_entries_.begin(), site_entries_.end(), [&](const auto& site) { return site.owner == owner && site.live; });
        if (!owner || found == site_entries_.end())
            throw CallablePageFailure(ERROR_INVALID_HANDLE, "Source CALL site lease is absent or already retired.");
        try {
            const auto first = found->address & ~(granularity_ - 1);
            const auto last = (found->address + found->extent - 1) & ~(granularity_ - 1);
            for (std::uint64_t base = first; base <= last; base += granularity_) {
                auto& actual = page(static_cast<std::uint32_t>(base));
                if (actual.executable) protect(actual, PAGE_READWRITE);
            }
            std::memset(reinterpret_cast<void*>(found->address), 0xcc, found->extent);
            found->live = false; publish_pages();
        }
        catch (...) { faulted_ = true; throw; }
    }
