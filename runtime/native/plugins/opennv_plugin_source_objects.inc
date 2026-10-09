SourceObjectRuntime& source_object_owner() {
    if (!state->nvse || !state->source_objects) throw Fatal(ERROR_INVALID_STATE, "Source object generation owner is absent.");
    return *state->source_objects;
}
SourceObject& source_object(std::uint64_t id) {
    for (const auto& object : source_object_owner().objects) if (object->id == id && object->live) {
        const auto image = state->arena.inspect(object->image_id);
        if (image.length != object->extent || image.access != GuestAccess::read_only || address(image.base) != object->pointer ||
            (object->kind != 2 && nvse_copy(reinterpret_cast<const std::uint32_t*>(object->pointer + 12)) != object->form))
            throw Fatal(ERROR_INVALID_DATA, "Original Script image/identity/protection changed.");
        return *object;
    }
    throw Fatal(ERROR_INVALID_HANDLE, "Source Script is unknown or retired.");
}
SourceObject& source_object_pointer(void* pointer) {
    for (const auto& object : source_object_owner().objects)
        if (object->pointer == address(pointer) && object->live) return source_object(object->id);
    throw Fatal(ERROR_INVALID_HANDLE, "Native TESForm pointer has no actual source object.");
}
std::uint64_t source_object_caller() {
    if (!state->expressions || state->expressions->callers.empty())
        nvse_unowned("TESForm method: actual original campaign caller absent");
    return state->expressions->callers.back()->id;
}
std::uint32_t source_object_method(void* pointer, std::uint32_t method) {
    auto& object = source_object_pointer(pointer);
    Bytes request; request.put(source_object_caller()); request.put(object.id); request.put(method); request.put(object.pointer);
    return expression_scalar(0x401, request);
}
std::uint32_t __fastcall source_form_type(void* pointer, void*) {
    return nvse_adapter([&] { return source_object_method(pointer, 0x8c); });
}
bool __fastcall source_form_reference(void* pointer, void*) {
    return nvse_adapter([&] { return source_object_method(pointer, 0xf0) != 0; });
}
bool __fastcall source_form_actor(void* pointer, void*) {
    return nvse_adapter([&] { return source_object_method(pointer, 0x100) != 0; });
}
const char* __fastcall source_form_editor(void* pointer, void*) {
    return nvse_adapter([&]() -> const char* {
        if (source_object_method(pointer, 0x130) != 1) throw Fatal(ERROR_INVALID_DATA, "Actual editor-ID owner refused publication.");
        auto& object = source_object_pointer(pointer); return reinterpret_cast<const char*>(object.editor);
    });
}
template<std::size_t Index> [[noreturn]] void __fastcall source_form_unowned(void*, void*) {
    const auto text = std::string("TESForm virtual method ") + std::to_string(Index * 4) + " has no authoritative engine behavior";
    nvse_unowned(text.c_str());
}
template<std::size_t... Index> std::array<void*, sizeof...(Index)> source_form_table(std::index_sequence<Index...>) {
    return {reinterpret_cast<void*>(&source_form_unowned<Index>)...};
}
#include "opennv_plugin_source_components.inc"

