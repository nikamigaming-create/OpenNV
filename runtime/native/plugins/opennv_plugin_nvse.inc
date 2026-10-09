// Included inside the companion's existing anonymous namespace, after its
// transport/call/arena helpers. No original executable or plugin code is here.
constexpr DWORD nvse_missing_owner_exception = 0xe04e5653U;

NvseRuntime& nvse_owner() {
    if (!state || GetCurrentThreadId() != state->thread || !state->nvse || state->calls.empty())
        throw Fatal(ERROR_INVALID_THREAD_ID, "NVSE callback lacks its actual module/call/thread owner.");
    return *state->nvse;
}
struct NvseInvocation {
    NvseRuntime& owner;
    explicit NvseInvocation(NvseRuntime& value) : owner(value) {
        if (owner.invocation_depth >= max_call_depth)
            throw Fatal(ERROR_STACK_OVERFLOW, "Original NVSE invocation/callback depth exceeded its native owner.");
        ++owner.invocation_depth;
    }
    ~NvseInvocation() { --owner.invocation_depth; }
};
void nvse_readable(const void* pointer, std::uint32_t length) {
    if (!length) return;
    auto at = reinterpret_cast<std::uintptr_t>(pointer);
    if (!pointer || at > std::numeric_limits<std::uint32_t>::max() - length)
        throw Fatal(ERROR_INVALID_ADDRESS, "NVSE source pointer has no complete readable extent.");
    const auto end = at + length;
    while (at < end) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(reinterpret_cast<void*>(at), &memory, sizeof(memory)) || memory.State != MEM_COMMIT ||
            (memory.Protect & PAGE_GUARD) || (memory.Protect & 0xffU) == PAGE_NOACCESS ||
            (memory.Protect & 0xffU) == PAGE_EXECUTE)
            throw Fatal(ERROR_INVALID_ADDRESS, "NVSE source extent is unreadable or guarded.");
        const auto region_end = reinterpret_cast<std::uintptr_t>(memory.BaseAddress) + memory.RegionSize;
        if (region_end <= at) throw Fatal(ERROR_INVALID_ADDRESS, "NVSE source region made no extent progress.");
        at = region_end < end ? region_end : end;
    }
}
template<class T> T nvse_copy(const T* pointer) {
    nvse_readable(pointer, sizeof(T)); T value{}; SIZE_T read = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), pointer, &value, sizeof(value), &read) || read != sizeof(value))
        throw Fatal(ERROR_INVALID_ADDRESS, "NVSE declaration changed during its native read.");
    return value;
}
void nvse_bytes(Bytes& out, const void* pointer, std::uint32_t length) {
    if (length > max_payload - 256) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "NVSE source bytes exceed the correlated-frame budget.");
    nvse_readable(pointer, length);
    const auto start = out.data.size(); out.data.resize(start + length); SIZE_T read = 0;
    if (length && (!ReadProcessMemory(GetCurrentProcess(), pointer, out.data.data() + start, length, &read) || read != length))
        throw Fatal(ERROR_INVALID_ADDRESS, "NVSE source bytes changed during their native read.");
}
std::string nvse_string(const char* pointer, bool optional) {
    if (!pointer) {
        if (!optional) throw Fatal(ERROR_INVALID_ADDRESS, "Required NVSE declaration string is null.");
        return {};
    }
    std::string result;
    for (std::uint32_t index = 0; index < max_payload - 256; ++index) {
        const auto byte = nvse_copy(pointer + index);
        if (!byte) return result;
        result.push_back(byte);
    }
    throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "NVSE declaration string has no bounded terminator.");
}
void nvse_text(Bytes& out, const char* pointer, bool optional) {
    const auto text = nvse_string(pointer, optional);
    out.put(address(pointer)); out.put(static_cast<std::uint32_t>(text.size()));
    out.data.insert(out.data.end(), text.begin(), text.end());
}
void nvse_executable(void* pointer, bool optional = false) {
    if (!pointer && optional) return;
    MEMORY_BASIC_INFORMATION memory{};
    if (!pointer || !VirtualQuery(pointer, &memory, sizeof(memory)) || memory.State != MEM_COMMIT ||
        memory.Type != MEM_IMAGE || memory.AllocationBase != state->module || (memory.Protect & PAGE_GUARD) ||
        ((memory.Protect & 0xffU) != PAGE_EXECUTE && (memory.Protect & 0xffU) != PAGE_EXECUTE_READ &&
            (memory.Protect & 0xffU) != PAGE_EXECUTE_READWRITE && (memory.Protect & 0xffU) != PAGE_EXECUTE_WRITECOPY))
        throw Fatal(ERROR_INVALID_ADDRESS, "NVSE callback is not executable code in its retained original module.");
}
std::uint32_t nvse_host(NvseHostCall call, const Bytes& payload) {
    auto& owner = nvse_owner();
    Bytes bytes; bytes.put(state->module_id); bytes.put(owner.handle);
    bytes.data.insert(bytes.data.end(), payload.data.begin(), payload.data.end());
    const auto value = perform_callback(Kind::nvse_callback, static_cast<std::uint32_t>(call), bytes);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason[0] ? state->callback_reason : "NVSE host callback failed.");
    return value;
}
[[noreturn]] void nvse_unowned(const char* operation) {
    if (!state || GetCurrentThreadId() != state->thread) {
        callback_fault.store(ERROR_INVALID_THREAD_ID);
        RaiseException(nvse_missing_owner_exception, EXCEPTION_NONCONTINUABLE, 0, nullptr);
        std::abort();
    }
    try { Bytes bytes; bytes.text(operation); nvse_host(NvseHostCall::unsupported, bytes); }
    catch (const Fatal& error) { retain_callback_fault(error.code, error.what()); }
    catch (...) { retain_callback_fault(ERROR_INVALID_DATA, "NVSE unowned-call evidence failed."); }
    if (!callback_fault.load()) retain_callback_fault(ERROR_NOT_SUPPORTED, operation);
    RaiseException(nvse_missing_owner_exception, EXCEPTION_NONCONTINUABLE, 0, nullptr);
    std::abort();
}
template<class F> auto nvse_adapter(F action) -> decltype(action()) {
    if (!state || GetCurrentThreadId() != state->thread) {
        callback_fault.store(ERROR_INVALID_THREAD_ID);
        RaiseException(nvse_missing_owner_exception, EXCEPTION_NONCONTINUABLE, 0, nullptr);
        std::abort();
    }
    try { return action(); }
    catch (const Fatal& error) { retain_callback_fault(error.code, error.what()); }
    catch (const std::exception& error) { retain_callback_fault(ERROR_INVALID_DATA, error.what()); }
    catch (...) { retain_callback_fault(ERROR_INVALID_DATA, "NVSE public callback failed without a typed receipt."); }
    // Never unwind a first-party C++ exception across an original DLL's ABI.
    // The guarded original invocation reports the retained native fault.
    RaiseException(nvse_missing_owner_exception, EXCEPTION_NONCONTINUABLE, 0, nullptr);
    std::abort();
}
void nvse_load_only() {
    if (nvse_owner().phase != NvsePhase::loading)
        throw Fatal(ERROR_INVALID_STATE, "NVSE command registration is outside actual NVSEPlugin_Load.");
}
bool nvse_register(NvseCommandInfo* pointer, std::uint8_t return_type, std::uint32_t required_version) {
    nvse_load_only();
    const auto raw = nvse_copy(pointer);
    nvse_executable(raw.execute, true); nvse_executable(raw.evaluate, true);
    if (address(raw.parse) != nvse_expression_parser) nvse_executable(raw.parse, true);
    Bytes bytes; bytes.put(address(pointer)); bytes.put(raw.opcode);
    bytes.put(static_cast<std::uint32_t>(return_type)); bytes.put(required_version);
    bytes.put(raw.needs_parent); bytes.put(raw.parameter_count); bytes.put(raw.flags);
    bytes.put(address(raw.execute)); bytes.put(address(raw.parse)); bytes.put(address(raw.evaluate));
    nvse_text(bytes, raw.name, false); nvse_text(bytes, raw.alias, true); nvse_text(bytes, raw.help, true);
    bytes.put(address(raw.parameters));
    if (raw.parameter_count) nvse_readable(raw.parameters, static_cast<std::uint32_t>(raw.parameter_count) * sizeof(NvseParamInfo));
    std::vector<NvseRegisteredParameter> parameter_declarations; parameter_declarations.reserve(raw.parameter_count);
    for (std::uint32_t index = 0; index < raw.parameter_count; ++index) {
        const auto parameter = nvse_copy(raw.parameters + index);
        parameter_declarations.push_back({address(parameter.name), parameter.type, parameter.optional, nvse_string(parameter.name, true)});
        nvse_text(bytes, parameter.name, true); bytes.put(parameter.type); bytes.put(parameter.optional);
    }
    // Zero means the C# owner refused. Commands start from an explicit, nonzero
    // source SetOpcodeBase; never fabricate the core's missing default opcode.
    const auto assigned = nvse_host(NvseHostCall::register_command, bytes);
    ++state->nvse->command_events;
    if (!assigned) return false;
    NvseRegisteredCommand declaration{address(pointer), assigned, address(raw.execute), address(raw.parse), address(raw.evaluate),
        raw.parameter_count, raw.needs_parent, return_type < 6 ? static_cast<std::uint32_t>(return_type) : 0U, std::move(parameter_declarations), {}};
    if (raw.parameter_count) {
        declaration.owned_parameters = std::make_unique<NvseParamInfo[]>(raw.parameter_count);
        for (std::uint32_t index = 0; index < raw.parameter_count; ++index) {
            const auto& parameter = declaration.parameter_declarations[index];
            declaration.owned_parameters[index] = {parameter.name_address ? parameter.name.c_str() : nullptr, parameter.type, parameter.optional};
        }
    }
    state->nvse->commands.push_back(std::move(declaration));
    return true;
}
bool __cdecl nvse_register_plain(NvseCommandInfo* info) { return nvse_adapter([&] { return nvse_register(info, 0, 0); }); }
bool __cdecl nvse_register_typed(NvseCommandInfo* info, std::uint8_t type) { return nvse_adapter([&] { return nvse_register(info, type, 0); }); }
bool __cdecl nvse_register_versioned(NvseCommandInfo* info, std::uint8_t type, std::uint32_t version) { return nvse_adapter([&] { return nvse_register(info, type, version); }); }
void __cdecl nvse_set_opcode(std::uint32_t opcode) {
    nvse_adapter([&] {
        nvse_load_only(); Bytes bytes; bytes.put(opcode);
        if (nvse_host(NvseHostCall::set_opcode, bytes) != 1) nvse_unowned("NVSE.SetOpcodeBase refused its source declaration");
    });
}
void* __cdecl nvse_query_interface(std::uint32_t id) {
    return nvse_adapter([&]() -> void* {
        auto& owner = nvse_owner(); Bytes bytes; bytes.put(id);
        const auto admitted = nvse_host(NvseHostCall::query_interface, bytes);
        if (!admitted) return nullptr;
        if (id == 2 && admitted == 4) return &owner.tables->messaging;
        if (id == 0 && admitted == 2) return &owner.tables->serialization;
        if ((id == 4 && admitted == 1) || (id == 5 && admitted == 2)) return values_interface(id);
        if (id == 6 && admitted == 1) return source_script_interface_address();
        throw Fatal(ERROR_INVALID_DATA, "NVSE interface reply has no first-party native owner.");
    });
}
std::uint32_t __cdecl nvse_plugin_handle() {
    return nvse_adapter([&] {
        auto& owner = nvse_owner();
        return owner.phase == NvsePhase::querying || owner.phase == NvsePhase::loading ? owner.handle : nvse_invalid_handle;
    });
}
const char* __cdecl nvse_runtime_directory() { return nvse_adapter([] { return nvse_owner().directory; }); }
void __cdecl nvse_expression_utils(void* destination) { nvse_adapter([&] { expression_initialize(destination); }); }

