// Source-owned internal ArrayVar publication. Included after values.inc inside
// the existing anonymous namespace. No original pool/member code is executed.
constexpr std::uint32_t array_object_begin = 0x240, array_object_read = 0x241,
    array_object_publish = 0x242, array_object_retire = 0x243;
void* __fastcall array_object_delete(void*, void*, std::uint32_t) {
    nvse_unowned("Internal ArrayElement destructor: original heap/virtual deletion consumer is not bound");
}
void __fastcall array_object_unset(void*, void*) {
    nvse_unowned("Internal ArrayElement.Unset: direct native element mutation is not bound to shared storage");
}
bool __fastcall array_object_set_array(void*, void*, std::uint32_t) {
    nvse_unowned("Internal ArrayElement.SetArray: direct native reference mutation is not bound to shared storage");
}
NvseArrayObjectRuntime& array_objects_owner() {
    values_owner();
    if (!state->array_objects) {
        auto owner = std::make_unique<NvseArrayObjectRuntime>();
        owner->virtuals = {address(reinterpret_cast<const void*>(array_object_delete)),
            address(reinterpret_cast<const void*>(array_object_unset)), address(reinterpret_cast<const void*>(array_object_set_array))};
        owner->vtable = owner->arena.allocate(sizeof(owner->virtuals), GuestAccess::read_only, owner->virtuals.data(), sizeof(owner->virtuals));
        state->array_objects = std::move(owner);
    }
    return *state->array_objects;
}
std::uint32_t array_objects_capacity(std::uint32_t current, std::uint32_t count, std::uint32_t item_size, std::uint32_t minimum) {
    auto capacity = current ? current : minimum;
    while (capacity < count) {
        if (capacity > 1000000) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Internal array capacity exceeds its admitted resource budget.");
        capacity *= 2;
    }
    // Published capacity is the actual first-party container allocation, not
    // a claim about the source process's allocation history or pool address.
    if (count && item_size % 16) { const auto multiple = 16 / (item_size % 16); capacity = (capacity + multiple - 1) / multiple * multiple; }
    return capacity;
}
void array_objects_verify_block(NvseArrayObjectRuntime& owner, const NvseArrayObjectBlock& block) {
    if (!block.region.id) { if (!block.bytes.empty()) throw Fatal(ERROR_INVALID_DATA, "Absent internal array buffer carries bytes."); return; }
    if (owner.arena.inspect(block.region.id).length != block.bytes.size()) throw Fatal(ERROR_INVALID_DATA, "Internal array buffer extent drifted.");
    for (std::uint32_t at = 0; at < block.bytes.size();) {
        const auto count = (std::min)(max_guest_transfer, static_cast<std::uint32_t>(block.bytes.size()) - at);
        if (std::memcmp(owner.arena.read(block.region.id, at, count), block.bytes.data() + at, count))
            throw Fatal(ERROR_INVALID_DATA, "Internal array bytes changed outside their shared state owner.");
        at += count;
    }
}
void array_objects_update(NvseArrayObjectRuntime& owner, NvseArrayObjectBlock& block, std::vector<std::uint8_t> bytes) {
    array_objects_verify_block(owner, block);
    if (block.region.id && block.bytes == bytes) return;
    if (block.region.id && block.bytes.size() == bytes.size()) {
        for (std::uint32_t at = 0; at < bytes.size();) {
            const auto count = (std::min)(max_guest_transfer, static_cast<std::uint32_t>(bytes.size()) - at);
            owner.arena.replace_read_only_range(block.region.id, at, block.bytes.data() + at, bytes.data() + at, count); at += count;
        }
        block.bytes = std::move(bytes); return;
    }
    GuestRegion next{};
    if (!bytes.empty()) next = owner.arena.allocate(static_cast<std::uint32_t>(bytes.size()), GuestAccess::read_only,
        bytes.data(), static_cast<std::uint32_t>(bytes.size()));
    // Do not retire an allocation still published in the ArrayVar header.
    // The caller replaces header pointers first and retires the old block.
    if (block.region.id) throw Fatal(ERROR_INVALID_STATE, "Growing internal buffer requires explicit header publication/retirement order.");
    block = {next, std::move(bytes)};
}
void array_objects_verify_header(NvseArrayObjectRuntime& owner, const NvseArrayObject& object, bool idle) {
    const auto actual = nvse_copy(static_cast<const NvseInternalArrayVar*>(owner.arena.read(object.header.id, 0, sizeof(object.image))));
    auto expected = object.image; expected.lock_thread = actual.lock_thread; expected.lock_depth = actual.lock_depth;
    if (std::memcmp(&actual, &expected, sizeof(actual)) ||
        (actual.lock_depth == 0) != (actual.lock_thread == 0) ||
        actual.lock_depth && (idle || actual.lock_thread != GetCurrentThreadId()))
        throw Fatal(ERROR_INVALID_DATA, "Internal ArrayVar identity/header/lock is not owned by this actual caller thread.");
    const auto* virtuals = owner.arena.read(owner.vtable.id, 0, sizeof(owner.virtuals));
    if (std::memcmp(virtuals, owner.virtuals.data(), sizeof(owner.virtuals))) throw Fatal(ERROR_INVALID_DATA, "Internal element virtual table drifted.");
    array_objects_verify_block(owner, object.data); array_objects_verify_block(owner, object.refs);
    for (const auto& block : object.cells) array_objects_verify_block(owner, block);
    for (const auto& block : object.strings) array_objects_verify_block(owner, block);
}
void array_objects_release_block(NvseArrayObjectRuntime& owner, NvseArrayObjectBlock& block) {
    if (block.region.id) { owner.arena.release(block.region.id); block.region = {}; block.bytes.clear(); }
}
void array_objects_release(NvseArrayObjectRuntime& owner, NvseArrayObject& object) {
    // Independent failures remain in the owner. Do not erase the object until
    // every allocation and the actual parent callback have retired.
    for (auto& block : object.strings) array_objects_release_block(owner, block);
    for (auto& block : object.cells) array_objects_release_block(owner, block);
    for (auto& block : object.retained_failures) array_objects_release_block(owner, block);
    array_objects_release_block(owner, object.data); array_objects_release_block(owner, object.refs);
    if (object.header.id) { owner.arena.release(object.header.id); object.header = {}; }
}
bool array_objects_same(const NvseValueWire& first, const NvseValueWire& second) {
    return first.type == second.type && first.identity == second.identity && first.text == second.text &&
        !std::memcmp(&first.number, &second.number, sizeof(double));
}
struct ArrayObjectDescription {
    std::uint64_t lease = 0; std::uint32_t id = 0, kind = 0; std::uint8_t mod = 0;
    std::string source; std::vector<std::uint8_t> refs;
    std::vector<std::pair<NvseValueWire, NvseValueWire>> entries;
};
ArrayObjectDescription array_objects_read(std::uint32_t id) {
    ArrayObjectDescription source; Bytes begin; begin.put(id);
    auto response = values_host(array_object_begin, begin); Reader reader{response.data}; source.lease = reader.get<std::uint64_t>();
    if (!source.lease) { reader.finish(); return source; }
    source.id = reader.get<std::uint32_t>(); source.kind = reader.get<std::uint32_t>(); source.mod = reader.get<std::uint8_t>();
    const auto entries = reader.get<std::uint32_t>(), refs = reader.get<std::uint32_t>(); source.source = reader.text(); reader.finish();
    if (source.id != id || source.kind > 2 || entries > 1000000 || refs > 1000000 || source.source.empty())
        throw Fatal(ERROR_INVALID_DATA, "Internal array snapshot lost source identity/category/complete extent.");
    source.entries.reserve(entries); source.refs.reserve(refs);
    for (std::uint32_t at = 0; at < entries;) {
        Bytes request; request.put(source.lease); request.put(0U); request.put(at); auto chunk = values_host(array_object_read, request); Reader part{chunk.data};
        const auto count = part.get<std::uint32_t>(); if (!count || count > entries - at) throw Fatal(ERROR_INVALID_DATA, "Internal array entries do not cover the source extent.");
        for (std::uint32_t index = 0; index < count; ++index) { auto key = values_read_wire(part); auto value = values_read_wire(part); source.entries.emplace_back(std::move(key), std::move(value)); }
        part.finish(); at += count;
    }
    for (std::uint32_t at = 0; at < refs;) {
        Bytes request; request.put(source.lease); request.put(1U); request.put(at); auto chunk = values_host(array_object_read, request); Reader part{chunk.data};
        const auto count = part.get<std::uint32_t>(); if (!count || count > refs - at || count != part.data.size() - part.at)
            throw Fatal(ERROR_INVALID_DATA, "Internal array references omit source occurrences.");
        source.refs.insert(source.refs.end(), part.data.begin() + part.at, part.data.end()); part.at += count; part.finish(); at += count;
    }
    return source;
}
std::uint32_t array_objects_pointer(std::uint32_t id) {
    if (!id) return 0;
    auto& owner = array_objects_owner();
    if (owner.refreshing) throw Fatal(ERROR_BUSY, "Internal array publication cannot recursively replace a pending snapshot.");
    struct Refresh { bool& active; explicit Refresh(bool& value) : active(value) { active = true; } ~Refresh() { active = false; } } refresh(owner.refreshing);
    auto source = array_objects_read(id);
    auto found = owner.objects.find(id);
    if (!source.lease) {
        if (found != owner.objects.end()) {
            auto& object = *found->second; array_objects_verify_header(owner, object, state->calls.size() == 1 && !values_caller_id());
            const auto lifetime = object.header.id; const auto pointer = address(object.header.base);
            array_objects_release(owner, object); Bytes end; end.put(id); end.put(lifetime); end.put(pointer);
            if (values_scalar(array_object_retire, end) != 1) throw Fatal(ERROR_INVALID_DATA, "Collected array lacks native/source retirement correlation.");
            owner.objects.erase(found);
        }
        return 0;
    }
    if (found == owner.objects.end()) {
        auto object = std::make_unique<NvseArrayObject>(); object->source = source.source;
        object->image = {source.kind, 0, 0, 2, id, source.mod, static_cast<std::uint8_t>(source.kind == 2 ? 3 : 1),
            static_cast<std::uint8_t>(source.kind == 0), 0, 0, 0, 8, 0, 0};
        found = owner.objects.emplace(id, std::move(object)).first;
        auto& constructed = *found->second;
        constructed.header = owner.arena.allocate(sizeof(constructed.image), GuestAccess::read_write, &constructed.image, sizeof(constructed.image));
    }
    auto& object = *found->second; array_objects_verify_header(owner, object, false);
    if (source.kind != object.image.kind || source.mod != object.image.mod || source.source != object.source)
        throw Fatal(ERROR_INVALID_DATA, "Living ArrayVar changed its source creator/kind.");
    // Native header stays fixed for its complete array lifetime. Packed cells
    // retain their vector slots until capacity grows. Map cells retain their
    // independent addresses across key insertion/reordering and table growth.
    const auto count = static_cast<std::uint32_t>(source.entries.size());
    const auto item_size = source.kind == 0 ? 24U : source.kind == 1 ? 16U : 8U;
    const auto capacity = array_objects_capacity(object.image.capacity, count, item_size, 2);
    const auto ref_capacity = array_objects_capacity(object.image.ref_capacity, static_cast<std::uint32_t>(source.refs.size()), 1, 8);
    std::vector<NvseArrayObjectCell> entries; entries.reserve(count); std::vector<std::uint8_t> text_bytes;
    std::vector<std::pair<std::size_t, bool>> text_fixups;
    std::map<std::string, std::size_t> previous_entries;
    auto key_identity = [](const NvseValueWire& key) {
        if (key.type == 3) return std::string("s:") + key.text;
        std::uint64_t bits = 0; std::memcpy(&bits, &key.number, sizeof(bits)); if (key.number == 0) bits = 0;
        return std::string("n:") + std::to_string(bits);
    };
    for (std::size_t old = 0; old < object.entries.size(); ++old) previous_entries.emplace(key_identity(object.entries[old].key), old);
    for (std::uint32_t index = 0; index < count; ++index) {
        const auto& pair = source.entries[index]; NvseArrayObjectCell cell; bool previous = false;
        if (const auto old = previous_entries.find(key_identity(pair.first)); old != previous_entries.end()) { cell = object.entries[old->second]; previous = true; }
        if (!previous && source.kind != 0) {
            const auto slot = object.next_cell++; cell.pool = slot / 1024; cell.offset = slot % 1024 * sizeof(NvseInternalElement);
            if (cell.pool == object.cells.size()) {
                NvseArrayObjectBlock block; block.bytes.resize(1024 * sizeof(NvseInternalElement));
                block.region = owner.arena.allocate(static_cast<std::uint32_t>(block.bytes.size()), GuestAccess::read_only, block.bytes.data(), static_cast<std::uint32_t>(block.bytes.size()));
                try { object.cells.push_back(std::move(block)); } catch (...) { owner.arena.release(block.region.id); throw; }
            }
            cell.address = address(static_cast<std::uint8_t*>(object.cells[cell.pool].region.base) + cell.offset);
        }
        if (source.kind == 2 && !previous) { cell.key_text = static_cast<std::uint32_t>(text_bytes.size()); text_bytes.insert(text_bytes.end(), pair.first.text.begin(), pair.first.text.end()); text_bytes.push_back(0); text_fixups.emplace_back(entries.size(), true); }
        if (pair.second.type == 3 && (!previous || !array_objects_same(pair.second, cell.value))) {
            cell.value_text = static_cast<std::uint32_t>(text_bytes.size()); text_bytes.insert(text_bytes.end(), pair.second.text.begin(), pair.second.text.end()); text_bytes.push_back(0); text_fixups.emplace_back(entries.size(), false);
        }
        if (pair.second.type != 3) cell.value_text = 0;
        cell.key = pair.first; cell.value = pair.second; entries.push_back(std::move(cell));
    }
    if (!text_bytes.empty()) {
        NvseArrayObjectBlock block; block.bytes = std::move(text_bytes);
        block.region = owner.arena.allocate(static_cast<std::uint32_t>(block.bytes.size()), GuestAccess::read_only, block.bytes.data(), static_cast<std::uint32_t>(block.bytes.size()));
        for (const auto& fixup : text_fixups) { auto& entry = entries[fixup.first]; auto& pointer = fixup.second ? entry.key_text : entry.value_text; pointer = address(static_cast<std::uint8_t*>(block.region.base) + pointer); }
        try { object.strings.push_back(std::move(block)); } catch (...) { owner.arena.release(block.region.id); throw; }
    }
    const auto width = capacity * item_size;
    std::vector<std::uint8_t> data_bytes(object.data.region.id || count ? width : 0);
    auto cell_bytes = object.cells;
    for (std::uint32_t index = 0; index < count; ++index) {
        auto& cell = entries[index]; NvseInternalElement value{}; value.vtable = address(owner.vtable.base); value.type = static_cast<std::uint8_t>(cell.value.type); value.array = id;
        if (cell.value.type == 1) std::memcpy(&value.bits, &cell.value.number, sizeof(value.bits));
        else if (cell.value.type == 3) value.bits = cell.value_text;
        else if (cell.value.type == 2 || cell.value.type == 4) value.bits = cell.value.identity;
        else throw Fatal(ERROR_INVALID_DATA, "Internal element has no typed payload.");
        auto* destination = data_bytes.data() + static_cast<std::size_t>(index) * item_size;
        if (source.kind == 0) std::memcpy(destination, &value, sizeof(value));
        else {
            std::memcpy(cell_bytes[cell.pool].bytes.data() + cell.offset, &value, sizeof(value));
            if (source.kind == 1) { const NvseInternalNumericEntry record{cell.key.number, cell.address, 0}; std::memcpy(destination, &record, sizeof(record)); }
            else { const NvseInternalStringEntry record{cell.key_text, cell.address}; std::memcpy(destination, &record, sizeof(record)); }
        }
    }
    for (std::size_t index = 0; index < cell_bytes.size(); ++index) array_objects_update(owner, object.cells[index], std::move(cell_bytes[index].bytes));
    std::vector<std::uint8_t> refs_bytes(object.refs.region.id || !source.refs.empty() ? ref_capacity : 0); std::copy(source.refs.begin(), source.refs.end(), refs_bytes.begin());
    NvseArrayObjectBlock old_data, old_refs;
    if (object.data.region.id && object.data.bytes.size() != data_bytes.size()) { old_data = std::move(object.data); object.data = {}; }
    if (object.refs.region.id && object.refs.bytes.size() != refs_bytes.size()) { old_refs = std::move(object.refs); object.refs = {}; }
    // Failed growth retains both old and new allocations for fatal cleanup;
    // never decommit a pointer still present in the published old header.
    try { array_objects_update(owner, object.data, std::move(data_bytes)); array_objects_update(owner, object.refs, std::move(refs_bytes)); }
    catch (...) { if (old_data.region.id) object.retained_failures.push_back(std::move(old_data)); if (old_refs.region.id) object.retained_failures.push_back(std::move(old_refs)); throw; }
    auto actual = nvse_copy(static_cast<const NvseInternalArrayVar*>(owner.arena.read(object.header.id, 0, sizeof(object.image))));
    auto next = object.image; next.data = address(object.data.region.base); next.count = count; next.capacity = capacity;
    next.refs = address(object.refs.region.base); next.ref_count = static_cast<std::uint32_t>(source.refs.size()); next.ref_capacity = ref_capacity;
    next.lock_thread = actual.lock_thread; next.lock_depth = actual.lock_depth;
    owner.arena.write(object.header.id, 0, &next, sizeof(next)); object.image = next; object.entries = std::move(entries);
    if (source.kind == 0) for (std::uint32_t index = 0; index < count; ++index) object.entries[index].address = next.data + index * 24;
    array_objects_release_block(owner, old_data); array_objects_release_block(owner, old_refs);
    // Old text blocks remain only while another living key/value still borrows
    // them. A replaced or removed string cannot alias a later allocation.
    for (auto& block : object.strings) if (block.region.id) {
        const auto base = static_cast<std::uint64_t>(address(block.region.base)), end = base + block.region.length;
        const auto live = std::any_of(object.entries.begin(), object.entries.end(), [&](const auto& entry) {
            return entry.key_text >= base && entry.key_text < end || entry.value_text >= base && entry.value_text < end;
        });
        if (!live) array_objects_release_block(owner, block);
    }
    array_objects_verify_header(owner, object, false);
    Bytes publish; publish.put(source.lease); publish.put(object.header.id); publish.put(address(object.header.base));
    const auto* header = static_cast<const std::uint8_t*>(owner.arena.read(object.header.id, 0, sizeof(object.image)));
    publish.data.insert(publish.data.end(), header, header + sizeof(object.image));
    auto result = values_host(array_object_publish, publish); Reader receipt{result.data};
    if (receipt.get<std::uint64_t>() != object.header.id || receipt.get<std::uint32_t>() != address(object.header.base))
        throw Fatal(ERROR_INVALID_DATA, "Internal array publication has no exact C# native lifetime receipt.");
    receipt.finish(); object.published = true; return address(object.header.base);
}
void array_objects_refresh_all() {
    if (!state->array_objects || state->array_objects->refreshing) return;
    std::vector<std::uint32_t> ids; for (const auto& entry : state->array_objects->objects) ids.push_back(entry.first);
    for (const auto id : ids) (void)array_objects_pointer(id);
}
void array_objects_dispatch(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->values || !state->calls.empty()) {
        reply(frame, ERROR_INVALID_STATE, "Array collection requires its exact living module and no active native caller."); return;
    }
    struct Call { std::uint64_t request; explicit Call(std::uint64_t value) : request(value) { state->calls.push_back(request); } ~Call() { state->calls.pop_back(); } } call(frame.header.id);
    if (state->array_objects) for (const auto& entry : state->array_objects->objects) array_objects_verify_header(*state->array_objects, *entry.second, true);
    array_objects_refresh_all(); Bytes receipt; receipt.put(state->array_objects ? static_cast<std::uint32_t>(state->array_objects->objects.size()) : 0U); receipt.put(0U);
    reply(frame, ERROR_SUCCESS, "", receipt);
}
void array_objects_retire(const Frame& frame) {
    if (!state->array_objects) return;
    if (!state->calls.empty()) throw Fatal(ERROR_BUSY, "Array generation retirement overlaps a native caller.");
    struct Call { explicit Call(std::uint64_t value) { state->calls.push_back(value); } ~Call() { state->calls.pop_back(); } } call(frame.header.id);
    auto& owner = *state->array_objects;
    for (auto at = owner.objects.begin(); at != owner.objects.end();) {
        auto& object = *at->second;
        if (object.header.id) array_objects_verify_header(owner, object, true);
        const auto lifetime = object.header.id; const auto pointer = address(object.header.base); const auto id = at->first;
        array_objects_release(owner, object);
        if (object.published) { Bytes end; end.put(id); end.put(lifetime); end.put(pointer); if (values_scalar(array_object_retire, end) != 1) throw Fatal(ERROR_INVALID_DATA, "Array generation retirement lost its actual source correlation."); }
        at = owner.objects.erase(at);
    }
    if (owner.vtable.id) { owner.arena.release(owner.vtable.id); owner.vtable = {}; }
    owner.arena.retire();
    if (owner.arena.live() || owner.arena.committed() || owner.arena.reserved()) throw Fatal(ERROR_INVALID_DATA, "Internal array retirement retains real native allocations.");
    state->array_objects.reset();
}
