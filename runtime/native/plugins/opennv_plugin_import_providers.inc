// Included after real CNG caller thunks and before the unchanged NVSE loader.
void import_provider_snapshot(ImportedProvider& provider) {
    if (!provider.image || !provider.inspect || provider.loader_released)
        throw Fatal(ERROR_INVALID_HANDLE, "First-party dependency no longer owns its actual mapped image.");
    ImportProviderSnapshot actual{};
    if (!provider.inspect(&actual, sizeof(actual))) throw Fatal(GetLastError(), "First-party dependency inspection failed.");
    if (actual.abi != import_provider_abi || actual.bytes != sizeof(actual) || actual.generation != state->generation ||
        actual.thread != state->thread || actual.active || actual.bound != (provider.bound ? 1U : 0U) ||
        actual.process_attach != 1 || actual.process_detach)
        throw Fatal(ERROR_INVALID_DATA, "First-party dependency changed its real ABI/call/loader lifetime.");
    for (std::size_t at = 0; at < 7; ++at)
        if (actual.calls[at] < provider.last.calls[at]) throw Fatal(ERROR_INVALID_DATA, "First-party dependency lost a used export invocation.");
    provider.last = actual;
}
void import_provider_bytes(Bytes& data, ImportedProvider& provider) {
    if (!provider.loader_released) import_provider_snapshot(provider);
    data.put(provider.kind); data.text(provider.library); data.text(io_utf8(provider.path)); data.text(provider.sha);
    data.put(address(provider.image)); data.put(provider.last.thread); data.put(provider.bound ? 1U : 0U);
    data.put(provider.last.process_attach); data.put(provider.last.process_detach); data.put(provider.last.active);
    for (const auto count : provider.last.calls) data.put(count);
    data.put(provider.loader_released ? 1U : 0U); data.put(provider.image ? 1U : 0U);
}
void* import_provider_export(ImportedProvider& provider, const std::string& name) {
    const auto found = std::find_if(provider.exports.begin(), provider.exports.end(), [&](const auto& row) { return row.name == name; });
    if (found == provider.exports.end() || !provider.image || !found->rva)
        throw Fatal(ERROR_PROC_NOT_FOUND, "First-party dependency export lacks its retained complete source declaration.");
    const auto actual = function_pointer<void*>(GetProcAddress(provider.image, name.c_str()));
    if (static_cast<std::uint64_t>(address(provider.image)) + found->rva > UINT32_MAX ||
        address(actual) != address(provider.image) + found->rva)
        throw Fatal(ERROR_INVALID_DATA, "First-party dependency export did not bind to its actual declared image coordinate.");
    MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(actual, &memory, sizeof(memory)) || memory.AllocationBase != provider.image || memory.Type != MEM_IMAGE ||
        memory.State != MEM_COMMIT || !(memory.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)))
        throw Fatal(ERROR_INVALID_ADDRESS, "First-party dependency export has no genuine executable image owner.");
    return actual;
}
ImportedProvider* import_provider_for(HMODULE image) {
    if (!state || !state->import_providers || !image) return nullptr;
    for (const auto& provider : state->import_providers->providers)
        if (provider->image == image && provider->bound && !provider->loader_released) return provider.get();
    return nullptr;
}
bool import_provider_original_entry(HMODULE image, const std::string& library, const std::string& name, void* actual) {
    auto* provider = import_provider_for(image); if (!provider) return false;
    if (provider->library != library || import_provider_export(*provider, name) != actual)
        throw Fatal(ERROR_INVALID_DATA, "Original import differs from its retained first-party dependency export.");
    std::array<wchar_t, 32768> path{};
    const auto length = GetModuleFileNameW(image, path.data(), static_cast<DWORD>(path.size()));
    if (!length || length >= path.size()) throw Fatal(GetLastError(), "First-party mapped dependency lost its complete file identity.");
    crt_require_provider_file(provider->path, path.data()); return true;
}
void import_providers_prepare(const Frame& frame) {
    Reader reader{frame.payload}; const auto original = path_from_utf8(reader.text()); const auto original_sha = reader.text();
    const auto count = reader.get<std::uint32_t>();
    if (state->import_providers || state->module || state->nvse_image_attempted || !state->io || !state->calls.empty() || count != 1 || original_sha.size() != 64)
        throw Fatal(ERROR_INVALID_STATE, "Original dependency preparation lacks a unique pre-entry selected source.");
    auto owner = std::make_unique<ImportProvidersRuntime>(); owner->original_path = original; owner->original_sha = original_sha;
    owner->attempt = ++state->next_module;
    if (!owner->attempt || owner->attempt >= nvse_invalid_handle) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Original loader attempt identity exhausted.");
    auto provider = std::make_unique<ImportedProvider>(); provider->kind = reader.get<std::uint32_t>();
    provider->library = reader.text(); provider->path = path_from_utf8(reader.text()); provider->sha = reader.text();
    if (provider->kind != import_provider_cng || provider->library != "bcrypt.dll" || provider->sha.size() != 64)
        throw Fatal(ERROR_NOT_SUPPORTED, "Selected dependency has no first-party public ABI owner.");
    const auto export_count = reader.get<std::uint32_t>();
    if (export_count != 10) throw Fatal(ERROR_INVALID_DATA, "First-party CNG dependency has incomplete source exports.");
    std::set<std::string> names;
    for (std::uint32_t at = 0; at < export_count; ++at) {
        const auto name = reader.text(); const auto rva = reader.get<std::uint32_t>();
        if (!rva || !names.insert(name).second ||
            (!cng_client_entry(name) && name != "OpenNVImportProviderBind" && name != "OpenNVImportProviderInspect" && name != "OpenNVImportProviderUnbind"))
            throw Fatal(ERROR_INVALID_DATA, "First-party dependency declaration contains a duplicate or unowned export.");
        provider->exports.push_back({name, rva});
    }
    const auto slot_count = reader.get<std::uint32_t>();
    if (!slot_count || slot_count > max_payload / 8) throw Fatal(ERROR_INVALID_DATA, "Original dependency has no bounded complete winning import slots.");
    std::set<std::uint32_t> slots;
    for (std::uint32_t at = 0; at < slot_count; ++at) {
        const auto name = reader.text(); const auto rva = reader.get<std::uint32_t>();
        if (!rva || !slots.insert(rva).second || !cng_client_entry(name))
            throw Fatal(ERROR_NOT_SUPPORTED, "Original dependency slot has no exact source-backed CNG ABI owner.");
        provider->slots.push_back({name, rva});
    }
    reader.finish(); owner->providers.push_back(std::move(provider)); state->import_providers = std::move(owner);
    auto& retained = *state->import_providers->providers.front();
    // Windows' loaded-module list owns ordinary named dependency resolution.
    // An existing competing image is refused; it is never unloaded or replaced.
    if (GetModuleHandleW(L"bcrypt.dll")) throw Fatal(ERROR_ALREADY_EXISTS, "A competing mapped dependency prevents exact first-party source binding.");
    retained.image = LoadLibraryExW(retained.path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!retained.image) throw Fatal(GetLastError(), "First-party import dependency failed genuine Windows loader entry.");
    std::array<wchar_t, 32768> mapped{}; const auto mapped_length = GetModuleFileNameW(retained.image, mapped.data(), static_cast<DWORD>(mapped.size()));
    if (!mapped_length || mapped_length >= mapped.size() || GetModuleHandleW(L"bcrypt.dll") != retained.image)
        throw Fatal(ERROR_INVALID_DATA, "First-party dependency was not registered as its actual imported basename.");
    crt_require_provider_file(retained.path, mapped.data());
    retained.bind = reinterpret_cast<ImportProviderBind>(import_provider_export(retained, "OpenNVImportProviderBind"));
    retained.inspect = reinterpret_cast<ImportProviderInspect>(import_provider_export(retained, "OpenNVImportProviderInspect"));
    retained.unbind = reinterpret_cast<ImportProviderUnbind>(import_provider_export(retained, "OpenNVImportProviderUnbind"));
    CngImportProviderBridge bridge{import_provider_abi, sizeof(CngImportProviderBridge), state->generation, state->thread, import_provider_cng,
        cng_client_open, cng_client_property, cng_client_create, cng_client_data, cng_client_finish, cng_client_destroy, cng_client_close};
    if (!retained.bind(&bridge)) throw Fatal(GetLastError(), "First-party dependency did not bind actual callable CNG owner thunks.");
    retained.bound = true; Bytes data; data.put(state->import_providers->attempt); data.put(count); import_provider_bytes(data, retained);
    reply(frame, ERROR_SUCCESS, "", data);
}
void import_providers_begin_original(const Frame& frame, const std::wstring& path) {
    if (!state->import_providers) return;
    auto& owner = *state->import_providers;
    if (owner.phase != OriginalLoaderPhase::prepared || state->module_id || !state->calls.empty())
        throw Fatal(ERROR_INVALID_STATE, "Original loader attempt was replayed or has an active foreign call.");
    crt_require_provider_file(owner.original_path, path.c_str());
    owner.phase = OriginalLoaderPhase::entering; owner.entered_call = frame.header.id;
    state->module_id = owner.attempt; state->calls.push_back(frame.header.id);
}
void import_providers_end_original(const Frame& frame, bool mapped) {
    if (!state->import_providers) return;
    auto& owner = *state->import_providers;
    if (owner.phase != OriginalLoaderPhase::entering || owner.entered_call != frame.header.id ||
        state->calls.size() != 1 || state->calls.back() != frame.header.id || state->module_id != owner.attempt)
        throw Fatal(ERROR_INVALID_STATE, "Original loader lost its entered source/callback frame.");
    state->calls.pop_back(); owner.phase = mapped ? OriginalLoaderPhase::mapped : OriginalLoaderPhase::failed;
    if (!mapped) state->module_id = 0;
}
void import_providers_validate_original(HMODULE image) {
    if (!state->import_providers) return;
    auto& owner = *state->import_providers;
    if (owner.phase != OriginalLoaderPhase::mapped || !image || state->module_id != owner.attempt)
        throw Fatal(ERROR_INVALID_STATE, "Original IAT source validation precedes genuine loader admission.");
    std::array<wchar_t, 32768> mapped{}; const auto length = GetModuleFileNameW(image, mapped.data(), static_cast<DWORD>(mapped.size()));
    if (!length || length >= mapped.size()) throw Fatal(GetLastError(), "Original mapped image has no complete actual filename.");
    crt_require_provider_file(owner.original_path, mapped.data());
    const auto* base = reinterpret_cast<const std::uint8_t*>(image); const auto dos = nvse_copy(reinterpret_cast<const IMAGE_DOS_HEADER*>(base));
    if (dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew <= 0)
        throw Fatal(ERROR_BAD_EXE_FORMAT, "Original dependency has a malformed actual DOS header.");
    const auto nt = nvse_copy(reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos.e_lfanew));
    const auto extent = nt.OptionalHeader.SizeOfImage; const auto& directory = nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (nt.Signature != IMAGE_NT_SIGNATURE ||
        nt.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC || !directory.Size || !directory.VirtualAddress ||
        directory.VirtualAddress > extent || directory.Size > extent - directory.VirtualAddress)
        throw Fatal(ERROR_BAD_EXE_FORMAT, "Original dependency IAT has no complete source image envelope.");
    bool ended = false; std::set<std::uint32_t> seen;
    for (std::uint32_t offset = 0; offset + sizeof(IMAGE_IMPORT_DESCRIPTOR) <= directory.Size; offset += sizeof(IMAGE_IMPORT_DESCRIPTOR)) {
        const auto row = nvse_copy(reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + directory.VirtualAddress + offset));
        if (!row.Name && !row.FirstThunk && !row.OriginalFirstThunk) { ended = true; break; }
        if (!row.Name || row.Name >= extent || !row.FirstThunk || row.FirstThunk >= extent || !row.OriginalFirstThunk || row.OriginalFirstThunk >= extent)
            throw Fatal(ERROR_BAD_EXE_FORMAT, "Original dependency import descriptor has incomplete source extents.");
        auto library = nvse_string(reinterpret_cast<const char*>(base + row.Name), false);
        for (auto& character : library) if (character >= 'A' && character <= 'Z') character = static_cast<char>(character + ('a' - 'A'));
        for (const auto& item : owner.providers) {
            auto& provider = *item; if (library != provider.library) continue; bool closed = false;
            for (std::uint32_t at = 0; at < extent / 4; ++at) {
                const auto delta = static_cast<std::uint64_t>(at) * 4;
                if (delta + 4 > extent - row.FirstThunk || delta + 4 > extent - row.OriginalFirstThunk)
                    throw Fatal(ERROR_BAD_EXE_FORMAT, "Original dependency thunk exceeds its actual image.");
                const auto original = nvse_copy(reinterpret_cast<const IMAGE_THUNK_DATA32*>(base + row.OriginalFirstThunk + static_cast<std::size_t>(delta)));
                if (!original.u1.AddressOfData) { closed = true; break; }
                if (IMAGE_SNAP_BY_ORDINAL32(original.u1.Ordinal) || original.u1.AddressOfData >= extent - 2)
                    throw Fatal(ERROR_NOT_SUPPORTED, "Original dependency has an unowned ordinal/name layout.");
                const auto name = nvse_string(reinterpret_cast<const char*>(base + original.u1.AddressOfData + 2), false);
                const auto slot = row.FirstThunk + static_cast<std::uint32_t>(delta);
                const auto source = std::find_if(provider.slots.begin(), provider.slots.end(), [&](const auto& expected) { return expected.rva == slot && expected.name == name; });
                if (source == provider.slots.end() || !seen.insert(slot).second)
                    throw Fatal(ERROR_INVALID_DATA, "Original dependency slot/name differs from its retained winning declaration.");
                const auto actual = nvse_copy(reinterpret_cast<void* const*>(base + slot));
                if (!import_provider_original_entry(provider.image, library, name, actual))
                    throw Fatal(ERROR_INVALID_DATA, "Windows did not bind the original IAT to its selected actual first-party dependency.");
            }
            if (!closed) throw Fatal(ERROR_BAD_EXE_FORMAT, "Original dependency thunk lacks its complete source terminator.");
        }
    }
    std::size_t expected = 0; for (const auto& provider : owner.providers) expected += provider->slots.size();
    if (!ended || seen.size() != expected) throw Fatal(ERROR_INVALID_DATA, "Original IAT omits declared dependency slots or its final descriptor.");
}
void import_providers_retire(const Frame& frame) {
    if (!state->import_providers || state->module || !state->calls.empty())
        throw Fatal(ERROR_BUSY, "Original module/call lifetime prevents first-party dependency retirement.");
    auto& owner = *state->import_providers;
    if (owner.phase == OriginalLoaderPhase::entering || owner.phase == OriginalLoaderPhase::retired)
        throw Fatal(ERROR_INVALID_STATE, "Original dependency retirement is active or replayed.");
    cng_client_require_retired();
    for (const auto& item : owner.providers) {
        auto& provider = *item; import_provider_snapshot(provider);
        if (!provider.unbind(state->generation)) throw Fatal(GetLastError(), "First-party dependency could not release its actual callable bridge.");
        provider.bound = false; import_provider_snapshot(provider);
        const auto actual = provider.image;
        if (!FreeLibrary(actual)) throw Fatal(GetLastError(), "First-party dependency Windows loader reference did not retire.");
        provider.loader_released = true;
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(actual, &memory, sizeof(memory)) || memory.State != MEM_FREE)
            throw Fatal(ERROR_BUSY, "First-party dependency remains mapped after its real loader release.");
        provider.image = nullptr;
    }
    owner.phase = OriginalLoaderPhase::retired; Bytes data; data.put(owner.attempt); data.put(static_cast<std::uint32_t>(owner.providers.size()));
    for (const auto& provider : owner.providers) import_provider_bytes(data, *provider);
    reply(frame, ERROR_SUCCESS, "", data);
}
void import_providers_operation(const Frame& frame) {
    Reader reader{frame.payload}; const auto step = reader.get<std::uint32_t>();
    if (step == 1) {
        Frame payload = frame; payload.payload.assign(frame.payload.begin() + static_cast<std::ptrdiff_t>(reader.at), frame.payload.end());
        import_providers_prepare(payload); return;
    }
    reader.finish();
    if (step == 3) { import_providers_retire(frame); return; }
    if (step != 2 || !state->import_providers || !state->calls.empty()) throw Fatal(ERROR_INVALID_STATE, "First-party dependency inspection lacks its current idle owner.");
    Bytes data; data.put(state->import_providers->attempt); data.put(static_cast<std::uint32_t>(state->import_providers->providers.size()));
    for (const auto& provider : state->import_providers->providers) import_provider_bytes(data, *provider);
    reply(frame, ERROR_SUCCESS, "", data);
}
void import_providers_require_retired() {
    if (state->import_providers && state->import_providers->phase != OriginalLoaderPhase::retired)
        throw Fatal(ERROR_BUSY, "First-party import dependencies still own mapped/callable resources.");
}
