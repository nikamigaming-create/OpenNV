// After the actual private-I/O definitions. These public Windows calls use
// real native caller buffers and the same source winner/private config routes.
constexpr std::uint32_t io_profile_result_callback = 10;
void io_profile_result(const PluginIoRoute& route, std::uint32_t kind, std::uint32_t requested,
    std::uint32_t result, DWORD last, bool mutating) {
    Bytes request; request.put(route.id); request.put(kind); request.put(requested); request.put(result); request.put(last); request.put(mutating ? 1U : 0U);
    auto response = io_host(io_profile_result_callback, request); Reader receipt{response.data};
    if (receipt.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual profile result lost its selected configuration receipt.");
    receipt.finish();
}
void io_profile_output(void* buffer, DWORD size, bool wide) {
    const auto bytes = static_cast<std::uint64_t>(size) * (wide ? sizeof(wchar_t) : 1);
    if (bytes > std::numeric_limits<std::uint32_t>::max()) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Native profile buffer byte extent overflows x86.");
    if (bytes) expression_writable(buffer, static_cast<std::uint32_t>(bytes));
}
DWORD WINAPI io_profile_section_w(LPCWSTR section, LPWSTR buffer, DWORD size, LPCWSTR path) {
    return nvse_adapter([&] {
        const auto route = io_resolve(io_profile_api, 6, io_wide(path)); io_profile_output(buffer, size, true);
        const auto result = GetPrivateProfileSectionW(section, buffer, size, io_profile_path(route).c_str()); const auto last = GetLastError();
        io_profile_result(route, 1, size, result, last, false); SetLastError(last); return result;
    });
}
DWORD WINAPI io_profile_section_a(LPCSTR section, LPSTR buffer, DWORD size, LPCSTR path) {
    return nvse_adapter([&] {
        const auto route = io_resolve(io_profile_api, 6, io_ansi(path)); io_profile_output(buffer, size, false);
        const auto physical = io_native_ansi(io_profile_path(route));
        const auto result = GetPrivateProfileSectionA(section, buffer, size, physical.c_str()); const auto last = GetLastError();
        io_profile_result(route, 2, size, result, last, false); SetLastError(last); return result;
    });
}
BOOL WINAPI io_profile_struct_w(LPCWSTR section, LPCWSTR key, LPVOID buffer, UINT size, LPCWSTR path) {
    return nvse_adapter([&] {
        const auto route = io_resolve(io_profile_api, 6, io_wide(path)); io_profile_output(buffer, size, false);
        const auto result = GetPrivateProfileStructW(section, key, buffer, size, io_profile_path(route).c_str()); const auto last = GetLastError();
        io_profile_result(route, 3, size, static_cast<std::uint32_t>(result), last, false); SetLastError(last); return result;
    });
}
BOOL WINAPI io_profile_struct_a(LPCSTR section, LPCSTR key, LPVOID buffer, UINT size, LPCSTR path) {
    return nvse_adapter([&] {
        const auto route = io_resolve(io_profile_api, 6, io_ansi(path)); io_profile_output(buffer, size, false);
        const auto physical = io_native_ansi(io_profile_path(route));
        const auto result = GetPrivateProfileStructA(section, key, buffer, size, physical.c_str()); const auto last = GetLastError();
        io_profile_result(route, 4, size, static_cast<std::uint32_t>(result), last, false); SetLastError(last); return result;
    });
}
BOOL WINAPI io_profile_section_write_w(LPCWSTR section, LPCWSTR values, LPCWSTR path) {
    return nvse_adapter([&] {
        if (!section) throw Fatal(ERROR_NOT_SUPPORTED, "Global profile-cache control lacks a selected configuration owner.");
        const auto route = io_resolve(io_profile_write_api, 7, io_wide(path));
        const auto result = WritePrivateProfileSectionW(section, values, route.path.c_str()); const auto last = GetLastError();
        io_profile_result(route, 5, 0, static_cast<std::uint32_t>(result), last, true); SetLastError(last); return result;
    });
}
BOOL WINAPI io_profile_section_write_a(LPCSTR section, LPCSTR values, LPCSTR path) {
    return nvse_adapter([&] {
        if (!section) throw Fatal(ERROR_NOT_SUPPORTED, "Global profile-cache control lacks a selected configuration owner.");
        const auto route = io_resolve(io_profile_write_api, 7, io_ansi(path));
        const auto physical = io_native_ansi(route.path);
        const auto result = WritePrivateProfileSectionA(section, values, physical.c_str()); const auto last = GetLastError();
        io_profile_result(route, 6, 0, static_cast<std::uint32_t>(result), last, true); SetLastError(last); return result;
    });
}
BOOL WINAPI io_profile_struct_write_w(LPCWSTR section, LPCWSTR key, LPVOID buffer, UINT size, LPCWSTR path) {
    return nvse_adapter([&] {
        const auto route = io_resolve(io_profile_write_api, 7, io_wide(path));
        const auto result = WritePrivateProfileStructW(section, key, buffer, size, route.path.c_str()); const auto last = GetLastError();
        io_profile_result(route, 7, size, static_cast<std::uint32_t>(result), last, true); SetLastError(last); return result;
    });
}
BOOL WINAPI io_profile_struct_write_a(LPCSTR section, LPCSTR key, LPVOID buffer, UINT size, LPCSTR path) {
    return nvse_adapter([&] {
        const auto route = io_resolve(io_profile_write_api, 7, io_ansi(path));
        const auto physical = io_native_ansi(route.path);
        const auto result = WritePrivateProfileStructA(section, key, buffer, size, physical.c_str()); const auto last = GetLastError();
        io_profile_result(route, 8, size, static_cast<std::uint32_t>(result), last, true); SetLastError(last); return result;
    });
}
DWORD WINAPI io_profile_names_w(LPWSTR buffer, DWORD size, LPCWSTR path) {
    return nvse_adapter([&] {
        const auto route = io_resolve(io_profile_api, 6, io_wide(path)); io_profile_output(buffer, size, true);
        const auto result = GetPrivateProfileSectionNamesW(buffer, size, io_profile_path(route).c_str()); const auto last = GetLastError();
        io_profile_result(route, 9, size, result, last, false); SetLastError(last); return result;
    });
}
DWORD WINAPI io_profile_names_a(LPSTR buffer, DWORD size, LPCSTR path) {
    return nvse_adapter([&] {
        const auto route = io_resolve(io_profile_api, 6, io_ansi(path)); io_profile_output(buffer, size, false);
        const auto physical = io_native_ansi(io_profile_path(route));
        const auto result = GetPrivateProfileSectionNamesA(buffer, size, physical.c_str()); const auto last = GetLastError();
        io_profile_result(route, 10, size, result, last, false); SetLastError(last); return result;
    });
}
void* io_profile_extended_entry(const std::string& name) {
    struct Entry { const char* name; void* function; };
    const Entry entries[] = {
        {"GetPrivateProfileSectionW", reinterpret_cast<void*>(io_profile_section_w)}, {"GetPrivateProfileSectionA", reinterpret_cast<void*>(io_profile_section_a)},
        {"GetPrivateProfileStructW", reinterpret_cast<void*>(io_profile_struct_w)}, {"GetPrivateProfileStructA", reinterpret_cast<void*>(io_profile_struct_a)},
        {"WritePrivateProfileSectionW", reinterpret_cast<void*>(io_profile_section_write_w)}, {"WritePrivateProfileSectionA", reinterpret_cast<void*>(io_profile_section_write_a)},
        {"WritePrivateProfileStructW", reinterpret_cast<void*>(io_profile_struct_write_w)}, {"WritePrivateProfileStructA", reinterpret_cast<void*>(io_profile_struct_write_a)},
        {"GetPrivateProfileSectionNamesW", reinterpret_cast<void*>(io_profile_names_w)}, {"GetPrivateProfileSectionNamesA", reinterpret_cast<void*>(io_profile_names_a)},
    };
    for (const auto& entry : entries) if (name == entry.name) return entry.function;
    return nullptr;
}
