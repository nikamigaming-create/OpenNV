constexpr std::uint32_t cpp_runtime_callback = 28;
constexpr const char* cpp_open_wide = "?_Fiopen@std@@YAPAU_iobuf@@PB_WHH@Z";
constexpr const char* cpp_open_narrow = "?_Fiopen@std@@YAPAU_iobuf@@PBDHH@Z";
void* __cdecl cpp_fiopen_w(const wchar_t*, int, int);
void* __cdecl cpp_fiopen_a(const char*, int, int);

void cpp_prepare(PluginIoRuntime& io, Reader& reader) {
    const auto count = reader.get<std::uint32_t>();
    if (count > 1) throw Fatal(ERROR_NOT_SUPPORTED, "C++ object imports require one exact selected Microsoft provider.");
    if (!count) return;
    auto provider = std::make_unique<CppRuntimeProvider>();
    provider->path = io_from_utf8(reader.text()); provider->sha = reader.text(); provider->declaration = reader.text();
    const auto exports = reader.get<std::uint32_t>();
    if (provider->path.empty() || provider->sha.size() != 64 || provider->declaration.empty() || !exports || exports > 128 || !io.crt)
        throw Fatal(ERROR_INVALID_DATA, "C++ declaration lacks exact provider/UCRT/export ownership.");
    std::set<std::string> names;
    for (std::uint32_t at = 0; at < exports; ++at) {
        const auto name = reader.text(); const auto rva = reader.get<std::uint32_t>(), code = reader.get<std::uint32_t>();
        if (name.empty() || !rva || code > 1 || !names.insert(name).second)
            throw Fatal(ERROR_INVALID_DATA, "C++ export repeats or changes its complete source declaration.");
        provider->exports.push_back({name, rva, code != 0, nullptr});
    }
    io.cpp = std::move(provider);
}
CppRuntimeProvider& cpp_owner() {
    auto& io = io_owner();
    if (!io.cpp || io.cpp->retired || !io.cpp->retained)
        throw Fatal(ERROR_INVALID_HANDLE, "C++ callable lacks its selected live provider image.");
    return *io.cpp;
}
void cpp_validate_mapping(CppRuntimeProvider& provider, HMODULE actual) {
    std::vector<wchar_t> path(32768);
    const auto size = GetModuleFileNameW(actual, path.data(), static_cast<DWORD>(path.size()));
    if (!size || size >= path.size()) throw Fatal(GetLastError(), "C++ actual provider path is absent/truncated.");
    crt_require_provider_file(provider.path, path.data());
    for (auto& item : provider.exports) {
        const auto pointer = reinterpret_cast<void*>(GetProcAddress(actual, item.name.c_str()));
        if (!pointer || static_cast<std::uint64_t>(address(actual)) + item.rva != address(pointer))
            throw Fatal(ERROR_INVALID_DATA, "C++ imported code/global differs from its exact source export RVA.");
        MEMORY_BASIC_INFORMATION region{};
        if (VirtualQuery(pointer, &region, sizeof(region)) != sizeof(region) || region.AllocationBase != actual || region.State != MEM_COMMIT)
            throw Fatal(ERROR_INVALID_ADDRESS, "C++ export has no retained native image extent.");
        const auto protection = region.Protect & 0xffU;
        const bool executable = protection == PAGE_EXECUTE || protection == PAGE_EXECUTE_READ || protection == PAGE_EXECUTE_READWRITE || protection == PAGE_EXECUTE_WRITECOPY;
        if (executable != item.code || region.Protect & (PAGE_NOACCESS | PAGE_GUARD))
            throw Fatal(ERROR_INVALID_DATA, "C++ actual code/global page kind changed from its selected declaration.");
        item.actual = pointer;
    }
}
void* cpp_import_entry(const std::string& library, const std::string& name, void* prior) {
    if (library != "msvcp140.dll" || !io_owner().cpp) return nullptr;
    auto& provider = *io_owner().cpp;
    const auto found = std::find_if(provider.exports.begin(), provider.exports.end(), [&](const auto& item) { return item.name == name; });
    if (found == provider.exports.end()) return nullptr;
    HMODULE mapped = nullptr;
    const auto flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | (provider.retained ? GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT : 0);
    if (!prior || !GetModuleHandleExW(flags, reinterpret_cast<LPCWSTR>(prior), &mapped))
        throw Fatal(GetLastError(), "C++ import lacks its real Windows export-provider mapping.");
    if (!provider.retained) provider.retained = mapped;
    if (provider.retained != mapped) throw Fatal(ERROR_INVALID_DATA, "C++ source imports span different runtime instances.");
    cpp_validate_mapping(provider, mapped);
    if (found->actual != prior) throw Fatal(ERROR_INVALID_DATA, "C++ IAT has an altered/foreign original provider pointer.");
    if (name == cpp_open_wide) return reinterpret_cast<void*>(cpp_fiopen_w);
    if (name == cpp_open_narrow) return reinterpret_cast<void*>(cpp_fiopen_a);
    // Keep the actual provider address. This preserves thiscall, hidden
    // aggregate-result pointers, virtual dispatch and C++ exception ABI;
    // no guessed C++ layout or scalar ABI adapter sits between them.
    return prior;
}
void cpp_publish() {
    if (!io_owner().cpp) return;
    auto& provider = cpp_owner();
    if (provider.published) throw Fatal(ERROR_INVALID_STATE, "C++ provider publication cannot replay.");
    Bytes request; request.put(1U); request.put(address(provider.retained)); request.text(io_utf8(provider.path)); request.text(provider.sha);
    request.put(static_cast<std::uint32_t>(provider.exports.size()));
    for (const auto& item : provider.exports) {
        if (!item.actual) throw Fatal(ERROR_INVALID_DATA, "C++ original import has no actual mapped provider export.");
        request.text(item.name); request.put(address(item.actual)); request.put(item.rva); request.put(item.code ? 1U : 0U);
    }
    crt_owned_callback(cpp_runtime_callback, request); provider.published = true;
}
void* cpp_dynamic_entry(HMODULE module, const std::string& name) {
    if (!io_owner().cpp || io_owner().cpp->retired || module != io_owner().cpp->retained) return nullptr;
    auto& owner = cpp_owner();
    const auto declaration = std::find_if(owner.exports.begin(), owner.exports.end(), [&](const auto& item) { return item.name == name; });
    if (declaration == owner.exports.end())
        throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic C++ lookup lacks its exact original source export declaration.");
    if (name == cpp_open_wide) return reinterpret_cast<void*>(cpp_fiopen_w);
    if (name == cpp_open_narrow) return reinterpret_cast<void*>(cpp_fiopen_a);
    if (!declaration->actual) throw Fatal(ERROR_INVALID_STATE, "Dynamic C++ method/global has no actual provider publication.");
    return declaration->actual;
}
void cpp_retire() {
    if (!io_owner().cpp) return; auto& provider = *io_owner().cpp;
    if (provider.retired) return;
    if (!provider.retained) { provider.retired = true; return; }
    if (provider.retiring) throw Fatal(ERROR_INVALID_STATE, "Entered C++ provider retirement cannot replay.");
    provider.retiring = true; const auto mapped = provider.retained;
    // Provider globals destruct/flush while their genuine UCRT FILEs remain
    // live. Actual SDK failure retains the entered owner, never a success.
    if (!FreeLibrary(mapped)) throw Fatal(GetLastError(), "C++ provider loader reference did not retire.");
    provider.retained = nullptr; provider.retired = true;
    if (provider.published) {
        Bytes request; request.put(2U); request.put(address(mapped)); request.text(io_utf8(provider.path)); request.text(provider.sha); request.put(0U);
        crt_owned_callback(cpp_runtime_callback, request);
    }
}

