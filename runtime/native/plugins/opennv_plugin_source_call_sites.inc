// Include after the complete object and shared callable-page owners.
std::unique_ptr<SourceCallSites> source_call_sites;

SourceCallSite* source_call_site_find(std::uint32_t pointer) {
    if (!source_call_sites) return nullptr;
    for (const auto& site : source_call_sites->sites) if (site->address == pointer) return site.get();
    return nullptr;
}
bool source_call_site_image_range(const void* pointer, SIZE_T length) {
    if (!original_callable_pages) return false;
    const auto image = original_callable_pages->source_image_identity();
    const auto start = static_cast<std::uint64_t>(address(pointer));
    const auto end = start + static_cast<std::uint64_t>(length);
    const auto image_end = static_cast<std::uint64_t>(image[0]) + image[1];
    if (end <= image[0] || start >= image_end) return false;
    if (start < image[0] || end > image_end || end > (1ULL << 32))
        throw Fatal(ERROR_INVALID_ADDRESS, "Source code operation overlaps its image with an unowned outer extent.");
    return true;
}
void source_call_site_require(SourceCallSite& site, bool retired = false, bool require_target = true) {
    if (!state || GetCurrentThreadId() != state->thread || site.generation != state->generation ||
        site.module != state->module_id || !site.published || site.failed || site.retired != retired || site.writable)
        throw Fatal(ERROR_INVALID_STATE, "Source CALL has no current module/thread/publication owner.");
    const auto image = original_callable_pages->source_image_identity();
    const auto end = static_cast<std::uint64_t>(site.address) + 6;
    for (auto cursor = static_cast<std::uint64_t>(site.address); cursor < end;) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(reinterpret_cast<void*>(static_cast<std::uintptr_t>(cursor)), &memory, sizeof(memory)) ||
            memory.AllocationBase != reinterpret_cast<void*>(image[0]) || memory.State != MEM_COMMIT ||
            memory.Type != MEM_PRIVATE || memory.Protect != PAGE_EXECUTE_READ)
            throw Fatal(ERROR_INVALID_DATA, "Source CALL changed its shared selected-image page/protection.");
        const auto next = static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(memory.BaseAddress)) + memory.RegionSize;
        if (next <= cursor) throw Fatal(ERROR_INVALID_DATA, "Source CALL memory observation made no progress.");
        cursor = std::min(next, end);
    }
    std::array<std::uint8_t, 6> bytes{}; SIZE_T read = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(site.address), bytes.data(), bytes.size(), &read) || read != bytes.size())
        throw Fatal(GetLastError(), "Source CALL has no complete native byte readback.");
    if (retired) {
        for (const auto value : bytes) if (value != 0xcc) throw Fatal(ERROR_INVALID_DATA, "Retired source CALL is not its actual trap extent.");
        return;
    }
    std::uint32_t displacement = 0; std::memcpy(&displacement, bytes.data() + 1, 4);
    if (bytes[0] != 0xe8 || bytes[5] != 0xc3 || site.address + 5U + displacement != site.current)
        throw Fatal(ERROR_INVALID_DATA, "Source CALL changed outside its exact first-party/installed target publication.");
    if (require_target) {
        if (site.current == site.target) {
            if (owned_thunk(reinterpret_cast<void*>(site.target)) != site.target)
                throw Fatal(ERROR_INVALID_DATA, "Source getter has no real retained first-party code target.");
        }
        else nvse_executable(reinterpret_cast<void*>(site.current), false, "installed source CALL target");
    }
}
std::uint32_t source_call_site_getter(std::size_t slot, void* receiver) {
    return nvse_adapter([&] {
        nvse_owner();
        if (!source_call_sites || source_call_sites->failed || slot >= source_call_sites->sites.size())
            throw Fatal(ERROR_INVALID_HANDLE, "Source getter has no retained first-party slot.");
        auto& site = *source_call_sites->sites[slot]; source_call_site_require(site);
        if (!site.busy || site.current != site.target)
            throw Fatal(ERROR_INVALID_STATE, "Pure getter escaped its actual bound first-party invocation.");
        // The real existing class registry validates the receiver's complete
        // allocation, source identity and read-only native lease. No pointer
        // descriptor or fabricated scene object enters this call.
        source_object_pointer(receiver);
        Bytes request; request.put(site.id); request.put(site.address); request.put(address(receiver));
        const auto result = nvse_host(NvseHostCall::source_call_site_execute, request);
        if (site.getter_calls == std::numeric_limits<std::uint32_t>::max())
            throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Source getter lifetime call count exhausted after its actual returned callback.");
        ++site.getter_calls; source_call_site_require(site); return result;
    });
}
using SourceGetterFunction = std::uint32_t(__fastcall*)(void*, void*);
template<std::size_t Slot> std::uint32_t __fastcall source_getter_entry(void* receiver, void*) {
    return source_call_site_getter(Slot, receiver);
}
template<std::size_t... Slots> constexpr std::array<SourceGetterFunction, sizeof...(Slots)> source_getter_entries(std::index_sequence<Slots...>) {
    return {&source_getter_entry<Slots>...};
}
const auto source_getter_functions = source_getter_entries(std::make_index_sequence<source_call_site_limit>{});

