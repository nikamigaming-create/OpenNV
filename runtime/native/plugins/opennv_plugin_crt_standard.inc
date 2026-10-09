constexpr std::uint32_t crt_standard_callback = 27;
void* __cdecl crt_iob(unsigned);
int __cdecl crt_file_number(void*);
std::intptr_t __cdecl crt_os_handle(int);

PluginCrtStandardRuntime& crt_standard_owner() {
    auto& io = io_owner();
    if (!io.standard) io.standard = std::make_unique<PluginCrtStandardRuntime>();
    return *io.standard;
}
bool crt_same_kernel_object(HANDLE first, HANDLE second) {
    using Compare = BOOL (WINAPI*)(HANDLE, HANDLE);
    const auto image = GetModuleHandleW(L"kernelbase.dll");
    const auto function = image ? GetProcAddress(image, "CompareObjectHandles") : nullptr;
    if (!function) throw Fatal(GetLastError(), "Standard descriptor lacks the actual kernel identity API.");
    SetLastError(ERROR_SUCCESS);
    const auto equal = reinterpret_cast<Compare>(function)(first, second); const auto error = GetLastError();
    if (!equal && error) throw Fatal(error, "Standard descriptor kernel identity comparison failed.");
    return equal != FALSE;
}
HANDLE crt_duplicate_identity(HANDLE handle) {
    HANDLE retained = INVALID_HANDLE_VALUE;
    if (!DuplicateHandle(GetCurrentProcess(), handle, GetCurrentProcess(), &retained, 0, FALSE, DUPLICATE_SAME_ACCESS))
        throw Fatal(GetLastError(), "Descriptor identity reference could not be retained.");
    return retained;
}
void crt_preserve_protocol_handle(HANDLE closing) {
    // A preloaded dynamic CRT can have inherited the same numeric pipe handle.
    // Preserve the actual first-party pipe before a genuine _dup2 closes it.
    if (closing == input) input = crt_duplicate_identity(closing);
    if (closing == output) output = crt_duplicate_identity(closing);
}
CrtProvider& crt_load_source_provider() {
    auto& owner = crt_owner();
    if (owner.providers.size() != 1) throw Fatal(ERROR_NOT_SUPPORTED, "Standard FILE requires one exact actual UCRT instance.");
    auto& provider = *owner.providers[0];
    if (!provider.retained) {
        provider.retained = LoadLibraryExW(io_native_path(provider.path).c_str(), nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!provider.retained) throw Fatal(GetLastError(), "Selected UCRT standard provider failed real Windows admission.");
    }
    std::vector<wchar_t> mapped(32768);
    const auto count = GetModuleFileNameW(provider.retained, mapped.data(), static_cast<DWORD>(mapped.size()));
    if (!count || count >= mapped.size()) throw Fatal(GetLastError(), "Standard UCRT provider path is absent/truncated.");
    crt_require_provider_file(provider.path, mapped.data()); return provider;
}
void crt_prepare_standard(unsigned index) {
    auto& standard = crt_standard_owner(); auto& stream = standard.streams[index];
    if (stream.prepared) return;
    if (stream.entered) throw Fatal(ERROR_INVALID_STATE, "Entered standard descriptor construction cannot replay.");
    if (index != 1 && index != 2) throw Fatal(ERROR_NOT_SUPPORTED, "Original stdin has no admitted genuine input source.");
    stream.index = index; stream.entered = true;
    auto& io = io_owner(); auto& provider = crt_load_source_provider();
    auto* pointer = crt_export<void* (__cdecl*)(unsigned)>(provider, "__acrt_iob_func")(index);
    if (!pointer) throw Fatal(ERROR_INVALID_ADDRESS, "Actual UCRT standard FILE is absent.");
    const auto descriptor = crt_export<int (__cdecl*)(void*)>(provider, "_fileno")(pointer);
    if (descriptor != static_cast<int>(index)) throw Fatal(ERROR_INVALID_DATA, "Actual UCRT standard FILE descriptor index differs from its public identity.");
    auto actual = reinterpret_cast<HANDLE>(crt_export<std::intptr_t (__cdecl*)(int)>(provider, "_get_osfhandle")(descriptor));
    const auto expected = index == 1 ? io.stdout_file : io.stderr_file;
    if (expected == INVALID_HANDLE_VALUE || !expected) throw Fatal(ERROR_INVALID_HANDLE, "Standard output lost its actual module-private Windows file.");
    bool equal = actual != INVALID_HANDLE_VALUE && actual && crt_same_kernel_object(actual, expected);
    if (!equal) {
        if (actual != INVALID_HANDLE_VALUE && actual) crt_preserve_protocol_handle(actual);
        // Transfer a genuinely duplicated private kernel file to the selected
        // UCRT. _dup2 owns its own duplication and closes the prior target.
        const auto duplicate = crt_duplicate_identity(expected);
        const auto temporary = crt_export<int (__cdecl*)(std::intptr_t, int)>(provider, "_open_osfhandle")(
            reinterpret_cast<std::intptr_t>(duplicate), 0x0001 | 0x4000);
        if (temporary < 0) {
            const auto returned = crt_status(provider); const auto last = GetLastError();
            if (!CloseHandle(duplicate)) throw Fatal(GetLastError(), "Unadopted standard descriptor handle cleanup failed.");
            crt_restore_error(provider, returned); SetLastError(last);
            throw Fatal(ERROR_INVALID_HANDLE, "Actual UCRT rejected the private standard descriptor handle.");
        }
        const auto duplicated = crt_export<int (__cdecl*)(int, int)>(provider, "_dup2")(temporary, descriptor);
        const auto returned = crt_status(provider); const auto last = GetLastError();
        const auto closed = crt_export<int (__cdecl*)(int)>(provider, "_close")(temporary);
        crt_restore_error(provider, returned); SetLastError(last);
        if (duplicated != 0 || closed != 0) throw Fatal(ERROR_INVALID_HANDLE, "Actual UCRT standard descriptor duplication/independent temporary close failed.");
        actual = reinterpret_cast<HANDLE>(crt_export<std::intptr_t (__cdecl*)(int)>(provider, "_get_osfhandle")(descriptor));
        equal = actual != INVALID_HANDLE_VALUE && actual && crt_same_kernel_object(actual, expected);
        if (!equal) throw Fatal(ERROR_INVALID_DATA, "Actual UCRT standard descriptor does not alias the module-private kernel file.");
    }
    stream.pointer = pointer; stream.descriptor = descriptor; stream.handle = actual; stream.prepared = true;
    // If the provider adopted the identical OS handle, it owns its eventual
    // close. The private-I/O owner must not close the same numeric handle twice.
    if (actual == io.stdout_file) io.stdout_crt_owned = true;
    if (actual == io.stderr_file) io.stderr_crt_owned = true;
}
void crt_publish_standard(unsigned index) {
    auto& owner = crt_standard_owner(); crt_prepare_standard(index); auto& stream = owner.streams.at(index);
    if (stream.published) return;
    auto& provider = crt_active_provider(); crt_register(provider);
    Bytes request; request.put(1U); request.put(provider.id); request.put(index); request.put(address(stream.pointer));
    request.put(stream.descriptor); request.put(address(stream.handle)); request.put(1U); crt_put_status(request, crt_status(provider, stream.pointer));
    auto response = io_host(crt_standard_callback, request); Reader receipt{response.data};
    stream.route = receipt.get<std::uint64_t>(); receipt.finish();
    if (!stream.route) throw Fatal(ERROR_INVALID_DATA, "Standard FILE has no actual correlated private C# route.");
    auto& streams = crt_owner().streams;
    if (!streams.emplace(address(stream.pointer), CrtStream{provider.id, stream.route, stream.pointer, true, L"a"}).second)
        throw Fatal(ERROR_INVALID_HANDLE, "Standard FILE repeats a live native stream capability.");
    CrtDescriptor descriptor; descriptor.provider = provider.id; descriptor.route = stream.route; descriptor.stream = stream.pointer;
    descriptor.descriptor = stream.descriptor; descriptor.handle = stream.handle; descriptor.writable = true;
    const auto inserted = owner.descriptors.emplace(descriptor.descriptor, descriptor);
    if (!inserted.second) throw Fatal(ERROR_INVALID_HANDLE, "Standard FILE repeats an actual native descriptor.");
    inserted.first->second.identity = crt_duplicate_identity(stream.handle);
    stream.published = true;
}
void crt_prepare_preentry_standard() {
    if (!io_owner().crt) return;
    // Only system-provider exports execute here, before original TLS/entry.
    // A real dynamically loaded UCRT must never inherit the protocol pipes.
    crt_prepare_standard(1); crt_prepare_standard(2);
    auto& provider = crt_active_provider();
    auto* pointer = crt_export<void* (__cdecl*)(unsigned)>(provider, "__acrt_iob_func")(0);
    if (!pointer) throw Fatal(ERROR_INVALID_ADDRESS, "Actual UCRT stdin global is absent.");
    const auto fd = crt_export<int (__cdecl*)(void*)>(provider, "_fileno")(pointer);
    if (fd == -2) return; // Genuine SDK no-console/input-object disposition.
    if (fd != 0) throw Fatal(ERROR_INVALID_DATA, "Selected UCRT stdin has an unknown descriptor construction.");
    const auto handle = reinterpret_cast<HANDLE>(crt_export<std::intptr_t (__cdecl*)(int)>(provider, "_get_osfhandle")(fd));
    if (handle == INVALID_HANDLE_VALUE || reinterpret_cast<std::intptr_t>(handle) == -2) return;
    if (!crt_same_kernel_object(handle, input))
        throw Fatal(ERROR_NOT_SUPPORTED, "Preloaded UCRT stdin has no genuine selected input owner.");
    crt_preserve_protocol_handle(handle);
    if (crt_export<int (__cdecl*)(int)>(provider, "_close")(fd) != 0)
        throw Fatal(ERROR_INVALID_HANDLE, "Actual inherited UCRT input descriptor could not retire independently.");
}
void* __cdecl crt_iob(unsigned index) {
    return nvse_adapter([&]() -> void* {
        const auto incoming = GetLastError(); auto& owner = crt_standard_owner();
        if (!owner.published || index == 0) throw Fatal(ERROR_NOT_SUPPORTED, "Standard FILE query lacks its actual published output/input authority.");
        const auto found = owner.streams.find(index);
        if (found == owner.streams.end() || found->second.retired) throw Fatal(ERROR_INVALID_HANDLE, "Standard FILE query targets a foreign/closed lifetime.");
        auto& provider = crt_active_provider(); SetLastError(incoming);
        auto* result = crt_export<void* (__cdecl*)(unsigned)>(provider, "__acrt_iob_func")(index);
        const auto last = GetLastError();
        if (result != found->second.pointer) throw Fatal(ERROR_INVALID_DATA, "Actual standard FILE global pointer drifted.");
        SetLastError(last); return result;
    });
}
void crt_standard_runtime(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (!state->module || state->module_id != module || !state->io || !state->calls.empty())
        throw Fatal(ERROR_INVALID_STATE, "CRT runtime construction requires the actual mapped original module and an independent entered call.");
    auto& standard = crt_standard_owner();
    if (standard.publication_entered) throw Fatal(ERROR_INVALID_STATE, "CRT runtime publication cannot retry after an entered failure.");
    standard.publication_entered = true; state->calls.push_back(frame.header.id);
    try {
        bool needs_standard = state->io->cpp != nullptr;
        if (state->io->crt) for (const auto& provider : state->io->crt->providers)
            for (const auto& library : provider->imports) if (library.second.count("__acrt_iob_func")) needs_standard = true;
        if (needs_standard) { crt_publish_standard(1); crt_publish_standard(2); }
        standard.published = true; cpp_publish();
        Bytes result; result.put(static_cast<std::uint32_t>(std::count_if(standard.streams.begin(), standard.streams.end(),
            [](const auto& row) { return row.second.published; })));
        result.put(state->io->cpp ? static_cast<std::uint32_t>(state->io->cpp->exports.size()) : 0U);
        state->calls.pop_back(); reply(frame, ERROR_SUCCESS, "", result);
    } catch (...) { state->calls.pop_back(); throw; }
}
void crt_descriptor_views_closed(void* pointer) {
    auto& owner = crt_standard_owner();
    for (auto at = owner.descriptors.begin(); at != owner.descriptors.end();) {
        if (!at->second.independent && at->second.stream == pointer) {
            if (at->second.identity != INVALID_HANDLE_VALUE && !CloseHandle(at->second.identity))
                throw Fatal(GetLastError(), "Closed FILE descriptor identity reference did not independently retire.");
            at = owner.descriptors.erase(at);
        } else ++at;
    }
    for (auto& row : owner.streams) if (row.second.pointer == pointer) row.second.retired = true;
}
bool crt_standard_borrowed_handle(HANDLE handle) {
    if (!io_owner().standard) return false;
    for (const auto& row : io_owner().standard->streams)
        if (row.second.prepared && !row.second.retired && row.second.handle == handle) return true;
    for (const auto& row : io_owner().standard->descriptors)
        if (row.second.handle == handle) return true;
    return false;
}
void crt_standard_before_close(void* pointer) {
    if (!io_owner().standard) return;
    auto& io = io_owner();
    for (auto& row : io.standard->streams) if (row.second.pointer == pointer && !row.second.retired) {
        if (row.second.close_entered) throw Fatal(ERROR_INVALID_STATE, "Entered standard FILE close cannot replay.");
        row.second.close_entered = true;
        if (row.second.handle == io.stdout_file && io.stdout_crt_owned) { io.stdout_file = INVALID_HANDLE_VALUE; io.stdout_crt_owned = false; }
        if (row.second.handle == io.stderr_file && io.stderr_crt_owned) { io.stderr_file = INVALID_HANDLE_VALUE; io.stderr_crt_owned = false; }
    }
}
void crt_standard_after_close(void* pointer, int result, DWORD last, const CrtStatus& status) {
    if (!io_owner().standard) return;
    for (auto& row : io_owner().standard->streams) if (row.second.pointer == pointer && row.second.close_entered) {
        row.second.close_result = result; row.second.close_error = last;
        row.second.close_errno = status.error; row.second.close_doserrno = status.dos;
        // fclose consumes the FILE even when flushing it fails. Retain its
        // actual failure; never attempt another close on that pointer.
        row.second.retired = true;
    }
}
void crt_standard_retire() {
    if (!io_owner().standard) return;
    auto& owner = crt_standard_owner();
    for (auto& row : owner.streams) {
        auto& stream = row.second;
        if (stream.close_entered && (!stream.retired || stream.close_result))
            throw Fatal(stream.close_error ? stream.close_error : ERROR_WRITE_FAULT, "Standard FILE retains its entered/failed close prefix.");
        if (!stream.prepared || stream.retired) continue;
        if (stream.published) (void)crt_fclose(stream.pointer);
        else {
            // This first-party preparation never published a plugin FILE or
            // C# route. Close the actual SDK object without inventing either.
            auto& provider = crt_active_provider(); const auto incoming = GetLastError();
            crt_standard_before_close(stream.pointer); SetLastError(incoming);
            const auto result = crt_export<int (__cdecl*)(void*)>(provider, "fclose")(stream.pointer);
            const auto last = GetLastError(); const auto status = crt_status(provider);
            crt_standard_after_close(stream.pointer, result, last, status);
            crt_restore_error(provider, status); SetLastError(last);
            if (result) throw Fatal(last ? last : ERROR_WRITE_FAULT, "Unpublished genuine standard FILE cleanup failed.");
        }
    }
    if (!owner.descriptors.empty()) throw Fatal(ERROR_BUSY, "Independent original CRT descriptor lifetimes remain after actual module retirement.");
}
