// Include after the existing private-I/O helper definitions. Public CRT
// exports are called through the actual import provider, not the companion's
// unrelated FILE implementation or any original game executable code.
constexpr std::uint32_t crt_provider_event = 5, crt_route_event = 6, crt_opened_event = 7,
    crt_stream_event = 8, crt_path_event = 9;
void* crt_entry(const std::string&);
bool crt_io_import_name(const std::string& library, const std::string& name) {
    if (library == "api-ms-win-crt-environment-l1-1-0.dll") return name == "getenv_s" || name == "_putenv_s";
    if (library == "api-ms-win-crt-stdio-l1-1-0.dll") {
        static const std::set<std::string> pure {
            "__stdio_common_vsprintf", "__stdio_common_vsprintf_s", "__stdio_common_vsnprintf_s", "__stdio_common_vsscanf",
            "__stdio_common_vswprintf", "__stdio_common_vswprintf_s", "__stdio_common_vsnwprintf_s", "__stdio_common_vswscanf",
            "sprintf", "sprintf_s", "snprintf", "_snprintf", "_snprintf_s", "vsprintf", "vsprintf_s", "vsnprintf", "_vsnprintf", "_vsnprintf_s",
            "sscanf", "sscanf_s", "swprintf", "swprintf_s", "vswprintf", "vswprintf_s", "swscanf", "swscanf_s", "__local_stdio_printf_options", "__local_stdio_scanf_options"
        };
        return !pure.count(name);
    }
    if (library == "api-ms-win-crt-filesystem-l1-1-0.dll") return true;
    if (library != "ucrtbase.dll") return false;
    if (crt_entry(name)) return true;
    static const std::set<std::string> unowned {
        "freopen", "_wfreopen", "freopen_s", "_wfreopen_s", "_fdopen", "_wfdopen", "_fcloseall", "_flushall",
        "fgetpos", "fsetpos", "setbuf", "setvbuf", "_fileno", "_get_osfhandle", "_open_osfhandle",
        "_open", "_wopen", "_sopen", "_wsopen", "_sopen_s", "_wsopen_s", "_close", "_read", "_write",
        "_lseek", "_lseeki64", "_tell", "_telli64", "_eof", "_filelength", "_filelengthi64",
        "_rmdir", "_wrmdir", "_unlink", "_wunlink", "rename", "_wrename", "_chmod", "_wchmod",
        "_access", "_waccess", "_access_s", "_waccess_s", "tmpfile", "tmpfile_s", "_tempnam", "_wtempnam",
        "__acrt_iob_func", "__stdio_common_vfwprintf", "__stdio_common_vfscanf", "__stdio_common_vfwscanf"
    };
    return unowned.count(name) != 0;
}
PluginCrtRuntime& crt_owner() {
    auto& io = io_owner();
    if (!io.crt) throw Fatal(ERROR_NOT_SUPPORTED, "Original CRT file calls have no selected actual export provider.");
    return *io.crt;
}
template<class Function> Function crt_export(CrtProvider& provider, const char* name) {
    if (!provider.retained) throw Fatal(ERROR_INVALID_HANDLE, "CRT export provider retired before its actual stream.");
    const auto function = GetProcAddress(provider.retained, name);
    if (!function) throw Fatal(GetLastError(), "Selected CRT export provider lacks a required public callable arm.");
    HMODULE actual = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(function), &actual) || actual != provider.retained)
        throw Fatal(ERROR_INVALID_DATA, "CRT callable is not this retained actual export-provider image.");
    return reinterpret_cast<Function>(function);
}
CrtProvider& crt_provider(std::uint64_t id) {
    for (auto& provider : crt_owner().providers) if (provider->id == id) return *provider;
    throw Fatal(ERROR_INVALID_HANDLE, "CRT stream belongs to an absent provider lifetime.");
}
CrtProvider& crt_active_provider() {
    // Fixed C signatures do not carry a provider parameter. The admitted
    // original import graph therefore has exactly one actual UCRT provider.
    auto& owner = crt_owner();
    if (owner.providers.size() != 1 || !owner.providers[0]->retained)
        throw Fatal(ERROR_NOT_SUPPORTED, "CRT entry has no unique genuine source import provider.");
    return *owner.providers[0];
}
CrtStatus crt_status(CrtProvider& provider, void* stream = nullptr) {
    CrtStatus status{};
    const auto get_error = crt_export<int (__cdecl*)(int*)>(provider, "_get_errno");
    const auto get_dos = crt_export<int (__cdecl*)(unsigned long*)>(provider, "_get_doserrno");
    if (get_error(&status.error) || get_dos(&status.dos)) throw Fatal(ERROR_INVALID_DATA, "Actual CRT error state could not be read.");
    status.available = stream != nullptr;
    if (stream) {
        status.eof = crt_export<int (__cdecl*)(void*)>(provider, "feof")(stream);
        status.stream_error = crt_export<int (__cdecl*)(void*)>(provider, "ferror")(stream);
    }
    return status;
}
void crt_restore_error(CrtProvider& provider, const CrtStatus& status) {
    if (crt_export<int (__cdecl*)(int)>(provider, "_set_errno")(status.error) ||
        crt_export<int (__cdecl*)(unsigned long)>(provider, "_set_doserrno")(status.dos))
        throw Fatal(ERROR_INVALID_DATA, "CRT bridge could not preserve the actual source operation's error state.");
}
void crt_put_status(Bytes& request, const CrtStatus& status) {
    request.put(status.error); request.put(status.dos); request.put(status.available ? 1U : 0U);
    request.put(status.eof); request.put(status.stream_error);
}
void crt_register(CrtProvider& provider) {
    if (provider.registered) return;
    Bytes request; request.put(provider.id); request.put(address(provider.retained));
    request.text(io_utf8(provider.path)); request.text(provider.sha);
    auto response = io_host(crt_provider_event, request); Reader result{response.data};
    if (result.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual CRT provider has no correlated C# source receipt.");
    result.finish(); provider.registered = true;
}
void crt_emit(const CrtEvent& event, std::uint32_t callback = crt_stream_event) {
    auto& provider = crt_provider(event.provider);
    if (io_owner().loader_retiring) {
        if (callback != crt_stream_event) throw Fatal(ERROR_NOT_SUPPORTED, "CRT path/open cannot invoke C# while original loader retirement holds its lock.");
        if (!provider.registered) throw Fatal(ERROR_NOT_SUPPORTED, "First CRT detach operation has no registered original export provider.");
    }
    else crt_register(provider); Bytes request; request.put(event.provider); request.put(event.route); request.put(event.stream);
    request.put(static_cast<std::uint32_t>(event.operation)); request.put(event.argument); request.put(event.requested);
    request.put(event.result); request.put(event.retired ? 1U : 0U); crt_put_status(request, event.status);
    crt_owned_callback(callback, request);
}
CrtStream crt_stream(void* pointer, bool write = false) {
    auto& owner = crt_owner(); const auto found = owner.streams.find(address(pointer));
    if (!pointer || found == owner.streams.end() || found->second.pointer != pointer || (write && !found->second.writable))
        throw Fatal(ERROR_INVALID_HANDLE, "CRT operation has a foreign/closed stream or writes an original read-only input.");
    (void)crt_provider(found->second.provider); return found->second;
}
void crt_prepare(PluginIoRuntime& io, Reader& reader) {
    const auto count = reader.get<std::uint32_t>();
    if (count > 1) throw Fatal(ERROR_NOT_SUPPORTED, "Original CRT opaque pointer imports require one actual provider.");
    if (!count) return;
    auto owner = std::make_unique<PluginCrtRuntime>();
    auto provider = std::make_unique<CrtProvider>(); provider->id = 1;
    provider->path = io_from_utf8(reader.text()); provider->sha = reader.text(); provider->declaration = reader.text();
    if (provider->path.empty() || provider->sha.size() != 64 || provider->declaration.empty())
        throw Fatal(ERROR_INVALID_DATA, "CRT source provider lacks its exact path/hash/declaration.");
    const auto libraries = reader.get<std::uint32_t>();
    if (!libraries || libraries > 4) throw Fatal(ERROR_INVALID_DATA, "CRT source provider has no exact declared import-library set.");
    for (std::uint32_t at = 0; at < libraries; ++at) {
        const auto library = reader.text(); const auto names = reader.get<std::uint32_t>();
        if ((library != "ucrtbase.dll" && library != "api-ms-win-crt-stdio-l1-1-0.dll" && library != "api-ms-win-crt-filesystem-l1-1-0.dll" && library != "api-ms-win-crt-environment-l1-1-0.dll") ||
            !names || names > 128 || provider->imports.count(library))
            throw Fatal(ERROR_INVALID_DATA, "CRT source library has an unknown/duplicate declaration.");
        auto& declared = provider->imports[library];
        for (std::uint32_t index = 0; index < names; ++index) {
            const auto name = reader.text(); if (!crt_entry(name) || !declared.insert(name).second)
                throw Fatal(ERROR_NOT_SUPPORTED, "CRT declared import lacks an actual first-party callable owner.");
        }
    }
    owner->providers.push_back(std::move(provider)); io.crt = std::move(owner);
}
void crt_require_provider_file(const std::wstring& selected, const wchar_t* mapped) {
    HANDLE selected_file = INVALID_HANDLE_VALUE, mapped_file = INVALID_HANDLE_VALUE;
    try {
        // WOW64/API-set loading can report another path spelling for the same
        // backed provider. Its retained source is a file identity, not spelling.
        selected_file = CreateFileW(io_native_path(selected).c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (selected_file == INVALID_HANDLE_VALUE) throw Fatal(GetLastError(), "Selected CRT provider file identity is unavailable.");
        mapped_file = CreateFileW(io_native_path(mapped).c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (mapped_file == INVALID_HANDLE_VALUE) throw Fatal(GetLastError(), "Mapped CRT provider file identity is unavailable.");
        BY_HANDLE_FILE_INFORMATION expected{}, actual{};
        if (!GetFileInformationByHandle(selected_file, &expected) || !GetFileInformationByHandle(mapped_file, &actual))
            throw Fatal(GetLastError(), "CRT provider has no actual kernel file identity.");
        if (expected.dwVolumeSerialNumber != actual.dwVolumeSerialNumber || expected.nFileIndexHigh != actual.nFileIndexHigh ||
            expected.nFileIndexLow != actual.nFileIndexLow || expected.nFileSizeHigh != actual.nFileSizeHigh ||
            expected.nFileSizeLow != actual.nFileSizeLow || CompareFileTime(&expected.ftLastWriteTime, &actual.ftLastWriteTime) != 0)
            throw Fatal(ERROR_INVALID_DATA, "Mapped CRT provider differs from its retained exact selected source file.");
        const auto mapped_closed = CloseHandle(mapped_file); mapped_file = INVALID_HANDLE_VALUE;
        const auto mapped_error = mapped_closed ? ERROR_SUCCESS : GetLastError();
        const auto selected_closed = CloseHandle(selected_file); selected_file = INVALID_HANDLE_VALUE;
        const auto selected_error = selected_closed ? ERROR_SUCCESS : GetLastError();
        if (!mapped_closed || !selected_closed) throw Fatal(mapped_error ? mapped_error : selected_error, "CRT provider identity handles did not retire.");
    } catch (...) {
        if (mapped_file != INVALID_HANDLE_VALUE) CloseHandle(mapped_file);
        if (selected_file != INVALID_HANDLE_VALUE) CloseHandle(selected_file);
        throw;
    }
}
void* crt_import_entry(const std::string& library, const std::string& name, void* actual_import) {
    if (!io_owner().crt) return nullptr;
    auto& provider = *crt_owner().providers.at(0); const auto declaration = provider.imports.find(library);
    if (declaration == provider.imports.end() || !declaration->second.count(name)) return nullptr;
    auto* replacement = crt_entry(name); if (!replacement) throw Fatal(ERROR_NOT_SUPPORTED, "CRT source import has no owned actual entry.");
    HMODULE actual_module = nullptr;
    const auto reference_flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | (provider.retained ? GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT : 0);
    if (!actual_import || !GetModuleHandleExW(reference_flags,
        reinterpret_cast<LPCWSTR>(actual_import), &actual_module)) throw Fatal(GetLastError(), "CRT original import has no retained export-provider mapping.");
    // Capture the new actual reference before any allocation/validation can
    // throw. Terminal cleanup must still know which module reference it owns.
    if (!provider.retained) provider.retained = actual_module;
    std::vector<wchar_t> path(32768); const auto count = GetModuleFileNameW(actual_module, path.data(), static_cast<DWORD>(path.size()));
    if (!count || count >= path.size()) throw Fatal(GetLastError(), "Mapped CRT provider path is unavailable or truncated.");
    crt_require_provider_file(provider.path, path.data());
    if (reinterpret_cast<void*>(GetProcAddress(actual_module, name.c_str())) != actual_import || provider.retained != actual_module) {
        const auto failure = GetLastError();
        throw Fatal(failure ? failure : ERROR_INVALID_DATA, "CRT import export/pointer differs from its exact retained provider image.");
    }
    // Resolve genuine status/lifetime exports before any original FILE is
    // returned. The caller-selected provider is never substituted with our CRT.
    (void)crt_export<int (__cdecl*)(int*)>(provider, "_get_errno");
    (void)crt_export<int (__cdecl*)(unsigned long*)>(provider, "_get_doserrno");
    (void)crt_export<int (__cdecl*)(int)>(provider, "_set_errno");
    (void)crt_export<int (__cdecl*)(unsigned long)>(provider, "_set_doserrno");
    (void)crt_export<int (__cdecl*)(void*)>(provider, "fclose");
    (void)crt_export<int (__cdecl*)(void*)>(provider, "feof");
    (void)crt_export<int (__cdecl*)(void*)>(provider, "ferror");
    return replacement;
}
void* crt_dynamic_entry(HMODULE module, const std::string& name) {
    if (!io_owner().crt) return nullptr;
    auto& provider = crt_active_provider();
    if (module != provider.retained) return nullptr;
    if (!crt_io_import_name("ucrtbase.dll", name)) return nullptr;
    const auto allowed = std::any_of(provider.imports.begin(), provider.imports.end(), [&](const auto& declaration) { return declaration.second.count(name) != 0; });
    if (!allowed) throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic CRT entry lacks an exact selected source import declaration.");
    if (!crt_entry(name)) throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic CRT file arm remains unowned.");
    return crt_entry(name);
}
void crt_flush_detach() {
    if (!io_owner().crt) return; auto& owner = crt_owner();
    for (const auto& event : owner.pending) crt_emit(event); owner.pending.clear();
}
void crt_retire() {
    if (!io_owner().crt) return; auto& owner = crt_owner();
    if (!owner.streams.empty() || !owner.pending.empty() || !owner.failed_closes.empty())
        throw Fatal(ERROR_BUSY, "CRT actual stream/close result lifetimes remain after original module retirement.");
    for (auto& provider : owner.providers) if (provider->retained) {
        if (!FreeLibrary(provider->retained)) throw Fatal(GetLastError(), "CRT exact export-provider reference did not retire.");
        provider->retained = nullptr;
    }
}
