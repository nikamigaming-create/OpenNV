// Included after the actual CNG service. Its forward declarations precede that
// include, so both streamed and shared calls enter the same seven SDK methods.
CngSharedService& cng_shared_service_owner(CngSystemService& owner) {
    if (!owner.shared) owner.shared = std::make_unique<CngSharedService>();
    return *owner.shared;
}
CngSharedServiceSection& cng_shared_service_section(CngSystemService& owner, std::uint64_t id) {
    auto& shared = cng_shared_service_owner(owner); const auto found = shared.sections.find(id);
    if (!id || found == shared.sections.end() || found->second.release_entered || !found->second.handle)
        throw Fatal(ERROR_INVALID_HANDLE, "Shared CNG pointer has no current mapped section capability.");
    return found->second;
}
CngSharedServiceView& cng_shared_service_view(CngSystemService& owner, const CngSharedPointer& pointer) {
    auto& section = cng_shared_service_section(owner, pointer.section); const auto found = section.views.find(pointer.view);
    if (found == section.views.end() || found->second.release_entered || !found->second.base)
        throw Fatal(ERROR_INVALID_HANDLE, "Shared CNG pointer has no live actual original view.");
    auto& view = found->second; MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(view.base, &memory, sizeof(memory)) || memory.AllocationBase != view.base ||
        memory.BaseAddress != view.base || memory.Type != MEM_MAPPED || memory.State != MEM_COMMIT ||
        memory.Protect != PAGE_READWRITE || memory.RegionSize < view.length)
        throw Fatal(ERROR_INVALID_DATA, "Shared CNG section mapping/protection drifted.");
    return view;
}
PUCHAR cng_shared_service_pointer(CngSystemService& owner, const CngSharedPointer& pointer) {
    if (!pointer) return nullptr;
    auto& view = cng_shared_service_view(owner, pointer);
    if (pointer.offset < view.offset || pointer.offset - view.offset >= view.length || pointer.length > view.length - (pointer.offset - view.offset))
        throw Fatal(ERROR_INVALID_PARAMETER, "Shared CNG caller pointer crosses its actual section extent.");
    return static_cast<PUCHAR>(view.base) + (pointer.offset - view.offset);
}
CngSharedPointer cng_shared_service_read_pointer(Reader& reader, std::uint32_t length) {
    CngSharedPointer pointer{reader.get<std::uint64_t>(), reader.get<std::uint64_t>(), reader.get<std::uint32_t>(), length};
    if (!pointer || !pointer.view) throw Fatal(ERROR_INVALID_DATA, "Shared CNG argument omitted its section/view identity.");
    return pointer;
}
void cng_shared_service_before(CngSystemService& owner, CngInvocation& call) {
    if (call.shared_object) (void)cng_shared_service_pointer(owner, call.shared_object);
    if (call.shared_copied) (void)cng_shared_service_pointer(owner, call.shared_copied);
    for (const auto& pointer : call.shared_buffers) if (pointer) (void)cng_shared_service_pointer(owner, pointer);
    if (owner.shared) {
        const auto found = owner.shared->hash_objects.find(call.handle);
        if (found != owner.shared->hash_objects.end()) (void)cng_shared_service_pointer(owner, found->second);
    }
}
void cng_shared_service_after(CngSystemService& owner, const CngInvocation& call) {
    if (call.status < 0) return;
    auto& shared = cng_shared_service_owner(owner);
    if (call.operation == 3 && call.shared_object) {
        if (!call.created || !shared.hash_objects.emplace(call.created, call.shared_object).second ||
            !cng_shared_service_view(owner, call.shared_object).hashes.insert(call.created).second)
            throw Fatal(ERROR_INVALID_DATA, "Actual hash-object memory did not retain its unique live Windows hash.");
    } else if (call.operation == 6) {
        const auto found = shared.hash_objects.find(call.handle);
        if (found != shared.hash_objects.end()) {
            auto& view = cng_shared_service_view(owner, found->second);
            if (!view.hashes.erase(call.handle)) throw Fatal(ERROR_INVALID_DATA, "Destroyed CNG hash lost its original object memory lease.");
            shared.hash_objects.erase(found);
        }
    }
}
void cng_shared_service_release_invocation(CngSystemService& owner, const CngInvocation& call) {
    if (!owner.shared) return;
    for (auto& item : owner.shared->sections) for (auto& view : item.second.views) view.second.calls.erase(call.id);
}
void cng_shared_service_hash_abandoned(CngSystemService& owner, std::uint64_t id) {
    if (!owner.shared) return;
    const auto found = owner.shared->hash_objects.find(id);
    if (found == owner.shared->hash_objects.end()) return;
    auto& view = cng_shared_service_view(owner, found->second);
    if (!view.hashes.erase(id)) throw Fatal(ERROR_INVALID_DATA, "Emergency hash retirement lost its actual shared storage lease.");
    owner.shared->hash_objects.erase(found);
}
void cng_shared_service_require_retired(CngSystemService& owner) {
    if (owner.shared && (!owner.shared->sections.empty() || !owner.shared->hash_objects.empty()))
        throw Fatal(ERROR_BUSY, "Actual shared CNG views/handles/object memory remain after provider retirement.");
}
void cng_shared_service_release_view(CngSystemService& owner, std::uint64_t id, std::uint64_t view_id) {
    auto& shared = cng_shared_service_owner(owner); auto& section = cng_shared_service_section(owner, id);
    auto& view = cng_shared_service_view(owner, CngSharedPointer{id, view_id, 0, 0});
    if (!view.calls.empty() || !view.hashes.empty())
        throw Fatal(ERROR_BUSY, "Shared CNG section is retained by an entered invocation or live hash object.");
    view.release_entered = true;
    const auto base = view.base;
    if (!view.placement) {
        if (!UnmapViewOfFile(base)) throw Fatal(GetLastError(), "Shared CNG service view release failed with retained section.");
        view.base = nullptr; MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(base, &memory, sizeof(memory)) || memory.State != MEM_FREE)
            throw Fatal(ERROR_INVALID_DATA, "Shared CNG service view remains mapped after release.");
    } else {
        // The entered CNG borrow retires here. The actual mapping remains
        // owned by the parent/source allocator until its independent release.
        view.base = nullptr;
    }
    section.views.erase(view_id);
    if (section.views.empty()) {
        section.release_entered = true;
        if (!CloseHandle(section.handle)) throw Fatal(GetLastError(), "Shared CNG service section handle release failed.");
        section.handle = nullptr; shared.sections.erase(id);
    }
}
void cng_shared_service_abandon(CngSystemService& owner) {
    if (!owner.shared) return;
    // Only the exact original-child-closed path reaches this owner. Failed SDK
    // destroys still retain their shared memory and prevent provider retirement.
    for (auto& section : owner.shared->sections) for (auto& view : section.second.views) view.second.calls.clear();
    if (!owner.shared->hash_objects.empty()) return;
    while (!owner.shared->sections.empty()) {
        auto& section = owner.shared->sections.begin()->second;
        if (section.views.empty()) throw Fatal(ERROR_INVALID_STATE, "Failed shared section close retains an entered retirement prefix.");
        cng_shared_service_release_view(owner, section.id, section.views.begin()->first);
    }
}
void cng_shared_service_step(const Frame& frame, Reader& reader, CngServiceStep step) {
    auto& owner = cng_service_owner(); auto& shared = cng_shared_service_owner(owner); Bytes receipt;
    if (step == CngServiceStep::shared_bind) {
        const auto id = reader.get<std::uint64_t>(); const auto handle = reader.get<std::uint32_t>();
        const auto maximum = reader.get<std::uint32_t>(); const auto view_id = reader.get<std::uint64_t>();
        const auto base = reader.get<std::uint32_t>(); const auto length = reader.get<std::uint32_t>(); const auto offset = reader.get<std::uint32_t>(); const auto placement = reader.get<std::uint64_t>(); reader.finish();
        if (!id || handle == UINT32_MAX || !maximum || !view_id || !base || !length || offset >= maximum || length > maximum - offset)
            throw Fatal(ERROR_INVALID_DATA, "Shared CNG section bind repeats or omits its actual duplicated handle/extent.");
        if (!shared.sections.count(id)) {
            if (!handle) throw Fatal(ERROR_INVALID_HANDLE, "New shared CNG object omitted its genuine duplicated section handle.");
            CngSharedServiceSection value; value.id = id; value.handle = reinterpret_cast<HANDLE>(handle); value.maximum = maximum;
            shared.sections.emplace(id, value);
        } else if (handle || shared.sections.at(id).maximum != maximum)
            throw Fatal(ERROR_INVALID_DATA, "Shared CNG alias changed its canonical section handle/extent.");
        auto& current = cng_shared_service_section(owner, id);
        CngSharedServiceView view; view.id = view_id; view.placement = placement; view.length = length; view.offset = offset;
        if (!current.views.emplace(view_id, view).second) throw Fatal(ERROR_INVALID_DATA, "Shared CNG source view bind repeated.");
        auto& mapped = current.views.at(view_id);
        mapped.base = placement ? reinterpret_cast<void*>(base) :
            MapViewOfFileEx(current.handle, FILE_MAP_READ | FILE_MAP_WRITE, 0, offset, length, reinterpret_cast<void*>(base));
        if (!mapped.base) throw Fatal(GetLastError(), "Exact original CNG view address collides or could not map in the service.");
        if (address(mapped.base) != base) throw Fatal(ERROR_INVALID_ADDRESS, "CNG service map replaced the original caller address.");
        (void)cng_shared_service_view(owner, CngSharedPointer{id, view_id, offset, length});
        receipt.put(id); receipt.put(view_id); receipt.put(address(mapped.base)); receipt.put(length); receipt.put(offset); receipt.put(placement);
    } else if (step == CngServiceStep::shared_release) {
        const auto id = reader.get<std::uint64_t>(); const auto view = reader.get<std::uint64_t>(); reader.finish();
        cng_shared_service_release_view(owner, id, view); receipt.put(id); receipt.put(view); receipt.put(1U);
    } else if (step == CngServiceStep::shared_begin) {
        CngInvocation call; call.id = ++owner.next_invocation;
        if (!call.id) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Shared CNG invocation identity exhausted.");
        call.operation = reader.get<std::uint32_t>(); call.handle = reader.get<std::uint64_t>();
        call.flags = reader.get<std::uint32_t>(); call.caller_error = reader.get<std::uint32_t>();
        call.object_bytes = reader.get<std::uint32_t>(); call.destination_present = cng_presence(reader);
        call.copied_present = cng_presence(reader); call.copied = reader.get<std::uint32_t>();
        for (std::size_t at = 0; at < call.buffers.size(); ++at) {
            const auto present = cng_presence(reader); const auto length = reader.get<std::uint32_t>();
            const auto mode = reader.get<std::uint32_t>(); auto& buffer = call.buffers[at];
            if (mode > 2 || (mode != 0) != present)
                throw Fatal(ERROR_INVALID_DATA, "Shared CNG buffer mode changed actual caller pointer presence.");
            if (mode == 2) {
                auto pointer = cng_shared_service_read_pointer(reader, length);
                buffer.present = true; buffer.length = buffer.written = length;
                buffer.shared = cng_shared_service_pointer(owner, pointer); call.shared_buffers[at] = pointer;
            } else buffer.allocate(present, length);
        }
        const auto object_present = cng_presence(reader);
        if (object_present) call.shared_object = cng_shared_service_read_pointer(reader, call.object_bytes);
        const auto copied_shared = cng_presence(reader);
        if (copied_shared) call.shared_copied = cng_shared_service_read_pointer(reader, sizeof(ULONG));
        reader.finish();
        if (call.shared_object && call.operation != 3 || call.shared_copied && !call.copied_present)
            throw Fatal(ERROR_INVALID_DATA, "Shared CNG object/size storage belongs to an unrelated SDK signature.");
        (void)cng_shared_service_pointer(owner, call.shared_object);
        (void)cng_shared_service_pointer(owner, call.shared_copied);
        std::map<std::pair<std::uint64_t, std::uint64_t>, CngSharedPointer> views;
        for (const auto& pointer : call.shared_buffers) if (pointer) views.emplace(std::make_pair(pointer.section, pointer.view), pointer);
        if (call.shared_object) views.emplace(std::make_pair(call.shared_object.section, call.shared_object.view), call.shared_object);
        if (call.shared_copied) views.emplace(std::make_pair(call.shared_copied.section, call.shared_copied.view), call.shared_copied);
        const auto id = call.id; owner.invocations.emplace(id, std::move(call));
        for (const auto& view : views) cng_shared_service_view(owner, view.second).calls.insert(id);
        receipt.put(id);
    } else throw Fatal(ERROR_INVALID_FUNCTION, "CNG shared service suboperation has no actual consumer.");
    reply(frame, ERROR_SUCCESS, "", receipt);
}