SourceCallSite& source_call_site_prepare(std::uint32_t address_value) {
    auto& owner = nvse_owner();
    if (state->calls.size() != 1 || owner.invocation_depth > 1 ||
        !(owner.phase == NvsePhase::loading || owner.phase == NvsePhase::loaded_true))
        throw Fatal(ERROR_BUSY, "Source CALL declaration requires its actual outer original or typed producer scope.");
    if (source_call_sites && (source_call_sites->failed ||
        std::any_of(source_call_sites->sites.begin(), source_call_sites->sites.end(), [](const auto& site) { return site->busy || site->writable; })))
        throw Fatal(ERROR_BUSY, "Source CALL declaration overlaps an entered/failed mutation or invocation.");
    Bytes declaration; declaration.put(address_value); declaration.put(5U);
    const auto id = nvse_host(NvseHostCall::source_call_site_declare, declaration);
    if (!id) throw Fatal(ERROR_NOT_SUPPORTED, "Selected source CALL/getter declaration has no C# semantic owner.");
    if (auto* retained = source_call_site_find(address_value)) {
        if (retained->id != id || retained->retired) throw Fatal(ERROR_INVALID_DATA, "Source CALL replayed a retired/foreign declaration.");
        source_call_site_require(*retained); return *retained;
    }
    if (!source_call_sites) source_call_sites = std::make_unique<SourceCallSites>();
    if (source_call_sites->sites.size() >= source_call_site_limit)
        throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Source CALL native lifetime quota is exhausted.");
    auto site = std::make_unique<SourceCallSite>(); site->id = id; site->address = address_value;
    site->generation = state->generation; site->module = state->module_id;
    site->slot = static_cast<std::uint32_t>(source_call_sites->sites.size());
    site->target = owned_thunk(reinterpret_cast<void*>(source_getter_functions[site->slot])); site->current = site->target;
    source_call_sites->sites.push_back(std::move(site)); auto& retained = *source_call_sites->sites.back();
    try {
        if (!original_callable_pages) throw Fatal(ERROR_INVALID_STATE, "Source CALL has no actual pre-entry image reservation.");
        retained.lease = original_callable_pages->acquire_site(retained.address, retained.target);
        retained.published = true; source_call_site_require(retained);
        Bytes publication; publication.put(retained.id); publication.put(retained.address);
        publication.put(retained.lease); publication.put(retained.target);
        if (nvse_host(NvseHostCall::source_call_site_publish, publication) != 1)
            throw Fatal(ERROR_INVALID_DATA, "Source CALL first-party publication lacks its actual parent native readback.");
        return retained;
    }
    catch (const CallablePageFailure& failure) {
        retained.failed = source_call_sites->failed = true; throw Fatal(failure.code, failure.what());
    }
    catch (...) { retained.failed = source_call_sites->failed = true; throw; }
}
void source_call_site_transition(SourceCallSite& site, std::uint32_t action, DWORD requested,
    BOOL result, DWORD last_error, DWORD previous) {
    Bytes receipt; receipt.put(site.id); receipt.put(site.address); receipt.put(action); receipt.put(requested);
    receipt.put(static_cast<std::uint32_t>(result)); receipt.put(last_error); receipt.put(previous);
    if (nvse_host(NvseHostCall::source_call_site_transition, receipt) != 1)
        throw Fatal(ERROR_INVALID_DATA, "Source CALL SDK result lacks its exact C# entered-prefix receipt.");
}
BOOL WINAPI source_call_site_protect(LPVOID pointer, SIZE_T bytes, DWORD access, PDWORD old_access) {
    const auto incoming = GetLastError();
    return nvse_adapter([&]() -> BOOL {
        if (!source_call_site_image_range(pointer, bytes)) { SetLastError(incoming); return VirtualProtect(pointer, bytes, access, old_access); }
        nvse_owner();
        if (bytes != 5) throw Fatal(ERROR_NOT_SUPPORTED, "Source image protection requires its exact typed code-site extent; other mutations remain unowned.");
        auto* retained = source_call_site_find(address(pointer));
        if (!retained) {
            if (access != PAGE_EXECUTE_READWRITE) throw Fatal(ERROR_NOT_SUPPORTED, "Unpublished source CALL has no requested writable construction.");
            retained = &source_call_site_prepare(address(pointer));
        }
        auto& site = *retained;
        try {
            if (site.failed || site.retired || site.busy || source_call_sites->failed)
                throw Fatal(ERROR_INVALID_STATE, "Source CALL protection escaped its current healthy lifetime.");
            const auto restoring = site.writable;
            if (restoring ? access != site.previous_protection : access != PAGE_EXECUTE_READWRITE)
                throw Fatal(ERROR_NOT_SUPPORTED, "Source CALL protection has no exact prior-access restoration owner.");
            if (!restoring) source_call_site_require(site);
            SetLastError(incoming); const auto result = VirtualProtect(pointer, bytes, access, old_access); const auto last = GetLastError();
            const auto previous = result && old_access ? nvse_copy(old_access) : 0U;
            if (result) {
                if (!restoring) {
                    site.previous_protection = previous; site.writable = true; site.restored = site.flushed = false;
                }
                else {
                    site.writable = false; site.restored = true;
                    const auto displacement = nvse_copy(reinterpret_cast<const std::uint32_t*>(site.address + 1));
                    site.current = site.address + 5U + displacement;
                }
            }
            source_call_site_transition(site, restoring ? 2U : 1U, access, result, last, previous);
            if (result && restoring) source_call_site_require(site);
            SetLastError(last); return result;
        }
        catch (...) { site.failed = source_call_sites->failed = true; throw; }
    });
}
BOOL WINAPI source_call_site_flush(HANDLE process, LPCVOID pointer, SIZE_T bytes) {
    const auto incoming = GetLastError();
    return nvse_adapter([&]() -> BOOL {
        if (!source_call_site_image_range(pointer, bytes)) { SetLastError(incoming); return FlushInstructionCache(process, pointer, bytes); }
        nvse_owner();
        auto* retained = source_call_site_find(address(pointer));
        if (!retained || bytes != 5 || process != GetCurrentProcess())
            throw Fatal(ERROR_NOT_SUPPORTED, "Source CALL cache flush has no exact current-process/source-site capability.");
        auto& site = *retained;
        try {
            if (site.failed || site.busy || !site.restored || site.flushed || source_call_sites->failed)
                throw Fatal(ERROR_INVALID_STATE, "Source CALL cache flush replayed or escaped its restored mutation prefix.");
            source_call_site_require(site); SetLastError(incoming);
            const auto result = FlushInstructionCache(process, pointer, bytes); const auto last = GetLastError();
            source_call_site_transition(site, 3U, 0, result, last, 0);
            if (result) site.flushed = true;
            SetLastError(last); return result;
        }
        catch (...) { site.failed = source_call_sites->failed = true; throw; }
    });
}
void* source_call_site_api(const std::string& name) {
    if (name == "VirtualProtect") return reinterpret_cast<void*>(source_call_site_protect);
    if (name == "FlushInstructionCache") return reinterpret_cast<void*>(source_call_site_flush);
    return nullptr;
}