void nvse_check_call(const CallReceipt& receipt) {
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason[0] ? state->callback_reason : "NVSE callback ownership failed.");
    if (receipt.exception_code) throw Fatal(receipt.exception_code, "Original NVSE module raised a structured exception.");
    if (receipt.stack_delta != 0 || receipt.preserved_registers != 7)
        throw Fatal(ERROR_INVALID_DATA, "Original NVSE ABI violated stack or EBX/ESI/EDI preservation.");
}
bool nvse_equal(const std::string& first, const std::string& second) {
    // Public identifiers here use ASCII folding. Keep other raw declaration
    // bytes, but refuse locale-dependent identity rather than guessing it.
    if (first.size() != second.size()) return false;
    for (std::size_t index = 0; index < first.size(); ++index) {
        const auto a = static_cast<unsigned char>(first[index]), b = static_cast<unsigned char>(second[index]);
        if (a >= 128 || b >= 128) nvse_unowned("NVSE messaging identifier requires its non-ASCII locale owner");
        const auto fold_a = a >= 'A' && a <= 'Z' ? a + ('a' - 'A') : a;
        const auto fold_b = b >= 'A' && b <= 'Z' ? b + ('a' - 'A') : b;
        if (fold_a != fold_b) return false;
    }
    return true;
}
bool nvse_listen_impl(std::uint32_t handle, const char* sender, NvseMessageCallback function) {
    auto& owner = nvse_owner(); nvse_executable(reinterpret_cast<void*>(function));
    Bytes bytes; bytes.put(handle); nvse_text(bytes, sender, true); bytes.put(address(reinterpret_cast<void*>(function)));
    const auto accepted = nvse_host(NvseHostCall::register_listener, bytes); ++owner.listener_events;
    if (accepted > 1) throw Fatal(ERROR_INVALID_DATA, "NVSE listener reply is not Boolean.");
    if (!accepted) return false;
    // NULL subscribes to other currently loaded plugins, excluding NVSE/self.
    // This generation owns one actual plugin; there are no such recipients.
    if (!sender) return true;
    const auto name = nvse_string(sender, false);
    const auto source = nvse_equal(name, "NVSE") ? 0U : nvse_equal(name, owner.name) ? owner.handle : nvse_invalid_handle;
    if (source == nvse_invalid_handle) throw Fatal(ERROR_INVALID_DATA, "C# admitted a messaging sender without a retained module.");
    for (const auto& listener : owner.listeners) if (listener.sender == source) return true;
    owner.listeners.push_back({source, address(reinterpret_cast<void*>(function))}); return true;
}
bool __cdecl nvse_listen(std::uint32_t handle, const char* sender, NvseMessageCallback function) {
    return nvse_adapter([&] { return nvse_listen_impl(handle, sender, function); });
}
std::uint32_t nvse_deliver(std::uint32_t sender, std::uint32_t type, void* data, std::uint32_t length, const char* receiver) {
    auto& owner = nvse_owner();
    if (sender != 0 && sender != owner.handle) return 0;
    const auto receiver_name = nvse_string(receiver, true);
    if (receiver && !nvse_equal(receiver_name, owner.name)) return 0;
    const auto sender_name = sender == 0 ? std::string("NVSE") : owner.name;
    const auto dispatch = ++owner.next_dispatch;
    if (!dispatch) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "NVSE message lifetime identity exhausted.");
    Bytes bytes; bytes.put(dispatch); bytes.put(sender); bytes.put(type); bytes.put(address(data)); bytes.put(length);
    nvse_text(bytes, receiver, true); nvse_bytes(bytes, data, length);
    const auto expected = nvse_host(NvseHostCall::dispatch_message, bytes);
    std::uint32_t delivered = 0;
    // Snapshot registrations, so reentrant registration cannot invalidate the
    // iteration and cannot receive an already-active dispatch a second time.
    const auto listeners = owner.listeners;
    for (const auto& listener : listeners) {
        if (listener.sender != sender) continue;
        nvse_executable(reinterpret_cast<void*>(listener.function));
        NvseMessage message{sender_name.c_str(), type, length, data}; CallReceipt receipt{};
        { NvseInvocation invocation(owner);
          InvokeNvseOneGuarded(reinterpret_cast<void*>(listener.function), address(&message), &receipt); }
        nvse_check_call(receipt);
        Bytes completion; completion.put(dispatch); completion.put(listener.function); completion.put(sender); completion.put(type);
        completion.put(address(data)); completion.put(length);
        if (nvse_host(NvseHostCall::delivered_message, completion) != 1)
            throw Fatal(ERROR_INVALID_DATA, "NVSE message callback return lacks its C# lifetime receipt.");
        ++delivered;
    }
    if (delivered != expected) throw Fatal(ERROR_INVALID_DATA, "NVSE callback delivery cardinality drifted.");
    return delivered;
}
bool __cdecl nvse_dispatch(std::uint32_t sender, std::uint32_t type, void* data, std::uint32_t length, const char* receiver) {
    return nvse_adapter([&] {
        auto& owner = nvse_owner();
        if (sender != owner.handle) return false; // plugins may not impersonate NVSE
        return nvse_deliver(sender, type, data, length, receiver) != 0;
    });
}
void nvse_set_serialization(std::uint32_t handle, NvseSerializationCallback function, NvseSerializationEvent event) {
    auto& owner = nvse_owner(); nvse_executable(reinterpret_cast<void*>(function), true);
    Bytes bytes; bytes.put(handle); bytes.put(static_cast<std::uint32_t>(event)); bytes.put(address(reinterpret_cast<void*>(function)));
    if (nvse_host(NvseHostCall::serialization_callback, bytes) != 1)
        nvse_unowned("NVSE serialization callback registration refused its module/handle");
    owner.serialization[static_cast<std::size_t>(event)] = address(reinterpret_cast<void*>(function)); ++owner.serialization_events;
}
void __cdecl nvse_save(std::uint32_t handle, NvseSerializationCallback function) { nvse_adapter([&] { nvse_set_serialization(handle, function, NvseSerializationEvent::save); }); }
void __cdecl nvse_load_save(std::uint32_t handle, NvseSerializationCallback function) { nvse_adapter([&] { nvse_set_serialization(handle, function, NvseSerializationEvent::load); }); }
void __cdecl nvse_new(std::uint32_t handle, NvseSerializationCallback function) { nvse_adapter([&] { nvse_set_serialization(handle, function, NvseSerializationEvent::new_game); }); }
void __cdecl nvse_preload(std::uint32_t handle, NvseSerializationCallback function) { nvse_adapter([&] { nvse_set_serialization(handle, function, NvseSerializationEvent::pre_load); }); }
// Each unavailable method has its actual public ABI. None echoes a FormID,
// invents an empty co-save, zero-fills an output or reports an I/O success.
bool __cdecl nvse_write_record(std::uint32_t, std::uint32_t, const void*, std::uint32_t) { nvse_unowned("Serialization.WriteRecord: authoritative co-save transaction absent"); }
bool __cdecl nvse_open_record(std::uint32_t, std::uint32_t) { nvse_unowned("Serialization.OpenRecord: authoritative co-save transaction absent"); }
bool __cdecl nvse_write_data(const void*, std::uint32_t) { nvse_unowned("Serialization.WriteRecordData: authoritative co-save transaction absent"); }
bool __cdecl nvse_next_record(std::uint32_t*, std::uint32_t*, std::uint32_t*) { nvse_unowned("Serialization.GetNextRecordInfo: authoritative co-save transaction absent"); }
std::uint32_t __cdecl nvse_read_data(void*, std::uint32_t) { nvse_unowned("Serialization.ReadRecordData: authoritative co-save transaction absent"); }
bool __cdecl nvse_resolve_form(std::uint32_t, std::uint32_t*) { nvse_unowned("Serialization.ResolveRefID: exact source/save FormID join absent"); }
const char* __cdecl nvse_save_path() { nvse_unowned("Serialization.GetSavePath: authoritative save path absent"); }
std::uint32_t __cdecl nvse_peek(void*, std::uint32_t) { nvse_unowned("Serialization.PeekRecordData: authoritative co-save transaction absent"); }
void __cdecl nvse_write8(std::uint8_t) { nvse_unowned("Serialization.WriteRecord8: authoritative co-save transaction absent"); }
void __cdecl nvse_write16(std::uint16_t) { nvse_unowned("Serialization.WriteRecord16: authoritative co-save transaction absent"); }
void __cdecl nvse_write32(std::uint32_t) { nvse_unowned("Serialization.WriteRecord32: authoritative co-save transaction absent"); }
void __cdecl nvse_write64(const void*) { nvse_unowned("Serialization.WriteRecord64: authoritative co-save transaction absent"); }
std::uint8_t __cdecl nvse_read8() { nvse_unowned("Serialization.ReadRecord8: authoritative co-save transaction absent"); }
std::uint16_t __cdecl nvse_read16() { nvse_unowned("Serialization.ReadRecord16: authoritative co-save transaction absent"); }
std::uint32_t __cdecl nvse_read32() { nvse_unowned("Serialization.ReadRecord32: authoritative co-save transaction absent"); }
void __cdecl nvse_read64(void*) { nvse_unowned("Serialization.ReadRecord64: authoritative co-save transaction absent"); }
void __cdecl nvse_skip(std::uint32_t) { nvse_unowned("Serialization.SkipNBytes: authoritative co-save transaction absent"); }

