std::uint64_t file_size_ordinal = 0;
bool file_size_entered = false;

void shared_file_size_callback(std::uint32_t stage, std::uint64_t ordinal, std::uint64_t resource,
    std::uint64_t route, HANDLE handle, PLARGE_INTEGER sizeOutput, std::uint64_t before, BOOL result, DWORD last) {
    Bytes bytes; bytes.put(state->module_id); bytes.put(stage); bytes.put(ordinal); bytes.put(resource);
    bytes.put(route); bytes.put(address(handle)); bytes.put(sizeOutput ? 1U : 0U); bytes.put(before);
    bytes.put(stage == 2 && sizeOutput ? nvse_copy(reinterpret_cast<const std::uint64_t*>(sizeOutput)) : 0ULL);
    bytes.put(static_cast<std::uint32_t>(result)); bytes.put(last);
    auto response = perform_callback_bytes(Kind::io_callback, file_size_callback, bytes);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason);
    Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Shared file size lost its real caller receipt.");
    reader.finish(); ++state->io->callbacks;
}

BOOL WINAPI io_shared_size_ex(HANDLE handle, PLARGE_INTEGER sizeOutput) {
    const auto incoming = GetLastError();
    DWORD sdkLast = incoming;
    const auto callResult = nvse_adapter([&]() -> BOOL {
        if (!state->io || !state->nvse || !state->module || !state->module_id || state->calls.empty() ||
            state->io->loader_retiring || file_size_entered)
            throw Fatal(ERROR_BUSY, "Shared file size has no returned loader/current source call and live file owner.");
        const auto resource = state->module_set ? state->module_set->primary : state->module_id;
        if (state->module_set) (void)nvse_module_record(resource);
        const auto found = state->io->files.find(address(handle));
        std::uint64_t route = 0;
        if (found != state->io->files.end()) {
            if (found->second.handle != handle) throw Fatal(ERROR_INVALID_HANDLE, "Shared file size changed its actual retained HANDLE.");
            route = found->second.capability;
        } else if (handle && handle != INVALID_HANDLE_VALUE)
            throw Fatal(ERROR_INVALID_HANDLE, "Shared file size cannot consume a foreign or retired HANDLE.");
        if (sizeOutput) expression_writable(sizeOutput, sizeof(LARGE_INTEGER));
        if (file_size_ordinal == UINT64_MAX) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Shared file-size invocation identity exhausted.");
        const auto ordinal = ++file_size_ordinal;
        const auto before = sizeOutput ? nvse_copy(reinterpret_cast<const std::uint64_t*>(sizeOutput)) : 0ULL;
        file_size_entered = true;
        try {
            shared_file_size_callback(1, ordinal, resource, route, handle, sizeOutput, before, FALSE, 0);
            // The real SDK owns access checks, 64-bit length, failure buffer and
            // last error. No seek, path lookup or synthesized size occurs here.
            SetLastError(incoming);
            const auto result = GetFileSizeEx(handle, sizeOutput); const auto last = GetLastError(); sdkLast = last;
            shared_file_size_callback(2, ordinal, resource, route, handle, sizeOutput, before, result, last);
            file_size_entered = false; return result;
        } catch (...) { file_size_entered = false; throw; }
    });
    SetLastError(sdkLast); return callResult;
}

void* shared_file_size_import_entry(const std::string& library, const std::string& name, void* actual) {
    const auto platform = library == "kernel32.dll" || library == "kernelbase.dll" || library.rfind("api-ms-win-core-file-", 0) == 0;
    if (!platform || name != "GetFileSizeEx") return nullptr;
    const auto provider = GetModuleHandleW(io_from_utf8(library).c_str());
    if (!actual || !provider || actual != reinterpret_cast<void*>(GetProcAddress(provider, name.c_str())))
        throw Fatal(ERROR_INVALID_DATA, "Shared GetFileSizeEx is not its actual selected Windows IAT export.");
    return reinterpret_cast<void*>(io_shared_size_ex);
}
void shared_file_size_require_retired() {
    if (file_size_entered) throw Fatal(ERROR_BUSY, "Shared file size has an entered real SDK/HANDLE borrow.");
}
