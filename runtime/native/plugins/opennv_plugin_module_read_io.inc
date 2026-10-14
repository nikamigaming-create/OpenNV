std::uint64_t module_read_io_ordinal = 0;
bool module_read_io_entered = false;

struct ModuleReadIoCall {
    std::uint32_t operation, handle, output, capacity, incoming;
    std::uint64_t ordinal, route;
    std::vector<std::uint32_t> arguments;
    std::string path;
    std::vector<unsigned char> before;
};
void module_read_io_buffer(Bytes& bytes, const std::vector<unsigned char>& value) {
    bytes.put(static_cast<std::uint32_t>(value.size()));
    bytes.data.insert(bytes.data.end(), value.begin(), value.end());
}
Bytes module_read_io_callback_call(const ModuleReadIoCall& call, std::uint32_t stage, DWORD result, DWORD last) {
    if (!state->io || !state->nvse || !state->module || !state->module_id || state->calls.empty() || state->io->loader_retiring)
        throw Fatal(ERROR_BUSY, "Module read I/O has no current returned loader/source call/shared file owner.");
    Bytes bytes; bytes.put(state->module_id); bytes.put(call.operation); bytes.put(stage); bytes.put(call.ordinal);
    bytes.put(call.route); bytes.put(call.handle); bytes.put(result); bytes.put(last);
    bytes.put(call.output); bytes.put(call.capacity); bytes.put(call.incoming);
    bytes.put(static_cast<std::uint32_t>(call.arguments.size()));
    for (auto word : call.arguments) bytes.put(word);
    bytes.text(call.path); module_read_io_buffer(bytes, call.before);
    std::vector<unsigned char> after;
    if (stage == 2 && call.capacity) {
        const auto* source = reinterpret_cast<const unsigned char*>(call.output);
        nvse_readable(source, call.capacity); after.assign(source, source + call.capacity);
    }
    module_read_io_buffer(bytes, after);
    auto response = perform_callback_bytes(Kind::io_callback, module_read_io_callback, bytes);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason);
    ++state->io->callbacks; return response;
}
PluginIoRoute module_read_io_begin(ModuleReadIoCall& call) {
    if (module_read_io_entered || module_read_io_ordinal == UINT64_MAX)
        throw Fatal(ERROR_BUSY, "Module read I/O repeats an actual entered source caller.");
    module_read_io_entered = true; call.ordinal = ++module_read_io_ordinal;
    if (call.capacity) {
        if (call.capacity > module_read_io_buffer_limit) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Module read caller buffer exceeds its correlated payload.");
        const auto* source = reinterpret_cast<const unsigned char*>(call.output);
        expression_writable(reinterpret_cast<void*>(call.output), call.capacity);
        call.before.assign(source, source + call.capacity);
    }
    auto response = module_read_io_callback_call(call, 1, 0, 0); Reader reader{response.data};
    const auto route = reader.get<std::uint64_t>(); const auto error = reader.get<std::uint32_t>();
    const auto path = io_from_utf8(reader.text()); reader.finish();
    if ((call.route && call.route != route) || (error != ERROR_SUCCESS && error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND))
        throw Fatal(ERROR_INVALID_DATA, "Module read I/O changed its selected source route/absence.");
    call.route = route; return PluginIoRoute{route, error, path};
}
void module_read_io_returned(const ModuleReadIoCall& call, DWORD result, DWORD last) {
    auto response = module_read_io_callback_call(call, 2, result, last); Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Module read I/O lost its returned original caller receipt.");
    reader.finish(); module_read_io_entered = false;
}

