// Source names retain their Windows default/Local/Global meaning. The parent
// passes a reference to that actual directory, never a substitute namespace.
bool mutex_namespace_named(const MutexSignature& signature) {
    return signature.name.pointer && (signature.api == MutexApi::create_a || signature.api == MutexApi::create_w ||
        signature.api == MutexApi::create_ex_a || signature.api == MutexApi::create_ex_w ||
        signature.api == MutexApi::open_a || signature.api == MutexApi::open_w);
}
PluginMutexNamespaceRuntime& mutex_namespace_owner() {
    auto& owner = mutex_owner();
    if (!owner.namespaces) owner.namespaces = std::make_unique<PluginMutexNamespaceRuntime>();
    return *owner.namespaces;
}
void mutex_namespace_prepare(MutexSignature& signature) {
    if (!mutex_namespace_named(signature)) return;
    auto& owner = mutex_namespace_owner();
    if (!owner.windows) {
        owner.windows = GetModuleHandleW(L"ntdll.dll");
        if (!owner.windows) throw Fatal(GetLastError(), "Original named mutex lacks its actual process Windows image.");
        owner.create = reinterpret_cast<MutexCreateMutant>(GetProcAddress(owner.windows, "NtCreateMutant"));
        owner.open = reinterpret_cast<MutexOpenMutant>(GetProcAddress(owner.windows, "NtOpenMutant"));
        owner.error = reinterpret_cast<MutexStatusError>(GetProcAddress(owner.windows, "RtlNtStatusToDosError"));
    }
    if (!owner.create || !owner.open || !owner.error)
        throw Fatal(ERROR_PROC_NOT_FOUND, "Named mutex's actual native Windows constructor/error ABI is absent.");
    signature.windows_image = address(owner.windows);
    signature.windows_create = address(reinterpret_cast<void*>(owner.create));
    signature.windows_open = address(reinterpret_cast<void*>(owner.open));
    signature.windows_error = address(reinterpret_cast<void*>(owner.error));
}
void mutex_namespace_accept(MutexSignature& signature, Reader& reader) {
    const auto capability = reader.get<std::uint64_t>(); const auto handle = reader.get<std::uint32_t>();
    const auto bytes = reader.get<std::uint32_t>();
    if (bytes > 520U || bytes & 1U) throw Fatal(ERROR_INVALID_DATA, "Directory name reply exceeds its genuine source extent.");
    std::vector<std::uint8_t> actual; actual.reserve(bytes);
    for (std::uint32_t at = 0; at < bytes; ++at) actual.push_back(reader.get<std::uint8_t>());
    if (!mutex_namespace_named(signature)) {
        if (capability || handle || bytes) throw Fatal(ERROR_INVALID_DATA, "Unnamed/handle mutex call received a fabricated namespace.");
        return;
    }
    if (!capability || !handle || handle == UINT32_MAX || bytes < 4U || actual[bytes - 1] || actual[bytes - 2])
        throw Fatal(ERROR_INVALID_DATA, "Named mutex lacks its actual retained directory/terminated relative name.");
    std::vector<wchar_t> original; original.reserve(signature.name.wide.size() / 2);
    for (std::size_t at = 0; at < signature.name.wide.size(); at += 2)
        original.push_back(static_cast<wchar_t>(signature.name.wide[at] | static_cast<std::uint16_t>(signature.name.wide[at + 1]) << 8));
    if (original.empty() || original.back()) throw Fatal(ERROR_INVALID_DATA, "Original Windows name conversion is incomplete.");
    const std::wstring name(original.data(), original.size() - 1);
    const auto offset = name.compare(0, 6, L"Local\\") == 0 ? 6U : name.compare(0, 7, L"Global\\") == 0 ? 7U : 0U;
    const auto relative = name.substr(offset);
    if (relative.empty() || relative.find(L'\\') != std::wstring::npos || actual.size() != (relative.size() + 1) * 2)
        throw Fatal(ERROR_NOT_SUPPORTED, "Original mutex private/empty namespace is not an admitted Windows directory.");
    std::vector<wchar_t> converted; converted.reserve(actual.size() / 2);
    for (std::size_t at = 0; at < actual.size(); at += 2)
        converted.push_back(static_cast<wchar_t>(actual[at] | static_cast<std::uint16_t>(actual[at + 1]) << 8));
    if (std::wstring(converted.data(), converted.size() - 1) != relative)
        throw Fatal(ERROR_INVALID_DATA, "Directory reply changed the original source's Windows relative name.");
    auto& owner = mutex_namespace_owner(); const auto native = reinterpret_cast<HANDLE>(handle);
    auto found = owner.directories.emplace(capability, PluginMutexNamespaceDirectory{capability, native});
    if (found.first->second.handle != native || found.first->second.close_entered)
        throw Fatal(ERROR_INVALID_DATA, "Mutex directory reply repeats a foreign or retired native capability.");
    signature.directory.capability = capability; signature.directory.directory = native;
    signature.directory.relative = std::move(converted);
}
HANDLE mutex_namespace_construct(MutexSignature& signature) {
    auto& owner = mutex_namespace_owner(); auto& call = signature.directory;
    if (!call.capability || !call.directory || call.used || call.relative.size() < 2 || !owner.create || !owner.open || !owner.error)
        throw Fatal(ERROR_INVALID_STATE, "Native mutex construction lost its admitted once-only caller/directory.");
    MutexUnicodeString name; name.length = static_cast<USHORT>((call.relative.size() - 1) * sizeof(wchar_t));
    name.maximum = static_cast<USHORT>(call.relative.size() * sizeof(wchar_t)); name.buffer = call.relative.data();
    const auto open = signature.api == MutexApi::open_a || signature.api == MutexApi::open_w;
    MutexObjectAttributes attributes; attributes.root = call.directory; attributes.name = &name;
    attributes.attributes = open ? 0U : 0x80U; // Actual source OBJ_OPENIF, case-sensitive object names.
    // Inheritance/custom security/APC arms have already failed at admission.
    // Null descriptor lets the unchanged original caller token supply its DACL.
    HANDLE handle = nullptr; SetLastError(signature.incoming); call.used = true;
    const auto access = signature.api == MutexApi::create_a || signature.api == MutexApi::create_w ?
        static_cast<DWORD>(MUTEX_ALL_ACCESS) : signature.access;
    call.status = open ? owner.open(&handle, access, &attributes) :
        owner.create(&handle, access, &attributes, static_cast<BOOLEAN>(signature.flags != 0));
    const auto error = call.status < 0 ? owner.error(call.status) : open ? signature.incoming :
        call.status == static_cast<LONG>(0x40000000) ? static_cast<DWORD>(ERROR_ALREADY_EXISTS) : static_cast<DWORD>(ERROR_SUCCESS);
    SetLastError(error);
    // Do not erase a real unexpected acquired reference. The outer strong slot
    // receives this return before C# validates the exact status/handle semantics.
    return handle;
}
void mutex_namespace_retire() {
    if (!state->io || !state->io->mutexes || !state->io->mutexes->namespaces) return;
    mutex_retire(); // All real mutex refs/queued detach effects must retire first.
    auto& owner = *state->io->mutexes->namespaces;
    DWORD first_error = ERROR_SUCCESS;
    for (auto& row : owner.directories) {
        auto& directory = row.second;
        if (directory.closed) continue;
        if (directory.close_entered) { if (!first_error) first_error = directory.close_error ? directory.close_error : ERROR_INVALID_STATE; continue; }
        directory.close_entered = true;
        directory.close_result = CloseHandle(directory.handle); directory.close_error = GetLastError();
        directory.closed = directory.close_result != FALSE;
        Bytes data; data.put(6U); data.put(directory.capability); data.put(address(directory.handle));
        data.put(static_cast<std::uint32_t>(directory.close_result)); data.put(directory.close_error);
        auto response = mutex_host(data); Reader reader{response.data};
        if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Directory closure lost its actual C# lifetime receipt.");
        reader.finish();
        if (!directory.closed && !first_error) first_error = directory.close_error ? directory.close_error : ERROR_INVALID_STATE;
    }
    if (first_error) throw Fatal(first_error, "An actual native object-directory CloseHandle failed with its entered prefix retained.");
}
void mutex_namespace_require_retired() {
    if (!state->io || !state->io->mutexes || !state->io->mutexes->namespaces) return;
    for (const auto& row : state->io->mutexes->namespaces->directories)
        if (!row.second.closed) throw Fatal(ERROR_BUSY, "Native namespace retirement retains an actual directory reference.");
}
