// The compiled first-party thiscall entries own these operations. Their source
// addresses arrive only with the exact selected-image declarations. No retail
// instructions, trampoline body or executable mapping are used as authority.
constexpr std::uint32_t source_file_begin_event = 0x420, source_file_slice_event = 0x421, source_file_end_event = 0x422;

SourceFileRuntime& source_files_owner() {
    if (!state->nvse || !state->source_files) nvse_unowned("Contributor parser: selected source method declarations absent");
    return *state->source_files;
}
void source_file_request_prefix(Bytes& request, std::uint64_t caller, const SourceObject& object) {
    request.put(caller); request.put(object.id); request.put(object.pointer);
}
std::uint32_t source_file_call(void* receiver, SourceFileMethod method, void* destination, std::uint32_t capacity) {
    auto& methods = source_files_owner(); auto& object = source_object_pointer(receiver);
    if (object.kind != 2 || object.extent != 0x42c ||
        std::none_of(methods.thunks.begin(), methods.thunks.end(), [&](const auto& thunk) { return thunk.method == method; }))
        throw Fatal(ERROR_INVALID_DATA, "Native source method receiver has no actual contributor class/declaration.");
    const auto caller = source_object_caller();
    Bytes request; source_file_request_prefix(request, caller, object); request.put(static_cast<std::uint32_t>(method)); request.put(capacity);
    auto response = expression_host(source_file_begin_event, request); Reader reader{response.data};
    const auto result = reader.get<std::uint32_t>(); const auto transfer = reader.get<std::uint64_t>();
    const auto length = reader.get<std::uint32_t>(), extent = reader.get<std::uint32_t>();
    const auto* before = static_cast<const std::uint8_t*>(reader.bytes(extent));
    const auto* after = static_cast<const std::uint8_t*>(reader.bytes(extent)); reader.finish();
    if (extent != object.extent || (!transfer != !length) ||
        ((method == SourceFileMethod::next_chunk || method == SourceFileMethod::advance_chunk) && length) ||
        (method == SourceFileMethod::read32 && length > 4) ||
        (method == SourceFileMethod::read_chunk && capacity && length > capacity))
        throw Fatal(ERROR_INVALID_DATA, "Source parser reply changed its exact native image/output extent.");
    for (std::uint32_t at = 0; at < extent; ++at)
        if (before[at] != after[at] && (at < 0x240 || (at >= 0x260 && at < 0x264) || at >= 0x270))
            throw Fatal(ERROR_INVALID_DATA, "Source parser changed a contributor field outside its cursor owner.");
    state->arena.replace_read_only(object.image_id, before, after, extent);
    ++methods.calls;
    if (!length) return result;
    expression_writable(destination, length);
    std::uint32_t position = 0;
    while (position < length) {
        const auto count = std::min(length - position, max_payload - 64);
        Bytes next; source_file_request_prefix(next, caller, object); next.put(transfer); next.put(position); next.put(count);
        auto slice = expression_host(source_file_slice_event, next); Reader slice_reader{slice.data};
        if (slice_reader.get<std::uint32_t>() != count) throw Fatal(ERROR_INVALID_DATA, "Native source slice changed its admitted output extent.");
        const auto* bytes = slice_reader.bytes(count); slice_reader.finish();
        expression_write(static_cast<std::uint8_t*>(destination) + position, bytes, count);
        position += count;
    }
    Bytes end; source_file_request_prefix(end, caller, object); end.put(transfer);
    if (expression_scalar(source_file_end_event, end) != 1) throw Fatal(ERROR_INVALID_DATA, "Native source bytes have no final lifetime receipt.");
    ++methods.outputs; return result;
}
std::uint32_t __fastcall source_file_next(void* receiver, void*) {
    return nvse_adapter([&] { return source_file_call(receiver, SourceFileMethod::next_chunk, nullptr, 0); });
}
bool __fastcall source_file_advance(void* receiver, void*) {
    return nvse_adapter([&] { return source_file_call(receiver, SourceFileMethod::advance_chunk, nullptr, 0) != 0; });
}
bool __fastcall source_file_read(void* receiver, void*, void* destination, std::uint32_t capacity) {
    return nvse_adapter([&] { return source_file_call(receiver, SourceFileMethod::read_chunk, destination, capacity) != 0; });
}
void __fastcall source_file_read32(void* receiver, void*, std::uint32_t* destination) {
    nvse_adapter([&] { (void)source_file_call(receiver, SourceFileMethod::read32, destination, 4); });
}
void* source_file_entry(SourceFileMethod method) {
    switch (method) {
    case SourceFileMethod::next_chunk: return reinterpret_cast<void*>(&source_file_next);
    case SourceFileMethod::advance_chunk: return reinterpret_cast<void*>(&source_file_advance);
    case SourceFileMethod::read_chunk: return reinterpret_cast<void*>(&source_file_read);
    case SourceFileMethod::read32: return reinterpret_cast<void*>(&source_file_read32);
    default: throw Fatal(ERROR_NOT_SUPPORTED, "Selected file method has no genuine native operation.");
    }
}

