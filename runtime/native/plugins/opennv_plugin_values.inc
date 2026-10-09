// First-party implementation of the declared NVSE value interfaces. Included
// after expression admission helpers; all state requests use correlated IPC.
constexpr std::uint32_t value_get_string = 0x200, value_set_string = 0x201, value_create_string = 0x202,
    value_register_string = 0x203, value_assign_string = 0x204, value_create_array = 0x210,
    value_create_string_map = 0x211, value_create_map = 0x212, value_assign_array = 0x213,
    value_set_element = 0x214, value_append_element = 0x215, value_array_size = 0x216,
    value_lookup_array = 0x217, value_get_element = 0x218, value_snapshot_begin = 0x219,
    value_array_packed = 0x21a, value_array_kind = 0x21b, value_array_has_key = 0x21c,
    value_snapshot_read = 0x21d, value_snapshot_end = 0x21e, value_constructor_write = 0x221, value_constructor_finish = 0x222,
    value_assign_number = 0x205;

NvseValueRuntime& values_owner() {
    nvse_owner();
    if (!state->values) nvse_unowned("StringVar/ArrayVar: authoritative C# value store absent");
    return *state->values;
}
std::uint64_t values_caller_id() {
    return state->expressions && !state->expressions->callers.empty() ? state->expressions->callers.back()->id : 0;
}
Bytes values_host(std::uint32_t operation, const Bytes& payload) {
    auto& owner = values_owner(); auto& plugin = nvse_owner();
    Bytes bytes; bytes.put(state->module_id); bytes.put(plugin.handle); bytes.put(values_caller_id());
    bytes.data.insert(bytes.data.end(), payload.data.begin(), payload.data.end());
    auto result = perform_callback_bytes(Kind::nvse_callback, operation, bytes);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason[0] ? state->callback_reason : "Native value callback failed.");
    if (owner.callbacks == std::numeric_limits<std::uint32_t>::max()) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native value callback identity exhausted.");
    ++owner.callbacks; return result;
}
std::uint32_t values_scalar(std::uint32_t operation, const Bytes& payload) {
    auto response = values_host(operation, payload); Reader reader{response.data};
    const auto value = reader.get<std::uint32_t>(); reader.finish(); return value;
}
void values_text(Bytes& bytes, const std::string& text) {
    if (text.size() > 16384 || text.find('\0') != std::string::npos) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native value text exceeds its typed extent.");
    bytes.put(static_cast<std::uint32_t>(text.size())); bytes.data.insert(bytes.data.end(), text.begin(), text.end());
}
std::string values_read_text(Reader& reader) {
    const auto count = reader.get<std::uint32_t>();
    if (count > 16384 || reader.at > reader.data.size() || count > reader.data.size() - reader.at) throw Fatal(ERROR_INVALID_DATA, "Native value reply has an invalid text extent.");
    std::string text(reinterpret_cast<const char*>(reader.data.data() + reader.at), count); reader.at += count;
    if (text.find('\0') != std::string::npos) throw Fatal(ERROR_INVALID_DATA, "Native value reply has an embedded null.");
    return text;
}
void values_invalidate_string(std::uint32_t id) {
    auto& owner = values_owner(); const auto found = owner.strings.find(id);
    if (found == owner.strings.end()) return;
    owner.arena.release(found->second.region.id); owner.strings.erase(found);
}
const char* __cdecl values_get_string(std::uint32_t id) {
    return nvse_adapter([&]() -> const char* {
        auto& owner = values_owner(); Bytes request; request.put(id);
        auto response = values_host(value_get_string, request); Reader reader{response.data}; const auto present = reader.get<std::uint32_t>();
        if (present > 1) throw Fatal(ERROR_INVALID_DATA, "StringVar.GetString presence reply is invalid.");
        if (!present) { reader.finish(); values_invalidate_string(id); return nullptr; }
        const auto text = values_read_text(reader); reader.finish();
        if (const auto found = owner.strings.find(id); found != owner.strings.end()) {
            if (found->second.bytes == text) {
                const auto* actual = static_cast<const char*>(owner.arena.read(found->second.region.id, 0, static_cast<std::uint32_t>(text.size() + 1)));
                if (std::memcmp(actual, text.c_str(), text.size() + 1) != 0) throw Fatal(ERROR_INVALID_DATA, "Borrowed StringVar bytes drifted outside their owner.");
                return actual;
            }
            values_invalidate_string(id);
        }
        const auto length = static_cast<std::uint32_t>(text.size() + 1);
        const auto region = owner.arena.allocate(length, GuestAccess::read_only, text.c_str(), length);
        try { owner.strings.emplace(id, NvseValueString{region, text}); }
        catch (...) { owner.arena.release(region.id); throw; }
        return static_cast<const char*>(region.base);
    });
}
void __cdecl values_set_string(std::uint32_t id, const char* pointer) {
    nvse_adapter([&] {
        // A missing ID is a declared no-op; it must not read an unused pointer.
        Bytes lookup; lookup.put(id); auto reply_bytes = values_host(value_get_string, lookup); Reader lookup_reader{reply_bytes.data};
        const auto present = lookup_reader.get<std::uint32_t>();
        if (present > 1) throw Fatal(ERROR_INVALID_DATA, "StringVar.SetString presence reply is invalid.");
        if (!present) { lookup_reader.finish(); values_invalidate_string(id); return; }
        values_read_text(lookup_reader); lookup_reader.finish();
        Bytes request; request.put(id); values_text(request, nvse_string(pointer, false));
        if (values_scalar(value_set_string, request) != 1) throw Fatal(ERROR_INVALID_DATA, "StringVar.SetString has no state mutation receipt.");
        values_invalidate_string(id);
    });
}
std::uint32_t __cdecl values_create_string(const char* text, void* script) {
    return nvse_adapter([&] {
        Bytes request; request.put(address(script)); values_text(request, script ? nvse_string(text, false) : std::string{});
        // The public null-Script arm returns zero. A nonnull original Script is
        // never dereferenced/projected without an authoritative C# object owner.
        return values_scalar(value_create_string, request);
    });
}
void __cdecl values_register_string(NvseStringVarInterface* pointer) {
    nvse_adapter([&] {
        auto& owner = values_owner(); const auto actual = nvse_copy(pointer);
        const auto expected = nvse_copy(static_cast<const NvseStringVarInterface*>(owner.tables.base));
        if (actual.version != expected.version || actual.get != expected.get || actual.set != expected.set || actual.create != expected.create ||
            actual.register_interface != expected.register_interface || actual.assign != expected.assign)
            nvse_unowned("StringVar.Register: foreign string-owner table has no source/state lifetime");
        Bytes request; request.put(address(pointer));
        if (values_scalar(value_register_string, request) != 1) throw Fatal(ERROR_INVALID_DATA, "StringVar.Register lacks its retained table receipt.");
        owner.registered = actual; owner.has_registered = true;
        // Native formatting/%z extraction still needs its genuine caller owner.
    });
}
bool __cdecl values_assign_string(const NvseParamInfo* parameters, void* data, void* object, void* container,
    void* script, void* events, double* destination, std::uint32_t* offset, const char* text) {
    return nvse_adapter([&] {
        const auto caller_id = values_caller_id();
        if (!caller_id) nvse_unowned("StringVar.Assign: actual C# command/result assignment owner absent");
        auto& caller = expression_caller(caller_id);
        const std::array<std::uint32_t, 8> frame{address(parameters), address(data), address(object), address(container), address(script), address(events), address(destination), address(offset)};
        if (frame != caller.frame) throw Fatal(ERROR_INVALID_DATA, "StringVar.Assign differs from the actual command frame.");
        expression_writable(destination, sizeof(double)); const auto cursor = nvse_copy(offset);
        auto bytes = nvse_string(text, true);
        if (bytes.size() >= 0x4000) bytes.clear(); // Exact public Assign normalization.
        Bytes request; for (const auto pointer : frame) request.put(pointer); request.put(cursor); values_text(request, bytes);
        const auto id = values_scalar(value_assign_string, request);
        if (!id) throw Fatal(ERROR_INVALID_DATA, "StringVar.Assign has no authoritative published string identity.");
        const double result = id; expression_write(destination, &result, sizeof(result)); values_invalidate_string(id); return true;
    });
}
NvseValueWire values_input(const NvseArrayElement& declaration) {
    const auto element = nvse_copy(&declaration); NvseValueWire value; value.type = element.type;
    switch (element.type) {
    case 1: value.number = element.data.number;
        if (!std::isfinite(value.number)) throw Fatal(ERROR_INVALID_DATA, "Native Element number is non-finite."); break;
    case 2:
        if (element.data.form) nvse_unowned("ArrayVar.Element: nonnull TESForm has no genuine authoritative object projection");
        value.identity = 0; break;
    case 3: value.text = nvse_string(element.data.string, false);
        if (value.text.size() > 16384) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native Element string exceeds its typed state extent."); break;
    case 4: value.identity = address(element.data.array); break;
    default: nvse_unowned("ArrayVar.Element: invalid native value has no typed store operation");
    }
    return value;
}
bool values_key_allowed(const NvseArrayElement& key, std::uint32_t array) {
    const auto declaration = nvse_copy(&key);
    if (declaration.type > 4) throw Fatal(ERROR_INVALID_DATA, "Native key category is undeclared.");
    // The declared Invalid/Form/Array key categories do not select a key arm;
    // their union and an unused value pointer must not be read or projected.
    if (declaration.type != 1 && declaration.type != 3) return false;
    Bytes request; request.put(array); const auto kind = values_scalar(value_array_kind, request);
    return kind == 2 ? declaration.type == 3 : (kind == 0 || kind == 1) && declaration.type == 1;
}
void values_write_wire(Bytes& bytes, const NvseValueWire& value) {
    bytes.put(value.type);
    if (value.type == 1) bytes.put(value.number);
    else if (value.type == 2 || value.type == 4) bytes.put(value.identity);
    else if (value.type == 3) values_text(bytes, value.text);
    else throw Fatal(ERROR_INVALID_DATA, "Native value wire has an invalid category.");
}
NvseValueWire values_read_wire(Reader& reader) {
    NvseValueWire value; value.type = reader.get<std::uint32_t>();
    if (value.type == 1) { value.number = reader.get<double>(); if (!std::isfinite(value.number)) throw Fatal(ERROR_INVALID_DATA, "Native Element reply number is non-finite."); }
    else if (value.type == 2 || value.type == 4) value.identity = reader.get<std::uint32_t>();
    else if (value.type == 3) value.text = values_read_text(reader);
    else throw Fatal(ERROR_INVALID_DATA, "Native Element reply category is invalid.");
    return value;
}
void values_publish_elements(const std::vector<std::pair<NvseArrayElement*, NvseValueWire>>& assignments) {
    struct Publication { NvseArrayElement* destination; NvseArrayElement value; NvseValueWire source; void* previous; void* allocated; bool published; };
    // Exact overlapping Element destinations use the last source assignment,
    // as the public keys-then-value iteration specifies. Partial byte overlaps
    // are not complete Element objects and cannot be admitted.
    std::map<std::uint32_t, std::pair<NvseArrayElement*, NvseValueWire>> selected;
    for (const auto& assignment : assignments) selected.insert_or_assign(address(assignment.first), assignment);
    std::vector<Publication> publications; publications.reserve(selected.size());
    std::map<std::uint32_t, bool> previous_strings;
    std::uint64_t last_end = 0;
    for (const auto& selection : selected) {
        auto* destination = selection.second.first; const auto& source = selection.second.second;
        if (selection.first < last_end) throw Fatal(ERROR_INVALID_ADDRESS, "Native output Element objects overlap incomplete byte extents.");
        last_end = static_cast<std::uint64_t>(selection.first) + sizeof(NvseArrayElement);
        auto value = nvse_copy(destination); expression_writable(destination, sizeof(value));
        if (value.type > 4) throw Fatal(ERROR_INVALID_DATA, "Native output Element is not initialized.");
        void* previous = nullptr;
        if (value.type == 3 && value.data.string) {
            previous = const_cast<char*>(value.data.string);
            if (!values_heap_contains(previous)) nvse_unowned("ArrayVar.Element replacement: foreign string has no admitted FormHeap lifetime");
            if (!previous_strings.emplace(address(previous), true).second) throw Fatal(ERROR_INVALID_DATA, "Distinct Elements double-own one native string allocation.");
        }
        value.data.bits = 0; value.type = static_cast<std::uint8_t>(source.type);
        if (source.type == 1) value.data.number = source.number;
        else if (source.type == 2) {
            if (source.identity) nvse_unowned("ArrayVar.Element output: nonnull TESForm source projection absent");
            value.data.form = nullptr;
        }
        else if (source.type == 4) value.data.array = reinterpret_cast<NvseOpaqueArray*>(source.identity);
        else if (source.type == 3) {
            if (!state->value_heap) nvse_unowned("ArrayVar.Element string output: exact original allocation/free thunk contract absent");
        }
        else throw Fatal(ERROR_INVALID_DATA, "Native output Element category is invalid.");
        publications.push_back({destination, value, source, previous, nullptr, false});
    }
    try {
        for (auto& publication : publications) if (publication.source.type == 3) {
            publication.allocated = values_heap_allocate(static_cast<std::uint32_t>(publication.source.text.size() + 1), 0, 0);
            std::memcpy(publication.allocated, publication.source.text.c_str(), publication.source.text.size() + 1);
            publication.value.data.string = static_cast<const char*>(publication.allocated);
        }
        for (auto& publication : publications) {
            expression_write(publication.destination, &publication.value, sizeof(publication.value)); publication.published = true;
        }
        for (const auto& publication : publications) if (publication.previous) values_heap_release(publication.previous, 0, 0);
    }
    catch (...) {
        // A published allocation now belongs to the real native Element. The
        // fatal generation records it instead of freeing memory still exposed
        // to that caller. Rejected unpublished copies retire immediately.
        for (const auto& publication : publications) if (publication.allocated && !publication.published)
            values_heap_release(publication.allocated, 0, 0);
        throw;
    }
}
void values_expression_element(void* token_pointer, void* destination) {
    const auto pair = expression_token(token_pointer); const auto& token = *pair.second;
    NvseValueWire value;
    if (token.type <= 1) { value.type = 1; value.number = token.number; }
    else if (token.type == 2) { value.type = 3; value.text = token.text; }
    else if (token.type == 3) { value.type = 2; value.identity = static_cast<std::uint32_t>(token.number); }
    else if (token.type == 6) { value.type = 4; value.identity = values_array_reference(static_cast<std::uint32_t>(token.number)); }
    else nvse_unowned("Expression.GetElement: token category lacks a native basic Element representation");
    values_publish_elements({{static_cast<NvseArrayElement*>(destination), value}});
}
void values_expression_assign(void* evaluator_pointer, void* source_pointer) {
    const auto& evaluator = expression_evaluator(evaluator_pointer); auto& caller = expression_caller(evaluator.caller);
    const auto declaration = nvse_copy(static_cast<const NvseArrayElement*>(source_pointer));
    const auto value = values_input(declaration);
    auto* destination = reinterpret_cast<double*>(caller.frame[6]);
    if (value.type == 1) {
        expression_writable(destination, sizeof(double)); Bytes request; request.put(address(destination)); request.put(value.number);
        if (values_scalar(value_assign_number, request) != 1) throw Fatal(ERROR_INVALID_DATA, "Numeric Element result lacks an actual C# caller publication receipt.");
        expression_write(destination, &value.number, sizeof(value.number));
    }
    else if (value.type == 3) values_assign_string(reinterpret_cast<const NvseParamInfo*>(caller.frame[0]), reinterpret_cast<void*>(caller.frame[1]),
        reinterpret_cast<void*>(caller.frame[2]), reinterpret_cast<void*>(caller.frame[3]), reinterpret_cast<void*>(caller.frame[4]),
        reinterpret_cast<void*>(caller.frame[5]), destination, reinterpret_cast<std::uint32_t*>(caller.frame[7]), value.text.c_str());
    else if (value.type == 4) {
        if (!values_assign_array(reinterpret_cast<NvseOpaqueArray*>(value.identity), destination))
            throw Fatal(ERROR_INVALID_HANDLE, "Array Element result refers to a missing source array.");
    }
    else nvse_unowned("Expression.AssignCommandResultFromElement: form result has no typed C# caller/projection owner");
}
std::uint32_t values_array_scalar(std::uint32_t operation, std::uint32_t id) { Bytes request; request.put(id); return values_scalar(operation, request); }
std::uint32_t values_array_reference(std::uint32_t id) {
    if (!id) return 0;
    if (values_array_scalar(value_lookup_array, id) != id) throw Fatal(ERROR_INVALID_HANDLE, "Expression array token has a missing authoritative shared identity.");
    return id;
}
NvseOpaqueArray* values_create(std::uint32_t operation, const void* keys, const NvseArrayElement* values, std::uint32_t size, void* script) {
    // The original public constructor requires Script.GetModIndex. No owned
    // Script projection exists yet; its correlated state owner refuses exactly
    // this boundary, before native array construction or result publication.
    if (!script) nvse_unowned("ArrayVar.Create: null Script has no source creation owner");
    if (size > 1000000) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native constructor exceeds shared array element budget.");
    if (size) {
        nvse_readable(values, size * sizeof(NvseArrayElement));
        if (operation != value_create_array) nvse_readable(keys, size * (operation == value_create_map ? sizeof(double) : sizeof(const char*)));
    }
    Bytes begin; begin.put(address(script)); begin.put(size);
    auto begin_reply = values_host(operation, begin); Reader begin_reader{begin_reply.data}; const auto lease = begin_reader.get<std::uint64_t>(); begin_reader.finish();
    if (!lease) throw Fatal(ERROR_INVALID_DATA, "Native constructor has no source-owned C# lease.");
    Bytes entries; std::uint32_t offset = 0, pending = 0;
    auto flush = [&] {
        if (!pending) return;
        Bytes request; request.put(lease); request.put(offset); request.put(pending); request.data.insert(request.data.end(), entries.data.begin(), entries.data.end());
        if (values_scalar(value_constructor_write, request) != pending) throw Fatal(ERROR_INVALID_DATA, "Native constructor chunk lacks an ordered consumption receipt.");
        offset += pending; pending = 0; entries.data.clear();
    };
    for (std::uint32_t index = 0; index < size; ++index) {
        NvseValueWire key;
        if (operation == value_create_string_map) { key.type = 3; key.text = nvse_string(nvse_copy(static_cast<const char* const*>(keys) + index), false); }
        else { key.type = 1; key.number = operation == value_create_array ? index : nvse_copy(static_cast<const double*>(keys) + index); }
        Bytes pair; values_write_wire(pair, key); values_write_wire(pair, values_input(values[index]));
        if (entries.data.size() + pair.data.size() > max_payload - 128) flush();
        if (pair.data.size() > max_payload - 128) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "One native constructor element crosses its correlated-frame budget.");
        entries.data.insert(entries.data.end(), pair.data.begin(), pair.data.end()); ++pending;
    }
    flush(); Bytes finish; finish.put(lease);
    return reinterpret_cast<NvseOpaqueArray*>(values_scalar(value_constructor_finish, finish));
}
NvseOpaqueArray* __cdecl values_create_array(const NvseArrayElement* values, std::uint32_t size, void* script) {
    return nvse_adapter([&] { return values_create(value_create_array, nullptr, values, size, script); });
}
NvseOpaqueArray* __cdecl values_create_string_map(const char** keys, const NvseArrayElement* values, std::uint32_t size, void* script) {
    return nvse_adapter([&] { return values_create(value_create_string_map, keys, values, size, script); });
}
NvseOpaqueArray* __cdecl values_create_map(const double* keys, const NvseArrayElement* values, std::uint32_t size, void* script) {
    return nvse_adapter([&] { return values_create(value_create_map, keys, values, size, script); });
}
bool __cdecl values_assign_array(NvseOpaqueArray* array, double* destination) {
    return nvse_adapter([&] {
        if (!values_caller_id()) nvse_unowned("ArrayVar.AssignCommandResult: genuine current C# result target absent");
        auto& caller = expression_caller(values_caller_id());
        if (caller.frame[6] != address(destination)) throw Fatal(ERROR_INVALID_ADDRESS, "ArrayVar result destination differs from the actual command frame.");
        expression_writable(destination, sizeof(double)); Bytes request; request.put(address(array)); request.put(address(destination));
        const auto accepted = values_scalar(value_assign_array, request);
        if (accepted > 1) throw Fatal(ERROR_INVALID_DATA, "Array result assignment returned an invalid bool.");
        if (accepted) { const double raw = static_cast<std::int32_t>(address(array)); expression_write(destination, &raw, sizeof(raw)); }
        return accepted == 1;
    });
}
void __cdecl values_set(NvseOpaqueArray* array, const NvseArrayElement& key, const NvseArrayElement& value) {
    nvse_adapter([&] {
        if (!values_array_scalar(value_lookup_array, address(array))) return;
        if (!values_key_allowed(key, address(array))) return;
        Bytes request; request.put(address(array)); values_write_wire(request, values_input(key)); values_write_wire(request, values_input(value));
        if (values_scalar(value_set_element, request) != 1) throw Fatal(ERROR_INVALID_DATA, "Array SetElement has no authoritative mutation receipt.");
    });
}
void __cdecl values_append(NvseOpaqueArray* array, const NvseArrayElement& value) {
    nvse_adapter([&] {
        if (values_array_scalar(value_array_kind, address(array)) != 0) return;
        Bytes request; request.put(address(array)); values_write_wire(request, values_input(value));
        if (values_scalar(value_append_element, request) != 1) throw Fatal(ERROR_INVALID_DATA, "Array AppendElement has no authoritative mutation receipt.");
    });
}
std::uint32_t __cdecl values_size(NvseOpaqueArray* array) { return nvse_adapter([&] { return values_array_scalar(value_array_size, address(array)); }); }
NvseOpaqueArray* __cdecl values_lookup(std::uint32_t id) { return nvse_adapter([&] { return reinterpret_cast<NvseOpaqueArray*>(values_array_scalar(value_lookup_array, id)); }); }
std::uint32_t __cdecl values_packed(NvseOpaqueArray* array) { return nvse_adapter([&] { return values_array_scalar(value_array_packed, address(array)); }); }
int __cdecl values_kind(NvseOpaqueArray* array) { return nvse_adapter([&] { return static_cast<int>(values_array_scalar(value_array_kind, address(array))); }); }
bool __cdecl values_has_key(NvseOpaqueArray* array, const NvseArrayElement& key) {
    return nvse_adapter([&] {
        if (!values_array_scalar(value_lookup_array, address(array))) return false;
        if (!values_key_allowed(key, address(array))) return false;
        Bytes request; request.put(address(array)); values_write_wire(request, values_input(key));
        const auto found = values_scalar(value_array_has_key, request);
        if (found > 1) throw Fatal(ERROR_INVALID_DATA, "ArrayHasKey reply is not a bool."); return found == 1;
    });
}
bool __cdecl values_get(NvseOpaqueArray* array, const NvseArrayElement& key, NvseArrayElement& destination) {
    return nvse_adapter([&] {
        if (!values_array_scalar(value_lookup_array, address(array))) return false;
        if (!values_key_allowed(key, address(array))) return false;
        Bytes request; request.put(address(array)); values_write_wire(request, values_input(key));
        auto response = values_host(value_get_element, request); Reader reader{response.data}; const auto found = reader.get<std::uint32_t>();
        if (found > 1) throw Fatal(ERROR_INVALID_DATA, "Array GetElement reply is not a bool.");
        if (!found) { reader.finish(); return false; }
        const auto value = values_read_wire(reader); reader.finish(); values_publish_elements({{&destination, value}}); return true;
    });
}
bool __cdecl values_elements(NvseOpaqueArray* array, NvseArrayElement* elements, NvseArrayElement* keys) {
    return nvse_adapter([&] {
        if (!elements) return false;
        Bytes begin; begin.put(address(array)); auto begin_reply = values_host(value_snapshot_begin, begin); Reader begin_reader{begin_reply.data};
        const auto lease = begin_reader.get<std::uint64_t>(); const auto count = begin_reader.get<std::uint32_t>(); begin_reader.finish();
        if (!lease) { if (count) throw Fatal(ERROR_INVALID_DATA, "Absent array snapshot declared elements."); return false; }
        if (count > 1000000) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Array enumeration exceeds shared element budget.");
        // Preserve the actual ordered snapshot even if C# reenters another
        // command while servicing a correlated callback. All destinations and
        // all Element ownership preflight before any caller bytes are changed.
        if (count) { expression_writable(elements, count * sizeof(NvseArrayElement)); if (keys) expression_writable(keys, count * sizeof(NvseArrayElement)); }
        std::vector<std::pair<NvseArrayElement*, NvseValueWire>> assignments;
        assignments.reserve(static_cast<std::size_t>(count) * (keys ? 2 : 1));
        for (std::uint32_t at = 0; at < count;) {
            Bytes request; request.put(lease); request.put(at); auto chunk = values_host(value_snapshot_read, request); Reader reader{chunk.data};
            const auto received = reader.get<std::uint32_t>();
            if (!received || received > count - at) throw Fatal(ERROR_INVALID_DATA, "Array enumeration reply crosses its complete snapshot extent.");
            for (std::uint32_t index = 0; index < received; ++index) {
                const auto key = values_read_wire(reader), value = values_read_wire(reader);
                const auto actual_index = static_cast<std::size_t>(at) + index;
                if (keys) assignments.emplace_back(keys + actual_index, key);
                assignments.emplace_back(elements + actual_index, value);
            }
            reader.finish(); at += received;
        }
        Bytes end; end.put(lease);
        if (values_scalar(value_snapshot_end, end) != 1) throw Fatal(ERROR_INVALID_DATA, "Array enumeration has no snapshot retirement receipt.");
        values_publish_elements(assignments);
        return true;
    });
}
void* values_interface(std::uint32_t id) {
    auto& owner = values_owner(); owner.arena.inspect(owner.tables.id);
    if (id == 4) return owner.tables.base;
    if (id == 5) return static_cast<std::uint8_t*>(owner.tables.base) + sizeof(NvseStringVarInterface);
    throw Fatal(ERROR_INVALID_DATA, "Native value interface ID has no table.");
}
void values_configure(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->calls.empty() || state->values ||
        (state->nvse->phase != NvsePhase::mapped && state->nvse->phase != NvsePhase::queried_true)) {
        reply(frame, ERROR_INVALID_STATE, "Native value authority requires a once-only empty module before Query/Load."); return;
    }
    auto owner = std::make_unique<NvseValueRuntime>();
    const NvseStringVarInterface strings{1, values_get_string, values_set_string, values_create_string, values_register_string, values_assign_string};
    const NvseArrayVarInterface arrays{values_create_array, values_create_string_map, values_create_map, values_assign_array, values_set, values_append,
        values_size, values_lookup, values_get, values_elements, values_packed, values_kind, values_has_key};
    std::array<std::uint8_t, sizeof(strings) + sizeof(arrays)> bytes{};
    std::memcpy(bytes.data(), &strings, sizeof(strings)); std::memcpy(bytes.data() + sizeof(strings), &arrays, sizeof(arrays));
    owner->tables = owner->arena.allocate(static_cast<std::uint32_t>(bytes.size()), GuestAccess::read_only, bytes.data(), static_cast<std::uint32_t>(bytes.size()));
    state->values = std::move(owner); Bytes receipt; receipt.put(static_cast<std::uint32_t>(sizeof(strings))); receipt.put(static_cast<std::uint32_t>(sizeof(arrays)));
    receipt.put(static_cast<std::uint32_t>(sizeof(NvseArrayElement))); receipt.put(static_cast<std::uint32_t>(offsetof(NvseArrayElement, type)));
    reply(frame, ERROR_SUCCESS, "", receipt);
}
void values_statistics(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->values) { reply(frame, ERROR_INVALID_HANDLE, "Native value statistics have no generation owner."); return; }
    const auto& owner = *state->values; Bytes receipt; receipt.put(owner.callbacks); receipt.put(static_cast<std::uint32_t>(owner.strings.size()));
    receipt.put(owner.arena.live()); receipt.put(owner.arena.retired()); receipt.put(owner.arena.committed()); receipt.put(owner.arena.reserved());
    receipt.put(state->value_heap ? state->value_heap->created : 0U); receipt.put(state->value_heap ? state->value_heap->destroyed : 0U);
    receipt.put(state->value_heap ? static_cast<std::uint32_t>(state->value_heap->allocations.size()) : 0U);
    reply(frame, ERROR_SUCCESS, "", receipt);
}
void values_retire() {
    if (!state->values) return;
    auto& owner = *state->values;
    for (const auto& entry : owner.strings) owner.arena.release(entry.second.region.id);
    owner.strings.clear(); owner.arena.release(owner.tables.id); owner.registered = {}; owner.has_registered = false;
    owner.arena.retire();
    if (owner.arena.live() || owner.arena.committed() || owner.arena.reserved()) throw Fatal(ERROR_INVALID_DATA, "Native value interface retirement retains pages.");
    state->values.reset();
}
