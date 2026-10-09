void* cng_client_import_entry(const std::string&, const std::string&, void*);
void* cng_client_dynamic_entry(HMODULE, const std::string&);
void cng_client_flush_detach();
void cng_client_require_retired();
constexpr std::uint32_t io_route = 1, io_complete = 2, io_handle_event = 3, io_current_directory = 4;
constexpr std::uint32_t io_open_api = 1, io_close_api = 2, io_read_api = 3, io_write_api = 4,
    io_directory_api = 5, io_delete_api = 6, io_attributes_api = 7, io_profile_api = 8, io_profile_write_api = 9,
    io_seek_api = 10, io_size_api = 11, io_flush_api = 12;
struct PluginIoRoute { std::uint64_t id; std::uint32_t error; std::wstring path; };
void io_profile_result(const PluginIoRoute&, std::uint32_t, std::uint32_t, std::uint32_t, DWORD, bool);
PluginIoRuntime& io_owner() {
    if (!state || !state->io || GetCurrentThreadId() != state->thread)
        throw Fatal(ERROR_INVALID_THREAD_ID, "Native file API has no selected module/generation/thread owner.");
    return *state->io;
}
std::wstring io_from_utf8(const std::string& bytes) {
    if (bytes.empty()) return {};
    const auto count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
    if (!count) throw Fatal(GetLastError(), "Private native path has invalid UTF-8.");
    std::wstring text(static_cast<std::size_t>(count), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), text.data(), count) != count)
        throw Fatal(GetLastError(), "Private native path conversion drifted.");
    return text;
}
std::string io_native_ansi(const std::wstring& text) {
    const auto code_page = GetACP();
    const auto flags = code_page == CP_UTF8 ? WC_ERR_INVALID_CHARS : WC_NO_BEST_FIT_CHARS;
    BOOL lost = FALSE; auto* default_pointer = code_page == CP_UTF8 ? nullptr : &lost;
    const auto count = WideCharToMultiByte(code_page, flags, text.c_str(), -1, nullptr, 0, nullptr, default_pointer);
    if (!count || lost) throw Fatal(ERROR_NO_UNICODE_TRANSLATION, "Private native path has no exact ANSI representation.");
    std::string bytes(static_cast<std::size_t>(count), '\0'); lost = FALSE;
    if (WideCharToMultiByte(code_page, flags, text.c_str(), -1, bytes.data(), count, nullptr, default_pointer) != count || lost)
        throw Fatal(ERROR_NO_UNICODE_TRANSLATION, "Private native path changed its ANSI identity.");
    return bytes;
}
std::wstring io_native_path(const std::wstring& path) {
    if (path.empty() || path.rfind(L"\\\\?\\", 0) == 0) return path;
    if (path.rfind(L"\\\\", 0) == 0) return L"\\\\?\\UNC\\" + path.substr(2);
    if (path.size() < 3 || path[1] != L':' || path[2] != L'\\')
        throw Fatal(ERROR_INVALID_NAME, "Selected native filesystem path has no absolute identity.");
    return L"\\\\?\\" + path;
}
std::wstring io_wide(const wchar_t* pointer, bool optional = false) {
    if (!pointer) { if (optional) return {}; throw Fatal(ERROR_INVALID_ADDRESS, "Required native file path is null."); }
    std::wstring text;
    for (std::uint32_t at = 0; at < max_payload / 4; ++at) { const auto value = nvse_copy(pointer + at); if (!value) return text; text.push_back(value); }
    throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native file string exceeds its correlated request budget.");
}
std::wstring io_ansi(const char* pointer) {
    const auto bytes = nvse_string(pointer, false);
    if (bytes.empty()) return {};
    const auto count = MultiByteToWideChar(CP_ACP, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
    if (!count) throw Fatal(GetLastError(), "Native ANSI file path has no active-codepage interpretation.");
    std::wstring value(static_cast<std::size_t>(count), L'\0');
    if (MultiByteToWideChar(CP_ACP, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), value.data(), count) != count)
        throw Fatal(GetLastError(), "Native ANSI file path conversion drifted.");
    return value;
}
std::string io_utf8(const std::wstring& text) {
    if (text.empty()) return {};
    const auto count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    if (!count) throw Fatal(GetLastError(), "Native file path has invalid UTF-16.");
    std::string bytes(static_cast<std::size_t>(count), '\0');
    if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), bytes.data(), count, nullptr, nullptr) != count)
        throw Fatal(GetLastError(), "Native file path UTF-8 conversion drifted.");
    return bytes;
}
Bytes io_host(std::uint32_t operation, const Bytes& payload) {
    auto& owner = io_owner();
    if (owner.loader_retiring) throw Fatal(ERROR_NOT_SUPPORTED, "A new file route cannot call C# under the original DLL loader lock.");
    Bytes data; data.put(state->module_id); data.data.insert(data.data.end(), payload.data.begin(), payload.data.end());
    auto result = perform_callback_bytes(Kind::io_callback, operation, data);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason[0] ? state->callback_reason : "Native private I/O callback failed.");
    ++owner.callbacks; return result;
}
PluginIoRoute io_resolve(std::uint32_t api, std::uint32_t action, const std::wstring& path) {
    Bytes request; request.put(api); request.put(action); request.text(io_utf8(path));
    auto result = io_host(io_route, request); Reader reader{result.data};
    PluginIoRoute route{reader.get<std::uint64_t>(), reader.get<std::uint32_t>(), io_native_path(io_from_utf8(reader.text()))}; reader.finish();
    if (!route.id || (route.error && route.error != ERROR_FILE_NOT_FOUND && route.error != ERROR_PATH_NOT_FOUND)) throw Fatal(ERROR_INVALID_DATA, "Native file route lacks an exact C# decision.");
    return route;
}
void io_completed(const PluginIoRoute& route, std::uint32_t api, DWORD error, HANDLE file = nullptr, bool writable = false, bool deleted = false) {
    Bytes request; request.put(route.id); request.put(api); request.put(error); request.put(address(file)); request.put(writable ? 1U : 0U); request.put(deleted ? 1U : 0U);
    auto result = io_host(io_complete, request); Reader reader{result.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Native file completion has no C# receipt."); reader.finish();
}
PluginIoFile& io_file(HANDLE file) {
    auto& owner = io_owner(); const auto found = owner.files.find(address(file));
    if (found == owner.files.end() || found->second.handle != file) throw Fatal(ERROR_INVALID_HANDLE, "Native file handle has no selected-module lifetime.");
    return found->second;
}
void io_event(const PluginIoFile& file, std::uint32_t api, DWORD count, DWORD error) {
    const PluginIoEvent event{file.capability, api, address(file.handle), count, error};
    Bytes request; request.put(event.capability); request.put(api); request.put(event.handle); request.put(count); request.put(error);
    crt_owned_callback(io_handle_event, request);
}
HANDLE io_open(const std::wstring& path, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES security, DWORD disposition, DWORD flags, HANDLE template_file) {
    if (security || template_file || access & MAXIMUM_ALLOWED || flags & (FILE_FLAG_OVERLAPPED | FILE_FLAG_DELETE_ON_CLOSE) ||
        disposition < CREATE_NEW || disposition > TRUNCATE_EXISTING)
        throw Fatal(ERROR_NOT_SUPPORTED, "Native file security/template/async/delete-on-close/access mode is unowned.");
    const auto writable = (access & (GENERIC_WRITE | GENERIC_ALL | 0x000d0156)) != 0 || disposition != OPEN_EXISTING;
    const auto route = io_resolve(io_open_api, writable ? 2U : 1U, path);
    if (route.error) { io_completed(route, io_open_api, route.error); SetLastError(route.error); return INVALID_HANDLE_VALUE; }
    const auto file = CreateFileW(route.path.c_str(), access, share, nullptr, disposition, flags, nullptr); const auto last = GetLastError();
    if (file == INVALID_HANDLE_VALUE) { io_completed(route, io_open_api, last); SetLastError(last); return file; }
    try {
        if (!io_owner().files.emplace(address(file), PluginIoFile{route.id, file, writable}).second) throw Fatal(ERROR_INVALID_HANDLE, "Native file handle repeats a live capability.");
        io_completed(route, io_open_api, ERROR_SUCCESS, file, writable);
    }
    catch (...) { CloseHandle(file); io_owner().files.erase(address(file)); throw; }
    SetLastError(last); return file;
}
HANDLE WINAPI io_create_w(LPCWSTR path, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES security, DWORD disposition, DWORD flags, HANDLE template_file) {
    return nvse_adapter([&] { return io_open(io_wide(path), access, share, security, disposition, flags, template_file); });
}
HANDLE WINAPI io_create_a(LPCSTR path, DWORD access, DWORD share, LPSECURITY_ATTRIBUTES security, DWORD disposition, DWORD flags, HANDLE template_file) {
    return nvse_adapter([&] { return io_open(io_ansi(path), access, share, security, disposition, flags, template_file); });
}
BOOL WINAPI io_close(HANDLE handle) {
    const auto incoming = GetLastError();
    return nvse_adapter([&]() -> BOOL {
        SetLastError(incoming); BOOL mutex_result = FALSE;
        if (mutex_close_handle(handle, mutex_result)) return mutex_result;
        auto& owner = io_owner();
        BOOL mapping_result = FALSE;
        if (mapping_close_handle(handle, mapping_result)) return mapping_result;
        if (handle == owner.stdin_file || handle == owner.stdout_file || handle == owner.stderr_file) {
            const auto result = CloseHandle(handle); const auto last = GetLastError();
            if (result) { if (handle == owner.stdout_file) owner.stdout_file = INVALID_HANDLE_VALUE;
                else if (handle == owner.stderr_file) owner.stderr_file = INVALID_HANDLE_VALUE; else owner.stdin_file = INVALID_HANDLE_VALUE; }
            SetLastError(last); return result;
        }
        const auto file = io_file(handle); const auto result = CloseHandle(handle); const auto last = GetLastError();
        io_event(file, io_close_api, 0, result ? ERROR_SUCCESS : last); if (result) owner.files.erase(address(handle)); SetLastError(last); return result;
    });
}
BOOL WINAPI io_read(HANDLE handle, LPVOID buffer, DWORD length, LPDWORD read, LPOVERLAPPED overlap) {
    return nvse_adapter([&]() -> BOOL {
        if (overlap || !read) throw Fatal(ERROR_NOT_SUPPORTED, "Native asynchronous read/count ownership is unbound.");
        const auto file = io_file(handle); if (length) expression_writable(buffer, length); expression_writable(read, sizeof(DWORD));
        const auto result = ReadFile(handle, buffer, length, read, nullptr); const auto last = GetLastError();
        io_event(file, io_read_api, result ? nvse_copy(read) : 0U, result ? ERROR_SUCCESS : last); SetLastError(last); return result;
    });
}
BOOL WINAPI io_write(HANDLE handle, LPCVOID buffer, DWORD length, LPDWORD written, LPOVERLAPPED overlap) {
    return nvse_adapter([&]() -> BOOL {
        if (overlap || !written) throw Fatal(ERROR_NOT_SUPPORTED, "Native asynchronous write/count ownership is unbound.");
        auto& owner = io_owner();
        if (handle == owner.stdout_file || handle == owner.stderr_file) return WriteFile(handle, buffer, length, written, nullptr);
        const auto file = io_file(handle); if (!file.writable) throw Fatal(ERROR_ACCESS_DENIED, "Native write targets an original read-only capability.");
        nvse_readable(buffer, length); expression_writable(written, sizeof(DWORD));
        const auto result = WriteFile(handle, buffer, length, written, nullptr); const auto last = GetLastError();
        io_event(file, io_write_api, result ? nvse_copy(written) : 0U, result ? ERROR_SUCCESS : last); SetLastError(last); return result;
    });
}
DWORD WINAPI io_seek(HANDLE handle, LONG low, PLONG high, DWORD method) {
    return nvse_adapter([&] {
        const auto file = io_file(handle); if (high) expression_writable(high, sizeof(LONG)); SetLastError(0);
        const auto result = SetFilePointer(handle, low, high, method); const auto last = GetLastError();
        io_event(file, io_seek_api, 0, result == INVALID_SET_FILE_POINTER ? last : 0U); SetLastError(last); return result;
    });
}
BOOL WINAPI io_seek_ex(HANDLE handle, LARGE_INTEGER distance, PLARGE_INTEGER position, DWORD method) {
    return nvse_adapter([&]() -> BOOL { const auto file = io_file(handle); if (position) expression_writable(position, sizeof(LARGE_INTEGER));
        const auto result = SetFilePointerEx(handle, distance, position, method); const auto last = GetLastError(); io_event(file, io_seek_api, 0, result ? 0U : last); SetLastError(last); return result; });
}
DWORD WINAPI io_size(HANDLE handle, LPDWORD high) {
    return nvse_adapter([&] { const auto file = io_file(handle); if (high) expression_writable(high, sizeof(DWORD)); SetLastError(0);
        const auto result = GetFileSize(handle, high); const auto last = GetLastError(); io_event(file, io_size_api, 0, result == INVALID_FILE_SIZE ? last : 0U); SetLastError(last); return result; });
}
BOOL WINAPI io_size_ex(HANDLE handle, PLARGE_INTEGER size) {
    return nvse_adapter([&]() -> BOOL { const auto file = io_file(handle); expression_writable(size, sizeof(LARGE_INTEGER));
        const auto result = GetFileSizeEx(handle, size); const auto last = GetLastError(); io_event(file, io_size_api, 0, result ? 0U : last); SetLastError(last); return result; });
}
BOOL WINAPI io_flush(HANDLE handle) {
    return nvse_adapter([&]() -> BOOL { auto& owner = io_owner(); if (handle == owner.stdout_file || handle == owner.stderr_file) return FlushFileBuffers(handle);
        const auto file = io_file(handle); const auto result = FlushFileBuffers(handle); const auto last = GetLastError(); io_event(file, io_flush_api, 0, result ? 0U : last); SetLastError(last); return result; });
}
BOOL io_directory(const std::wstring& path, LPSECURITY_ATTRIBUTES security) {
    if (security) throw Fatal(ERROR_NOT_SUPPORTED, "Native directory security attributes have no selected policy owner.");
    const auto route = io_resolve(io_directory_api, 3, path);
    if (route.error) { io_completed(route, io_directory_api, route.error); SetLastError(route.error); return FALSE; }
    const auto result = CreateDirectoryW(route.path.c_str(), nullptr); const auto last = GetLastError();
    io_completed(route, io_directory_api, result ? 0U : last); SetLastError(last); return result;
}
BOOL WINAPI io_directory_w(LPCWSTR path, LPSECURITY_ATTRIBUTES security) { return nvse_adapter([&] { return io_directory(io_wide(path), security); }); }
BOOL WINAPI io_directory_a(LPCSTR path, LPSECURITY_ATTRIBUTES security) { return nvse_adapter([&] { return io_directory(io_ansi(path), security); }); }
BOOL io_delete(const std::wstring& path, bool directory) {
    const auto route = io_resolve(io_delete_api, 4, path);
    if (route.error) { io_completed(route, io_delete_api, route.error, nullptr, false, true); SetLastError(route.error); return FALSE; }
    const auto result = directory ? RemoveDirectoryW(route.path.c_str()) : DeleteFileW(route.path.c_str()); const auto last = GetLastError();
    io_completed(route, io_delete_api, result ? 0U : last, nullptr, false, true); SetLastError(last); return result;
}
BOOL WINAPI io_delete_w(LPCWSTR path) { return nvse_adapter([&] { return io_delete(io_wide(path), false); }); }
BOOL WINAPI io_delete_a(LPCSTR path) { return nvse_adapter([&] { return io_delete(io_ansi(path), false); }); }
BOOL WINAPI io_remove_w(LPCWSTR path) { return nvse_adapter([&] { return io_delete(io_wide(path), true); }); }
BOOL WINAPI io_remove_a(LPCSTR path) { return nvse_adapter([&] { return io_delete(io_ansi(path), true); }); }
DWORD io_attributes(const std::wstring& path) {
    const auto route = io_resolve(io_attributes_api, 5, path);
    const auto result = route.error ? INVALID_FILE_ATTRIBUTES : GetFileAttributesW(route.path.c_str()); const auto last = route.error ? route.error : GetLastError();
    io_completed(route, io_attributes_api, result == INVALID_FILE_ATTRIBUTES ? last : 0U); SetLastError(last); return result;
}
DWORD WINAPI io_attributes_w(LPCWSTR path) { return nvse_adapter([&] { return io_attributes(io_wide(path)); }); }
DWORD WINAPI io_attributes_a(LPCSTR path) { return nvse_adapter([&] { return io_attributes(io_ansi(path)); }); }
std::wstring io_profile_path(const PluginIoRoute& route) {
    if (route.path.empty()) throw Fatal(ERROR_INVALID_DATA, "Native profile request lacks its exact selected/private path.");
    if (route.error && GetFileAttributesW(route.path.c_str()) != INVALID_FILE_ATTRIBUTES)
        throw Fatal(ERROR_INVALID_DATA, "Native absent profile input unexpectedly exists.");
    return route.path;
}
DWORD WINAPI io_profile_string_w(LPCWSTR section, LPCWSTR key, LPCWSTR fallback, LPWSTR buffer, DWORD size, LPCWSTR path) {
    return nvse_adapter([&] { const auto route = io_resolve(io_profile_api, 6, io_wide(path));
        if (size > std::numeric_limits<std::uint32_t>::max() / sizeof(wchar_t)) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Native profile output has an overflowing UTF-16 extent.");
        if (size) expression_writable(buffer, size * sizeof(wchar_t));
        const auto result = GetPrivateProfileStringW(section, key, fallback, buffer, size, io_profile_path(route).c_str()); const auto last = GetLastError(); io_profile_result(route, 11, size, result, last, false); SetLastError(last); return result; });
}
DWORD WINAPI io_profile_string_a(LPCSTR section, LPCSTR key, LPCSTR fallback, LPSTR buffer, DWORD size, LPCSTR path) {
    return nvse_adapter([&] { const auto route = io_resolve(io_profile_api, 6, io_ansi(path)); if (size) expression_writable(buffer, size);
        const auto physical = io_native_ansi(io_profile_path(route));
        const auto result = GetPrivateProfileStringA(section, key, fallback, buffer, size, physical.c_str()); const auto last = GetLastError(); io_profile_result(route, 12, size, result, last, false); SetLastError(last); return result; });
}
UINT WINAPI io_profile_int_w(LPCWSTR section, LPCWSTR key, INT fallback, LPCWSTR path) {
    return nvse_adapter([&] { const auto route = io_resolve(io_profile_api, 6, io_wide(path)); const auto result = GetPrivateProfileIntW(section, key, fallback, io_profile_path(route).c_str()); const auto last = GetLastError(); io_profile_result(route, 13, 0, result, last, false); SetLastError(last); return result; });
}
BOOL WINAPI io_profile_write_w(LPCWSTR section, LPCWSTR key, LPCWSTR value, LPCWSTR path) {
    return nvse_adapter([&] { const auto route = io_resolve(io_profile_write_api, 7, io_wide(path));
        const auto result = WritePrivateProfileStringW(section, key, value, route.path.c_str()); const auto last = GetLastError(); io_profile_result(route, 15, 0, static_cast<std::uint32_t>(result), last, section || key || value); SetLastError(last); return result; });
}
BOOL WINAPI io_profile_write_a(LPCSTR section, LPCSTR key, LPCSTR value, LPCSTR path) {
    return nvse_adapter([&] { const auto route = io_resolve(io_profile_write_api, 7, io_ansi(path));
        const auto physical = io_native_ansi(route.path);
        const auto result = WritePrivateProfileStringA(section, key, value, physical.c_str()); const auto last = GetLastError();
        io_profile_result(route, 16, 0, static_cast<std::uint32_t>(result), last, section || key || value); SetLastError(last); return result; });
}
DWORD WINAPI io_current_w(DWORD size, LPWSTR destination) {
    return nvse_adapter([&] { const auto& directory = io_owner().logical_directory; const auto required = static_cast<DWORD>(directory.size() + 1);
        if (size < required) return required;
        expression_writable(destination, required * sizeof(wchar_t)); expression_write(destination, directory.c_str(), required * sizeof(wchar_t)); return required - 1; });
}
DWORD WINAPI io_current_a(DWORD size, LPSTR destination) {
    return nvse_adapter([&] { const auto& directory = io_owner().logical_directory;
        const auto bytes = io_native_ansi(directory); const auto count = static_cast<DWORD>(bytes.size());
        if (size < count) return count;
        expression_writable(destination, static_cast<DWORD>(count)); expression_write(destination, bytes.data(), static_cast<DWORD>(count)); return static_cast<DWORD>(count - 1); });
}
BOOL io_change_directory(const std::wstring& directory) {
    Bytes request; request.text(io_utf8(directory)); auto result = io_host(io_current_directory, request); Reader reader{result.data};
    io_owner().logical_directory = io_from_utf8(reader.text()); reader.finish(); return TRUE;
}
BOOL WINAPI io_change_w(LPCWSTR directory) { return nvse_adapter([&] { return io_change_directory(io_wide(directory)); }); }
BOOL WINAPI io_change_a(LPCSTR directory) { return nvse_adapter([&] { return io_change_directory(io_ansi(directory)); }); }
DWORD WINAPI io_type(HANDLE handle) {
    return nvse_adapter([&] { auto& owner = io_owner(); if (handle != owner.stdin_file && handle != owner.stdout_file && handle != owner.stderr_file) io_file(handle); return GetFileType(handle); });
}
BOOL WINAPI io_end(HANDLE handle) {
    return nvse_adapter([&]() -> BOOL { const auto file = io_file(handle); if (!file.writable) throw Fatal(ERROR_ACCESS_DENIED, "Native truncation targets an original read-only capability.");
        const auto result = SetEndOfFile(handle); const auto last = GetLastError(); io_event(file, io_write_api, 0, result ? 0U : last); SetLastError(last); return result; });
}
void* io_entry(const std::string&);
FARPROC WINAPI io_proc(HMODULE module, LPCSTR pointer) {
    return nvse_adapter([&] {
        if (reinterpret_cast<std::uintptr_t>(pointer) <= 0xffff) throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic ordinal/native import ownership is unbound.");
        const auto name = nvse_string(pointer, false); std::array<wchar_t, 32768> path{};
        const auto count = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
        if (!count || count >= path.size()) throw Fatal(GetLastError(), "Dynamic import module has no complete actual filename identity.");
        auto filename = io_utf8(std::wstring(path.data(), count)); const auto slash = filename.find_last_of("\\/"); if (slash != std::string::npos) filename.erase(0, slash + 1);
        for (auto& character : filename) if (character >= 'A' && character <= 'Z') character = static_cast<char>(character + ('a' - 'A'));
        if (filename == "kernel32.dll" || filename == "kernelbase.dll" || filename.rfind("api-ms-win-core-", 0) == 0)
            if (auto* replacement = io_entry(name)) return reinterpret_cast<FARPROC>(replacement);
        if (auto* replacement = crt_dynamic_entry(module, name)) return reinterpret_cast<FARPROC>(replacement);
        if (auto* replacement = cng_client_dynamic_entry(module, name)) return reinterpret_cast<FARPROC>(replacement);
        if (crt_io_import_name(filename, name)) throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic CRT file API has no actual selected provider/callable owner.");
        if (module != state->module && !io_owner().non_io_imports.count(filename + "!" + name))
            throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic API lookup has no selected native I/O/non-I/O owner.");
        return GetProcAddress(module, pointer);
    });
}
HMODULE WINAPI io_load_a(LPCSTR) { return nvse_adapter([]() -> HMODULE { throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic native dependency entry/I/O ownership is unbound."); }); }
HMODULE WINAPI io_load_w(LPCWSTR) { return nvse_adapter([]() -> HMODULE { throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic native dependency entry/I/O ownership is unbound."); }); }
HMODULE WINAPI io_load_ex_a(LPCSTR, HANDLE, DWORD) { return nvse_adapter([]() -> HMODULE { throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic native dependency entry/I/O ownership is unbound."); }); }
HMODULE WINAPI io_load_ex_w(LPCWSTR, HANDLE, DWORD) { return nvse_adapter([]() -> HMODULE { throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic native dependency entry/I/O ownership is unbound."); }); }
UINT WINAPI io_profile_int_a(LPCSTR section, LPCSTR key, INT fallback, LPCSTR path) {
    return nvse_adapter([&] { const auto route = io_resolve(io_profile_api, 6, io_ansi(path)); const auto physical = io_native_ansi(io_profile_path(route));
        const auto result = GetPrivateProfileIntA(section, key, fallback, physical.c_str()); const auto last = GetLastError();
        io_profile_result(route, 14, 0, result, last, false); SetLastError(last); return result; });
}
void* io_entry(const std::string& name) {
    if (auto* entry = mutex_entry(name)) return entry;
    struct Entry { const char* name; void* function; };
    const Entry entries[] = {
        {"CreateFileW", reinterpret_cast<void*>(io_create_w)}, {"CreateFileA", reinterpret_cast<void*>(io_create_a)},
        {"CloseHandle", reinterpret_cast<void*>(io_close)}, {"ReadFile", reinterpret_cast<void*>(io_read)}, {"WriteFile", reinterpret_cast<void*>(io_write)},
        {"SetFilePointer", reinterpret_cast<void*>(io_seek)}, {"SetFilePointerEx", reinterpret_cast<void*>(io_seek_ex)},
        {"GetFileSize", reinterpret_cast<void*>(io_size)}, {"GetFileSizeEx", reinterpret_cast<void*>(io_size_ex)}, {"FlushFileBuffers", reinterpret_cast<void*>(io_flush)},
        {"CreateDirectoryW", reinterpret_cast<void*>(io_directory_w)}, {"CreateDirectoryA", reinterpret_cast<void*>(io_directory_a)},
        {"DeleteFileW", reinterpret_cast<void*>(io_delete_w)}, {"DeleteFileA", reinterpret_cast<void*>(io_delete_a)},
        {"RemoveDirectoryW", reinterpret_cast<void*>(io_remove_w)}, {"RemoveDirectoryA", reinterpret_cast<void*>(io_remove_a)},
        {"GetFileAttributesW", reinterpret_cast<void*>(io_attributes_w)}, {"GetFileAttributesA", reinterpret_cast<void*>(io_attributes_a)},
        {"GetFileAttributesExW", reinterpret_cast<void*>(io_attributes_ex_w)}, {"GetFileAttributesExA", reinterpret_cast<void*>(io_attributes_ex_a)},
        {"GetFileInformationByHandleEx", reinterpret_cast<void*>(io_information_by_handle_ex)},
        {"GetEnvironmentVariableA", reinterpret_cast<void*>(io_get_environment_a)},
        {"GetPrivateProfileStringW", reinterpret_cast<void*>(io_profile_string_w)}, {"GetPrivateProfileStringA", reinterpret_cast<void*>(io_profile_string_a)},
        {"GetPrivateProfileIntW", reinterpret_cast<void*>(io_profile_int_w)}, {"GetPrivateProfileIntA", reinterpret_cast<void*>(io_profile_int_a)},
        {"WritePrivateProfileStringW", reinterpret_cast<void*>(io_profile_write_w)}, {"WritePrivateProfileStringA", reinterpret_cast<void*>(io_profile_write_a)},
        {"GetCurrentDirectoryW", reinterpret_cast<void*>(io_current_w)}, {"GetCurrentDirectoryA", reinterpret_cast<void*>(io_current_a)},
        {"SetCurrentDirectoryW", reinterpret_cast<void*>(io_change_w)}, {"SetCurrentDirectoryA", reinterpret_cast<void*>(io_change_a)},
        {"GetFileType", reinterpret_cast<void*>(io_type)}, {"SetEndOfFile", reinterpret_cast<void*>(io_end)},
        {"GetProcAddress", reinterpret_cast<void*>(io_proc)}, {"LoadLibraryA", reinterpret_cast<void*>(io_load_a)}, {"LoadLibraryW", reinterpret_cast<void*>(io_load_w)},
        {"LoadLibraryExA", reinterpret_cast<void*>(io_load_ex_a)}, {"LoadLibraryExW", reinterpret_cast<void*>(io_load_ex_w)} };
    for (const auto& entry : entries) if (name == entry.name) return entry.function;
    if (auto* entry = mapping_entry(name)) return entry;
    if (auto* entry = io_profile_extended_entry(name)) return entry;
    return io_find_entry(name);
}
void io_prepare(const Frame& frame) {
    Reader reader{frame.payload}; const auto root = io_native_path(io_from_utf8(reader.text())), directory = io_from_utf8(reader.text()), sid_text = io_from_utf8(reader.text()); const auto count = reader.get<std::uint32_t>();
    if (state->io || state->module || !state->calls.empty() || count > 4096) { reply(frame, ERROR_INVALID_STATE, "Private I/O must prepare once before native loader admission."); return; }
    auto owner = std::make_unique<PluginIoRuntime>(); owner->root = root; owner->logical_directory = directory;
    for (std::uint32_t at = 0; at < count; ++at) if (!owner->non_io_imports.insert(reader.text()).second) throw Fatal(ERROR_INVALID_DATA, "Private I/O import declarations repeat.");
    crt_prepare(*owner, reader); crypto_prepare(*owner, reader); reader.finish();
    HANDLE token = nullptr; PSID expected = nullptr; DWORD size = 0; bool found = false;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token) || !ConvertStringSidToSidW(sid_text.c_str(), &expected)) throw Fatal(GetLastError(), "Native write restriction has no actual token/SID.");
    GetTokenInformation(token, TokenRestrictedSids, nullptr, 0, &size); std::vector<std::uint8_t> bytes(size);
    if (size && GetTokenInformation(token, TokenRestrictedSids, bytes.data(), size, &size)) {
        const auto* groups = reinterpret_cast<const TOKEN_GROUPS*>(bytes.data());
        if (groups->GroupCount == 1) found = EqualSid(expected, groups->Groups[0].Sid) != FALSE;
    }
    LocalFree(expected); CloseHandle(token);
    if (!found) throw Fatal(ERROR_ACCESS_DENIED, "Native loader is not running with the exact selected-module restricting SID.");
    const auto diagnostic = root + L"\\Diagnostic\\";
    // Retain the companion's original binary pipe in its first-party input
    // owner; unchanged DLL standard-input access must not consume that pipe.
    owner->stdin_file = CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    owner->stdout_file = CreateFileW((diagnostic + L"stdout.private.log").c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    owner->stderr_file = CreateFileW((diagnostic + L"stderr.private.log").c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (owner->stdin_file == INVALID_HANDLE_VALUE || owner->stdout_file == INVALID_HANDLE_VALUE || owner->stderr_file == INVALID_HANDLE_VALUE) throw Fatal(GetLastError(), "Native standard channels have no private boundary.");
    LARGE_INTEGER end{};
    if (!SetFilePointerEx(owner->stdout_file, end, nullptr, FILE_END) || !SetFilePointerEx(owner->stderr_file, end, nullptr, FILE_END) ||
        !SetStdHandle(STD_INPUT_HANDLE, owner->stdin_file) || !SetStdHandle(STD_OUTPUT_HANDLE, owner->stdout_file) || !SetStdHandle(STD_ERROR_HANDLE, owner->stderr_file)) throw Fatal(GetLastError(), "Native private diagnostic channel publication failed.");
    state->io = std::move(owner); Bytes data; data.put(1U); reply(frame, ERROR_SUCCESS, "", data);
}
void io_bind_imports(HMODULE image) {
    auto& owner = io_owner(); const auto* base = reinterpret_cast<const std::uint8_t*>(image); const auto dos = nvse_copy(reinterpret_cast<const IMAGE_DOS_HEADER*>(base));
    if (dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew <= 0) throw Fatal(ERROR_BAD_EXE_FORMAT, "Selected I/O image header is malformed.");
    const auto nt = nvse_copy(reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos.e_lfanew));
    if (nt.Signature != IMAGE_NT_SIGNATURE || nt.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) throw Fatal(ERROR_BAD_EXE_FORMAT, "Selected I/O image has an unowned PE kind.");
    const auto extent = nt.OptionalHeader.SizeOfImage;
    const auto& directory = nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT].Size) throw Fatal(ERROR_NOT_SUPPORTED, "Delay import I/O lifetime is unowned.");
    if (!directory.VirtualAddress && !directory.Size) return;
    if (directory.VirtualAddress > extent || directory.Size > extent - directory.VirtualAddress) throw Fatal(ERROR_BAD_EXE_FORMAT, "Selected import directory exceeds its image extent.");
    bool terminated = false;
    for (std::uint32_t offset = 0; offset + sizeof(IMAGE_IMPORT_DESCRIPTOR) <= directory.Size; offset += sizeof(IMAGE_IMPORT_DESCRIPTOR)) {
        const auto item = nvse_copy(reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + directory.VirtualAddress + offset));
        if (!item.Name && !item.FirstThunk && !item.OriginalFirstThunk) { terminated = true; break; }
        if (!item.Name || item.Name >= extent || !item.OriginalFirstThunk || item.OriginalFirstThunk >= extent || !item.FirstThunk || item.FirstThunk >= extent)
            throw Fatal(ERROR_BAD_EXE_FORMAT, "Selected import names/thunks have no complete image provenance.");
        auto library = nvse_string(reinterpret_cast<const char*>(base + item.Name), false);
        for (auto& character : library) if (character >= 'A' && character <= 'Z') character = static_cast<char>(character + ('a' - 'A'));
        const auto platform = library == "kernel32.dll" || library == "kernelbase.dll" || library.rfind("api-ms-win-core-", 0) == 0;
        bool ended = false;
        for (std::uint32_t index = 0; index < extent / sizeof(IMAGE_THUNK_DATA32); ++index) {
            const auto relative = static_cast<std::uint64_t>(index) * sizeof(IMAGE_THUNK_DATA32);
            if (relative + sizeof(IMAGE_THUNK_DATA32) > extent - item.OriginalFirstThunk || relative + sizeof(IMAGE_THUNK_DATA32) > extent - item.FirstThunk)
                throw Fatal(ERROR_BAD_EXE_FORMAT, "Selected import thunk exceeds its image.");
            const auto original = nvse_copy(reinterpret_cast<const IMAGE_THUNK_DATA32*>(base + item.OriginalFirstThunk + static_cast<std::size_t>(relative)));
            if (!original.u1.AddressOfData) { ended = true; break; }
            if (IMAGE_SNAP_BY_ORDINAL32(original.u1.Ordinal) || original.u1.AddressOfData >= extent - sizeof(WORD)) throw Fatal(ERROR_NOT_SUPPORTED, "Ordinal native I/O import ownership is unbound.");
            const auto name = nvse_string(reinterpret_cast<const char*>(base + original.u1.AddressOfData + sizeof(WORD)), false);
            auto** slot = reinterpret_cast<void**>(const_cast<std::uint8_t*>(base) + item.FirstThunk + static_cast<std::size_t>(relative));
            const auto prior = nvse_copy(slot); const auto crt_file = crt_io_import_name(library, name);
            auto* replacement = platform ? io_entry(name) : crt_file ? crt_import_entry(library, name, prior) : cng_client_import_entry(library, name, prior);
            if (!replacement) {
                if (crt_file || !owner.non_io_imports.count(library + "!" + name)) throw Fatal(ERROR_NOT_SUPPORTED, "Imported function has neither actual native I/O nor selected non-I/O ownership.");
                continue;
            }
            if (platform) {
                const auto expected32 = GetProcAddress(GetModuleHandleW(L"kernel32.dll"), name.c_str());
                const auto expected_base = GetProcAddress(GetModuleHandleW(L"kernelbase.dll"), name.c_str());
                if (prior != reinterpret_cast<void*>(expected32) && prior != reinterpret_cast<void*>(expected_base))
                    throw Fatal(ERROR_INVALID_DATA, "Selected native I/O import was altered or has an unowned platform binding.");
            }
            if (std::any_of(owner.imports.begin(), owner.imports.end(), [&](const auto& imported) { return imported.first == slot; }))
                throw Fatal(ERROR_INVALID_DATA, "Native I/O import repeats an already-owned writable slot.");
            // Retain the preimage before any native mutation. A failed page
            // publication cannot discard the actual module/callable ownership.
            owner.imports.emplace_back(slot, prior);
            DWORD previous = 0;
            if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &previous)) throw Fatal(GetLastError(), "Selected import table could not publish its owned API entry.");
            *slot = replacement;
            DWORD ignored = 0; if (!VirtualProtect(slot, sizeof(void*), previous, &ignored)) throw Fatal(GetLastError(), "Selected import table protection could not restore.");
            if (nvse_copy(slot) != replacement) throw Fatal(ERROR_INVALID_DATA, "Native I/O import publication has no actual readback equality.");
            ++owner.bound_imports;
        }
        if (!ended) throw Fatal(ERROR_BAD_EXE_FORMAT, "Selected import list has no complete terminator.");
    }
    if (!terminated) throw Fatal(ERROR_BAD_EXE_FORMAT, "Selected import directory has no complete terminator.");
}
void io_flush_detach(const Frame& frame) {
    if (!state->io) return; auto& owner = *state->io; owner.loader_retiring = false;
    state->calls.push_back(frame.header.id);
    for (const auto& event : owner.pending) io_event(PluginIoFile{event.capability, reinterpret_cast<HANDLE>(event.handle), false}, event.api, event.count, event.error);
    owner.pending.clear(); mutex_flush_detach(); mutex_namespace_retire(); crt_flush_detach(); crt_support_flush_detach(); find_flush_detach(); mapping_flush_detach(); shared_placement_flush_detach(); cng_client_flush_detach(); crypto_flush_detach(); state->calls.pop_back();
}
void io_retire() {
    if (!state->io) return; auto& owner = *state->io;
    if (!owner.files.empty() || !owner.pending.empty()) throw Fatal(ERROR_BUSY, "Selected native file capabilities remain after module retirement.");
    shared_placement_require_retired(); mutex_retire(); mutex_namespace_require_retired(); mapping_retire(); cng_client_require_retired(); crypto_retire(); crt_support_require_retired(); crt_retire(); find_retire(); owner.imports.clear();
    if (owner.stdin_file != INVALID_HANDLE_VALUE) { if (!CloseHandle(owner.stdin_file)) throw Fatal(GetLastError(), "Native standard input boundary did not retire."); owner.stdin_file = INVALID_HANDLE_VALUE; }
    if (owner.stdout_file != INVALID_HANDLE_VALUE) { if (!CloseHandle(owner.stdout_file)) throw Fatal(GetLastError(), "Native stdout diagnostic file did not retire."); owner.stdout_file = INVALID_HANDLE_VALUE; }
    if (owner.stderr_file != INVALID_HANDLE_VALUE) { if (!CloseHandle(owner.stderr_file)) throw Fatal(GetLastError(), "Native stderr diagnostic file did not retire."); owner.stderr_file = INVALID_HANDLE_VALUE; }
}
