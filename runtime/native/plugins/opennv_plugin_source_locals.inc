constexpr std::uint32_t local_begin_event = 0x300, local_chunk_event = 0x301,
    local_commit_event = 0x302, local_read_event = 0x303, local_end_event = 0x304;
constexpr std::uint32_t local_chunk_count = 2048;

SourceLocalRuntime& source_local_owner() {
    if (!state->nvse || !state->source_locals) throw Fatal(ERROR_INVALID_STATE, "Native event-list has no original module/generation owner.");
    return *state->source_locals;
}
SourceLocalContext& source_local_context(std::uint64_t id, bool require_sealed = true) {
    auto& owner = source_local_owner();
    for (const auto& context : owner.contexts) if (context->id == id && context->live) {
        if (require_sealed && !context->sealed) throw Fatal(ERROR_INVALID_STATE, "Native event-list has not admitted every declared slot.");
        return *context;
    }
    throw Fatal(ERROR_INVALID_HANDLE, "Native event-list is foreign, unknown or retired.");
}
SourceLocalCell source_local_cell(SourceLocalContext& context, std::uint32_t index) {
    auto& owner = source_local_owner();
    if (index >= context.count) throw Fatal(ERROR_INVALID_INDEX, "Native local read crosses its complete source declaration.");
    const auto cell = nvse_copy(static_cast<const SourceLocalCell*>(owner.arena.read(context.cells.id, index * 24U, 24U)));
    if (cell.id != context.declarations[index].id || cell.reserved_link != 0 || cell.storage_flags != context.declarations[index].storage_flags ||
        cell.reserved_bytes[0] || cell.reserved_bytes[1] || cell.reserved_bytes[2] || cell.reserved_tail)
        throw Fatal(ERROR_INVALID_DATA, "Native local identity/reserved storage changed.");
    return cell;
}
void source_local_validate(SourceLocalContext& context) {
    auto& owner = source_local_owner();
    if (!context.sealed) throw Fatal(ERROR_INVALID_STATE, "Native local declaration is incomplete.");
    const auto header = nvse_copy(static_cast<const SourceEventList*>(owner.arena.read(context.event_list.id, 0, 20)));
    if (header.script != reinterpret_cast<void*>(context.script_pointer) || header.reserved || header.auxiliary || header.variables != context.nodes.base || header.events != context.empty_events.base)
        throw Fatal(ERROR_INVALID_DATA, "Native event-list header lacks its retained exact source/local-storage owner.");
    if (context.script_id && source_object(context.script_id).pointer != context.script_pointer)
        throw Fatal(ERROR_INVALID_DATA, "Actual Script/event-list ownership changed.");
    const auto empty = nvse_copy(static_cast<const SourceListNode*>(owner.arena.read(context.empty_events.id, 0, 8)));
    if (empty.data || empty.next) throw Fatal(ERROR_INVALID_DATA, "Unowned native event declarations were inserted into a local-only event list.");
    const auto node_region = owner.arena.inspect(context.nodes.id);
    for (std::uint32_t index = 0; index < (context.count ? context.count : 1U); ++index) {
        const auto* pointer = static_cast<const SourceListNode*>(node_region.base) + index;
        const auto node = nvse_copy(pointer);
        const auto* expected_data = context.count ? static_cast<const SourceLocalCell*>(context.cells.base) + index : nullptr;
        const auto* expected_next = index + 1 < context.count ? pointer + 1 : nullptr;
        if (node.data != expected_data || node.next != expected_next)
            throw Fatal(ERROR_INVALID_DATA, "Native local-list topology changed after source publication.");
        if (context.count) (void)source_local_cell(context, index);
    }
}
Bytes source_local_host(std::uint32_t event, SourceLocalContext& context, std::uint64_t caller, std::uint64_t transfer, const Bytes& data) {
    Bytes request; request.put(state->module_id); request.put(state->nvse->handle);
    request.put(context.id); request.put(caller); request.put(transfer);
    request.data.insert(request.data.end(), data.data.begin(), data.data.end());
    auto reply_bytes = perform_callback_bytes(Kind::nvse_callback, event, request);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason[0] ? state->callback_reason : "Native event-list callback ownership failed.");
    return reply_bytes;
}
void source_local_sync(bool entry) {
    if (!state || !state->source_locals || state->source_locals->synchronizing || state->source_locals->calls.empty()) return;
    auto& owner = source_local_owner(); auto& call = owner.calls.back();
    if (!call.context) return;
    auto& context = *call.context; source_local_validate(context);
    if (!context.active || !context.live || state->calls.empty()) throw Fatal(ERROR_INVALID_STATE, "Native local synchronization has no current call.");
    if (owner.next_transfer == std::numeric_limits<std::uint64_t>::max()) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native local transfer identity exhausted.");
    const auto transfer = ++owner.next_transfer;
    struct Synchronization { bool& active; explicit Synchronization(bool& flag) : active(flag) { active = true; } ~Synchronization() { active = false; } } guard(owner.synchronizing);
    Bytes begin; begin.put(entry ? 1U : 0U); begin.put(context.count); begin.put(address(context.event_list.base));
    auto begin_reply = source_local_host(local_begin_event, context, call.caller, transfer, begin); Reader begin_reader{begin_reply.data};
    if (begin_reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Native local begin has no C# lifetime receipt."); begin_reader.finish();
    for (std::uint32_t offset = 0; offset < context.count;) {
        const auto count = (context.count - offset) < local_chunk_count ? context.count - offset : local_chunk_count;
        Bytes chunk; chunk.put(offset); chunk.put(count);
        for (std::uint32_t index = offset; index < offset + count; ++index) {
            const auto cell = source_local_cell(context, index); chunk.put(cell.id); chunk.put(cell.value);
        }
        auto chunk_reply = source_local_host(local_chunk_event, context, call.caller, transfer, chunk); Reader chunk_reader{chunk_reply.data};
        if (chunk_reader.get<std::uint32_t>() != offset + count) throw Fatal(ERROR_INVALID_DATA, "Native local input extent lacks its complete C# receipt."); chunk_reader.finish(); offset += count;
    }
    auto commit_reply = source_local_host(local_commit_event, context, call.caller, transfer, {}); Reader commit_reader{commit_reply.data};
    if (commit_reader.get<std::uint32_t>() != context.count) throw Fatal(ERROR_INVALID_DATA, "Native local C# publication count drifted."); commit_reader.finish();
    for (std::uint32_t offset = 0; offset < context.count;) {
        const auto count = (context.count - offset) < local_chunk_count ? context.count - offset : local_chunk_count;
        Bytes read; read.put(offset); read.put(count);
        auto read_reply = source_local_host(local_read_event, context, call.caller, transfer, read); Reader read_reader{read_reply.data};
        if (read_reader.get<std::uint32_t>() != count) throw Fatal(ERROR_INVALID_DATA, "Native local output extent drifted.");
        for (std::uint32_t index = offset; index < offset + count; ++index) {
            const auto value = read_reader.get<std::uint64_t>();
            owner.arena.write(context.cells.id, index * 24U + 8U, &value, 8U); context.declarations[index].baseline = value;
        }
        read_reader.finish(); offset += count;
    }
    auto end_reply = source_local_host(local_end_event, context, call.caller, transfer, {}); Reader end_reader{end_reply.data};
    if (end_reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Native local synchronization lacks complete retirement."); end_reader.finish();
    source_local_validate(context);
}
bool source_local_callback_needs_sync(Kind kind, std::uint32_t event) {
    if (kind == Kind::nvse_callback && (event == 0x600 || event == 0x601)) return false;
    return (kind != Kind::nvse_callback || event < local_begin_event || event > local_end_event) &&
        state && state->source_locals && !state->source_locals->synchronizing && !state->source_locals->calls.empty();
}
struct SourceLocalScope {
    SourceLocalRuntime* owner = nullptr;
    SourceLocalScope(std::uint64_t id, std::uint64_t caller) {
        if (!state->source_locals && !id) return;
        auto& actual = source_local_owner();
        if (actual.synchronizing) throw Fatal(ERROR_BUSY, "Native local publication cannot execute a nested command before its complete extent.");
        auto* context = id ? &source_local_context(id) : nullptr;
        actual.calls.push_back({context, caller}); owner = &actual;
        if (context) ++context->active;
    }
    SourceLocalScope(const SourceLocalScope&) = delete;
    ~SourceLocalScope() { if (owner) { if (owner->calls.back().context) --owner->calls.back().context->active; owner->calls.pop_back(); } }
    std::uint32_t event_list() const { return owner && owner->calls.back().context ? address(owner->calls.back().context->event_list.base) : 0; }
};
void source_local_create(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>(); const auto count = reader.get<std::uint32_t>(); const auto script_id = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || state->nvse->phase != NvsePhase::loaded_true || !state->calls.empty() || !id) {
        reply(frame, ERROR_INVALID_STATE, "Native local construction requires the actual loaded module and no active caller."); return;
    }
    if (count > max_guest_committed / 24U) { reply(frame, ERROR_NOT_ENOUGH_QUOTA, "Native local construction exceeds its actual committed-page budget."); return; }
    if (!state->source_locals) state->source_locals = std::make_unique<SourceLocalRuntime>();
    auto& owner = source_local_owner();
    for (const auto& previous : owner.contexts) if (previous->id == id) throw Fatal(ERROR_INVALID_HANDLE, "Native local context identity was already used in this module generation.");
    auto context = std::make_unique<SourceLocalContext>(); context->id = id; context->count = count; context->script_id = script_id; context->declarations.reserve(count);
    if (script_id) { auto& script = source_object(script_id); if (script.kind != 1) throw Fatal(ERROR_INVALID_DATA, "Local owner is not an actual Script."); context->script_pointer = script.pointer; }
    context->cells = owner.arena.allocate(count ? count * 24U : 1U, GuestAccess::read_write, nullptr, 0);
    owner.contexts.push_back(std::move(context)); Bytes bytes; bytes.put(id); reply(frame, ERROR_SUCCESS, "", bytes);
}
void source_local_fill(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>();
    const auto offset = reader.get<std::uint32_t>(), count = reader.get<std::uint32_t>();
    if (module != state->module_id || !state->calls.empty()) throw Fatal(ERROR_INVALID_STATE, "Native local initialization has a foreign module or active caller.");
    auto& owner = source_local_owner(); auto& context = source_local_context(id, false);
    if (context.sealed || offset != context.filled || !count || count > local_chunk_count || count > context.count - context.filled)
        throw Fatal(ERROR_INVALID_DATA, "Native local initialization lost its ordered complete extent.");
    std::vector<SourceLocalDeclaration> incoming; incoming.reserve(count);
    for (std::uint32_t index = 0; index < count; ++index) {
        const auto slot = reader.get<std::uint32_t>(), kind = reader.get<std::uint32_t>(), flags = reader.get<std::uint32_t>(); const auto bits = reader.get<std::uint64_t>();
        if (kind > 4 || flags > UINT8_MAX) throw Fatal(ERROR_INVALID_DATA, "Native local has no declared kind/flag byte.");
        // Ordinal identity retains every original entry, including repeated IDs.
        incoming.push_back({slot, kind, flags, bits});
    }
    reader.finish();
    for (const auto& declaration : incoming) {
        const SourceLocalCell cell{declaration.id, 0, declaration.baseline, static_cast<std::uint8_t>(declaration.storage_flags), {0, 0, 0}, 0}; owner.arena.write(context.cells.id, context.filled * 24U, &cell, 24U);
        context.declarations.push_back(declaration); ++context.filled;
    }
    Bytes bytes; bytes.put(context.filled); reply(frame, ERROR_SUCCESS, "", bytes);
}
void source_local_seal(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>(); reader.finish();
    if (module != state->module_id || !state->calls.empty()) throw Fatal(ERROR_INVALID_STATE, "Native local seal has a foreign module or active caller.");
    auto& owner = source_local_owner(); auto& context = source_local_context(id, false);
    if (context.sealed || context.filled != context.count) throw Fatal(ERROR_INVALID_DATA, "Native event-list has not initialized every source slot.");
    const auto nodes_count = context.count ? context.count : 1U;
    // Addresses must be known before making the linked metadata read-only.
    context.nodes = owner.arena.allocate(nodes_count * 8U, GuestAccess::read_write, nullptr, 0);
    for (std::uint32_t index = 0; index < nodes_count; ++index) {
        const SourceListNode node{context.count ? static_cast<SourceLocalCell*>(context.cells.base) + index : nullptr,
            index + 1 < context.count ? static_cast<SourceListNode*>(context.nodes.base) + index + 1 : nullptr};
        owner.arena.write(context.nodes.id, index * 8U, &node, 8U);
    }
    owner.arena.seal(context.nodes.id);
    const SourceListNode empty{nullptr, nullptr}; context.empty_events = owner.arena.allocate(8, GuestAccess::read_only, &empty, 8);
    const SourceEventList header{reinterpret_cast<void*>(context.script_pointer), 0, static_cast<SourceListNode*>(context.empty_events.base), static_cast<SourceListNode*>(context.nodes.base), nullptr};
    context.event_list = owner.arena.allocate(20, GuestAccess::read_only, &header, 20); context.sealed = true; source_local_validate(context);
    Bytes bytes; bytes.put(address(context.event_list.base)); bytes.put(address(context.cells.base)); bytes.put(context.count); reply(frame, ERROR_SUCCESS, "", bytes);
}
void source_local_release(SourceLocalContext& context) {
    auto& owner = source_local_owner();
    if (context.active) throw Fatal(ERROR_BUSY, "An actual caller still owns native event-list storage.");
    if (context.sealed) {
        source_local_validate(context);
        for (std::uint32_t index = 0; index < context.count; ++index)
            if (source_local_cell(context, index).value != context.declarations[index].baseline)
                throw Fatal(ERROR_INVALID_DATA, "Native local writes outside command authority cannot retire as success.");
    }
    for (const auto region : {context.event_list, context.empty_events, context.nodes, context.cells}) if (region.id) owner.arena.release(region.id);
    context.live = false;
}
void source_local_retire(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>(); reader.finish();
    if (module != state->module_id || !state->calls.empty()) { reply(frame, ERROR_BUSY, "Native local retirement requires its empty current module."); return; }
    source_local_release(source_local_context(id)); Bytes bytes; bytes.put(1U); reply(frame, ERROR_SUCCESS, "", bytes);
}
void source_locals_retire_all() {
    if (!state->source_locals) return;
    auto& owner = source_local_owner();
    if (!owner.calls.empty() || owner.synchronizing) throw Fatal(ERROR_BUSY, "Native local callbacks prevent module retirement.");
    for (const auto& context : owner.contexts) if (context->live) source_local_release(*context);
    owner.arena.retire();
    if (owner.arena.live() || owner.arena.committed() || owner.arena.reserved()) throw Fatal(ERROR_INVALID_DATA, "Native event-list retirement retains pages.");
    state->source_locals.reset();
}
void source_local_statistics(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id) { reply(frame, ERROR_INVALID_HANDLE, "Native local statistics have a foreign module."); return; }
    std::uint32_t live = 0, retired = 0, active = 0;
    if (state->source_locals) for (const auto& context : state->source_locals->contexts) {
        if (context->live) { if (!context->sealed) throw Fatal(ERROR_INVALID_STATE, "Native local statistics reached incomplete construction."); source_local_validate(*context); ++live; active += context->active; }
        else ++retired;
    }
    Bytes bytes; bytes.put(live); bytes.put(retired); bytes.put(active);
    if (state->source_locals) { const auto& arena = state->source_locals->arena; bytes.put(arena.live()); bytes.put(arena.retired()); bytes.put(arena.committed()); bytes.put(arena.reserved()); }
    else { bytes.put(0U); bytes.put(0U); bytes.put(0U); bytes.put(0U); }
    reply(frame, ERROR_SUCCESS, "", bytes);
}