void source_files_release_pages(SourceFileRuntime& owner) {
    if (!owner.callable_lease) return;
    source_callables_release(owner.callable_lease); owner.callable_lease = 0;
}
void source_files_configure(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); const auto count = reader.get<std::uint32_t>();
    if (!state->nvse || state->source_files || module != state->module_id || !state->calls.empty() ||
        (state->nvse->phase != NvsePhase::mapped && state->nvse->phase != NvsePhase::queried_true))
        throw Fatal(ERROR_INVALID_STATE, "Source methods need an empty actual module before Query/Load.");
    if (!count || count > 128) throw Fatal(ERROR_INVALID_DATA, "Source method declarations have no bounded exact count.");
    auto owner = std::make_unique<SourceFileRuntime>();
    for (std::uint32_t index = 0; index < count; ++index) {
        SourceFileThunk thunk{reader.get<std::uint32_t>(), static_cast<SourceFileMethod>(reader.get<std::uint32_t>()), static_cast<Abi>(reader.get<std::uint32_t>())};
        if (!thunk.address || thunk.address > std::numeric_limits<std::uint32_t>::max() - 7 ||
            thunk.abi != Abi::thiscall_call || static_cast<std::uint32_t>(thunk.method) < 1 || static_cast<std::uint32_t>(thunk.method) > 4)
            throw Fatal(ERROR_INVALID_DATA, "Source method has no exact address/thiscall/callable declaration.");
        for (const auto& previous : owner->thunks)
            if (static_cast<std::uint64_t>(thunk.address) < static_cast<std::uint64_t>(previous.address) + 7 &&
                static_cast<std::uint64_t>(previous.address) < static_cast<std::uint64_t>(thunk.address) + 7)
                throw Fatal(ERROR_INVALID_DATA, "Source file method entry extents overlap.");
        owner->thunks.push_back(thunk);
    }
    reader.finish();
    // Retain the owner before allocating, including a failed rollback. An
    // allocation error cannot discard the identity of an unreleased page.
    state->source_files = std::move(owner); auto& actual = *state->source_files;
    std::vector<CallableEntry> entries; entries.reserve(actual.thunks.size());
    SYSTEM_INFO system{}; GetSystemInfo(&system); const auto granularity = system.dwAllocationGranularity;
    if (!granularity || (granularity & (granularity - 1))) throw Fatal(ERROR_INVALID_DATA, "Source callable granularity is unowned.");
    std::set<std::uint32_t> units;
    for (const auto& thunk : actual.thunks) {
        entries.push_back({thunk.address, source_file_entry(thunk.method)}); units.insert(thunk.address & ~(granularity - 1));
    }
    // Shared owner retains partial failed reservations and faults the whole
    // generation; a source-family rollback cannot unmap another live family.
    actual.callable_lease = source_callables_acquire(entries);
    Bytes receipt; receipt.put(count); receipt.put(static_cast<std::uint32_t>(units.size())); reply(frame, ERROR_SUCCESS, "", receipt);
}
void source_files_retire() {
    if (!state->source_files) return;
    // FreeLibrary has completed. Original callbacks may no longer observe a
    // method page while its exact generation is being released.
    source_files_release_pages(*state->source_files); state->source_files.reset();
}