void source_object_bind(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>();
    const auto kind = reader.get<std::uint32_t>(); const auto image_id = reader.get<std::uint64_t>();
    const auto pointer = reader.get<std::uint32_t>(), extent = reader.get<std::uint32_t>(), form = reader.get<std::uint32_t>();
    const auto metadata_id = reader.get<std::uint64_t>(); const auto editor = reader.get<std::uint32_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->calls.empty() || !id || kind < 1 || kind > 3 ||
        ((kind == 2) != (form == 0)) || state->nvse->phase != NvsePhase::loaded_true)
        throw Fatal(ERROR_INVALID_STATE, "Source Script publication has no empty genuinely loaded owner.");
    if (!state->source_objects) state->source_objects = std::make_unique<SourceObjectRuntime>();
    auto& owner = source_object_owner();
    for (const auto& prior : owner.objects) if (prior->id == id || prior->pointer == pointer || (form && prior->live && prior->form == form))
        throw Fatal(ERROR_ALREADY_EXISTS, "Native source object identity repeats a live/quarantined owner.");
    const auto image = state->arena.inspect(image_id), metadata = state->arena.inspect(metadata_id);
    if (image.length != extent || (kind == 1 && extent != 84) || (kind == 3 && extent != 108) || image.access != GuestAccess::read_write || address(image.base) != pointer ||
        metadata.access != GuestAccess::read_only || editor < address(metadata.base) ||
        static_cast<std::uint64_t>(editor) >= static_cast<std::uint64_t>(address(metadata.base)) + metadata.length ||
        (kind != 2 && (nvse_copy(reinterpret_cast<const std::uint8_t*>(pointer + 4)) != (kind == 1 ? 17 : 71) ||
            nvse_copy(reinterpret_cast<const std::uint32_t*>(pointer + 12)) != form)))
        throw Fatal(ERROR_INVALID_DATA, "Native Script does not match its full source image/metadata declaration.");
    bool terminated = false;
    for (std::uint32_t at = editor - address(metadata.base); at < metadata.length; ++at)
        if (!nvse_copy(static_cast<const std::uint8_t*>(metadata.base) + at)) { terminated = true; break; }
    if (!terminated) throw Fatal(ERROR_INVALID_DATA, "Source editor ID exceeds its actual metadata allocation.");
    const auto vtable = source_object_install_tables(owner, kind, image_id);
    state->arena.seal(image_id);
    auto object = std::make_unique<SourceObject>(); object->id = id; object->image_id = image_id; object->metadata_id = metadata_id;
    object->pointer = pointer; object->form = form; object->editor = editor; object->kind = kind; object->extent = extent; owner.objects.push_back(std::move(object));
    Bytes response; response.put(id); response.put(pointer); response.put(vtable); reply(frame, ERROR_SUCCESS, "", response);
}
void source_object_retire(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->calls.empty()) throw Fatal(ERROR_BUSY, "Native source object retirement has an active/foreign owner.");
    auto& object = source_object(id);
    if (state->source_locals) for (const auto& local : state->source_locals->contexts)
        if (local->live && local->script_id == id) throw Fatal(ERROR_BUSY, "Actual local event list still owns this Script.");
    object.live = false; Bytes response; response.put(std::uint32_t{1}); reply(frame, ERROR_SUCCESS, "", response);
}
void source_objects_retire_all() {
    if (!state->source_objects) return;
    auto& owner = source_object_owner();
    for (const auto& object : owner.objects) object->live = false;
    // FreeLibrary has completed before these actual callable tables retire.
    // Decommit each live capability before releasing its generation quarantine.
    for (auto& component : owner.components) if (component.id) { owner.arena.release(component.id); component = {}; }
    if (owner.quest_vtable.id) { owner.arena.release(owner.quest_vtable.id); owner.quest_vtable = {}; }
    if (owner.vtable.id) { owner.arena.release(owner.vtable.id); owner.vtable = {}; }
    owner.arena.retire();
    if (owner.arena.live() || owner.arena.committed() || owner.arena.reserved())
        throw Fatal(ERROR_INVALID_DATA, "Native source-object vtable retirement retains pages.");
    state->source_objects.reset();
}
void guest_seal(const Frame& frame) {
    Reader reader{frame.payload}; const auto id = reader.get<std::uint64_t>(); reader.finish();
    if (!state->calls.empty()) throw Fatal(ERROR_BUSY, "Native source protection changes require an empty caller.");
    state->arena.seal(id); const auto region = state->arena.inspect(id); Bytes response; response.put(address(region.base)); response.put(region.length);
    reply(frame, ERROR_SUCCESS, "", response);
}

