// Include inside OriginalCallablePages' public section. These six-byte bodies
// are authored CALL/RET producers, never copies of an original code body.
private:
    struct SiteEntry { std::uint64_t owner; std::uint32_t address; bool live; };
    std::vector<SiteEntry> site_entries_;
    bool site_overlap(std::uint32_t address, std::uint32_t extent) const {
        return std::any_of(site_entries_.begin(), site_entries_.end(), [&](const auto& site) {
            return static_cast<std::uint64_t>(address) < static_cast<std::uint64_t>(site.address) + 6 &&
                static_cast<std::uint64_t>(site.address) < static_cast<std::uint64_t>(address) + extent;
        });
    }
public:
    std::uint64_t acquire_site(std::uint32_t address, std::uint32_t target) {
        healthy();
        const auto image = reinterpret_cast<std::uintptr_t>(source_image_base_);
        if (!source_image_base_ || !address || !target || address < image ||
            static_cast<std::uint64_t>(address) + 6 > static_cast<std::uint64_t>(image) + source_image_bytes_ ||
            next_owner_ == std::numeric_limits<std::uint64_t>::max() || site_overlap(address, 6) ||
            std::any_of(entries_.begin(), entries_.end(), [&](const auto& entry) {
                return static_cast<std::uint64_t>(address) < static_cast<std::uint64_t>(entry.call.address) + 7 &&
                    static_cast<std::uint64_t>(entry.call.address) < static_cast<std::uint64_t>(address) + 6;
            }))
            throw CallablePageFailure(ERROR_INVALID_ADDRESS, "Source CALL site overlaps another lease or leaves its actual reserved image.");
        pages_.reserve(pages_.size() + 2); site_entries_.reserve(site_entries_.size() + 1);
        const auto owner = ++next_owner_; site_entries_.push_back({owner, address, true});
        try {
            const auto first = address & ~(granularity_ - 1);
            const auto last = (address + 5) & ~(granularity_ - 1);
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
                auto* actual = VirtualAlloc(reinterpret_cast<void*>(static_cast<std::uintptr_t>(base)), granularity_, MEM_COMMIT, PAGE_READWRITE);
                if (actual != reinterpret_cast<void*>(static_cast<std::uintptr_t>(base)))
                    throw CallablePageFailure(GetLastError(), "Source CALL page failed its exact source reservation commitment.");
                pages_.push_back({actual, granularity_, false, true}); std::memset(actual, 0xcc, granularity_);
            }
            std::array<std::uint8_t, 6> body{0xe8, 0, 0, 0, 0, 0xc3};
            const auto displacement = target - address - 5U;
            std::memcpy(body.data() + 1, &displacement, 4);
            std::memcpy(reinterpret_cast<void*>(address), body.data(), body.size());
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
            const auto last = (found->address + 5) & ~(granularity_ - 1);
            for (std::uint64_t base = first; base <= last; base += granularity_) {
                auto& actual = page(static_cast<std::uint32_t>(base));
                if (actual.executable) protect(actual, PAGE_READWRITE);
            }
            std::memset(reinterpret_cast<void*>(found->address), 0xcc, 6);
            found->live = false; publish_pages();
        }
        catch (...) { faulted_ = true; throw; }
    }
