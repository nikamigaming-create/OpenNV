std::unique_ptr<EngineGlobals> engine_globals;

EngineGlobal& source_global(std::uint64_t id) {
    if (!engine_globals || engine_globals->failed) throw Fatal(ERROR_INVALID_STATE, "Engine globals retain an absent/failed actual owner.");
    for (auto& value : engine_globals->cells) if (value->id == id && value->published && !value->retired && !value->failed) {
        original_callable_pages->require_global(value->lease, value->address, value->value);
        if (value->kind == 2 && source_object(value->object).pointer != value->value)
            throw Fatal(ERROR_INVALID_DATA, "Engine global lost its complete living native source object.");
        return *value;
    }
    throw Fatal(ERROR_INVALID_HANDLE, "Engine global capability is unknown, uncommitted or retired.");
}
void* engine_tls_export(HMODULE image, const char* name) {
    const auto pointer = GetProcAddress(image, name);
    if (!pointer) throw Fatal(GetLastError(), "Actual first-party static TLS carrier lacks its complete ABI export.");
    MEMORY_BASIC_INFORMATION code{};
    if (!VirtualQuery(reinterpret_cast<void*>(pointer), &code, sizeof(code)) || code.AllocationBase != image ||
        code.Type != MEM_IMAGE || code.State != MEM_COMMIT || !(code.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)))
        throw Fatal(ERROR_INVALID_DATA, "Static TLS carrier export is not code in its actual retained first-party image.");
    return reinterpret_cast<void*>(pointer);
}
void engine_tls_prepare(const Frame& frame, Reader& request) {
    if (engine_globals->tls.entered) throw Fatal(ERROR_INVALID_STATE, "Static TLS carrier preparation cannot replay an entered lifetime.");
    auto& tls = engine_globals->tls; tls.path = path_from_utf8(request.text()); tls.source_extent = request.get<std::uint32_t>();
    const auto initial = request.bytes(tls.source_extent); request.finish();
    if (!tls.source_extent || tls.source_extent > 65536 || tls.path.empty())
        throw Fatal(ERROR_INVALID_DATA, "Static TLS carrier has no complete bounded source-initial image/path.");
    tls.entered = true;
    try {
        tls.image = LoadLibraryExW(io_native_path(tls.path).c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        const auto load_error = GetLastError();
        if (!tls.image) throw Fatal(load_error, "Actual first-party static TLS carrier Windows entry failed.");
        std::vector<wchar_t> mapped(32768);
        const auto copied = GetModuleFileNameW(tls.image, mapped.data(), static_cast<DWORD>(mapped.size()));
        if (!copied || copied >= mapped.size()) throw Fatal(GetLastError(), "Static TLS carrier mapping identity query failed.");
        crt_require_provider_file(tls.path, mapped.data());
        using Number = std::uint32_t(__cdecl*)(); using Pointer = void* (__cdecl*)();
        const auto version = reinterpret_cast<Number>(engine_tls_export(tls.image, "OpenNVEngineTlsVersion"));
        const auto index = reinterpret_cast<Number>(engine_tls_export(tls.image, "OpenNVEngineTlsIndex"));
        const auto extent = reinterpret_cast<Number>(engine_tls_export(tls.image, "OpenNVEngineTlsExtent"));
        const auto pointer = reinterpret_cast<Pointer>(engine_tls_export(tls.image, "OpenNVEngineTlsData"));
        if (version() != 1) throw Fatal(ERROR_REVISION_MISMATCH, "Static TLS carrier does not declare the admitted first-party ABI.");
        const auto dos = nvse_copy(reinterpret_cast<const IMAGE_DOS_HEADER*>(tls.image));
        if (dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0 || dos.e_lfanew > 1024 * 1024)
            throw Fatal(ERROR_INVALID_DATA, "Static TLS carrier has no complete actual PE image header.");
        const auto nt = nvse_copy(reinterpret_cast<const IMAGE_NT_HEADERS32*>(address(tls.image) + static_cast<std::uint32_t>(dos.e_lfanew)));
        const auto directory = nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS];
        if (nt.Signature != IMAGE_NT_SIGNATURE || nt.FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
            nt.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC || directory.Size != sizeof(IMAGE_TLS_DIRECTORY32) ||
            !directory.VirtualAddress || static_cast<std::uint64_t>(directory.VirtualAddress) + directory.Size > nt.OptionalHeader.SizeOfImage)
            throw Fatal(ERROR_INVALID_DATA, "Static TLS carrier has no complete actual Windows TLS-directory extent.");
        const auto declaration = nvse_copy(reinterpret_cast<const IMAGE_TLS_DIRECTORY32*>(address(tls.image) + directory.VirtualAddress));
        const auto raw = GetProcAddress(tls.image, "OpenNVEngineTlsRawData");
        if (!raw) throw Fatal(GetLastError(), "Static TLS carrier has no actual PE raw-data symbol declaration.");
        const auto raw_address = address(raw);
        if (raw_address < declaration.StartAddressOfRawData || raw_address >= declaration.EndAddressOfRawData)
            throw Fatal(ERROR_INVALID_DATA, "Static TLS raw-data symbol is outside its actual loader template.");
        const auto live_data = address(pointer()); const auto offset = raw_address - declaration.StartAddressOfRawData;
        if (live_data < offset) throw Fatal(ERROR_INVALID_DATA, "Static TLS live variable cannot join its actual raw-template displacement.");
        tls.index = index(); tls.extent = extent(); tls.pointer = reinterpret_cast<void*>(live_data - offset);
        if (!tls.pointer || tls.index == TLS_OUT_OF_INDEXES || tls.extent < tls.source_extent || tls.extent > 131072)
            throw Fatal(ERROR_INVALID_DATA, "Static TLS carrier has no genuine complete current-thread loader allocation/index.");
        auto cursor = static_cast<std::uint64_t>(address(tls.pointer)); const auto end = cursor + tls.source_extent;
        if (end > (1ULL << 32)) throw Fatal(ERROR_INVALID_DATA, "Current-thread static TLS extent leaves x86 memory.");
        while (cursor < end) {
            MEMORY_BASIC_INFORMATION memory{};
            if (!VirtualQuery(reinterpret_cast<void*>(static_cast<std::uint32_t>(cursor)), &memory, sizeof(memory)) ||
                memory.State != MEM_COMMIT || memory.Type != MEM_PRIVATE || memory.Protect != PAGE_READWRITE ||
                reinterpret_cast<std::uintptr_t>(memory.BaseAddress) > cursor)
                throw Fatal(ERROR_INVALID_DATA, "Current-thread static TLS bytes are outside their actual writable Windows allocation.");
            const auto next = static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(memory.BaseAddress)) + memory.RegionSize;
            if (next <= cursor) throw Fatal(ERROR_INVALID_DATA, "Current-thread static TLS region has no advancing complete extent.");
            cursor = (std::min)(next, end);
        }
        std::memcpy(tls.pointer, initial, tls.source_extent); tls.copied = true;
        Bytes receipt; receipt.put(address(tls.image)); receipt.put(tls.index); receipt.put(address(tls.pointer)); receipt.put(tls.extent); receipt.put(tls.source_extent); receipt.put(load_error); receipt.put(0U);
        if (nvse_host(NvseHostCall::engine_tls_publish, receipt) != 1)
            throw Fatal(ERROR_INVALID_DATA, "Source TLS prefix lacks its correlated C# source/actual native readback.");
        Bytes result; result.put(address(tls.image)); result.put(tls.index); result.put(address(tls.pointer)); result.put(tls.extent);
        reply(frame, ERROR_SUCCESS, "", result);
    }
    catch (...) { tls.failed = engine_globals->failed = true; throw; }
}
void engine_global_publish(const Frame& frame, Reader& request) {
    auto value = std::make_unique<EngineGlobal>(); value->id = request.get<std::uint64_t>(); value->address = request.get<std::uint32_t>();
    value->kind = request.get<std::uint32_t>(); value->value = request.get<std::uint32_t>(); value->object = request.get<std::uint64_t>(); request.finish();
    if (!value->id || value->kind < 1 || value->kind > 3 || engine_globals->cells.size() >= 4096 ||
        std::any_of(engine_globals->cells.begin(), engine_globals->cells.end(), [&](const auto& row) { return row->id == value->id || row->address == value->address; }))
        throw Fatal(ERROR_INVALID_DATA, "Source global declaration repeats an unknown/retired identity or exceeds its lifetime budget.");
    if (value->kind == 2) {
        if (!value->object || source_object(value->object).pointer != value->value)
            throw Fatal(ERROR_INVALID_HANDLE, "Pointer global lacks its actual complete native source object.");
    }
    else if (value->object || (value->kind == 3 && (!engine_globals->tls.copied || engine_globals->tls.retired || value->value != engine_globals->tls.index)))
        throw Fatal(ERROR_INVALID_DATA, "Scalar/TLS global has a foreign object or counterfeit static TLS index.");
    engine_globals->cells.push_back(std::move(value)); auto& retained = *engine_globals->cells.back();
    try {
        retained.lease = original_callable_pages->acquire_global(retained.address, retained.value); retained.published = true;
        source_global(retained.id); Bytes receipt;
        receipt.put(retained.id); receipt.put(retained.address); receipt.put(retained.kind); receipt.put(retained.value); receipt.put(retained.lease);
        if (nvse_host(NvseHostCall::engine_global_publish, receipt) != 1)
            throw Fatal(ERROR_INVALID_DATA, "Source global publication lacks its exact C# field/readback owner.");
        Bytes result; result.put(retained.id); result.put(retained.lease); reply(frame, ERROR_SUCCESS, "", result);
    }
    catch (...) { retained.failed = engine_globals->failed = true; throw; }
}
void engine_tls_current(const Frame& frame, Reader& request) {
    auto& tls = engine_globals->tls; const auto count = request.get<std::uint32_t>();
    const auto previous = request.bytes(count); const auto current = request.bytes(count); request.finish();
    if (!tls.image || !tls.copied || tls.retired || tls.failed || count != tls.source_extent || !count)
        throw Fatal(ERROR_INVALID_STATE, "Current static TLS lacks its actual retained carrier/template lifetime.");
    try {
        std::vector<std::uint8_t> observed(count); SIZE_T read = 0;
        if (!ReadProcessMemory(GetCurrentProcess(), tls.pointer, observed.data(), count, &read) || read != count ||
            std::memcmp(observed.data(), previous, count) != 0)
            throw Fatal(ERROR_INVALID_DATA, "Current static TLS changed outside its genuine prior source publication.");
        std::memcpy(tls.pointer, current, count);
        Bytes receipt; receipt.put(address(tls.image)); receipt.put(tls.index); receipt.put(address(tls.pointer));
        receipt.put(tls.extent); receipt.put(tls.source_extent); receipt.put(0U); receipt.put(1U);
        if (nvse_host(NvseHostCall::engine_tls_publish, receipt) != 1)
            throw Fatal(ERROR_INVALID_DATA, "Current static TLS lacks complete correlated authoritative field readback.");
        Bytes result; result.put(address(tls.image)); result.put(tls.index); result.put(address(tls.pointer)); result.put(tls.extent);
        reply(frame, ERROR_SUCCESS, "", result);
    }
    catch (...) { tls.failed = engine_globals->failed = true; throw; }
}
void source_globals_operation(const Frame& frame) {
    Reader request{frame.payload}; const auto module = request.get<std::uint64_t>(); const auto action = request.get<std::uint32_t>();
    if (!state->nvse || state->nvse->phase != NvsePhase::loaded_true || module != state->module_id || !state->calls.empty() || !original_callable_pages)
        throw Fatal(ERROR_INVALID_STATE, "Engine globals need their actual empty loaded module/thread/source-image owner.");
    if (!engine_globals) engine_globals = std::make_unique<EngineGlobals>();
    if (engine_globals->failed) throw Fatal(ERROR_INVALID_STATE, "Engine globals retain an entered failure.");
    state->calls.push_back(frame.header.id);
    try {
        NvseInvocation invocation(*state->nvse);
        if (action == 0) engine_tls_prepare(frame, request);
        else if (action == 1) engine_global_publish(frame, request);
        else if (action == 2) engine_tls_current(frame, request);
        else throw Fatal(ERROR_NOT_SUPPORTED, "Engine global operation requires its other actual source/state consumer.");
        state->calls.pop_back();
    }
    catch (...) { state->calls.pop_back(); throw; }
}
void source_globals_retire(const Frame& frame) {
    if (!engine_globals) return;
    if (!state->calls.empty() || engine_globals->failed)
        throw Fatal(ERROR_BUSY, "Engine global retirement retains an active/failed original lifetime.");
    state->calls.push_back(frame.header.id);
    try {
        for (auto& value : engine_globals->cells) {
            if (value->retired) continue;
            source_global(value->id); original_callable_pages->release_global(value->lease); value->retired = true;
            Bytes receipt; receipt.put(value->id); receipt.put(value->address); receipt.put(value->kind); receipt.put(value->value); receipt.put(value->lease);
            if (nvse_host(NvseHostCall::engine_global_retire, receipt) != 1)
                throw Fatal(ERROR_INVALID_DATA, "Engine global retirement has no independent native zero-cell readback.");
        }
        auto& tls = engine_globals->tls;
        if (tls.image && !tls.retired) {
            const auto image = tls.image; const auto result = FreeLibrary(image); const auto last = GetLastError();
            MEMORY_BASIC_INFORMATION memory{}; const auto observed = VirtualQuery(image, &memory, sizeof(memory));
            Bytes receipt; receipt.put(address(image)); receipt.put(tls.index); receipt.put(address(tls.pointer));
            receipt.put(static_cast<std::uint32_t>(result)); receipt.put(last); receipt.put(static_cast<std::uint32_t>(observed)); receipt.put(memory.State);
            if (nvse_host(NvseHostCall::engine_tls_retire, receipt) != 1)
                throw Fatal(ERROR_INVALID_DATA, "Static TLS carrier actual unload lacks its source/Windows receipt.");
            if (!result || !observed || memory.State != MEM_FREE)
                throw Fatal(result ? ERROR_INVALID_DATA : last, "Static TLS carrier still retains its real loader reference/image after retirement.");
            tls.image = nullptr; tls.retired = true;
        }
        state->calls.pop_back();
    }
    catch (...) { engine_globals->failed = true; state->calls.pop_back(); throw; }
}