void* expression_source_form(NvseExpressionEvaluator& evaluator, NvseExpressionToken& token) {
    if (token.type != 3) nvse_unowned("TESForm extraction requires an actual typed Form token");
    Bytes request; request.put(evaluator.caller); request.put(static_cast<std::uint32_t>(token.number));
    const auto pointer = expression_scalar(0x403, request);
    if (!pointer) {
        if (token.number != 0) throw Fatal(ERROR_INVALID_DATA, "Nonnull source FormID was replaced with an absent projection.");
        return nullptr;
    }
    auto& object = source_object_pointer(reinterpret_cast<void*>(pointer));
    if (object.form != static_cast<std::uint32_t>(token.number)) throw Fatal(ERROR_INVALID_DATA, "Actual native TESForm identity differs from its C# token.");
    return reinterpret_cast<void*>(pointer);
}
bool __cdecl script_extract_arguments(const void* parameters, void* data, std::uint32_t* offset, void* script, void* events, ...) {
    va_list args; va_start(args, events);
    const auto result = nvse_adapter([&] {
        auto& owner = expression_owner();
        if (owner.callers.empty()) nvse_unowned("Script.ExtractArgsEx: actual compiled caller absent");
        auto& caller = *owner.callers.back();
        if (address(parameters) != caller.frame[0] || address(data) != caller.frame[1] || address(script) != caller.frame[4] ||
            address(events) != caller.frame[5] || address(offset) != caller.frame[7])
            throw Fatal(ERROR_INVALID_DATA, "Script.ExtractArgsEx changed its genuine source/caller pointers.");
        auto* opaque = expression_create(static_cast<const NvseParamInfo*>(parameters), data, reinterpret_cast<void*>(caller.frame[2]),
            reinterpret_cast<void*>(caller.frame[3]), script, events, reinterpret_cast<double*>(caller.frame[6]), offset);
        struct RetireEvaluator { void* pointer; ~RetireEvaluator() { expression_destroy(pointer); } } lifetime{opaque};
        auto& evaluator = expression_evaluator(opaque); if (!expression_extract_impl(evaluator)) return false;
        const auto* command = static_cast<const NvseRegisteredCommand*>(nullptr);
        for (const auto& declaration : state->nvse->commands) if (declaration.opcode == caller.opcode) { command = &declaration; break; }
        if (!command || evaluator.roots.size() > command->parameter_count)
            throw Fatal(ERROR_INVALID_DATA, "Actual argument count exceeds its original registered parameter extent.");
        struct Destination { void* pointer; std::vector<std::uint8_t> bytes; };
        std::vector<Destination> outputs;
        for (std::size_t index = 0; index < evaluator.roots.size(); ++index) {
            auto& token = expression_token_id(evaluator, evaluator.roots[index]);
            const auto parameter = nvse_copy(static_cast<const NvseParamInfo*>(parameters) + index);
            Destination destination{va_arg(args, void*), {}};
            auto copy = [&](const auto& value) { const auto* bytes = reinterpret_cast<const std::uint8_t*>(&value); destination.bytes.assign(bytes, bytes + sizeof(value)); };
            // Numeric standard extraction has real Int32/Float32 outputs;
            // enum/string/format families retain separate source admission.
            if (parameter.type == 1 || parameter.type == 23) {
                if ((token.type != 0 && token.type != 1) || !std::isfinite(token.number) || token.number < INT32_MIN || token.number > INT32_MAX)
                    nvse_unowned("Script.ExtractArgsEx Int32 source/output conversion is unowned");
                copy(static_cast<std::int32_t>(token.number));
            } else if (parameter.type == 2) {
                const auto value = static_cast<float>(token.number);
                if ((token.type != 0 && token.type != 1) || !std::isfinite(value)) nvse_unowned("Script.ExtractArgsEx Float32 source/output conversion is unowned");
                copy(value);
            } else if (token.type == 3) {
                // ExtractArgsEx deliberately does not cast the TESForm subtype.
                // The source decoder separately owns the parameter category.
                auto* value = expression_source_form(evaluator, token); copy(value);
            } else nvse_unowned("Script.ExtractArgsEx string/enum/other output category has no exact source ABI owner");
            expression_writable(destination.pointer, static_cast<std::uint32_t>(destination.bytes.size())); outputs.push_back(std::move(destination));
        }
        for (const auto& destination : outputs) expression_write(destination.pointer, destination.bytes.data(), static_cast<std::uint32_t>(destination.bytes.size()));
        return true;
    });
    va_end(args); return result;
}
bool __cdecl script_function(void*, void*, void*, void*, std::uint8_t, ...) { nvse_unowned("Script.CallFunction: actual lambda/function invocation owner absent"); }
std::uint32_t __cdecl script_parameters(void*, std::uint8_t*) { nvse_unowned("Script.GetFunctionParams: actual compiled function parameter owner absent"); }
bool __cdecl script_format(std::uint32_t, char*, const void*, void*, std::uint32_t*, void*, void*, std::uint32_t, ...) { nvse_unowned("Script.ExtractFormatStringArgs: original format/output owner absent"); }
bool __cdecl script_alternate(void*, void*, std::uint8_t, ...) { nvse_unowned("Script.CallFunctionAlt: actual function invocation owner absent"); }
void* __cdecl script_compile(const char*) { nvse_unowned("Script.CompileScript: actual original source compiler owner absent"); }
void* __cdecl script_expression(const char*) { nvse_unowned("Script.CompileExpression: actual original expression compiler owner absent"); }
std::size_t __stdcall script_decompile(void*, void*, char*) { nvse_unowned("Script.DecompileToBuffer: source compiler/FILE owner absent"); }
const NvseScriptInterface nvse_script_table{script_function, script_parameters, script_extract_arguments, script_format,
    script_alternate, script_compile, script_expression, script_decompile};
void source_script_interface(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->calls.empty() ||
        (state->nvse->phase != NvsePhase::mapped && state->nvse->phase != NvsePhase::queried_true))
        throw Fatal(ERROR_INVALID_STATE, "Script interface attachment has no pre-Load source owner.");
    if (!state->source_objects) state->source_objects = std::make_unique<SourceObjectRuntime>();
    auto& owner = source_object_owner(); if (owner.script_interface) throw Fatal(ERROR_ALREADY_EXISTS, "Script interface already attached.");
    owner.script_interface = true; Bytes response; response.put(static_cast<std::uint32_t>(sizeof(nvse_script_table))); reply(frame, ERROR_SUCCESS, "", response);
}
void* source_script_interface_address() {
    if (!source_object_owner().script_interface) nvse_unowned("Script interface has no actual campaign registration owner");
    return const_cast<NvseScriptInterface*>(&nvse_script_table);
}
