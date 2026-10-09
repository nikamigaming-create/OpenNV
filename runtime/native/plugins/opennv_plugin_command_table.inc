constexpr std::uint32_t command_table_event = 0x480;

NativeCommandTableRuntime& command_table_owner() {
    nvse_owner();
    if (!state->command_table) state->command_table = std::make_unique<NativeCommandTableRuntime>();
    return *state->command_table;
}
Bytes command_table_host(CommandTableCall call, std::uint32_t argument = 0, std::uint32_t pointer = 0,
    const char* name = nullptr, std::uint32_t source = 0, std::uint64_t registration = 0, const Bytes* declaration = nullptr) {
    auto& module = nvse_owner(); Bytes request;
    request.put(state->module_id); request.put(module.handle); request.put(static_cast<std::uint32_t>(call));
    request.put(argument); request.put(pointer); nvse_text(request, name, true); request.put(source); request.put(registration);
    if (declaration) request.data.insert(request.data.end(), declaration->data.begin(), declaration->data.end());
    auto result = perform_callback_bytes(Kind::nvse_callback, command_table_event, request);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason[0] ? state->callback_reason : "CommandTable source callback failed.");
    return result;
}
void command_table_scalar(CommandTableCall call, std::uint32_t argument, std::uint32_t pointer,
    const char* name, std::uint32_t source, std::uint64_t registration) {
    const auto response = command_table_host(call, argument, pointer, name, source, registration); Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "CommandTable publication has no actual C# lifetime receipt.");
    reader.finish();
}
void command_table_declaration_text(Bytes& bytes, const char* source, const std::string& captured) {
    // Registration borrows the descriptor only for that call. Preserve its
    // original address as provenance, but never reread a released descriptor,
    // parameter array or string when the public table is queried later.
    bytes.put(address(source)); bytes.put(static_cast<std::uint32_t>(captured.size()));
    bytes.data.insert(bytes.data.end(), captured.begin(), captured.end());
}
void command_table_check(const NativeCommandPublication& publication) {
    const auto observed = nvse_copy(&publication.descriptor);
    if (observed.execute) engine_command_executable(observed.execute);
    if (std::memcmp(&observed, &publication.original, sizeof(observed)) != 0 ||
        nvse_string(observed.name, false) != publication.expected_name ||
        nvse_string(observed.alias, true) != publication.expected_alias || nvse_string(observed.help, true) != publication.expected_help)
        throw Fatal(ERROR_NOT_SUPPORTED, "CommandTable direct mutation requires its authoritative command/parser/hook replacement owner.");
    for (std::uint32_t index = 0; index < observed.parameter_count; ++index) {
        const auto actual = nvse_copy(observed.parameters + index); const auto& expected = publication.parameters[index];
        if (actual.name != publication.original_parameters[index].name || actual.type != expected.type || actual.optional != expected.optional ||
            (actual.name == nullptr) != (expected.name_address == 0) || nvse_string(actual.name, true) != expected.name)
            throw Fatal(ERROR_NOT_SUPPORTED, "CommandTable parameter mutation has no authoritative caller/compiler replacement owner.");
    }
}
void command_table_validate_all() {
    if (state->command_table) for (const auto& entry : state->command_table->commands) command_table_check(*entry);
}
bool __cdecl command_default_parse(std::uint32_t, NvseParamInfo*, void*, void*) {
    nvse_unowned("CommandTable default parser call requires the original source compiler/ScriptLineBuffer owner");
}
bool __cdecl command_expression_parse(std::uint32_t, NvseParamInfo*, void*, void*) {
    nvse_unowned("CommandTable expression parser call requires the original source compiler/ScriptLineBuffer owner");
}
const NvseCommandInfo* command_table_publish(Bytes response) {
    Reader reader{response.data}; const auto present = reader.get<std::uint32_t>();
    if (!present) { reader.finish(); return nullptr; }
    if (present != 1) throw Fatal(ERROR_INVALID_DATA, "CommandTable source disposition is not Boolean.");
    const auto source = reader.get<std::uint32_t>(), opcode = reader.get<std::uint32_t>();
    const auto registration = reader.get<std::uint64_t>(); const auto return_type = reader.get<std::uint32_t>(); reader.finish();
    const NvseRegisteredCommand* retained = nullptr;
    for (const auto& candidate : state->nvse->commands)
        if (candidate.original == source && candidate.opcode == opcode) { retained = &candidate; break; }
    if (!retained || return_type != retained->return_type || !registration)
        throw Fatal(ERROR_INVALID_HANDLE, "CommandTable pointer has no exact original local command registration.");
    auto& owner = command_table_owner();
    for (const auto& entry : owner.commands) if (entry->registration == registration) {
        if (entry->descriptor.opcode != opcode) throw Fatal(ERROR_INVALID_DATA, "CommandTable registration reused an unrelated opcode.");
        command_table_check(*entry); return &entry->descriptor;
    }
    auto entry = std::make_unique<NativeCommandPublication>(); entry->registration = registration;
    entry->descriptor = retained->source_descriptor;
    auto& descriptor = entry->descriptor;
    if (address(descriptor.execute) != retained->execute || address(descriptor.parse) != retained->parse ||
        address(descriptor.evaluate) != retained->evaluate || descriptor.parameter_count != retained->parameter_count || descriptor.needs_parent != retained->needs_parent)
        nvse_unowned("CommandTable original registration changed before its publication; replacement is unowned");
    Bytes declaration;
    declaration.put(descriptor.opcode); declaration.put(descriptor.needs_parent); declaration.put(descriptor.parameter_count);
    declaration.put(descriptor.flags); declaration.put(address(descriptor.execute)); declaration.put(address(descriptor.parse)); declaration.put(address(descriptor.evaluate));
    command_table_declaration_text(declaration, descriptor.name, retained->source_name);
    command_table_declaration_text(declaration, descriptor.alias, retained->source_alias);
    command_table_declaration_text(declaration, descriptor.help, retained->source_help);
    declaration.put(address(descriptor.parameters));
    entry->name = retained->source_name; entry->alias = retained->source_alias; entry->help = retained->source_help;
    entry->expected_name = entry->name; entry->expected_alias = entry->alias; entry->expected_help = entry->help;
    for (std::uint32_t index = 0; index < descriptor.parameter_count; ++index) {
        const auto& parameter = retained->parameter_declarations[index];
        command_table_declaration_text(declaration, reinterpret_cast<const char*>(parameter.name_address), parameter.name);
        declaration.put(parameter.type); declaration.put(parameter.optional);
    }
    entry->parameters = retained->parameter_declarations;
    descriptor.opcode = opcode;
    descriptor.name = entry->name.c_str(); descriptor.alias = entry->alias.c_str();
    descriptor.help = descriptor.help ? entry->help.c_str() : nullptr;
    descriptor.parameters = retained->owned_parameters.get();
    for (std::uint32_t index = 0; index < descriptor.parameter_count; ++index)
        entry->original_parameters.push_back(nvse_copy(descriptor.parameters + index));
    if (!descriptor.parse) descriptor.parse = reinterpret_cast<void*>(command_default_parse);
    else if (address(descriptor.parse) == nvse_expression_parser) descriptor.parse = reinterpret_cast<void*>(command_expression_parse);
    entry->original = descriptor;
    const auto* pointer = &entry->descriptor;
    const auto receipt = command_table_host(CommandTableCall::publish_command, opcode, address(pointer), nullptr, source, registration, &declaration);
    Reader published{receipt.data}; if (published.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Native command publication was refused."); published.finish();
    owner.commands.push_back(std::move(entry)); return pointer;
}
NativeCommandPublication& command_table_pointer(const NvseCommandInfo* pointer) {
    auto& owner = command_table_owner();
    for (const auto& entry : owner.commands) if (&entry->descriptor == pointer) {
        command_table_check(*entry); return *entry;
    }
    throw Fatal(ERROR_INVALID_HANDLE, "CommandTable pointer is not a current public command publication.");
}
const NvsePluginInfo* command_table_plugin(Bytes response) {
    Reader reader{response.data}; const auto present = reader.get<std::uint32_t>();
    if (!present) { reader.finish(); return nullptr; }
    if (present != 1) throw Fatal(ERROR_INVALID_DATA, "PluginInfo source disposition is not Boolean.");
    const auto generation = reader.get<std::uint64_t>(), module = reader.get<std::uint64_t>();
    const auto handle = reader.get<std::uint32_t>(), info_version = reader.get<std::uint32_t>(), version = reader.get<std::uint32_t>();
    const auto length = reader.get<std::uint32_t>(); const auto* text = reader.bytes(length); reader.finish();
    if (!generation || !module || module >= UINT32_MAX || !handle || handle == nvse_invalid_handle || info_version != 1 || !length ||
        std::memchr(text, 0, length)) throw Fatal(ERROR_INVALID_DATA, "PluginInfo is not an actual complete original Query identity.");
    const std::string name(static_cast<const char*>(text), length); auto& owner = command_table_owner();
    for (const auto& entry : owner.plugins) if (entry->generation == generation && entry->module == module) {
        if (entry->handle != handle || entry->name != name || entry->descriptor.info_version != info_version || entry->descriptor.version != version ||
            entry->descriptor.name != entry->name.c_str()) throw Fatal(ERROR_INVALID_DATA, "Published PluginInfo mutated or changed its source identity.");
        return &entry->descriptor;
    }
    auto entry = std::make_unique<NativePluginPublication>(); entry->generation = generation; entry->module = module;
    entry->handle = handle; entry->name = name; entry->descriptor = {info_version, entry->name.c_str(), version};
    const auto* pointer = &entry->descriptor;
    command_table_scalar(CommandTableCall::publish_plugin, handle, address(pointer), pointer->name, static_cast<std::uint32_t>(module), generation);
    owner.plugins.push_back(std::move(entry)); return pointer;
}
const NvseCommandInfo* __cdecl command_table_start() {
    return nvse_adapter([]() -> const NvseCommandInfo* { auto response = command_table_host(CommandTableCall::start); (void)response;
        nvse_unowned("CommandTable Start has no complete global table lifetime owner"); });
}
const NvseCommandInfo* __cdecl command_table_end() {
    return nvse_adapter([]() -> const NvseCommandInfo* { auto response = command_table_host(CommandTableCall::end); (void)response;
        nvse_unowned("CommandTable End has no complete global table lifetime owner"); });
}
const NvseCommandInfo* __cdecl command_table_opcode(std::uint32_t opcode) {
    return nvse_adapter([&] { return command_table_publish(command_table_host(CommandTableCall::by_opcode, opcode)); });
}
const NvseCommandInfo* __cdecl command_table_name(const char* name) {
    return nvse_adapter([&] { return command_table_publish(command_table_host(CommandTableCall::by_name, 0, 0, name)); });
}
std::uint32_t __cdecl command_table_return(const NvseCommandInfo* pointer) {
    return nvse_adapter([&] { const auto& entry = command_table_pointer(pointer);
        const auto response = command_table_host(CommandTableCall::return_type, entry.descriptor.opcode, address(pointer));
        Reader reader{response.data}; const auto value = reader.get<std::uint32_t>(); reader.finish(); return value; });
}
std::uint32_t __cdecl command_table_version(const NvseCommandInfo* pointer) {
    return nvse_adapter([&] { const auto opcode = pointer ? command_table_pointer(pointer).descriptor.opcode : 0U;
        const auto response = command_table_host(CommandTableCall::required_version, opcode, address(pointer));
        Reader reader{response.data}; const auto value = reader.get<std::uint32_t>(); reader.finish(); return value; });
}
const NvsePluginInfo* __cdecl command_table_parent(const NvseCommandInfo* pointer) {
    return nvse_adapter([&] { const auto& entry = command_table_pointer(pointer);
        return command_table_plugin(command_table_host(CommandTableCall::parent_plugin, entry.descriptor.opcode, address(pointer))); });
}
const NvsePluginInfo* __cdecl command_table_plugin_name(const char* name) {
    return nvse_adapter([&] { return command_table_plugin(command_table_host(CommandTableCall::plugin_name, 0, 0, name)); });
}
const NvsePluginInfo* __cdecl command_table_plugin_dll(const char* name) {
    return nvse_adapter([&] { return command_table_plugin(command_table_host(CommandTableCall::plugin_dll, 0, 0, name)); });
}
const NvseCommandTableInterface nvse_command_table{2, command_table_start, command_table_end,
    command_table_opcode, command_table_name, command_table_return, command_table_version,
    command_table_parent, command_table_plugin_name, command_table_plugin_dll};
void* command_table_interface_address() {
    command_table_owner(); return const_cast<NvseCommandTableInterface*>(&nvse_command_table);
}
void command_table_retire() { state->command_table.reset(); }