void nvse_counters(Bytes& bytes) {
    const auto& owner = *state->nvse;
    bytes.put(owner.command_events); bytes.put(owner.listener_events); bytes.put(owner.serialization_events);
    bytes.put(static_cast<std::uint32_t>(owner.commands.size())); bytes.put(static_cast<std::uint32_t>(owner.listeners.size()));
}
void nvse_call_bytes(Bytes& bytes, const CallReceipt& receipt) {
    bytes.put(receipt.result); bytes.put(receipt.stack_delta); bytes.put(receipt.preserved_registers); bytes.put(receipt.exception_code);
}
void load_nvse(const Frame& frame) {
    Reader reader{frame.payload}; const auto path = path_from_utf8(reader.text());
    const auto directory = path_from_utf8(reader.text());
    const auto nvse_version = reader.get<std::uint32_t>(), runtime_version = reader.get<std::uint32_t>();
    const auto no_gore = reader.get<std::uint32_t>(), budget = reader.get<std::uint32_t>(); reader.finish();
    if (state->module || state->nvse_image_attempted) { reply(frame, ERROR_BUSY, "This generation already owns or attempted its one NVSE image lifetime."); return; }
    if (!nvse_version || !runtime_version || no_gore > 1 || !budget || budget > 1048576) {
        reply(frame, ERROR_INVALID_DATA, "NVSE source ABI fields or work budget are invalid."); return;
    }
    if (!state->io) { reply(frame, ERROR_ACCESS_DENIED, "Original loader entry has no pre-entry private I/O restriction."); return; }
    state->nvse_image_attempted = true;
    auto owner = std::make_unique<NvseRuntime>(); owner->interface_budget = budget;
    const auto code_page = GetACP();
    const auto flags = code_page == CP_UTF8 ? WC_ERR_INVALID_CHARS : WC_NO_BEST_FIT_CHARS;
    BOOL used_default = FALSE; auto* default_pointer = code_page == CP_UTF8 ? nullptr : &used_default;
    const auto count = WideCharToMultiByte(code_page, flags, directory.data(), static_cast<int>(directory.size()), nullptr, 0, nullptr, default_pointer);
    if (count <= 0 || used_default) { reply(frame, ERROR_NO_UNICODE_TRANSLATION, "Owned runtime directory has no exact native ANSI representation."); return; }
    std::string native_directory(static_cast<std::size_t>(count), '\0');
    used_default = FALSE;
    if (!WideCharToMultiByte(code_page, flags, directory.data(), static_cast<int>(directory.size()), native_directory.data(), count, nullptr, default_pointer) || used_default)
        throw Fatal(ERROR_NO_UNICODE_TRANSLATION, "Owned runtime directory changed its ANSI representation.");
    owner->tables_extent = static_cast<std::uint32_t>(sizeof(NvseTables) + native_directory.size() + 1);
    owner->tables = static_cast<NvseTables*>(VirtualAlloc(nullptr, owner->tables_extent, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    if (!owner->tables) { reply(frame, GetLastError(), "NVSE interface pages could not be allocated."); return; }
    owner->directory = reinterpret_cast<char*>(owner->tables + 1);
    std::memcpy(const_cast<char*>(owner->directory), native_directory.c_str(), native_directory.size() + 1);
    owner->tables->core = {nvse_version, runtime_version, 0, 0, nvse_register_plain, nvse_set_opcode,
        nvse_query_interface, nvse_plugin_handle, nvse_register_typed, nvse_runtime_directory, no_gore,
        nvse_expression_utils, nvse_register_versioned};
    owner->tables->messaging = {4, nvse_listen, nvse_dispatch};
    owner->tables->serialization = {2, nvse_save, nvse_load_save, nvse_new, nvse_write_record, nvse_open_record,
        nvse_write_data, nvse_next_record, nvse_read_data, nvse_resolve_form, nvse_preload, nvse_save_path,
        nvse_peek, nvse_write8, nvse_write16, nvse_write32, nvse_write64, nvse_read8, nvse_read16,
        nvse_read32, nvse_read64, nvse_skip};
    DWORD previous = 0;
    if (!VirtualProtect(owner->tables, owner->tables_extent, PAGE_READONLY, &previous))
        throw Fatal(GetLastError(), "NVSE interface protection failed.");
    callback_fault.store(0); state->callback_reason[0] = '\0'; state->callback_reason_truncated = false;
    const auto loaded = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!loaded) { reply(frame, GetLastError(), "Original NVSE DLL imports/entry admission failed."); return; }
    state->module = loaded;
    io_bind_imports(loaded);
    // Loader entry has actually run. No authored diagnostic exports, TLS
    // counters, editor flag or successful Query receipt is substituted here.
    if (callback_fault.load()) throw Fatal(callback_fault.load(), "Original DLL entry attempted an unowned callback.");
    const auto query = function_pointer<void*>(GetProcAddress(loaded, "NVSEPlugin_Query"));
    const auto load_function = function_pointer<void*>(GetProcAddress(loaded, "NVSEPlugin_Load"));
    if (!query || !load_function) {
        if (!FreeLibrary(loaded)) throw Fatal(GetLastError(), "Rejected NVSE image did not retire.");
        state->module = nullptr; reply(frame, ERROR_PROC_NOT_FOUND, "Original NVSE Query/Load exports are absent."); return;
    }
    nvse_executable(query); nvse_executable(load_function);
    state->module_id = ++state->next_module;
    if (state->module_id >= nvse_invalid_handle) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "NVSE plugin handle identity exhausted.");
    owner->handle = static_cast<std::uint32_t>(state->module_id); owner->query = address(query); owner->load = address(load_function);
    state->nvse = std::move(owner);
    Bytes data; data.put(state->module_id); data.put(address(loaded)); data.put(state->nvse->handle);
    data.put(address(state->nvse->tables)); data.put(state->nvse->query); data.put(state->nvse->load);
    reply(frame, ERROR_SUCCESS, "", data);
}
void nvse_stage(const Frame& frame, bool load_stage) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->module || !state->nvse || module != state->module_id || !state->calls.empty()) {
        reply(frame, ERROR_INVALID_HANDLE, "NVSE stage lacks its empty current module/call owner."); return;
    }
    auto& owner = *state->nvse;
    if ((!load_stage && owner.phase != NvsePhase::mapped) || (load_stage && owner.phase != NvsePhase::queried_true)) {
        reply(frame, ERROR_INVALID_STATE, "NVSE Query/Load source phase is not eligible."); return;
    }
    state->transaction_callbacks = 0; owner.interface_calls = 0;
    state->calls.push_back(frame.header.id); owner.phase = load_stage ? NvsePhase::loading : NvsePhase::querying;
    CallReceipt receipt{};
    { NvseInvocation invocation(owner);
      if (load_stage) InvokeNvseOneGuarded(reinterpret_cast<void*>(owner.load), address(&owner.tables->core), &receipt);
      else InvokeGuarded(reinterpret_cast<void*>(owner.query), Abi::cdecl_call, nullptr, address(&owner.tables->core), address(&owner.info), &receipt); }
    state->calls.pop_back(); nvse_check_call(receipt);
    // MSVC's bool return lives in AL; the upper bits of EAX are not specified.
    const auto returned = receipt.result & 0xffU;
    if (returned > 1) throw Fatal(ERROR_INVALID_DATA, "Original NVSE bool return is invalid.");
    owner.phase = load_stage ? (returned ? NvsePhase::loaded_true : NvsePhase::loaded_false) :
        (returned ? NvsePhase::queried_true : NvsePhase::queried_false);
    Bytes data; nvse_call_bytes(data, receipt); data.put(returned);
    data.put(owner.info.info_version); data.put(owner.info.version); nvse_text(data, owner.info.name, true);
    owner.name = nvse_string(owner.info.name, true); nvse_counters(data);
    reply(frame, ERROR_SUCCESS, "", data);
}
void nvse_message(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>();
    const auto type = reader.get<std::uint32_t>(), length = reader.get<std::uint32_t>();
    const auto* payload = reader.bytes(length); reader.finish();
    if (!state->nvse || module != state->module_id || !state->calls.empty() || state->nvse->phase != NvsePhase::loaded_true) {
        reply(frame, ERROR_INVALID_STATE, "NVSE message lacks an initialized empty module/call owner."); return;
    }
    state->calls.push_back(frame.header.id); state->nvse->interface_calls = 0; state->transaction_callbacks = 0;
    // The actual native pointer is valid for this synchronous callback only.
    const auto delivered = nvse_deliver(0, type, const_cast<void*>(payload), length, nullptr);
    state->calls.pop_back(); Bytes data; data.put(delivered); nvse_counters(data); reply(frame, ERROR_SUCCESS, "", data);
}
void nvse_serialization_event(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); const auto event = reader.get<std::uint32_t>(); reader.finish();
    // Only NewGame has no save/record transaction. Save/preload/load remain
    // refused until their actual authoritative co-save owner is connected.
    if (event != static_cast<std::uint32_t>(NvseSerializationEvent::new_game)) {
        reply(frame, ERROR_NOT_SUPPORTED, "NVSE save/preload/load has no authoritative co-save transaction."); return;
    }
    if (!state->nvse || module != state->module_id || !state->calls.empty() || state->nvse->phase != NvsePhase::loaded_true) {
        reply(frame, ERROR_INVALID_STATE, "NVSE NewGame callback lacks its initialized module owner."); return;
    }
    const auto callback_pointer = state->nvse->serialization[event];
    if (!callback_pointer) { reply(frame, ERROR_PROC_NOT_FOUND, "No original NewGame callback is retained."); return; }
    nvse_executable(reinterpret_cast<void*>(callback_pointer));
    state->calls.push_back(frame.header.id); state->nvse->interface_calls = 0; state->transaction_callbacks = 0;
    Bytes begin; begin.put(event); begin.put(callback_pointer);
    if (nvse_host(NvseHostCall::begin_serialization, begin) != 1)
        throw Fatal(ERROR_INVALID_DATA, "Original NewGame callback lacks its C# invocation owner.");
    CallReceipt receipt{};
    { NvseInvocation invocation(*state->nvse);
      InvokeNvseOneGuarded(reinterpret_cast<void*>(callback_pointer), 0, &receipt); }
    nvse_check_call(receipt);
    Bytes completion; completion.put(event); completion.put(callback_pointer);
    if (nvse_host(NvseHostCall::delivered_serialization, completion) != 1)
        throw Fatal(ERROR_INVALID_DATA, "Original NewGame callback lacks its C# completion owner.");
    state->calls.pop_back(); Bytes data; nvse_call_bytes(data, receipt); nvse_counters(data); reply(frame, ERROR_SUCCESS, "", data);
}
void unload_nvse(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || !state->module || module != state->module_id || !state->calls.empty()) {
        reply(frame, ERROR_INVALID_HANDLE, "NVSE retirement lacks its empty current module owner."); return;
    }
    const auto image = state->module; const auto tables = state->nvse->tables;
    state->nvse->interface_calls = 0; state->transaction_callbacks = 0;
    if (state->value_heap) state->value_heap->loader_retiring = true;
    if (state->io) state->io->loader_retiring = true;
    if (!FreeLibrary(image)) throw Fatal(GetLastError(), "Original NVSE module FreeLibrary failed.");
    values_heap_flush_detach(frame);
    io_flush_detach(frame);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), "Original NVSE detach attempted an unowned callback.");
    MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(image, &memory, sizeof(memory)) || memory.State != MEM_FREE)
        throw Fatal(ERROR_INVALID_DATA, "Original NVSE image mapping remains after retirement.");
    // Unlike authored modules, an unchanged DLL does not provide our private
    // TLS/DllMain counters. Do not fabricate detach cardinality evidence.
    if (!VirtualFree(tables, 0, MEM_RELEASE)) throw Fatal(GetLastError(), "NVSE interface pages did not retire.");
    state->nvse->tables = nullptr;
    if (!VirtualQuery(tables, &memory, sizeof(memory)) || memory.State != MEM_FREE)
        throw Fatal(ERROR_INVALID_DATA, "NVSE interface mapping remains after retirement.");
    expression_retire(); values_heap_retire(); values_retire(); source_locals_retire_all(); source_files_retire(); source_objects_retire_all(); io_retire();
    Bytes data; nvse_counters(data); data.put(0U); data.put(0U);
    state->nvse.reset(); state->module = nullptr; state->module_id = 0;
    reply(frame, ERROR_SUCCESS, "", data);
}
