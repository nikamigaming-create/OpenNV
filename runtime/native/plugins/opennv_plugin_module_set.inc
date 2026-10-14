void nvse_module_set_load(const Frame& frame, Reader& reader) {
    if (!state->calls.empty() || !state->nvse || state->loading_handle)
        throw Fatal(ERROR_BUSY, "Additional original loading overlaps a native call or source loader context.");
    nvse_module_set_adopt_primary();
    const auto path = path_from_utf8(reader.text()); const auto handle = reader.get<std::uint32_t>();
    const auto count = reader.get<std::uint32_t>();
    if (!handle || handle == nvse_invalid_handle || count > max_payload / 12)
        throw Fatal(ERROR_INVALID_DATA, "Additional original loader declaration has no bounded handle/import extent.");
    struct Import { std::string library, name; std::uint32_t slot; };
    std::vector<Import> imports;
    for (std::uint32_t at = 0; at < count; ++at) {
        Import declaration{reader.text(), reader.text(), reader.get<std::uint32_t>()};
        if (declaration.library.empty() || declaration.name.empty() || !declaration.slot)
            throw Fatal(ERROR_INVALID_DATA, "Additional original import lost its named source IAT slot.");
        imports.push_back(std::move(declaration));
    }
    reader.finish();
    for (const auto& item : state->module_set->modules)
        if (!item.second->retired && item.second->runtime->handle == handle)
            throw Fatal(ERROR_INVALID_HANDLE, "Additional original source handle aliases another live module.");
    auto module = std::make_unique<NvseModuleRecord>();
    if (state->next_module == UINT32_MAX) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Original module identity allocation exhausted.");
    module->id = ++state->next_module; module->supplemental = true;
    module->runtime = std::make_shared<NvseRuntime>(); auto& runtime = *module->runtime;
    runtime.tables = state->module_set->shared_tables; runtime.tables_extent = state->module_set->shared_tables_extent;
    runtime.owns_tables = false; runtime.directory = state->nvse->directory;
    runtime.logging_path = state->nvse->logging_path; runtime.logging_path_length = state->nvse->logging_path_length;
    runtime.handle = handle; runtime.interface_budget = state->nvse->interface_budget;
    LoaderInvocation loader;
    const auto actual = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    const auto error = actual ? ERROR_SUCCESS : GetLastError(); loader.finish();
    if (!actual) { reply(frame, error, loader.refusal("Additional original Windows DLL admission failed.")); return; }
    module->image = actual;
    const auto id = module->id;
    auto& set = *state->module_set;
    if (set.images.count(address(actual))) {
        // This source is already mapped; discard only this new loader reference.
        if (!FreeLibrary(actual)) throw Fatal(GetLastError(), "Duplicate original loader reference did not retire.");
        throw Fatal(ERROR_ALREADY_EXISTS, "Additional original image aliases a retained module rather than constructing a new module.");
    }
    set.images.emplace(address(actual), module.get()); set.modules.emplace(id, std::move(module)); ++set.live;
    NvseExecutionScope entered(id);
    const auto dos = nvse_copy(reinterpret_cast<const IMAGE_DOS_HEADER*>(actual));
    if (dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew <= 0) throw Fatal(ERROR_BAD_EXE_FORMAT, "Additional original mapped DOS header changed.");
    const auto nt = nvse_copy(reinterpret_cast<const IMAGE_NT_HEADERS32*>(reinterpret_cast<const unsigned char*>(actual) + dos.e_lfanew));
    if (nt.Signature != IMAGE_NT_SIGNATURE || nt.FileHeader.Machine != IMAGE_FILE_MACHINE_I386 || nt.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC)
        throw Fatal(ERROR_BAD_EXE_FORMAT, "Additional original mapping lost its x86 PE declaration.");
    std::uint32_t bound_reads = 0;
    for (const auto& declaration : imports) {
        if (declaration.slot > nt.OptionalHeader.SizeOfImage || 4U > nt.OptionalHeader.SizeOfImage - declaration.slot)
            throw Fatal(ERROR_INVALID_ADDRESS, "Additional original IAT leaves its mapped source image.");
        const auto library = io_from_utf8(declaration.library);
        const auto provider = GetModuleHandleW(library.c_str());
        const auto original = provider ? reinterpret_cast<void*>(GetProcAddress(provider, declaration.name.c_str())) : nullptr;
        auto** slot = reinterpret_cast<void**>(reinterpret_cast<unsigned char*>(actual) + declaration.slot);
        if (!provider || !original || nvse_copy(slot) != original)
            throw Fatal(ERROR_INVALID_DATA, "Additional original IAT is not its actual Windows named export.");
        // Source-code mutations retain their real existing typed refusal.
        // An additional DLL never receives a raw VirtualProtect escape.
        auto* adapter = shared_file_size_import_entry(declaration.library, declaration.name, original);
        if (!adapter) {
            adapter = module_read_io_import_entry(declaration.library, declaration.name, original);
            if (adapter) ++bound_reads;
        }
        if (!adapter) adapter = source_call_site_api(declaration.name);
        if (adapter) {
            DWORD previous = 0;
            if (!VirtualProtect(slot, sizeof(void*), PAGE_READWRITE, &previous))
                throw Fatal(GetLastError(), "Additional source protection adapter could not enter its IAT store.");
            *slot = adapter; DWORD ignored = 0;
            if (!VirtualProtect(slot, sizeof(void*), previous, &ignored))
                throw Fatal(GetLastError(), "Additional source protection adapter did not restore its original IAT page.");
            if (nvse_copy(slot) != adapter)
                throw Fatal(ERROR_INVALID_DATA, "Additional original API adapter lost its actual IAT readback.");
        }
    }
    runtime.query = address(reinterpret_cast<void*>(GetProcAddress(actual, "NVSEPlugin_Query")));
    runtime.load = address(reinterpret_cast<void*>(GetProcAddress(actual, "NVSEPlugin_Load")));
    nvse_executable(reinterpret_cast<void*>(runtime.query)); nvse_executable(reinterpret_cast<void*>(runtime.load));
    Bytes result; result.put(id); result.put(address(actual)); result.put(handle);
    result.put(address(runtime.tables)); result.put(runtime.query); result.put(runtime.load); result.put(bound_reads);
    reply(frame, ERROR_SUCCESS, "", result);
}

