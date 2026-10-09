// Included after the shared NVSE owner. Every published function has its public
// x86 ABI; it either owns its real operation or reports a named terminal refusal.
constexpr std::uint32_t expression_initialize_event = 0x100, expression_create_event = 0x101,
    expression_extract_event = 0x102, expression_destroy_event = 0x103, expression_expected_event = 0x104, expression_begin_event = 0x105;

NvseExpressionRuntime& expression_owner() {
    nvse_owner();
    if (!state->expressions || !state->expressions->declared_bytes)
        nvse_unowned("Expression utilities: exact selected-client ABI declaration absent");
    return *state->expressions;
}
Bytes expression_host(std::uint32_t operation, const Bytes& payload) {
    auto& owner = nvse_owner();
    Bytes bytes; bytes.put(state->module_id); bytes.put(owner.handle);
    bytes.data.insert(bytes.data.end(), payload.data.begin(), payload.data.end());
    auto result = perform_callback_bytes(Kind::nvse_callback, operation, bytes);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason[0] ? state->callback_reason : "Expression callback ownership failed.");
    return result;
}
std::uint32_t expression_scalar(std::uint32_t operation, const Bytes& payload) {
    auto bytes = expression_host(operation, payload); Reader reply_reader{bytes.data};
    const auto result = reply_reader.get<std::uint32_t>(); reply_reader.finish(); return result;
}
NvseExpressionCaller& expression_caller(std::uint64_t id) {
    auto& owner = expression_owner();
    // Access to a suspended outer caller is not an active command operation.
    if (owner.callers.empty() || owner.callers.back()->id != id)
        throw Fatal(ERROR_INVALID_HANDLE, "Expression evaluator has no current authoritative caller frame.");
    return *owner.callers.back();
}
NvseExpressionEvaluator& expression_evaluator(void* pointer) {
    auto& owner = expression_owner();
    for (const auto& evaluator : owner.evaluators) if (evaluator->marker.base == pointer) {
        expression_caller(evaluator->caller);
        const auto observed = nvse_copy(static_cast<const std::uint64_t*>(owner.arena.read(evaluator->marker.id, 0, 8)));
        if (observed != evaluator->caller) throw Fatal(ERROR_INVALID_DATA, "Expression opaque evaluator marker changed.");
        return *evaluator;
    }
    throw Fatal(ERROR_INVALID_HANDLE, "Expression evaluator pointer is unknown or retired.");
}
NvseExpressionToken& expression_token_id(NvseExpressionEvaluator& evaluator, std::uint64_t id) {
    if (!id || id > evaluator.tokens.size())
        throw Fatal(ERROR_INVALID_DATA, "Expression token ID has no exact evaluator-owned declaration.");
    const auto index = static_cast<std::size_t>(id - 1);
    if (evaluator.tokens[index].id != id)
        throw Fatal(ERROR_INVALID_DATA, "Expression token ID has no exact evaluator-owned declaration.");
    return evaluator.tokens[index];
}
std::pair<NvseExpressionEvaluator*, NvseExpressionToken*> expression_token(void* pointer) {
    auto& owner = expression_owner();
    for (const auto& evaluator : owner.evaluators) {
        for (auto& token : evaluator->tokens) {
            auto* actual = static_cast<const std::uint8_t*>(evaluator->tokens_region.base) + token.offset;
            if (actual != pointer) continue;
            expression_caller(evaluator->caller);
            const auto* marker = static_cast<const std::uint64_t*>(owner.arena.read(evaluator->tokens_region.id, token.offset, 16));
            if (nvse_copy(marker) != evaluator->id || nvse_copy(marker + 1) != token.id)
                throw Fatal(ERROR_INVALID_DATA, "Expression opaque token identity changed.");
            return {evaluator.get(), &token};
        }
    }
    throw Fatal(ERROR_INVALID_HANDLE, "Expression token pointer is forged, unknown or retired.");
}
void expression_writable(void* pointer, std::uint32_t length) {
    if (!pointer || !length || address(pointer) > std::numeric_limits<std::uint32_t>::max() - length)
        throw Fatal(ERROR_INVALID_ADDRESS, "Expression destination lacks a complete writable extent.");
    auto at = static_cast<std::uintptr_t>(address(pointer)); const auto end = at + length;
    while (at < end) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(reinterpret_cast<void*>(at), &memory, sizeof(memory)) || memory.State != MEM_COMMIT ||
            (memory.Protect & PAGE_GUARD) || ((memory.Protect & 0xffU) != PAGE_READWRITE &&
                (memory.Protect & 0xffU) != PAGE_WRITECOPY && (memory.Protect & 0xffU) != PAGE_EXECUTE_READWRITE &&
                (memory.Protect & 0xffU) != PAGE_EXECUTE_WRITECOPY))
            throw Fatal(ERROR_INVALID_ADDRESS, "Expression destination is read-only, absent or guarded.");
        const auto next = reinterpret_cast<std::uintptr_t>(memory.BaseAddress) + memory.RegionSize;
        if (next <= at) throw Fatal(ERROR_INVALID_ADDRESS, "Expression writable-region walk made no progress.");
        at = next < end ? next : end;
    }
}
void expression_write(void* pointer, const void* source, std::uint32_t length) {
    expression_writable(pointer, length); SIZE_T written = 0;
    if (!WriteProcessMemory(GetCurrentProcess(), pointer, source, length, &written) || written != length)
        throw Fatal(ERROR_INVALID_ADDRESS, "Expression writable destination changed during assignment.");
}
void* __stdcall expression_create(const NvseParamInfo* parameters, void* data, void* this_object, void* container,
    void* script, void* events, double* result, std::uint32_t* offset) {
    return nvse_adapter([&]() -> void* {
        auto& owner = expression_owner();
        if (owner.callers.empty()) nvse_unowned("Expression.Create: authoritative command/caller owner absent");
        auto& caller = *owner.callers.back();
        const std::array<std::uint32_t, 8> frame{address(parameters), address(data), address(this_object), address(container), address(script), address(events), address(result), address(offset)};
        if (frame != caller.frame || nvse_copy(offset) != caller.start)
            throw Fatal(ERROR_INVALID_DATA, "Expression.Create differs from its actual eight-pointer command frame.");
        auto evaluator = std::make_unique<NvseExpressionEvaluator>(); evaluator->caller = caller.id; evaluator->source_start = caller.start;
        std::array<std::uint8_t, 9> marker{}; std::memcpy(marker.data(), &caller.id, 8);
        evaluator->marker = owner.arena.allocate(static_cast<std::uint32_t>(marker.size()), GuestAccess::read_only, marker.data(), static_cast<std::uint32_t>(marker.size()));
        Bytes request; request.put(caller.id); request.put(address(evaluator->marker.base));
        for (const auto pointer : frame) request.put(pointer); request.put(caller.start);
        auto response = expression_host(expression_create_event, request); Reader reply_reader{response.data};
        evaluator->id = reply_reader.get<std::uint64_t>(); reply_reader.finish();
        if (!evaluator->id) throw Fatal(ERROR_INVALID_DATA, "C# evaluator has no live identity.");
        auto* pointer = evaluator->marker.base;
        owner.evaluators.push_back(std::move(evaluator)); ++owner.created; ++caller.created;
        return pointer;
    });
}
bool expression_extract_impl(NvseExpressionEvaluator& evaluator, bool vanilla = false) {
    if (evaluator.extracted) throw Fatal(ERROR_INVALID_STATE, "Expression arguments were already consumed.");
    auto& owner = expression_owner(); auto& caller = expression_caller(evaluator.caller);
    Bytes request; request.put(caller.id); request.put(evaluator.id); request.put(address(evaluator.marker.base));
    request.put(nvse_copy(reinterpret_cast<const std::uint32_t*>(caller.frame[7]))); request.put(vanilla ? 1U : 0U);
    auto response = expression_host(expression_extract_event, request); Reader reader{response.data};
    const auto count = reader.get<std::uint32_t>(), end = reader.get<std::uint32_t>(), nodes = reader.get<std::uint32_t>();
    if (count > 255 || nodes > 3 * count || end < caller.start || end > caller.end)
        throw Fatal(ERROR_INVALID_DATA, "C# expression count, pair topology or source end extent drifted.");
    Bytes token_bytes; std::uint32_t pair_count = 0;
    evaluator.tokens.reserve(nodes); evaluator.roots.reserve(count);
    for (std::uint32_t index = 0; index < nodes; ++index) {
        NvseExpressionToken token; token.id = reader.get<std::uint64_t>(); token.type = reader.get<std::uint32_t>();
        if (token.id != static_cast<std::uint64_t>(index) + 1) throw Fatal(ERROR_INVALID_DATA, "C# token IDs are not an exact ordered lifetime.");
        while (token_bytes.data.size() % 8) token_bytes.put(std::uint8_t{0});
        token.offset = static_cast<std::uint32_t>(token_bytes.data.size()); token_bytes.put(evaluator.id); token_bytes.put(token.id);
        switch (token.type) {
        case 0: case 1:
            token.number = reader.get<double>();
            if (!std::isfinite(token.number) || (token.type == 1 && token.number != 0 && token.number != 1))
                throw Fatal(ERROR_INVALID_DATA, "Expression numeric/Boolean token is invalid.");
            break;
        case 2: {
            const auto length = reader.get<std::uint32_t>();
            if (length > 16384) throw Fatal(ERROR_INVALID_DATA, "Expression string token exceeds its byte extent.");
            const auto* bytes = static_cast<const char*>(reader.bytes(length));
            token.text.assign(bytes ? bytes : "", length);
            if (token.text.find('\0') != std::string::npos) throw Fatal(ERROR_INVALID_DATA, "Expression string token contains an embedded terminator.");
            token.string_offset = static_cast<std::uint32_t>(token_bytes.data.size());
            token_bytes.data.insert(token_bytes.data.end(), token.text.begin(), token.text.end()); token_bytes.put(std::uint8_t{0}); break;
        }
        case 3: case 6: token.number = reader.get<std::uint32_t>(); break;
        case 11: case 12: case 13: case 14:
            token.local_context = reader.get<std::uint64_t>(); token.local_entry = reader.get<std::uint32_t>();
            token.local_index = reader.get<std::uint32_t>(); token.cached_string = reader.get<std::uint32_t>();
            if (!token.local_context || !token.local_index) throw Fatal(ERROR_INVALID_DATA, "Expression local has no source cell identity.");
            break;
        case 21:
            token.left = reader.get<std::uint64_t>(); token.right = reader.get<std::uint64_t>();
            if (!token.left || !token.right || token.left >= token.id || token.right >= token.id)
                throw Fatal(ERROR_INVALID_DATA, "Expression pair does not own two preceding basic tokens.");
            token.pair_offset = pair_count++ * 8; break;
        default: nvse_unowned("Expression token: native category owner absent");
        }
        evaluator.tokens.push_back(std::move(token));
    }
    for (std::uint32_t index = 0; index < count; ++index) {
        const auto root = reader.get<std::uint64_t>(); expression_token_id(evaluator, root); evaluator.roots.push_back(root);
    }
    reader.finish();
    if (nodes) evaluator.tokens_region = owner.arena.allocate(static_cast<std::uint32_t>(token_bytes.data.size()), GuestAccess::read_only,
        token_bytes.data.data(), static_cast<std::uint32_t>(token_bytes.data.size()));
    if (pair_count) {
        Bytes pairs;
        for (const auto& token : evaluator.tokens) if (token.type == 21) {
            const auto& left = expression_token_id(evaluator, token.left); const auto& right = expression_token_id(evaluator, token.right);
            if ((left.type != 0 && left.type != 2) || right.type == 21)
                throw Fatal(ERROR_INVALID_DATA, "Expression pair key/value categories drifted.");
            pairs.put(address(static_cast<const std::uint8_t*>(evaluator.tokens_region.base) + left.offset));
            pairs.put(address(static_cast<const std::uint8_t*>(evaluator.tokens_region.base) + right.offset));
        }
        evaluator.pairs_region = owner.arena.allocate(static_cast<std::uint32_t>(pairs.data.size()), GuestAccess::read_only,
            pairs.data.data(), static_cast<std::uint32_t>(pairs.data.size()));
    }
    expression_write(reinterpret_cast<void*>(caller.frame[7]), &end, sizeof(end));
    evaluator.extracted = true; caller.tokens += nodes; return true;
}
bool __fastcall expression_extract(void* pointer) { return nvse_adapter([&] { return expression_extract_impl(expression_evaluator(pointer)); }); }
void __fastcall expression_destroy(void* pointer) {
    nvse_adapter([&] {
        auto& owner = expression_owner(); auto& evaluator = expression_evaluator(pointer); auto& caller = expression_caller(evaluator.caller);
        Bytes request; request.put(caller.id); request.put(evaluator.id); request.put(address(pointer));
        if (expression_scalar(expression_destroy_event, request) != 1) throw Fatal(ERROR_INVALID_DATA, "C# evaluator refused actual destruction.");
        if (evaluator.pairs_region.id) owner.arena.release(evaluator.pairs_region.id);
        if (evaluator.tokens_region.id) owner.arena.release(evaluator.tokens_region.id);
        owner.arena.release(evaluator.marker.id); ++owner.destroyed; ++caller.destroyed;
        for (auto at = owner.evaluators.begin(); at != owner.evaluators.end(); ++at) if (at->get() == &evaluator) { owner.evaluators.erase(at); return; }
        throw Fatal(ERROR_INVALID_DATA, "Native evaluator disappeared before destruction publication.");
    });
}
std::uint8_t __fastcall expression_count(void* pointer) {
    return nvse_adapter([&] { return static_cast<std::uint8_t>(expression_evaluator(pointer).roots.size()); });
}
void* __fastcall expression_argument(void* pointer, std::uint32_t index) {
    return nvse_adapter([&]() -> void* {
        auto& evaluator = expression_evaluator(pointer);
        if (index >= evaluator.roots.size()) return nullptr;
        auto& token = expression_token_id(evaluator, evaluator.roots[index]);
        return const_cast<void*>(expression_owner().arena.read(evaluator.tokens_region.id, token.offset, 16));
    });
}
std::uint8_t __fastcall expression_type(void* pointer) { return nvse_adapter([&] { return static_cast<std::uint8_t>(expression_token(pointer).second->type); }); }
double __fastcall expression_number(void* pointer) {
    return nvse_adapter([&] { const auto found = expression_token(pointer); auto& token = *found.second;
        if (expression_local_type(token.type)) return expression_local_number(*found.first, token);
        return token.type <= 1 ? token.number : 0.0; });
}
bool __fastcall expression_boolean(void* pointer) {
    return nvse_adapter([&] {
        const auto found = expression_token(pointer); auto& token = *found.second;
        if (expression_local_type(token.type)) return expression_local_boolean(*found.first, token);
        return token.type == 2 ? !token.text.empty() : ((token.type <= 1 || token.type == 3 || token.type == 6) && token.number != 0);
    });
}
std::uint32_t __fastcall expression_form_id(void* pointer) {
    return nvse_adapter([&] {
        const auto found = expression_token(pointer); auto* token = found.second;
        if (expression_local_type(token->type)) return expression_local_form_id(*found.first, *token);
        if (token->type == 3) return static_cast<std::uint32_t>(token->number);
        if (token->type == 0) { std::uint32_t low = 0; std::memcpy(&low, &token->number, sizeof(low)); return low; }
        return 0U;
    });
}
const char* expression_string_value(NvseExpressionEvaluator& evaluator, const NvseExpressionToken& token) {
    auto& owner = expression_owner();
    if (expression_local_type(token.type)) return expression_local_string(evaluator, token);
    if (token.type != 2) return static_cast<const char*>(owner.arena.read(evaluator.marker.id, 8, 1));
    const auto* actual = static_cast<const char*>(owner.arena.read(evaluator.tokens_region.id, token.string_offset, static_cast<std::uint32_t>(token.text.size()) + 1));
    if (std::memcmp(actual, token.text.c_str(), token.text.size() + 1)) throw Fatal(ERROR_INVALID_DATA, "Native expression string bytes changed.");
    return actual;
}
const char* __fastcall expression_string(void* pointer) {
    return nvse_adapter([&] { const auto pair = expression_token(pointer); return expression_string_value(*pair.first, *pair.second); });
}
const void* __fastcall expression_pair(void* pointer) {
    return nvse_adapter([&]() -> const void* {
        const auto found = expression_token(pointer); auto& evaluator = *found.first; auto& token = *found.second;
        if (token.type != 21) return nullptr;
        auto& left = expression_token_id(evaluator, token.left); auto& right = expression_token_id(evaluator, token.right);
        const auto* actual = static_cast<const std::uint32_t*>(expression_owner().arena.read(evaluator.pairs_region.id, token.pair_offset, 8));
        if (nvse_copy(actual) != address(static_cast<const std::uint8_t*>(evaluator.tokens_region.base) + left.offset) ||
            nvse_copy(actual + 1) != address(static_cast<const std::uint8_t*>(evaluator.tokens_region.base) + right.offset))
            throw Fatal(ERROR_INVALID_DATA, "Expression pair does not point to its two retained token lifetimes.");
        return actual;
    });
}
bool __fastcall expression_convert(void* pointer, std::uint8_t type) {
    return nvse_adapter([&] {
        const auto from = expression_token(pointer).second->type;
        if (from == type) return true;
        if (expression_local_type(from)) return type == 10 || type == 1 ||
            from == 11 && type == 0 || from == 12 && type == 3 || from == 13 && type == 2 || from == 14 && type == 6;
        return type == 1 && (from == 0 || from == 2 || from == 3 || from == 6) || from == 1 && type == 0;
    });
}
void* __fastcall expression_form(void* pointer) { return nvse_adapter([&]() -> void* {
    const auto token = expression_token(pointer);
    if (token.second->type == 3 || token.second->type == 12) return expression_source_form(*token.first, *token.second);
    if (expression_local_type(token.second->type)) (void)expression_local_read(*token.first, *token.second);
    return nullptr; }); }
