// The structural declaration arrived from the retained first-party PE reader.
// Windows executes the original constructor; no table, heap or initializer is
// recreated here. Provider queries run before entering the Windows loader lock.
struct ModuleConstructionField {
    std::uint32_t rva, bytes, initial;
    std::string role;
};
struct ModuleConstruction {
    std::uint32_t kind = 0, entry = 0, thread = 0;
    std::string sha, stack, ucrt_sha, vcruntime_sha;
    std::wstring ucrt_path, vcruntime_path;
    std::vector<ModuleConstructionField> fields;
    CrtProvider* provider = nullptr;
    HMODULE vcruntime = nullptr;
    std::uint32_t handler = 0;
    int mode = 0;
    bool prepared = false, entered = false, returned = false, released = false;
};
ModuleConstruction module_construction_read(Reader& reader) {
    ModuleConstruction result;
    result.kind = reader.get<std::uint32_t>(); result.entry = reader.get<std::uint32_t>();
    result.sha = reader.text(); result.stack = reader.text();
    const auto count = reader.get<std::uint32_t>();
    if (result.kind < 1 || result.kind > 3 || result.sha.size() != 64 || result.stack.size() != 64 || count > 64 ||
        (result.kind == 1) != (result.entry == 0) || (result.kind == 3) != (count != 0))
        throw Fatal(ERROR_INVALID_DATA, "Original construction lost its typed source family/field extent.");
    std::set<std::string> roles;
    for (std::uint32_t at = 0; at < count; ++at) {
        ModuleConstructionField field{reader.get<std::uint32_t>(), reader.get<std::uint32_t>(), reader.get<std::uint32_t>(), reader.text()};
        if (!field.rva || field.bytes < 4 || field.bytes > 1024 * 1024 || field.bytes % 4 ||
            field.role.empty() || !roles.insert(field.role).second)
            throw Fatal(ERROR_INVALID_DATA, "Original constructor initial field has no unique bounded source declaration.");
        result.fields.push_back(std::move(field));
    }
    if (result.kind == 3) {
        result.ucrt_path = path_from_utf8(reader.text()); result.ucrt_sha = reader.text();
        result.vcruntime_path = path_from_utf8(reader.text()); result.vcruntime_sha = reader.text();
        if (result.ucrt_path.empty() || result.vcruntime_path.empty() || result.ucrt_sha.size() != 64 || result.vcruntime_sha.size() != 64)
            throw Fatal(ERROR_INVALID_DATA, "Original memory constructor lacks its retained actual CRT/provider sources.");
    }
    return result;
}
void module_construction_prepare(ModuleConstruction& constructor, const std::wstring& path) {
    if (!state || !state->io || !state->module_set || !state->calls.empty() || state->loading_handle ||
        GetCurrentThreadId() != state->thread || state->io->loader_retiring)
        throw Fatal(ERROR_BUSY, "Original constructor has no idle actual thread/module/provider lifetime before entry.");
    constructor.thread = state->thread;
    const auto slash = path.find_last_of(L"/\\"); const auto basename = path.substr(slash == std::wstring::npos ? 0 : slash + 1);
    if (basename.empty() || GetModuleHandleW(path.c_str()) || GetModuleHandleW(basename.c_str()))
        throw Fatal(ERROR_ALREADY_EXISTS, "Original constructor fresh source state conflicts with an already mapped image/basename.");
    if (constructor.kind == 3) {
        if (!state->io->crt || state->io->crt->providers.size() != 1 || !state->io->crt->providers[0]->retained)
            throw Fatal(ERROR_NOT_SUPPORTED, "Original memory constructor lost the genuine existing shared CRT resource.");
        auto& provider = *state->io->crt->providers[0]; constructor.provider = &provider;
        if (provider.sha != constructor.ucrt_sha || provider.path != constructor.ucrt_path)
            throw Fatal(ERROR_INVALID_DATA, "Original constructor attempts to borrow another shared CRT source.");
        std::array<wchar_t, 32768> mapped{};
        auto length = GetModuleFileNameW(provider.retained, mapped.data(), static_cast<DWORD>(mapped.size()));
        if (!length || length >= mapped.size()) throw Fatal(GetLastError(), "Original shared CRT current mapped path is unavailable.");
        crt_require_provider_file(constructor.ucrt_path, mapped.data());
        const auto incoming = GetLastError(); const auto status = crt_status(provider);
        const auto query_handler = crt_export<void* (__cdecl*)()>(provider, "_query_new_handler");
        const auto query_mode = crt_export<int (__cdecl*)()>(provider, "_query_new_mode");
        constructor.handler = address(query_handler()); constructor.mode = query_mode();
        crt_restore_error(provider, status); SetLastError(incoming);
        if (constructor.handler || (constructor.mode != 0 && constructor.mode != 1))
            throw Fatal(ERROR_NOT_SUPPORTED, "Original constructor reached an unowned current shared new-handler/malloc-mode before entry.");
        const auto current_vcruntime = GetModuleHandleW(L"vcruntime140.dll");
        const auto anchor = current_vcruntime ? GetProcAddress(current_vcruntime, "__CxxFrameHandler3") : nullptr;
        if (!anchor || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(anchor), &constructor.vcruntime) || constructor.vcruntime != current_vcruntime)
            throw Fatal(ERROR_NOT_SUPPORTED, "Original exception constructor requires its already mapped actual source provider before entry.");
        // Retain the new loader borrow before any later validation can fail.
        // A failed prefix releases this reference only with exact child exit.
        length = GetModuleFileNameW(constructor.vcruntime, mapped.data(), static_cast<DWORD>(mapped.size()));
        if (!length || length >= mapped.size()) throw Fatal(GetLastError(), "Original exception provider current path is unavailable.");
        crt_require_provider_file(constructor.vcruntime_path, mapped.data());
        for (const auto* name : {"memset", "__CxxFrameHandler3", "_CxxThrowException"}) {
            const auto function = GetProcAddress(constructor.vcruntime, name); HMODULE actual = nullptr;
            if (!function || !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(function), &actual) || actual != constructor.vcruntime)
                throw Fatal(ERROR_INVALID_DATA, "Original exception/memory child escaped its current retained provider.");
        }
    }
    constructor.prepared = true;
}
void module_construction_release(ModuleConstruction& constructor) {
    if (!constructor.prepared || !constructor.entered || !constructor.returned || constructor.released)
        throw Fatal(ERROR_INVALID_STATE, "Original constructor provider release has no actual returned Windows loader call.");
    if (constructor.vcruntime && !FreeLibrary(constructor.vcruntime))
        throw Fatal(GetLastError(), "Original constructor exception-provider borrow did not retire after the SDK returned.");
    constructor.released = true;
}
void module_construction_bytes(Bytes& result, const ModuleConstruction& constructor, std::uint32_t sdk_error, bool mapped) {
    result.put(constructor.kind); result.put(constructor.entry); result.put(constructor.thread);
    result.put(constructor.prepared ? 1U : 0U); result.put(constructor.entered ? 1U : 0U); result.put(mapped ? 1U : 0U); result.put(sdk_error);
    result.put(constructor.provider ? address(constructor.provider->retained) : 0U); result.put(address(constructor.vcruntime));
    result.put(constructor.handler); result.put(constructor.mode); result.put(constructor.released ? 1U : 0U);
}
void module_construction_validate(const ModuleConstruction& constructor, HMODULE image, const IMAGE_NT_HEADERS32& nt) {
    if (constructor.entry != nt.OptionalHeader.AddressOfEntryPoint)
        throw Fatal(ERROR_INVALID_DATA, "Original mapped entry changed its selected source construction association.");
    for (const auto& field : constructor.fields) {
        if (field.rva > nt.OptionalHeader.SizeOfImage || field.bytes > nt.OptionalHeader.SizeOfImage - field.rva)
            throw Fatal(ERROR_INVALID_DATA, "Original constructor field leaves its actual mapped source image.");
        const auto pointer = reinterpret_cast<const std::uint32_t*>(reinterpret_cast<const unsigned char*>(image) + field.rva);
        if (field.role == "native-startup" && nvse_copy(pointer) != 2)
            throw Fatal(ERROR_INVALID_DATA, "Original Windows constructor did not return its real initialized module state.");
        if ((field.role == "raw-entry-callback" || field.role == "dynamic-TLS-callback" || field.role == "dynamic-UCRT") && nvse_copy(pointer) != field.initial)
            throw Fatal(ERROR_INVALID_DATA, "Original constructor changed its protected preceding source predicate.");
    }
}