HANDLE WINAPI io_module_read_create_w(LPCWSTR path, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES security,
    DWORD disposition, DWORD flags, HANDLE template_file) {
    const auto incoming = GetLastError(); DWORD sdk_last = incoming;
    const auto result = nvse_adapter([&]() -> HANDLE {
        ModuleReadIoCall call{1, 0, 0, 0, incoming, 0, 0,
            {access, share, address(security), disposition, flags, address(template_file)}, io_utf8(io_wide(path)), {}};
        const auto route = module_read_io_begin(call);
        if (!route.id || route.path.empty()) throw Fatal(ERROR_INVALID_DATA, "Module read opening lost its actual selected path.");
        const auto physical = io_native_path(route.path);
        SetLastError(incoming);
        // Preserve all six requested words. Admission rejected unsupported
        // writes/security/creation; this call does not silently reduce access.
        const auto file = CreateFileW(physical.c_str(), access, share, security, disposition, flags, template_file);
        sdk_last = GetLastError();
        if (file != INVALID_HANDLE_VALUE && !state->io->files.emplace(address(file), PluginIoFile{route.id, file, false}).second)
            throw Fatal(ERROR_INVALID_HANDLE, "Module read opening repeats a real live shared HANDLE.");
        module_read_io_returned(call, address(file), sdk_last); return file;
    });
    SetLastError(sdk_last); return result;
}
BOOL WINAPI io_module_read_close(HANDLE handle) {
    const auto incoming = GetLastError(); DWORD sdk_last = incoming;
    const auto result = nvse_adapter([&]() -> BOOL {
        std::uint64_t route = 0;
        if (state->io) {
            const auto found = state->io->files.find(address(handle));
            if (found != state->io->files.end() && found->second.handle == handle) route = found->second.capability;
        }
        ModuleReadIoCall call{2, address(handle), 0, 0, incoming, 0, route, {}, "", {}};
        (void)module_read_io_begin(call); SetLastError(incoming);
        const auto closed = CloseHandle(handle); sdk_last = GetLastError();
        module_read_io_returned(call, static_cast<DWORD>(closed), sdk_last);
        if (closed) state->io->files.erase(address(handle)); return closed;
    });
    SetLastError(sdk_last); return result;
}
DWORD WINAPI io_module_read_profile_a(LPCSTR section, LPCSTR key, LPCSTR fallback, LPSTR buffer, DWORD capacity, LPCSTR path) {
    const auto incoming = GetLastError(); DWORD sdk_last = incoming;
    const auto result = nvse_adapter([&]() -> DWORD {
        if (section) (void)nvse_string(section, false);
        if (key) (void)nvse_string(key, false);
        if (fallback) (void)nvse_string(fallback, false);
        ModuleReadIoCall call{3, 0, address(buffer), capacity, incoming, 0, 0,
            {address(section), address(key), address(fallback)}, io_utf8(io_ansi(path)), {}};
        const auto route = module_read_io_begin(call);
        if (!route.id || route.path.empty()) throw Fatal(ERROR_INVALID_DATA, "Module profile read lost its real source configuration path.");
        const auto physical = io_native_ansi(io_profile_path(route));
        SetLastError(incoming);
        const auto copied = GetPrivateProfileStringA(section, key, fallback, buffer, capacity, physical.c_str());
        sdk_last = GetLastError(); module_read_io_returned(call, copied, sdk_last); return copied;
    });
    SetLastError(sdk_last); return result;
}
DWORD WINAPI io_module_game_name_a(HMODULE module, LPSTR buffer, DWORD capacity) {
    const auto incoming = GetLastError(); DWORD source_last = incoming;
    const auto result = nvse_adapter([&]() -> DWORD {
        ModuleReadIoCall call{4, 0, address(buffer), capacity, incoming, 0, 0, {address(module)}, "", {}};
        const auto selected = module_read_io_begin(call);
        if (selected.id || selected.error || selected.path.empty())
            throw Fatal(ERROR_INVALID_DATA, "Module zero filename lost its actual selected game source identity.");
        // The companion is not the game's executable module. This explicit
        // first-party process-module query uses the retained selected game path;
        // the receipt distinguishes it from an SDK invocation.
        const auto name = io_native_ansi(selected.path);
        const auto length = static_cast<DWORD>(name.size() - 1);
        DWORD copied = length;
        if (length < capacity) expression_write(buffer, name.data(), length + 1);
        else {
            expression_write(buffer, name.data(), capacity - 1);
            const char end = 0; expression_write(buffer + capacity - 1, &end, 1);
            copied = capacity; source_last = ERROR_INSUFFICIENT_BUFFER;
        }
        module_read_io_returned(call, copied, source_last); return copied;
    });
    SetLastError(source_last); return result;
}
void* module_read_io_import_entry(const std::string& library, const std::string& name, void* actual) {
    const auto windows = library == "kernel32.dll" || library == "kernelbase.dll" || library.rfind("api-ms-win-core-", 0) == 0;
    if (!windows) return nullptr;
    void* adapter = nullptr;
    if (name == "CreateFileW") adapter = reinterpret_cast<void*>(io_module_read_create_w);
    else if (name == "CloseHandle") adapter = reinterpret_cast<void*>(io_module_read_close);
    else if (name == "GetPrivateProfileStringA") adapter = reinterpret_cast<void*>(io_module_read_profile_a);
    else if (name == "GetModuleFileNameA") adapter = reinterpret_cast<void*>(io_module_game_name_a);
    if (!adapter) return nullptr;
    const auto provider = GetModuleHandleW(io_from_utf8(library).c_str());
    if (!actual || !provider || actual != reinterpret_cast<void*>(GetProcAddress(provider, name.c_str())))
        throw Fatal(ERROR_INVALID_DATA, "Module read I/O is not its actual original named Windows IAT export.");
    return adapter;
}
void module_read_io_require_retired() {
    if (module_read_io_entered) throw Fatal(ERROR_BUSY, "Module read I/O retains an entered original caller/output/resource borrow.");
}
