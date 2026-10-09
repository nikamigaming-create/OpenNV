bool crt_writable_mode(const std::wstring& mode) {
    if (mode.empty() || (mode[0] != L'r' && mode[0] != L'w' && mode[0] != L'a'))
        throw Fatal(ERROR_NOT_SUPPORTED, "Original CRT invalid-parameter/access mode owner is absent.");
    const auto comma = mode.find(L','); const auto access = mode.substr(0, comma);
    for (std::size_t at = 1; at < access.size(); ++at)
        if (std::wstring(L"+btcnNSRTDx ").find(access[at]) == std::wstring::npos)
            throw Fatal(ERROR_NOT_SUPPORTED, "CRT access/translation/delete mode has an unowned option.");
    if (comma != std::wstring::npos) {
        auto encoding = mode.substr(comma + 1); const auto first = encoding.find_first_not_of(L" "); const auto last = encoding.find_last_not_of(L" ");
        encoding = first == std::wstring::npos ? L"" : encoding.substr(first, last - first + 1);
        if (encoding != L"ccs=UNICODE" && encoding != L"ccs=UTF-8" && encoding != L"ccs=UTF-16LE")
            throw Fatal(ERROR_NOT_SUPPORTED, "CRT encoded stream option has no declared source owner.");
    }
    return mode[0] != L'r' || access.find(L'+') != std::wstring::npos || access.find(L'D') != std::wstring::npos;
}
std::wstring crt_path_a(const char* pointer) {
    const auto bytes = nvse_string(pointer, false); const auto codepage = AreFileApisANSI() ? GetACP() : GetOEMCP();
    if (bytes.empty()) throw Fatal(ERROR_NOT_SUPPORTED, "CRT empty-path invalid-parameter handling is unowned.");
    const auto count = MultiByteToWideChar(codepage, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), nullptr, 0);
    if (!count) throw Fatal(GetLastError(), "CRT native path has no exact active file-API codepage identity.");
    std::wstring text(static_cast<std::size_t>(count), L'\0');
    if (MultiByteToWideChar(codepage, MB_ERR_INVALID_CHARS, bytes.data(), static_cast<int>(bytes.size()), text.data(), count) != count)
        throw Fatal(GetLastError(), "CRT source path conversion drifted.");
    return text;
}
std::string crt_path_bytes(const std::wstring& path) {
    const auto codepage = AreFileApisANSI() ? GetACP() : GetOEMCP(); BOOL lost = FALSE;
    const auto flags = codepage == CP_UTF8 ? WC_ERR_INVALID_CHARS : WC_NO_BEST_FIT_CHARS;
    auto* default_pointer = codepage == CP_UTF8 ? nullptr : &lost;
    const auto count = WideCharToMultiByte(codepage, flags, path.c_str(), -1, nullptr, 0, nullptr, default_pointer);
    if (!count || lost) throw Fatal(ERROR_NO_UNICODE_TRANSLATION, "CRT private destination has no exact original file-API codepage representation.");
    std::string bytes(static_cast<std::size_t>(count), '\0'); lost = FALSE;
    if (WideCharToMultiByte(codepage, flags, path.c_str(), -1, bytes.data(), count, nullptr, default_pointer) != count || lost)
        throw Fatal(ERROR_NO_UNICODE_TRANSLATION, "CRT private destination ANSI identity changed.");
    return bytes;
}
struct CrtRoute { std::uint64_t id; std::wstring path; };
CrtRoute crt_route(CrtProvider& provider, std::uint32_t api, const std::wstring& mode, const std::wstring& path, bool writable) {
    crt_register(provider); Bytes request; request.put(provider.id); request.put(api); request.text(io_utf8(mode));
    request.put(writable ? 1U : 0U); request.text(io_utf8(path)); auto response = io_host(crt_route_event, request); Reader result{response.data};
    CrtRoute route{result.get<std::uint64_t>(), io_native_path(io_from_utf8(result.text()))}; result.finish();
    if (!route.id || route.path.empty()) throw Fatal(ERROR_INVALID_DATA, "CRT path has no exact selected/private decision."); return route;
}
template<class Open> void* crt_open_actual(const std::wstring& path, const std::wstring& mode, Open open) {
    auto& provider = crt_active_provider(); const auto writable = crt_writable_mode(mode);
    const auto route = crt_route(provider, 1, mode, path, writable); auto* stream = open(provider, route.path); const auto last = GetLastError();
    const auto status = crt_status(provider, stream);
    if (stream) {
        // Retain the genuine returned pointer before requesting a C# receipt.
        // A callback failure cannot discard a still-open original-provider FILE.
        auto& streams = crt_owner().streams;
        if (streams.count(address(stream))) throw Fatal(ERROR_INVALID_HANDLE, "CRT returned an already-live opaque stream pointer.");
        try { streams.emplace(address(stream), CrtStream{provider.id, route.id, stream, writable, mode}); }
        catch (...) {
            // Ownership has not been published. Retire only this newly created
            // real CRT stream; a failed close remains a terminal refusal.
            if (crt_export<int (__cdecl*)(void*)>(provider, "fclose")(stream))
                throw Fatal(ERROR_WRITE_FAULT, "Unpublished actual CRT stream failed construction cleanup.");
            throw;
        }
    }
    crt_emit(CrtEvent{provider.id, route.id, address(stream), CrtOperation::open, 0, 0,
        static_cast<std::int64_t>(address(stream)), false, status}, crt_opened_event);
    crt_restore_error(provider, status); SetLastError(last); return stream;
}
void* __cdecl crt_fopen_a(const char* path, const char* mode) {
    return nvse_adapter([&] {
        const auto source_path = crt_path_a(path); const auto access = io_ansi(mode);
        return crt_open_actual(source_path, access, [&](CrtProvider& provider, const std::wstring& target) {
            const auto bytes = crt_path_bytes(target); return crt_export<void* (__cdecl*)(const char*, const char*)>(provider, "fopen")(bytes.c_str(), mode);
        });
    });
}
void* __cdecl crt_fopen_w(const wchar_t* path, const wchar_t* mode) {
    return nvse_adapter([&] { return crt_open_actual(io_wide(path), io_wide(mode), [&](CrtProvider& provider, const std::wstring& target) {
        return crt_export<void* (__cdecl*)(const wchar_t*, const wchar_t*)>(provider, "_wfopen")(target.c_str(), mode);
    }); });
}
void* __cdecl crt_fsopen_a(const char* path, const char* mode, int sharing) {
    return nvse_adapter([&] { return crt_open_actual(crt_path_a(path), io_ansi(mode), [&](CrtProvider& provider, const std::wstring& target) {
        const auto bytes = crt_path_bytes(target); return crt_export<void* (__cdecl*)(const char*, const char*, int)>(provider, "_fsopen")(bytes.c_str(), mode, sharing);
    }); });
}
void* __cdecl crt_fsopen_w(const wchar_t* path, const wchar_t* mode, int sharing) {
    return nvse_adapter([&] { return crt_open_actual(io_wide(path), io_wide(mode), [&](CrtProvider& provider, const std::wstring& target) {
        return crt_export<void* (__cdecl*)(const wchar_t*, const wchar_t*, int)>(provider, "_wfsopen")(target.c_str(), mode, sharing);
    }); });
}
int __cdecl crt_fopen_s_a(void** destination, const char* path, const char* mode) {
    return nvse_adapter([&] {
        if (!destination) throw Fatal(ERROR_NOT_SUPPORTED, "CRT null output invalid-parameter handler is unowned.");
        expression_writable(destination, sizeof(void*)); int result = 0;
        auto* stream = crt_open_actual(crt_path_a(path), io_ansi(mode), [&](CrtProvider& provider, const std::wstring& target) -> void* {
            const auto bytes = crt_path_bytes(target);
            result = crt_export<int (__cdecl*)(void**, const char*, const char*)>(provider, "fopen_s")(destination, bytes.c_str(), mode);
            const auto opened = nvse_copy(destination);
            if ((result == 0) != (opened != nullptr)) throw Fatal(ERROR_INVALID_DATA, "Actual CRT secure-open error and stream result disagree.");
            return opened;
        }); (void)stream; return result;
    });
}
int __cdecl crt_fopen_s_w(void** destination, const wchar_t* path, const wchar_t* mode) {
    return nvse_adapter([&] {
        if (!destination) throw Fatal(ERROR_NOT_SUPPORTED, "CRT null output invalid-parameter handler is unowned.");
        expression_writable(destination, sizeof(void*)); int result = 0;
        auto* stream = crt_open_actual(io_wide(path), io_wide(mode), [&](CrtProvider& provider, const std::wstring& target) -> void* {
            result = crt_export<int (__cdecl*)(void**, const wchar_t*, const wchar_t*)>(provider, "_wfopen_s")(destination, target.c_str(), mode);
            const auto opened = nvse_copy(destination);
            if ((result == 0) != (opened != nullptr)) throw Fatal(ERROR_INVALID_DATA, "Actual CRT secure-open error and stream result disagree.");
            return opened;
        }); (void)stream; return result;
    });
}
template<class Create> int crt_directory_actual(const std::wstring& path, Create create) {
    auto& provider = crt_active_provider(); const auto route = crt_route(provider, 5, L"", path, true);
    const auto result = create(provider, route.path); const auto status = crt_status(provider);
    crt_emit(CrtEvent{provider.id, route.id, 0, CrtOperation::make_directory, 0, 0, result, false, status}, crt_path_event);
    crt_restore_error(provider, status); return result;
}
int __cdecl crt_mkdir_a(const char* path) {
    return nvse_adapter([&] { return crt_directory_actual(crt_path_a(path), [&](CrtProvider& provider, const std::wstring& target) {
        const auto bytes = crt_path_bytes(target); return crt_export<int (__cdecl*)(const char*)>(provider, "_mkdir")(bytes.c_str());
    }); });
}
int __cdecl crt_mkdir_w(const wchar_t* path) {
    return nvse_adapter([&] { return crt_directory_actual(io_wide(path), [&](CrtProvider& provider, const std::wstring& target) {
        return crt_export<int (__cdecl*)(const wchar_t*)>(provider, "_wmkdir")(target.c_str());
    }); });
}