std::uint32_t __fastcall expression_array(void* pointer) {
    return nvse_adapter([&] {
        const auto found = expression_token(pointer); const auto& token = *found.second;
        const auto id = token.type == 14 ? expression_local_handle(*found.first, token) :
            token.type == 6 ? static_cast<std::uint32_t>(token.number) : 0U;
        return expression_owner().array_pointer ? array_objects_pointer(id) : values_array_reference(id);
    });
}
std::uint32_t __fastcall expression_actor_value(void*) { nvse_unowned("Expression.GetActorValue: source name/type conversion owner absent"); }
void* __fastcall expression_variable(void* pointer) {
    return nvse_adapter([&]() -> void* { const auto found = expression_token(pointer);
        if (!expression_local_type(found.second->type)) nvse_unowned("Expression.GetScriptLocal: token has no actual local cell");
        return expression_local_pointer(*found.first, *found.second); });
}
const void* __fastcall expression_slice(void*) { nvse_unowned("Expression.GetSlice: exact selected compiler std::string layout owner absent"); }
std::uint32_t __fastcall expression_animation(void*) { nvse_unowned("Expression.GetAnimationGroup: authoritative animation name/ID conversion absent"); }
void __fastcall expression_assign(void* evaluator, void* source) { nvse_adapter([&] { values_expression_assign(evaluator, source); }); }
void __fastcall expression_element(void* token, void* destination) { nvse_adapter([&] { values_expression_element(token, destination); }); }
void __fastcall expression_error(void*, const char*, va_list) { nvse_unowned("Expression.ReportError: variadic format/console error publication owner absent"); }
void __fastcall expression_expected(void* pointer, std::uint8_t type) {
    nvse_adapter([&] {
        const auto& evaluator = expression_evaluator(pointer);
        Bytes request; request.put(evaluator.caller); request.put(evaluator.id); request.put(address(pointer)); request.put(static_cast<std::uint32_t>(type));
        if (expression_scalar(expression_expected_event, request) != 1) throw Fatal(ERROR_INVALID_DATA, "C# typed result owner refused expected-return publication.");
    });
}
bool __fastcall expression_extract_v(void* pointer, va_list list) {
    return nvse_adapter([&] {
        auto& evaluator = expression_evaluator(pointer); expression_extract_impl(evaluator);
        const auto count = static_cast<std::uint32_t>(evaluator.roots.size());
        if (count) nvse_readable(list, count * sizeof(void*));
        // Preflight every real destination/category before the first write.
        // Unknown objects/arrays/slices must not return a null proxy or let
        // numeric/string siblings conceal the rejected destination.
        struct Destination { void* pointer; std::uint32_t size; double number; const char* text; };
        std::vector<Destination> destinations; destinations.reserve(count);
        for (std::uint32_t index = 0; index < count; ++index) {
            const auto destination = nvse_copy(reinterpret_cast<void* const*>(list) + index);
            const auto& token = expression_token_id(evaluator, evaluator.roots[index]);
            if (token.type <= 1 || token.type == 11) destinations.push_back({destination, sizeof(double),
                token.type == 11 ? expression_local_number(evaluator, token) : token.number, nullptr});
            else if (token.type == 2 || token.type == 13) destinations.push_back({destination, sizeof(const char*), 0, expression_string_value(evaluator, token)});
            else if (token.type == 3 || token.type == 12) destinations.push_back({destination, sizeof(void*), 0,
                static_cast<const char*>(expression_source_form(evaluator, const_cast<NvseExpressionToken&>(token)))});
            else if (token.type == 6 || token.type == 14) {
                const auto id = token.type == 14 ? expression_local_handle(evaluator, token) : static_cast<std::uint32_t>(token.number);
                destinations.push_back({destination, sizeof(void*), 0, reinterpret_cast<const char*>(array_objects_pointer(id))});
            }
            else nvse_unowned("Expression.ExtractArgsV: destination requires its real object/array/slice/pair owner");
            expression_writable(destinations.back().pointer, destinations.back().size);
        }
        for (const auto& destination : destinations) {
            if (destination.size == sizeof(double)) expression_write(destination.pointer, &destination.number, destination.size);
            else expression_write(destination.pointer, &destination.text, destination.size);
        }
        return true;
    });
}
void expression_initialize(void* destination) {
    auto& owner = expression_owner(); nvse_load_only();
    expression_writable(destination, owner.declared_bytes);
    const NvseExpressionUtilities table{expression_create, expression_destroy, expression_extract, expression_count, expression_argument,
        expression_type, expression_number, expression_boolean, expression_form_id, expression_form, expression_string, expression_array,
        expression_actor_value, expression_variable, expression_pair, expression_slice, expression_animation, expression_expected,
        expression_assign, expression_element, expression_convert, expression_extract_v, expression_error};
    Bytes request; request.put(address(destination)); request.put(owner.callable_bytes); request.put(owner.declared_bytes);
    if (expression_scalar(expression_initialize_event, request) != 1) throw Fatal(ERROR_INVALID_DATA, "C# expression utility declaration refused Init.");
    // Preserve all declared client tail/reserved bytes. The common verified
    // prefix is callable; no unknown slot is filled with a successful stub.
    expression_write(destination, &table, owner.callable_bytes); ++owner.initializations;
}
void expression_configure(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); const auto extent = reader.get<std::uint32_t>();
    const auto prefix = reader.get<std::uint32_t>(), array_pointer = reader.get<std::uint32_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->calls.empty() || state->nvse->phase != NvsePhase::queried_true || state->expressions) {
        reply(frame, ERROR_INVALID_STATE, "Expression declaration requires a once-only empty successful Query before Load."); return;
    }
    if ((prefix != 88 && prefix != sizeof(NvseExpressionUtilities)) || extent < prefix || extent > 4096 || extent % 4 || array_pointer > 1) {
        reply(frame, ERROR_INVALID_DATA, "Expression callable prefix has no complete source-declared client extent."); return;
    }
    auto owner = std::make_unique<NvseExpressionRuntime>(); owner->declared_bytes = extent; owner->callable_bytes = prefix; owner->array_pointer = array_pointer;
    state->expressions = std::move(owner); Bytes bytes; bytes.put(prefix); bytes.put(extent);
    reply(frame, ERROR_SUCCESS, "", bytes);
}
void expression_statistics(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->expressions) { reply(frame, ERROR_INVALID_HANDLE, "Expression statistics have no source-declared generation owner."); return; }
    const auto& owner = *state->expressions;
    Bytes bytes; bytes.put(owner.initializations); bytes.put(owner.created); bytes.put(owner.destroyed); bytes.put(static_cast<std::uint32_t>(owner.evaluators.size()));
    bytes.put(owner.arena.live()); bytes.put(owner.arena.retired()); bytes.put(owner.arena.committed()); bytes.put(owner.arena.reserved());
    reply(frame, ERROR_SUCCESS, "", bytes);
}
void expression_command(const Frame& frame) {
    command_table_validate_all();
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); const auto source = reader.get<std::uint32_t>(), opcode = reader.get<std::uint32_t>();
    const auto caller_id = reader.get<std::uint64_t>(), allocation = reader.get<std::uint64_t>();
    const auto script_data = reader.get<std::uint32_t>(), length = reader.get<std::uint32_t>();
    const auto start = reader.get<std::uint32_t>(), end = reader.get<std::uint32_t>(), target_type = reader.get<std::uint32_t>();
    const auto local_context = reader.get<std::uint64_t>(), script_object = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || !state->expressions || module != state->module_id || state->nvse->phase != NvsePhase::loaded_true || !caller_id) {
        reply(frame, ERROR_INVALID_STATE, "Expression command lacks its actual initialized module/caller owner."); return;
    }
    if (state->calls.size() >= max_call_depth) { reply(frame, ERROR_STACK_OVERFLOW, "Expression command reentry depth exceeded."); return; }
    auto& expression = *state->expressions;
    for (const auto* active : expression.callers) if (active->id == caller_id) throw Fatal(ERROR_INVALID_HANDLE, "Expression caller identity is already active.");
    const NvseRegisteredCommand* command = nullptr;
    for (const auto& candidate : state->nvse->commands) if (candidate.original == source && candidate.opcode == opcode) { command = &candidate; break; }
    if (!command || !command->execute || command->parse != nvse_expression_parser && !(command->parse == 0 && script_object && state->source_objects && state->source_objects->script_interface) &&
        !engine_command_zero_parameter_caller(command->execute, command->parameter_count, command->return_type) || command->needs_parent != 0 || (command->return_type != 0 && command->return_type != 2 && command->return_type != 3 && command->return_type != 5) ||
        (target_type != 0 && target_type != 2 && target_type != 3) ||
        (command->return_type == 0 && target_type == 3) ||
        ((command->return_type == 2 || command->return_type == 3) && command->return_type != target_type) || (target_type && !state->values)) {
        reply(frame, ERROR_NOT_SUPPORTED, "Original command has no admitted objectless expression/numeric frame."); return;
    }
    const auto region = state->arena.inspect(allocation);
    if (region.access != GuestAccess::read_only || region.length != length || address(region.base) != script_data || start > end || end > length)
        throw Fatal(ERROR_INVALID_DATA, "Expression caller has no exact original read-only script-data extent.");
    // A native command receives our retained parameter copy, not a potentially
    // temporary array passed by the original DLL during registration.
    for (std::uint32_t index = 0; index < command->parameter_count; ++index) {
        const auto actual = nvse_copy(command->owned_parameters.get() + index);
        const auto& declared = command->parameter_declarations[index];
        const auto* expected_name = declared.name_address ? declared.name.c_str() : nullptr;
        if (actual.name != expected_name || actual.type != declared.type || actual.optional != declared.optional ||
            nvse_string(actual.name, true) != declared.name)
            throw Fatal(ERROR_INVALID_DATA, "Native retained command parameter copy changed after registration.");
    }
    engine_command_executable(reinterpret_cast<void*>(command->execute));
    auto* source_script = script_object ? &source_object(script_object) : nullptr;
    if (source_script && (!local_context || source_local_context(local_context).script_id != script_object || source_script->kind != 1))
        throw Fatal(ERROR_INVALID_DATA, "Actual command Script/event-list context differs.");
    SourceLocalScope local_scope(local_context, caller_id);
    double result = 0; std::uint32_t offset = start;
    NvseExpressionCaller caller{caller_id, opcode, start, end,
        {address(command->owned_parameters.get()), script_data, 0, 0, source_script ? source_script->pointer : 0U, local_scope.event_list(), address(&result), address(&offset)}};
    const auto function = command->execute;
    if (state->calls.empty()) { state->nvse->interface_calls = 0; state->transaction_callbacks = 0; }
    state->calls.push_back(frame.header.id); expression.callers.push_back(&caller);
    source_local_sync(true);
    Bytes begin; begin.put(caller_id); begin.put(source); begin.put(opcode); begin.put(caller.frame[0]); begin.put(command->parameter_count); begin.put(caller.frame[6]); begin.put(caller.frame[7]);
    for (std::uint32_t index = 0; index < command->parameter_count; ++index) {
        const auto& parameter = command->owned_parameters[index]; nvse_text(begin, parameter.name, true); begin.put(parameter.type); begin.put(parameter.optional);
    }
    if (expression_scalar(expression_begin_event, begin) != 1) throw Fatal(ERROR_INVALID_DATA, "C# command caller refused its retained parameter copy.");
    CallReceipt receipt{};
    { NvseInvocation invocation(*state->nvse); InvokeNvseCommandGuarded(reinterpret_cast<void*>(function), caller.frame.data(), &receipt); }
    const auto stage = "NVSE command " + std::to_string(opcode);
    nvse_check_exception(receipt, stage, function);
    source_local_sync(false);
    array_objects_refresh_all();
    nvse_check_call(receipt, stage, function);
    for (const auto& evaluator : expression.evaluators) if (evaluator->caller == caller_id)
        throw Fatal(ERROR_INVALID_DATA, "Original command returned with a live evaluator/token lifetime.");
    if (!std::isfinite(result) || offset < start || offset > end || caller.created != caller.destroyed)
        throw Fatal(ERROR_INVALID_DATA, "Original command result/source cursor/lifetime ownership drifted.");
    expression.callers.pop_back(); state->calls.pop_back();
    Bytes response; nvse_call_bytes(response, receipt); response.put(result); response.put(offset);
    response.put(caller.created); response.put(caller.destroyed); response.put(caller.tokens); reply(frame, ERROR_SUCCESS, "", response);
}
void expression_retire() {
    if (!state->expressions) return;
    auto& owner = *state->expressions;
    if (!owner.callers.empty() || !owner.evaluators.empty() || owner.created != owner.destroyed || owner.arena.live())
        throw Fatal(ERROR_BUSY, "Expression lifetimes prevent actual module retirement.");
    owner.arena.retire();
    if (owner.arena.live() || owner.arena.committed() || owner.arena.reserved()) throw Fatal(ERROR_INVALID_DATA, "Expression retirement retains native pages.");
    state->expressions.reset();
}