void source_call_sites_operation(const Frame& frame) {
    Reader request{frame.payload}; const auto module = request.get<std::uint64_t>(); const auto action = request.get<std::uint32_t>();
    if (!state->nvse || state->nvse->phase != NvsePhase::loaded_true || !state->calls.empty() || module != state->module_id)
        throw Fatal(ERROR_INVALID_STATE, "Typed source CALL requires its actual loaded empty original-module owner.");
    state->calls.push_back(frame.header.id); state->nvse->interface_calls = 0; bool owned_call = true;
    try {
        NvseInvocation invocation(*state->nvse); Bytes result;
        if (action == 0) {
            const auto pointer = request.get<std::uint32_t>(); request.finish(); auto& site = source_call_site_prepare(pointer);
            if (site.current != site.target || site.flushed) throw Fatal(ERROR_INVALID_STATE, "Typed preparation cannot replay an original installed hook.");
            result.put(site.id); result.put(site.address);
        }
        else if (action == 1) {
            const auto id = request.get<std::uint32_t>(), pointer = request.get<std::uint32_t>();
            const auto object_id = request.get<std::uint64_t>(); const auto receiver = request.get<std::uint32_t>(); request.finish();
            auto* retained = source_call_site_find(pointer);
            if (!retained || retained->id != id || retained->busy || retained->failed || source_call_sites->failed)
                throw Fatal(ERROR_INVALID_HANDLE, "Typed source CALL has no genuine bound site capability.");
            auto& site = *retained; source_call_site_require(site);
            if (site.current != site.target)
                throw Fatal(ERROR_NOT_SUPPORTED, "Installed original hook still needs its complete typed runtime-global/object/behavior consumer.");
            if (source_object(object_id).pointer != receiver)
                throw Fatal(ERROR_INVALID_HANDLE, "Typed getter receiver changed its actual complete native object.");
            site.busy = true; const auto previous_calls = site.getter_calls; CallReceipt receipt{};
            InvokeSourceCallSiteGuarded(reinterpret_cast<void*>(site.address), reinterpret_cast<void*>(receiver), &receipt);
            try {
                nvse_check_call(receipt, "source pointer getter CALL", site.address);
                source_call_site_require(site); source_object(object_id);
                if (site.getter_calls - previous_calls != 1) throw Fatal(ERROR_INVALID_DATA, "Native getter callback cardinality differs from its genuine CALL.");
                site.busy = false;
            }
            catch (...) { site.failed = source_call_sites->failed = true; throw; }
            result.put(frame.header.id); result.put(receipt.result); result.put(receipt.stack_delta);
            result.put(receipt.preserved_registers); result.put(site.getter_calls - previous_calls);
        }
        else throw Fatal(ERROR_NOT_SUPPORTED, "Unknown typed source CALL operation.");
        state->calls.pop_back(); owned_call = false; reply(frame, ERROR_SUCCESS, "", result);
    }
    catch (...) { if (owned_call) state->calls.pop_back(); throw; }
}
void source_call_sites_retire(const Frame& frame) {
    if (!source_call_sites) return;
    if (!state->calls.empty() || source_call_sites->failed ||
        std::any_of(source_call_sites->sites.begin(), source_call_sites->sites.end(), [](const auto& site) { return site->busy || site->writable || site->failed; }))
        throw Fatal(ERROR_BUSY, "Source CALL retirement retains an entered/failed mutation or object invocation.");
    for (auto& site : source_call_sites->sites) {
        if (site->retired) continue;
        try {
            // The actual DLL has already detached. Read the retained CALL bytes
            // without pretending that its retired image target is executable.
            source_call_site_require(*site, false, false); original_callable_pages->release_site(site->lease);
            site->retired = true; source_call_site_require(*site, true, false);
            state->calls.push_back(frame.header.id);
            try {
                Bytes receipt; receipt.put(site->id); receipt.put(site->address); receipt.put(site->lease);
                if (nvse_host(NvseHostCall::source_call_site_retire, receipt) != 1)
                    throw Fatal(ERROR_INVALID_DATA, "Source CALL trap retirement lacks its independent actual C# readback.");
                state->calls.pop_back();
            }
            catch (...) { state->calls.pop_back(); throw; }
        }
        catch (const CallablePageFailure& failure) { site->failed = source_call_sites->failed = true; throw Fatal(failure.code, failure.what()); }
        catch (...) { site->failed = source_call_sites->failed = true; throw; }
    }
}