void nvse_module_set_operation(const Frame& frame) {
    Reader reader{frame.payload}; const auto action = reader.get<std::uint32_t>();
    if (action == 0) { nvse_module_set_load(frame, reader); return; }
    if (action == 1) nvse_module_set_adopt_primary();
    if (!state->module_set || !state->calls.empty() || state->loading_handle)
        throw Fatal(ERROR_BUSY, "Cross-image callable operation has no idle actual module-set lifetime.");
    auto& owner = *state->module_set;
    if (action == 1) {
        const auto caller = reader.get<std::uint64_t>(), provider = reader.get<std::uint64_t>();
        const auto name = reader.text(); const auto rva = reader.get<std::uint32_t>();
        const auto abi = static_cast<Abi>(reader.get<std::uint32_t>()); const auto words = reader.get<std::uint32_t>(); reader.finish();
        auto& caller_module = nvse_module_record(caller); auto& provider_module = nvse_module_record(provider);
        if (!rva || words > 4 || (abi != Abi::cdecl_call && abi != Abi::stdcall_call && abi != Abi::thiscall_call) ||
            caller_module.runtime->phase != NvsePhase::loaded_true || provider_module.runtime->phase != NvsePhase::loaded_true ||
            provider_module.borrows == UINT32_MAX || caller_module.borrows == UINT32_MAX || owner.next_borrow == UINT64_MAX)
            throw Fatal(ERROR_INVALID_DATA, "Original cross-image function lost its entered declaration/ABI/module phase.");
        const auto sum = static_cast<std::uint64_t>(address(provider_module.image)) + rva;
        if (sum > UINT32_MAX) throw Fatal(ERROR_INVALID_ADDRESS, "Original function address wraps its actual x86 source image.");
        const auto function = name.empty() ? reinterpret_cast<void*>(static_cast<std::uintptr_t>(sum)) :
            reinterpret_cast<void*>(GetProcAddress(provider_module.image, name.c_str()));
        NvseExecutionScope scope(provider); nvse_executable(function);
        if (static_cast<std::uint64_t>(address(provider_module.image)) + rva != address(function))
            throw Fatal(ERROR_INVALID_DATA, "Original cross-image function changed its selected source export RVA.");
        const auto id = ++owner.next_borrow;
        owner.callables.emplace(id, NvseCallableBorrow{id, caller, provider, address(function), rva, words, abi, true, false});
        ++provider_module.borrows;
        if (caller != provider) ++caller_module.borrows;
        Bytes result; result.put(id); result.put(address(function)); reply(frame, ERROR_SUCCESS, "", result); return;
    }
    const auto id = reader.get<std::uint64_t>();
    const auto found = owner.callables.find(id);
    if (found == owner.callables.end() || !found->second.live || found->second.entered)
        throw Fatal(ERROR_INVALID_HANDLE, "Original cross-image function is stale, retired or still entered.");
    auto& function = found->second;
    auto& caller = nvse_module_record(function.caller); auto& provider = nvse_module_record(function.provider);
    if (action == 3) {
        reader.finish(); --provider.borrows; if (function.caller != function.provider) --caller.borrows; owner.callables.erase(found);
        Bytes result; result.put(1U); reply(frame, ERROR_SUCCESS, "", result); return;
    }
    if (action != 2) throw Fatal(ERROR_INVALID_FUNCTION, "Unknown actual module-set callable operation.");
    const auto receiver_id = reader.get<std::uint64_t>();
    const auto receiver = reader.get<std::uint32_t>(), receiver_extent = reader.get<std::uint32_t>();
    const auto count = reader.get<std::uint32_t>();
    std::array<std::uint32_t, 4> words{};
    if (count != function.words || count > words.size() || (function.abi == Abi::thiscall_call) != (receiver != 0))
        throw Fatal(ERROR_INVALID_DATA, "Original function invocation changed its declared receiver/raw-word ABI.");
    for (std::uint32_t at = 0; at < count; ++at) words[at] = reader.get<std::uint32_t>(); reader.finish();
    if (receiver) {
        const auto actual = state->arena.inspect(receiver_id);
        if (address(actual.base) != receiver || actual.length != receiver_extent)
            throw Fatal(ERROR_INVALID_DATA, "Original receiver changed its actual retained native allocation/extent.");
    } else if (receiver_id || receiver_extent) throw Fatal(ERROR_INVALID_DATA, "Receiver-free original call carries a foreign native allocation.");
    if (caller.runtime->phase != NvsePhase::loaded_true || provider.runtime->phase != NvsePhase::loaded_true)
        throw Fatal(ERROR_INVALID_STATE, "Original function invocation escaped its completed actual module loader phases.");
    NvseExecutionScope caller_scope(caller.id); NvseExecutionScope provider_scope(provider.id);
    nvse_executable(reinterpret_cast<void*>(function.pointer)); if (receiver) nvse_readable(reinterpret_cast<void*>(receiver), 1);
    function.entered = true; state->transaction_callbacks = 0;
    caller.runtime->interface_calls = 0; provider.runtime->interface_calls = 0;
    state->calls.push_back(frame.header.id); CallReceipt receipt{};
    try {
        NvseInvocation invocation(*provider.runtime);
        InvokeNvseModuleFunctionGuarded(reinterpret_cast<void*>(function.pointer), function.abi, reinterpret_cast<void*>(receiver), words.data(), count, &receipt);
        nvse_check_call(receipt, "original cross-image callable", function.pointer);
        state->calls.pop_back(); function.entered = false;
        Bytes result; nvse_call_bytes(result, receipt); reply(frame, ERROR_SUCCESS, "", result);
    } catch (...) { state->calls.pop_back(); throw; }
}
