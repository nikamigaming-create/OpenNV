constexpr std::uint32_t value_heap_event = 0x230;

NvseValueHeap& values_heap_owner() {
    if (!state || GetCurrentThreadId() != state->thread || !state->value_heap || !state->nvse)
        throw Fatal(ERROR_INVALID_THREAD_ID, "Native heap operation lacks its process-generation/thread/declaration owner.");
    if (state->calls.empty() && !state->value_heap->loader_retiring)
        throw Fatal(ERROR_INVALID_STATE, "Native heap operation has no original call or owned detach lifetime.");
    return *state->value_heap;
}
void values_heap_receipt(const NvseHeapEvent& event) {
    Bytes request; request.put(event.capability); request.put(event.pointer); request.put(event.length);
    request.put(event.operation); request.put(event.abi); request.put(event.receiver);
    if (values_scalar(value_heap_event, request) != 1) throw Fatal(ERROR_INVALID_DATA, "Native heap operation lacks a correlated C# lifetime receipt.");
}
void values_heap_note(const GuestRegion& region, std::uint32_t operation, std::uint32_t abi, std::uint32_t receiver) {
    auto& owner = values_heap_owner();
    const NvseHeapEvent event{region.id, values_caller_id(), address(region.base), region.length, operation, abi, receiver};
    if (owner.loader_retiring) owner.pending.push_back(event); else values_heap_receipt(event);
}
void values_heap_contract(NvseHeapOperation operation, Abi abi, std::uint32_t receiver) {
    const auto& owner = values_heap_owner();
    for (const auto& declaration : owner.declarations)
        if (declaration.operation == operation && declaration.abi == abi && declaration.receiver == receiver) return;
    throw Fatal(ERROR_NOT_SUPPORTED, "Native heap caller ABI/receiver lacks an exact selected-client declaration.");
}
void* values_heap_allocate(std::uint32_t length, std::uint32_t abi = 0, std::uint32_t receiver = 0) {
    auto& owner = values_heap_owner();
    if (abi) values_heap_contract(NvseHeapOperation::allocate, static_cast<Abi>(abi), receiver);
    if (!length) throw Fatal(ERROR_NOT_SUPPORTED, "Zero-size original heap allocation has no admitted lifetime extent.");
    if (owner.created == std::numeric_limits<std::uint32_t>::max()) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native heap capability sequence exhausted.");
    const auto region = owner.shared_heap.allocate(length, [](std::uint64_t id, HANDLE section,
        std::uint32_t logical, std::uint32_t extent, DWORD& error) {
        return shared_placement_map(1U, id, id, section, extent, 0, logical, nullptr, error);
    });
    const auto pointer = address(region.base);
    try { owner.allocations.emplace(pointer, NvseHeapAllocation{region, values_caller_id()}); }
    catch (...) { owner.shared_heap.release(region.id); shared_placement_source_released(1U, region.id, region.base); throw; }
    ++owner.created; values_heap_note(region, 1, abi, receiver); return region.base;
}
bool values_heap_contains(const void* pointer) {
    if (!state->value_heap) return false;
    auto& owner = values_heap_owner(); const auto found = owner.allocations.find(address(pointer));
    if (found == owner.allocations.end()) return false;
    owner.shared_heap.inspect(found->second.region.id); return true;
}
void values_heap_release(void* pointer, std::uint32_t abi = 0, std::uint32_t receiver = 0) {
    auto& owner = values_heap_owner();
    if (abi) values_heap_contract(NvseHeapOperation::release, static_cast<Abi>(abi), receiver);
    if (!pointer) return;
    const auto found = owner.allocations.find(address(pointer));
    if (found == owner.allocations.end()) throw Fatal(ERROR_INVALID_HANDLE, "Native heap free targets a foreign, interior or retired allocation.");
    const auto region = found->second.region; cng_shared_require_releasable(1U, region.id);
    owner.shared_heap.release(region.id); cng_shared_forget(1U, region.id);
    shared_placement_source_released(1U, region.id, region.base); owner.allocations.erase(found);
    ++owner.destroyed; values_heap_note(region, 2, abi, receiver);
}
void* __cdecl values_heap_allocate_cdecl(std::uint32_t length) { return nvse_adapter([&] { return values_heap_allocate(length, 1); }); }
void __cdecl values_heap_free_cdecl(void* pointer) { nvse_adapter([&] { values_heap_release(pointer, 1); }); }
void* __stdcall values_heap_allocate_stdcall(std::uint32_t length) { return nvse_adapter([&] { return values_heap_allocate(length, 2); }); }
void __stdcall values_heap_free_stdcall(void* pointer) { nvse_adapter([&] { values_heap_release(pointer, 2); }); }
// A one-stack-argument thiscall entry is represented by fastcall+unused EDX;
// the compiler preserves its callee stack cleanup and nonvolatile registers.
void* __fastcall values_heap_allocate_thiscall(void* receiver, void*, std::uint32_t length) {
    return nvse_adapter([&] { return values_heap_allocate(length, 3, address(receiver)); });
}
void __fastcall values_heap_free_thiscall(void* receiver, void*, void* pointer) {
    nvse_adapter([&] { values_heap_release(pointer, 3, address(receiver)); });
}
void* values_heap_entry(NvseHeapOperation operation, Abi abi) {
    if (operation == NvseHeapOperation::allocate) {
        if (abi == Abi::cdecl_call) return reinterpret_cast<void*>(values_heap_allocate_cdecl);
        if (abi == Abi::stdcall_call) return reinterpret_cast<void*>(values_heap_allocate_stdcall);
        if (abi == Abi::thiscall_call) return reinterpret_cast<void*>(values_heap_allocate_thiscall);
    }
    if (operation == NvseHeapOperation::release) {
        if (abi == Abi::cdecl_call) return reinterpret_cast<void*>(values_heap_free_cdecl);
        if (abi == Abi::stdcall_call) return reinterpret_cast<void*>(values_heap_free_stdcall);
        if (abi == Abi::thiscall_call) return reinterpret_cast<void*>(values_heap_free_thiscall);
    }
    throw Fatal(ERROR_INVALID_DATA, "Native heap declaration names an unsupported callable ABI.");
}
void values_heap_configure(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); const auto count = reader.get<std::uint32_t>();
    if (!state->nvse || !state->values || state->value_heap || module != state->module_id || !state->calls.empty() ||
        (state->nvse->phase != NvsePhase::mapped && state->nvse->phase != NvsePhase::queried_true)) {
        reply(frame, ERROR_INVALID_STATE, "Native heap declarations require an empty attached value owner before Query/Load."); return;
    }
    if (!count || count > 128) { reply(frame, ERROR_INVALID_DATA, "Native heap thunk count has no bounded declaration extent."); return; }
    auto owner = std::make_unique<NvseValueHeap>(); bool allocate = false, release = false;
    for (std::uint32_t index = 0; index < count; ++index) {
        const NvseHeapThunkDeclaration declaration{reader.get<std::uint32_t>(), static_cast<NvseHeapOperation>(reader.get<std::uint32_t>()),
            static_cast<Abi>(reader.get<std::uint32_t>()), reader.get<std::uint32_t>()};
        if (!declaration.address || declaration.address > std::numeric_limits<std::uint32_t>::max() - 7 ||
            (declaration.operation != NvseHeapOperation::allocate && declaration.operation != NvseHeapOperation::release) ||
            (declaration.abi != Abi::cdecl_call && declaration.abi != Abi::stdcall_call && declaration.abi != Abi::thiscall_call) ||
            (declaration.abi == Abi::thiscall_call ? !declaration.receiver : declaration.receiver != 0))
            throw Fatal(ERROR_INVALID_DATA, "Native heap thunk lacks a complete address/kind/ABI/opaque receiver declaration.");
        for (const auto& previous : owner->declarations)
            if (static_cast<std::uint64_t>(declaration.address) < static_cast<std::uint64_t>(previous.address) + 7 &&
                static_cast<std::uint64_t>(previous.address) < static_cast<std::uint64_t>(declaration.address) + 7)
                throw Fatal(ERROR_INVALID_DATA, "Native heap thunk declarations overlap.");
        owner->declarations.push_back(declaration);
        allocate |= declaration.operation == NvseHeapOperation::allocate; release |= declaration.operation == NvseHeapOperation::release;
    }
    reader.finish();
    if (!allocate || !release) throw Fatal(ERROR_INVALID_DATA, "Native heap contract must own both allocation and free.");
    SYSTEM_INFO system{}; GetSystemInfo(&system); const auto granularity = system.dwAllocationGranularity;
    if (!granularity || (granularity & (granularity - 1))) throw Fatal(ERROR_INVALID_DATA, "Native thunk granularity is unowned.");
    for (const auto& declaration : owner->declarations) {
        const auto base = declaration.address & ~(granularity - 1); const auto relative = declaration.address - base;
        if (relative > granularity - 7) throw Fatal(ERROR_INVALID_ADDRESS, "Native heap thunk crosses its owned mapping granularity.");
        bool owned = false;
        for (const auto& page : owner->pages) if (address(page.base) == base) { owned = true; break; }
        if (owned) continue;
        MEMORY_BASIC_INFORMATION observed{};
        if (!VirtualQuery(reinterpret_cast<void*>(base), &observed, sizeof(observed)) || observed.State != MEM_FREE ||
            reinterpret_cast<std::uintptr_t>(observed.BaseAddress) > base ||
            static_cast<std::uint64_t>(base) + granularity > reinterpret_cast<std::uintptr_t>(observed.BaseAddress) + observed.RegionSize)
            throw Fatal(ERROR_INVALID_ADDRESS, "Source-declared native heap entry collides with an existing mapping; no page replacement is allowed.");
        auto* memory = VirtualAlloc(reinterpret_cast<void*>(base), granularity, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        if (!memory || address(memory) != base) throw Fatal(GetLastError(), "Exact native heap thunk mapping failed.");
        try { owner->pages.push_back({memory, granularity}); }
        catch (...) { VirtualFree(memory, 0, MEM_RELEASE); throw; }
    }
    for (const auto& declaration : owner->declarations) {
        // Authored x86 absolute jump to the exact compiled first-party ABI entry.
        // No instruction byte, executable page or trampoline comes from retail.
        std::array<std::uint8_t, 7> jump{0xb8, 0, 0, 0, 0, 0xff, 0xe0};
        const auto target = address(values_heap_entry(declaration.operation, declaration.abi));
        std::memcpy(jump.data() + 1, &target, sizeof(target)); std::memcpy(reinterpret_cast<void*>(declaration.address), jump.data(), jump.size());
    }
    for (const auto& page : owner->pages) {
        DWORD previous = 0;
        if (!VirtualProtect(page.base, page.length, PAGE_EXECUTE_READ, &previous) || !FlushInstructionCache(GetCurrentProcess(), page.base, page.length))
            throw Fatal(GetLastError(), "Native heap callable mapping could not publish executable first-party bytes.");
    }
    state->value_heap = std::move(owner); Bytes receipt; receipt.put(count); receipt.put(static_cast<std::uint32_t>(state->value_heap->pages.size()));
    reply(frame, ERROR_SUCCESS, "", receipt);
}
void values_heap_flush_detach(const Frame& frame) {
    if (!state->value_heap) return;
    auto& owner = *state->value_heap;
    owner.loader_retiring = false;
    state->calls.push_back(frame.header.id);
    for (const auto& event : owner.pending) {
        if (event.caller != 0) throw Fatal(ERROR_INVALID_DATA, "Loader-detach heap event retained a command caller.");
        values_heap_receipt(event);
    }
    owner.pending.clear(); state->calls.pop_back();
}
void values_heap_retire() {
    if (!state->value_heap) return;
    auto& owner = *state->value_heap;
    if (owner.loader_retiring || !owner.pending.empty() || !owner.allocations.empty() || owner.created != owner.destroyed || owner.shared_heap.live())
        throw Fatal(ERROR_BUSY, "Unreleased original native Element/heap allocations prevent clean generation retirement.");
    owner.shared_heap.retire();
    for (auto& page : owner.pages) {
        const auto previous = page.base;
        if (!VirtualFree(page.base, 0, MEM_RELEASE)) throw Fatal(GetLastError(), "Native heap thunk mapping could not retire.");
        page.base = nullptr; MEMORY_BASIC_INFORMATION observed{};
        if (!VirtualQuery(previous, &observed, sizeof(observed)) || observed.State != MEM_FREE)
            throw Fatal(ERROR_INVALID_DATA, "Native heap callable entry remains after generation retirement.");
    }
    owner.pages.clear(); state->value_heap.reset();
}