template<class Call> void* cpp_file_open(const std::wstring& source_path, int mode, int protection, Call call) {
    auto& cpp = cpp_owner();
    if (!cpp.published || !io_owner().standard || !io_owner().standard->published)
        throw Fatal(ERROR_NOT_SUPPORTED, "Pre-entry C++ file construction has no entered source route owner.");
    if (mode & ~0xff || (protection != 0x10 && protection != 0x20 && protection != 0x30 && protection != 0x40 && protection != 0x80))
        throw Fatal(ERROR_NOT_SUPPORTED, "C++ file mode/protection has an unowned public declaration.");
    // Public MSVC openmode bits: in1,out2,ate4,app8,trunc16,binary32,
    // nocreate64,noreplace128. Access is independent of its complete mode.
    const bool writable = (mode & (2 | 8 | 16)) != 0;
    std::wstring access = writable ? ((mode & 8) ? L"a" : (mode & 1) ? L"r+" : L"w") : L"r";
    const auto incoming = GetLastError();
    return crt_open_actual(source_path, access, [&](CrtProvider&, const std::wstring& path) {
        SetLastError(incoming); return call(cpp, path);
    });
}
void cpp_require_retired() {
    if (io_owner().cpp && !io_owner().cpp->retired)
        throw Fatal(ERROR_BUSY, "Selected C++ provider/global-object retirement remains incomplete.");
}
void* __cdecl cpp_fiopen_w(const wchar_t* path, int mode, int protection) {
    return nvse_adapter([&] { return cpp_file_open(io_wide(path), mode, protection, [&](CppRuntimeProvider& owner, const std::wstring& routed) {
        const auto actual = GetProcAddress(owner.retained, cpp_open_wide);
        if (!actual) throw Fatal(GetLastError(), "C++ wide file constructor lost its actual selected export.");
        return reinterpret_cast<void* (__cdecl*)(const wchar_t*, int, int)>(actual)(routed.c_str(), mode, protection);
    }); });
}
void* __cdecl cpp_fiopen_a(const char* path, int mode, int protection) {
    return nvse_adapter([&] { return cpp_file_open(crt_path_a(path), mode, protection, [&](CppRuntimeProvider& owner, const std::wstring& routed) {
        const auto bytes = crt_path_bytes(routed); const auto actual = GetProcAddress(owner.retained, cpp_open_narrow);
        if (!actual) throw Fatal(GetLastError(), "C++ narrow file constructor lost its actual selected export.");
        return reinterpret_cast<void* (__cdecl*)(const char*, int, int)>(actual)(bytes.c_str(), mode, protection);
    }); });
}
