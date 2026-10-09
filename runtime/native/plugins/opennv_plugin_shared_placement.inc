constexpr std::uint32_t shared_placement_callback = 23;

SharedPlacementRuntime& shared_placement_owner() {
    auto& io = io_owner();
    if (!io.placements) io.placements = std::make_unique<SharedPlacementRuntime>();
    return *io.placements;
}
void* shared_placement_map(std::uint32_t kind, std::uint64_t object, std::uint64_t view,
    HANDLE section, std::uint64_t maximum, std::uint64_t offset, std::uint32_t length,
    void* preferred, DWORD& error) {
    if (io_owner().loader_retiring || cng_detach_active())
        throw Fatal(ERROR_NOT_SUPPORTED, "Original loader detach cannot construct a new two-process buffer.");
    Bytes request; request.put(1U); request.put(kind); request.put(object); request.put(view);
    request.put(address(section)); request.put(maximum); request.put(offset); request.put(length); request.put(address(preferred));
    auto response = io_host(shared_placement_callback, request); Reader result{response.data};
    const auto id = result.get<std::uint64_t>(); const auto base = result.get<std::uint32_t>();
    const auto extent = result.get<std::uint32_t>(); error = result.get<DWORD>(); result.finish();
    if (!id || !extent || (base != 0) != (error == ERROR_SUCCESS) || extent < length)
        throw Fatal(ERROR_INVALID_DATA, "Common placement lacks both actual map results or its retained failure disposition.");
    if (base) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(reinterpret_cast<void*>(base), &memory, sizeof(memory)) || memory.AllocationBase != reinterpret_cast<void*>(base) ||
            memory.BaseAddress != reinterpret_cast<void*>(base) || memory.Type != MEM_MAPPED || memory.State != MEM_COMMIT ||
            memory.Protect != PAGE_READWRITE || memory.RegionSize != extent || preferred && address(preferred) != base)
            throw Fatal(ERROR_INVALID_DATA, "Published common placement differs from the actual source mapping/address/protection.");
    }
    return reinterpret_cast<void*>(base);
}
void shared_placement_release_receipt(const SharedPlacementRelease& event) {
    Bytes request; request.put(2U); request.put(event.kind); request.put(event.view); request.put(event.base);
    auto response = io_host(shared_placement_callback, request); Reader receipt{response.data};
    if (receipt.get<std::uint32_t>() != 1)
        throw Fatal(ERROR_INVALID_DATA, "Original source release lost its independent service-view lifetime receipt.");
    receipt.finish();
}
void shared_placement_source_released(std::uint32_t kind, std::uint64_t view, void* base) {
    auto& io = io_owner();
    const SharedPlacementRelease event{kind, address(base), view};
    if (io.loader_retiring) { shared_placement_owner().pending.push_back(event); return; }
    shared_placement_release_receipt(event);
}
void shared_placement_flush_detach() {
    auto& io = io_owner();
    if (!io.placements) return;
    // Called after actual FreeLibrary returns, while its real frame is held.
    // Nothing performs IPC or maps memory under the original loader lock.
    auto& pending = io.placements->pending;
    while (!pending.empty()) {
        shared_placement_release_receipt(pending.front());
        pending.erase(pending.begin());
    }
}
void shared_placement_require_retired() {
    if (state->io && state->io->placements && !state->io->placements->pending.empty())
        throw Fatal(ERROR_BUSY, "Original shared source releases remain unacknowledged after actual detach.");
}
void shared_placement_abort_map(std::uint64_t view, void* base, const std::exception& primary) {
    try {
        if (!UnmapViewOfFile(base)) throw Fatal(GetLastError(), "Unpublished common source view cleanup failed.");
        shared_placement_source_released(2U, view, base);
    } catch (const std::exception& cleanup) {
        const std::string reason = std::string("Unpublished common source construction failed: ") + primary.what() +
            "; independent cleanup failed: " + cleanup.what();
        const auto* windows = dynamic_cast<const Fatal*>(&cleanup);
        throw Fatal(windows ? windows->code : ERROR_INVALID_STATE, reason.c_str());
    }
}
