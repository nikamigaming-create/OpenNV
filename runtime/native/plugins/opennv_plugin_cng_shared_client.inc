// Included after CNG client and real mapping owners. Source pointers remain in
// the original process; only actual section handles/offsets cross the boundary.
CngSharedClient& cng_shared_client_owner() {
    auto& client = cng_client_owner();
    if (!client.shared) client.shared = std::make_unique<CngSharedClient>();
    return *client.shared;
}
CngSharedSource cng_shared_find(const void* pointer, std::uint32_t length) {
    if (!pointer) return {};
    if (state->value_heap) {
        auto* heap = state->value_heap->shared_heap.containing(pointer, length);
        if (heap) return CngSharedSource{1U, address(heap->section), address(heap->region.base), heap->region.length,
            heap->region.committed, 0U, heap->region.id, heap->region.id};
    }
    const auto at = reinterpret_cast<std::uintptr_t>(pointer); auto& io = io_owner();
    if (io.mappings) for (const auto& row : io.mappings->views) {
        const auto& view = row.second; const auto base = reinterpret_cast<std::uintptr_t>(view.base);
        if (at < base || at - base >= view.logical_bytes) continue;
        if (length > view.logical_bytes - (at - base)) throw Fatal(ERROR_INVALID_PARAMETER, "Shared CNG pointer crosses its actual source view.");
        const auto& object = io.mappings->objects.at(view.object);
        if (!object.writable || view.protection != PAGE_READWRITE || view.state != MEM_COMMIT ||
            ((view.access & FILE_MAP_COPY) && view.access != FILE_MAP_ALL_ACCESS) || !view.shared_handle || object.maximum > UINT32_MAX || view.offset > UINT32_MAX || view.logical_bytes > UINT32_MAX)
            throw Fatal(ERROR_NOT_SUPPORTED, "CNG source view is private-copy, readonly, reserved or lacks a complete x86 shared extent.");
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(view.base, &memory, sizeof(memory)) || memory.AllocationBase != view.base || memory.Type != MEM_MAPPED ||
            memory.State != MEM_COMMIT || memory.Protect != PAGE_READWRITE || memory.RegionSize < view.logical_bytes)
            throw Fatal(ERROR_INVALID_DATA, "CNG original shared view drifted before borrowing.");
        return CngSharedSource{2U, address(view.shared_handle), address(view.base), static_cast<std::uint32_t>(view.logical_bytes),
            static_cast<std::uint32_t>(object.maximum), static_cast<std::uint32_t>(view.offset), object.id, view.id};
    }
    return {};
}
bool cng_shared_same_source(const CngSharedSource& a, const CngSharedSource& b) {
    return a.kind == b.kind && a.handle == b.handle && a.base == b.base && a.length == b.length &&
        a.maximum == b.maximum && a.offset == b.offset && a.object == b.object && a.view == b.view;
}
CngSharedPointer cng_shared_bind(const void* pointer, std::uint32_t length, std::uint32_t operation) {
    const auto source = cng_shared_find(pointer, length); if (!source.kind) return {};
    auto& shared = cng_shared_client_owner(); const auto key = std::make_pair(source.kind, source.view);
    auto found = shared.views.find(key);
    if (found == shared.views.end()) {
        if (shared.detach_active) throw Fatal(ERROR_INVALID_STATE, "Original detach cannot create a new shared CNG provider/view.");
        Bytes bind; bind.put(operation); bind.put(source.kind); bind.put(source.object); bind.put(source.view);
        bind.put(source.handle); bind.put(source.base); bind.put(source.length); bind.put(source.maximum); bind.put(source.offset);
        auto response = cng_client_request(CngServiceStep::shared_bind, bind); Reader reader{response.data};
        const auto section = reader.get<std::uint64_t>(); const auto view = reader.get<std::uint64_t>(); reader.finish();
        if (!section || view != source.view) throw Fatal(ERROR_INVALID_DATA, "CNG shared source lost its actual section/view capability.");
        found = shared.views.emplace(key, CngSharedClientView{source, section}).first;
    } else if (!cng_shared_same_source(source, found->second.source))
        throw Fatal(ERROR_INVALID_DATA, "CNG original shared allocation/view changed within its retained lifetime.");
    const auto relative = reinterpret_cast<std::uintptr_t>(pointer) - source.base;
    if (relative > UINT32_MAX - source.offset) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "CNG shared pointer offset wraps its original section.");
    return CngSharedPointer{found->second.section, source.view, source.offset + static_cast<std::uint32_t>(relative), length};
}
void cng_shared_put(Bytes& request, const CngSharedPointer& pointer) {
    request.put(pointer.section); request.put(pointer.view); request.put(pointer.offset);
}
bool cng_shared_needed(std::uint64_t target, const std::array<CngCallerBuffer, 4>& buffers, ULONG* copied, PUCHAR object, ULONG object_bytes) {
    if (cng_detach_active()) return false;
    if (object) { (void)cng_shared_find(object, object_bytes); return true; }
    if (copied && cng_shared_find(copied, sizeof(*copied)).kind) return true;
    for (const auto& buffer : buffers) if (buffer.pointer && cng_shared_find(buffer.pointer, buffer.length).kind) return true;
    return state->io->cng_client && state->io->cng_client->shared && state->io->cng_client->shared->objects.count(target) != 0;
}
void cng_shared_aliases(const std::array<CngCallerBuffer, 6>& buffers, const std::array<CngSharedPointer, 6>& pointers) {
    const auto& client = cng_client_owner();
    for (std::size_t at = 0; at < buffers.size(); ++at) {
        for (const auto& token : client.tokens)
            if (cng_caller_overlap(buffers[at].pointer, buffers[at].length, token.second.get(), sizeof(CngImportedHandle)))
                throw Fatal(ERROR_NOT_SUPPORTED, "CNG caller buffer overlaps opaque imported handle storage.");
        for (std::size_t other = at + 1; other < buffers.size(); ++other)
            if (cng_caller_overlap(buffers[at].pointer, buffers[at].length, buffers[other].pointer, buffers[other].length) &&
                (!pointers[at] || !pointers[other]))
                throw Fatal(ERROR_NOT_SUPPORTED, "Aliased private/stack CNG buffers have no genuine shared-address lifetime.");
    }
}
CngCallerResult cng_shared_invoke(std::uint32_t operation, std::uint64_t target, ULONG flags, DWORD caller_error,
    ULONG object_bytes, bool destination_present, ULONG* copied, const std::array<CngCallerBuffer, 4>& buffers,
    void* output_destination, PUCHAR object) {
    if (output_destination && buffers[3].length) expression_writable(output_destination, buffers[3].length);
    if (copied) expression_writable(copied, sizeof(*copied));
    if (object) expression_writable(object, object_bytes ? object_bytes : 1U);
    if (object && !cng_shared_find(object, object_bytes).kind)
        throw Fatal(ERROR_NOT_SUPPORTED, "Nonnull original hash-object storage has no genuine shared allocation owner.");
    std::array<CngSharedPointer, 6> pointers{};
    for (std::size_t at = 0; at < buffers.size(); ++at) pointers[at] = cng_shared_bind(buffers[at].pointer, buffers[at].length, operation);
    pointers[4] = cng_shared_bind(copied, copied ? sizeof(*copied) : 0U, operation);
    pointers[5] = cng_shared_bind(object, object_bytes, operation);
    if (object && !pointers[5]) throw Fatal(ERROR_NOT_SUPPORTED, "Nonnull original hash-object storage is private/stack memory with no shared allocation owner.");
    const std::array<CngCallerBuffer, 6> all{{buffers[0], buffers[1], buffers[2], buffers[3],
        {copied, copied ? sizeof(*copied) : 0U}, {object, object_bytes}}};
    cng_shared_aliases(all, pointers);
    Bytes begin; begin.put(operation); begin.put(target); begin.put(flags); begin.put(caller_error); begin.put(object_bytes);
    begin.put(destination_present ? 1U : 0U); begin.put(copied ? 1U : 0U); begin.put(copied ? nvse_copy(copied) : 0U);
    for (std::size_t at = 0; at < buffers.size(); ++at) {
        begin.put(buffers[at].pointer ? 1U : 0U); begin.put(buffers[at].length);
        begin.put(!buffers[at].pointer ? 0U : pointers[at] ? 2U : 1U);
        if (pointers[at]) cng_shared_put(begin, pointers[at]);
    }
    begin.put(object ? 1U : 0U); if (object) cng_shared_put(begin, pointers[5]);
    begin.put(pointers[4] ? 1U : 0U); if (pointers[4]) cng_shared_put(begin, pointers[4]);
    auto started = cng_client_request(CngServiceStep::shared_begin, begin); Reader start{started.data};
    const auto id = start.get<std::uint64_t>(); start.finish(); if (!id) throw Fatal(ERROR_INVALID_DATA, "Shared CNG SDK invocation has no actual identity.");
    for (std::uint32_t role = 0; role < buffers.size(); ++role) if (!pointers[role]) cng_client_upload(id, role, buffers[role]);
    Bytes request; request.put(id); auto executed = cng_client_request(CngServiceStep::execute, request); Reader result{executed.data};
    CngCallerResult receipt{result.get<NTSTATUS>(), result.get<DWORD>(), result.get<std::uint64_t>(), false, 0, 0};
    const auto present = result.get<std::uint32_t>(); receipt.copied_present = present != 0;
    receipt.copied = result.get<std::uint32_t>(); receipt.output_length = result.get<std::uint32_t>(); result.finish();
    if (present > 1 || receipt.copied_present != (copied != nullptr) || receipt.output_length != buffers[3].length ||
        receipt.created && (receipt.status < 0 || operation != 1 && operation != 3))
        throw Fatal(ERROR_INVALID_DATA, "Shared SDK result changed the real caller signature.");
    if (receipt.status >= 0 && operation == 3 && object) {
        if (!receipt.created || !cng_shared_client_owner().objects.emplace(receipt.created, pointers[5]).second)
            throw Fatal(ERROR_INVALID_DATA, "Actual nonnull hash object lost its original shared memory lifetime.");
    }
    if (output_destination && !pointers[3]) for (std::uint32_t offset = 0; offset < receipt.output_length;) {
        const auto count = (std::min)(cng_transfer_bytes, receipt.output_length - offset); Bytes read;
        read.put(id); read.put(offset); read.put(count); auto bytes = cng_client_request(CngServiceStep::read, read); Reader output_bytes{bytes.data};
        if (output_bytes.get<std::uint32_t>() != count) throw Fatal(ERROR_INVALID_DATA, "Shared call's independent copied output extent changed.");
        auto* destination = static_cast<PUCHAR>(output_destination) + offset; expression_writable(destination, count);
        std::memcpy(destination, output_bytes.bytes(count), count); output_bytes.finish(); offset += count;
    }
    if (copied) {
        if (pointers[4]) {
            if (nvse_copy(copied) != receipt.copied) throw Fatal(ERROR_INVALID_DATA, "Actual shared result-size slot disagrees with Windows SDK readback.");
        } else *copied = receipt.copied;
    }
    auto released = cng_client_request(CngServiceStep::release, request); Reader release{released.data};
    if (release.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Shared CNG invocation retained its SDK/result leases.");
    release.finish(); return receipt;
}
void cng_shared_require_releasable(std::uint32_t kind, std::uint64_t view) {
    if (!state->io || !state->io->cng_client || !state->io->cng_client->shared) return;
    const auto& shared = *state->io->cng_client->shared; const auto found = shared.views.find({kind, view});
    if (found == shared.views.end()) return;
    for (const auto& hash : shared.objects)
        if (hash.second.section == found->second.section && hash.second.view == view)
            throw Fatal(ERROR_BUSY, "Original memory free/unmap would retire a live BCrypt hash-object buffer.");
}
void cng_shared_forget(std::uint32_t kind, std::uint64_t view) {
    if (!state->io || !state->io->cng_client || !state->io->cng_client->shared) return;
    auto& shared = *state->io->cng_client->shared; const auto found = shared.views.find({kind, view});
    if (found == shared.views.end()) return;
    cng_shared_require_releasable(kind, view); Bytes request;
    request.put(found->second.section); request.put(kind); request.put(view);
    auto reply = cng_client_request(CngServiceStep::shared_release, request); Reader result{reply.data};
    if (result.get<std::uint64_t>() != found->second.section || result.get<std::uint64_t>() != view || result.get<std::uint32_t>() != 1)
        throw Fatal(ERROR_INVALID_DATA, "Actual shared CNG view borrow did not retire independently.");
    result.finish(); shared.views.erase(found);
}
void cng_shared_hash_destroyed(std::uint64_t id) {
    if (state->io->cng_client->shared) state->io->cng_client->shared->objects.erase(id);
}
void cng_shared_flush_detach() {
    if (!state->io || !state->io->cng_client || !state->io->cng_client->shared) return;
    auto& shared = *state->io->cng_client->shared;
    if (!shared.objects.empty()) return;
    while (!shared.views.empty()) { const auto key = shared.views.begin()->first; cng_shared_forget(key.first, key.second); }
}
void cng_shared_retain_mapping_view(PluginMappingView& view, HANDLE original) {
    if (view.protection != PAGE_READWRITE || view.state != MEM_COMMIT || ((view.access & FILE_MAP_COPY) && view.access != FILE_MAP_ALL_ACCESS)) return;
    if (view.shared_handle) throw Fatal(ERROR_INVALID_STATE, "Source mapping shared reference cannot repeat its constructor.");
    if (!DuplicateHandle(GetCurrentProcess(), original, GetCurrentProcess(), &view.shared_handle,
        SECTION_MAP_READ | SECTION_MAP_WRITE, FALSE, 0))
        throw Fatal(GetLastError(), "Actual original mapping view did not retain its independent shared section reference.");
}
void cng_shared_close_mapping_view(PluginMappingView& view) {
    if (!view.shared_handle) return;
    if (!CloseHandle(view.shared_handle)) throw Fatal(GetLastError(), "Actual original view's independent section reference did not close.");
    view.shared_handle = nullptr;
}
void cng_shared_handle_destination(const void* destination, std::uint32_t length) {
    if (!destination || !state->io->cng_client || !state->io->cng_client->shared) return;
    const auto& shared = *state->io->cng_client->shared;
    for (const auto& hash : shared.objects) for (const auto& view : shared.views) {
        if (view.second.section != hash.second.section || view.second.source.view != hash.second.view) continue;
        const auto& source = view.second.source;
        const auto* object = reinterpret_cast<void*>(source.base + hash.second.offset - source.offset);
        if (cng_caller_overlap(destination, length, object, hash.second.length))
            throw Fatal(ERROR_NOT_SUPPORTED, "Opaque CNG handle-output slot aliases a live actual hash-object buffer.");
    }
}
bool cng_detach_active() {
    return state->io && state->io->cng_client && state->io->cng_client->shared &&
        state->io->cng_client->shared->detach_active && !state->calls.empty() &&
        state->calls.back() == state->io->cng_client->shared->detach_call;
}
void cng_detach_require_request(CngServiceStep step, const Bytes& payload) {
    if (!cng_detach_active()) throw Fatal(ERROR_INVALID_STATE, "CNG detach callback is outside actual original FreeLibrary.");
    if (step == CngServiceStep::begin) {
        Reader reader{payload.data}; const auto operation = reader.get<std::uint32_t>();
        if (operation != 6 && operation != 7) throw Fatal(ERROR_NOT_SUPPORTED, "Original detach cannot begin a new CNG construction/query/data call.");
    } else if (step != CngServiceStep::execute && step != CngServiceStep::release &&
        step != CngServiceStep::shared_release && step != CngServiceStep::detach_scope)
        throw Fatal(ERROR_NOT_SUPPORTED, "Original detach may only finish existing real CNG handle/memory lifetimes.");
}
Bytes cng_detach_host(const Bytes& payload) {
    auto& io = io_owner(); Bytes request; request.put(state->module_id);
    request.data.insert(request.data.end(), payload.data.begin(), payload.data.end());
    auto response = perform_callback_bytes(Kind::io_callback, crypto_callback, request);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), "Original CNG detach callback failed with retained actual prefix.");
    ++io.callbacks; return response;
}
BOOL cng_original_free_library(const Frame& frame, HMODULE image) {
    if (!state->calls.empty()) throw Fatal(ERROR_INVALID_STATE, "Original module unload requires an empty actual call owner.");
    CngSharedClient* shared = nullptr;
    if (state->io && state->io->cng_client && state->io->cng_client->entered) {
        if (!state->io->cng_client->shared) state->io->cng_client->shared = std::make_unique<CngSharedClient>();
        shared = state->io->cng_client->shared.get();
        if (shared->detach_entered || state->io->cng_client->retired) throw Fatal(ERROR_INVALID_STATE, "Original detach/provider entry cannot replay.");
        shared->detach_entered = shared->detach_active = true; shared->detach_call = frame.header.id;
    }
    state->calls.push_back(frame.header.id);
    try {
        mutex_before_free(frame, image);
        if (shared) { Bytes begin; begin.put(1U); begin.put(address(image)); begin.put(0U); begin.put(0U);
            auto reply = cng_client_request(CngServiceStep::detach_scope, begin); Reader receipt{reply.data};
            if (receipt.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Original CNG detach lacks its exact entered unload call receipt."); receipt.finish(); }
        const auto released = mutex_original_free_library(image); const auto error = GetLastError();
        if (shared) { Bytes end; end.put(2U); end.put(address(image)); end.put(released ? 1U : 0U); end.put(released ? ERROR_SUCCESS : error);
            auto reply = cng_client_request(CngServiceStep::detach_scope, end); Reader receipt{reply.data};
            if (receipt.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Original CNG detach return lost its actual FreeLibrary result."); receipt.finish();
            shared->detach_returned = true; shared->detach_active = false; }
        mutex_after_free(frame, image, released, error);
        state->calls.pop_back(); SetLastError(error); return released;
    } catch (...) {
        if (shared) shared->detach_active = false;
        if (!state->calls.empty() && state->calls.back() == frame.header.id) state->calls.pop_back();
        throw;
    }
}
