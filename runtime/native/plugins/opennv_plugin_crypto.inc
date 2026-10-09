constexpr std::uint32_t crypto_callback = 21;
void crypto_load_provider(PluginCryptoRuntime&);
PluginCryptoRuntime& crypto_owner() {
    auto& io = io_owner();
    if (!io.crypto || io.crypto->retired)
        throw Fatal(ERROR_INVALID_HANDLE, "CNG operation lost its actual retained Windows export provider.");
    crypto_load_provider(*io.crypto);
    return *io.crypto;
}
void crypto_register() {
    auto& owner = crypto_owner(); if (owner.registered) return;
    Bytes request; request.put(1U); request.put(address(owner.provider)); request.text(io_utf8(owner.path)); request.text(owner.sha);
    auto response = io_host(crypto_callback, request); Reader result{response.data};
    if (result.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual CNG provider has no complete correlated source receipt.");
    result.finish(); owner.registered = true;
}
void crypto_emit(const PluginCryptoEvent& event) {
    auto& owner = crypto_owner();
    if (io_owner().loader_retiring) { owner.pending.push_back(event); return; }
    crypto_register(); Bytes request; request.put(2U); request.put(static_cast<std::uint32_t>(event.operation));
    request.put(event.lifetime); request.put(event.algorithm); request.put(event.handle); request.put(event.flags);
    request.put(event.requested); request.put(event.result_bytes); request.put(event.result_available ? 1U : 0U); request.put(event.status);
    request.text(io_utf8(event.name)); request.text(io_utf8(event.implementation)); request.put(event.implementation_present ? 1U : 0U);
    request.put(event.object); request.put(event.object_bytes);
    auto response = io_host(crypto_callback, request); Reader result{response.data};
    if (result.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "CNG result has no exact native handle/status/lifetime receipt.");
    result.finish();
}
void crypto_prepare(PluginIoRuntime& io, Reader& reader) {
    auto owner = std::make_unique<PluginCryptoRuntime>();
    owner->path = io_from_utf8(reader.text()); owner->sha = reader.text(); owner->declaration = reader.text();
    if (owner->path.empty() || owner->sha.size() != 64 || owner->declaration.empty())
        throw Fatal(ERROR_INVALID_DATA, "CNG source has no exact Windows provider path/hash/declaration.");
    // A retained source declaration is not a caller. Unrelated module startup
    // must not load CNG merely because private file I/O was prepared.
    io.crypto = std::move(owner);
}
void crypto_load_provider(PluginCryptoRuntime& owner) {
    if (owner.load_entered) {
        if (owner.load_error != ERROR_SUCCESS || !owner.provider)
            throw Fatal(owner.load_error != ERROR_SUCCESS ? owner.load_error : ERROR_INVALID_HANDLE,
                "The actual selected CNG provider's failed admission cannot be replayed.");
        return;
    }
    if (io_owner().loader_retiring)
        throw Fatal(ERROR_INVALID_STATE, "CNG provider construction during module retirement is unowned.");
    owner.load_entered = true;
    try {
    owner.provider = LoadLibraryExW(io_native_path(owner.path).c_str(), nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!owner.provider) throw Fatal(GetLastError(), "Actual selected x86 CNG export provider did not load for its caller.");
    std::vector<wchar_t> mapped(32768); const auto count = GetModuleFileNameW(owner.provider, mapped.data(), static_cast<DWORD>(mapped.size()));
    if (!count || count >= mapped.size()) throw Fatal(GetLastError(), "Actual CNG provider has no complete mapped filename identity.");
    crt_require_provider_file(owner.path, mapped.data());
    owner.open = reinterpret_cast<decltype(owner.open)>(GetProcAddress(owner.provider, "BCryptOpenAlgorithmProvider"));
    owner.property = reinterpret_cast<decltype(owner.property)>(GetProcAddress(owner.provider, "BCryptGetProperty"));
    owner.create = reinterpret_cast<decltype(owner.create)>(GetProcAddress(owner.provider, "BCryptCreateHash"));
    owner.data = reinterpret_cast<decltype(owner.data)>(GetProcAddress(owner.provider, "BCryptHashData"));
    owner.finish = reinterpret_cast<decltype(owner.finish)>(GetProcAddress(owner.provider, "BCryptFinishHash"));
    owner.destroy = reinterpret_cast<decltype(owner.destroy)>(GetProcAddress(owner.provider, "BCryptDestroyHash"));
    owner.close = reinterpret_cast<decltype(owner.close)>(GetProcAddress(owner.provider, "BCryptCloseAlgorithmProvider"));
    if (!owner.open || !owner.property || !owner.create || !owner.data || !owner.finish || !owner.destroy || !owner.close)
        throw Fatal(ERROR_PROC_NOT_FOUND, "The actual selected CNG image lacks its public hash/provider callable exports.");
    }
    catch (const Fatal& failure) { owner.load_error = failure.code; throw; }
    catch (...) { owner.load_error = ERROR_INVALID_DATA; throw; }
}
PluginCryptoAlgorithm crypto_algorithm(BCRYPT_ALG_HANDLE handle) {
    auto& owner = crypto_owner(); const auto found = owner.algorithms.find(address(handle));
    if (found == owner.algorithms.end() || found->second.handle != handle)
        throw Fatal(ERROR_INVALID_HANDLE, "CNG algorithm has a foreign, retired, pseudo-handle or pre-entry-only lifetime.");
    return found->second;
}
PluginCryptoHash crypto_hash(BCRYPT_HASH_HANDLE handle) {
    auto& owner = crypto_owner(); const auto found = owner.hashes.find(address(handle));
    if (found == owner.hashes.end() || found->second.handle != handle)
        throw Fatal(ERROR_INVALID_HANDLE, "CNG hash has a foreign, retired or pre-entry-only lifetime.");
    if (found->second.object && found->second.object_bytes) expression_writable(found->second.object, found->second.object_bytes);
    return found->second;
}
NTSTATUS WINAPI crypto_open(BCRYPT_ALG_HANDLE* destination, LPCWSTR algorithm, LPCWSTR implementation, ULONG flags) {
    const auto caller_error = GetLastError();
    return nvse_adapter([&]() -> NTSTATUS {
        auto& owner = crypto_owner(); if (io_owner().loader_retiring) throw Fatal(ERROR_NOT_SUPPORTED, "New CNG algorithm construction under loader retirement is unowned.");
        const auto name = io_wide(algorithm), provider_name = io_wide(implementation, true);
        if (destination) expression_writable(destination, sizeof(*destination));
        crypto_register(); const auto id = ++owner.next_lifetime;
        if (!id) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "CNG lifetime identity exhausted.");
        SetLastError(caller_error); const auto status = owner.open(destination, algorithm, implementation, flags); const auto last = GetLastError();
        const auto handle = status >= 0 && destination ? nvse_copy(destination) : nullptr;
        if (status >= 0) {
            if (!handle || owner.hashes.count(address(handle)) || owner.algorithms.count(address(handle)))
                throw Fatal(ERROR_INVALID_HANDLE, "Actual CNG Open returned an absent/aliased algorithm handle.");
            try { owner.algorithms.emplace(address(handle), PluginCryptoAlgorithm{id, handle, flags}); }
            catch (...) { (void)owner.close(handle, 0); throw; }
        }
        crypto_emit({CryptoOperation::open_algorithm, id, 0, address(handle), flags, 0, 0, status,
            name, provider_name, implementation != nullptr, 0, 0}); SetLastError(last); return status;
    });
}
NTSTATUS WINAPI crypto_property(BCRYPT_HANDLE handle, LPCWSTR property, PUCHAR destination, ULONG length, ULONG* copied, ULONG flags) {
    const auto caller_error = GetLastError();
    return nvse_adapter([&]() -> NTSTATUS {
        auto& owner = crypto_owner(); const auto name = io_wide(property);
        PluginCryptoEvent event{CryptoOperation::get_property, 0, 0, address(handle), flags, length, 0, 0, name, {}, false, 0, 0};
        const auto hash = owner.hashes.find(address(handle));
        if (hash != owner.hashes.end()) { const auto active = crypto_hash(handle); event.lifetime = active.id; event.algorithm = active.algorithm;
            event.object = address(active.object); event.object_bytes = active.object_bytes; }
        else event.lifetime = crypto_algorithm(handle).id;
        if (destination && length) expression_writable(destination, length); if (copied) expression_writable(copied, sizeof(*copied));
        SetLastError(caller_error); const auto status = owner.property(handle, property, destination, length, copied, flags); const auto last = GetLastError();
        event.status = status;
        // The required-size value is also meaningful on BUFFER_TOO_SMALL and
        // on the supported null-output size query; never treat it as copied.
        event.result_available = copied && (status >= 0 || static_cast<std::uint32_t>(status) == 0xc0000023U);
        event.result_bytes = event.result_available ? nvse_copy(copied) : 0U;
        crypto_emit(event); SetLastError(last); return status;
    });
}
NTSTATUS WINAPI crypto_create(BCRYPT_ALG_HANDLE algorithm, BCRYPT_HASH_HANDLE* destination, PUCHAR object, ULONG object_bytes,
    PUCHAR secret, ULONG secret_bytes, ULONG flags) {
    const auto caller_error = GetLastError();
    return nvse_adapter([&]() -> NTSTATUS {
        auto& owner = crypto_owner(); if (io_owner().loader_retiring) throw Fatal(ERROR_NOT_SUPPORTED, "New CNG hash construction under loader retirement is unowned.");
        const auto active = crypto_algorithm(algorithm); if (destination) expression_writable(destination, sizeof(*destination));
        if (object && object_bytes) expression_writable(object, object_bytes); if (secret && secret_bytes) nvse_readable(secret, secret_bytes);
        const auto id = ++owner.next_lifetime;
        if (!id) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "CNG lifetime identity exhausted.");
        SetLastError(caller_error); const auto status = owner.create(algorithm, destination, object, object_bytes, secret, secret_bytes, flags);
        const auto last = GetLastError(); const auto handle = status >= 0 && destination ? nvse_copy(destination) : nullptr;
        if (status >= 0) {
            if (!handle || owner.hashes.count(address(handle)) || owner.algorithms.count(address(handle)))
                throw Fatal(ERROR_INVALID_HANDLE, "Actual CNG CreateHash returned an absent/aliased hash handle.");
            try { owner.hashes.emplace(address(handle), PluginCryptoHash{id, active.id, handle, object, object_bytes, flags}); }
            catch (...) { (void)owner.destroy(handle); throw; }
        }
        crypto_emit({CryptoOperation::create_hash, id, active.id, address(handle), flags, secret_bytes, 0, status,
            {}, {}, false, address(object), object_bytes}); SetLastError(last); return status;
    });
}
NTSTATUS WINAPI crypto_data(BCRYPT_HASH_HANDLE handle, PUCHAR bytes, ULONG length, ULONG flags) {
    const auto caller_error = GetLastError();
    return nvse_adapter([&]() -> NTSTATUS {
        const auto active = crypto_hash(handle); if (bytes && length) nvse_readable(bytes, length);
        SetLastError(caller_error); const auto status = crypto_owner().data(handle, bytes, length, flags); const auto last = GetLastError();
        crypto_emit({CryptoOperation::hash_data, active.id, active.algorithm, address(handle), flags, length, 0, status,
            {}, {}, false, address(active.object), active.object_bytes}); SetLastError(last); return status;
    });
}
NTSTATUS WINAPI crypto_finish(BCRYPT_HASH_HANDLE handle, PUCHAR bytes, ULONG length, ULONG flags) {
    const auto caller_error = GetLastError();
    return nvse_adapter([&]() -> NTSTATUS {
        const auto active = crypto_hash(handle); if (bytes && length) expression_writable(bytes, length);
        SetLastError(caller_error); const auto status = crypto_owner().finish(handle, bytes, length, flags); const auto last = GetLastError();
        crypto_emit({CryptoOperation::finish_hash, active.id, active.algorithm, address(handle), flags, length,
            status >= 0 ? length : 0U, status, {}, {}, false, address(active.object), active.object_bytes, status >= 0}); SetLastError(last); return status;
    });
}
NTSTATUS WINAPI crypto_destroy(BCRYPT_HASH_HANDLE handle) {
    const auto caller_error = GetLastError();
    return nvse_adapter([&]() -> NTSTATUS {
        const auto active = crypto_hash(handle); SetLastError(caller_error); const auto status = crypto_owner().destroy(handle); const auto last = GetLastError();
        if (status >= 0) crypto_owner().hashes.erase(address(handle));
        crypto_emit({CryptoOperation::destroy_hash, active.id, active.algorithm, address(handle), 0, 0, 0, status,
            {}, {}, false, address(active.object), active.object_bytes}); SetLastError(last); return status;
    });
}
NTSTATUS WINAPI crypto_close(BCRYPT_ALG_HANDLE handle, ULONG flags) {
    const auto caller_error = GetLastError();
    return nvse_adapter([&]() -> NTSTATUS {
        const auto active = crypto_algorithm(handle); SetLastError(caller_error); const auto status = crypto_owner().close(handle, flags); const auto last = GetLastError();
        if (status >= 0) crypto_owner().algorithms.erase(address(handle));
        crypto_emit({CryptoOperation::close_algorithm, active.id, 0, address(handle), flags, 0, 0, status,
            {}, {}, false, 0, 0}); SetLastError(last); return status;
    });
}
void* crypto_entry(const std::string& name) {
    struct Entry { const char* name; void* function; };
    const Entry entries[] = {
        {"BCryptOpenAlgorithmProvider", reinterpret_cast<void*>(crypto_open)}, {"BCryptGetProperty", reinterpret_cast<void*>(crypto_property)},
        {"BCryptCreateHash", reinterpret_cast<void*>(crypto_create)}, {"BCryptHashData", reinterpret_cast<void*>(crypto_data)},
        {"BCryptFinishHash", reinterpret_cast<void*>(crypto_finish)}, {"BCryptDestroyHash", reinterpret_cast<void*>(crypto_destroy)},
        {"BCryptCloseAlgorithmProvider", reinterpret_cast<void*>(crypto_close)} };
    for (const auto& entry : entries) if (name == entry.name) return entry.function;
    return nullptr;
}
void* crypto_import_entry(const std::string& library, const std::string& name, void* original) {
    if (library != "bcrypt.dll") return nullptr;
    auto* replacement = crypto_entry(name); if (!replacement) throw Fatal(ERROR_NOT_SUPPORTED, "Original CNG imported arm has no public callable/lifetime owner.");
    auto& owner = crypto_owner();
    if (!original || reinterpret_cast<void*>(GetProcAddress(owner.provider, name.c_str())) != original)
        throw Fatal(ERROR_INVALID_DATA, "Original CNG IAT pointer differs from the actual retained selected Windows export.");
    return replacement;
}
void* crypto_dynamic_entry(HMODULE module, const std::string& name) {
    if (!io_owner().crypto || module != io_owner().crypto->provider) return nullptr;
    auto* entry = crypto_entry(name); if (!entry) throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic CNG API has no actual public hash/provider owner.");
    return entry;
}
void crypto_flush_detach() {
    if (!io_owner().crypto) return; auto& owner = *io_owner().crypto;
    if (!owner.load_entered) { owner.retired = true; return; }
    if (owner.load_error != ERROR_SUCCESS || !owner.provider)
        throw Fatal(owner.load_error != ERROR_SUCCESS ? owner.load_error : ERROR_INVALID_HANDLE,
            "CNG provider admission failed; exact child closure must retire its retained prefix.");
    while (!owner.pending.empty()) { crypto_emit(owner.pending.front()); owner.pending.erase(owner.pending.begin()); }
    if (!owner.hashes.empty() || !owner.algorithms.empty()) return;
    crypto_register(); const auto module = owner.provider;
    const auto released = FreeLibrary(module); const auto last = released ? ERROR_SUCCESS : GetLastError();
    if (released) { owner.provider = nullptr; owner.retired = true; }
    Bytes request; request.put(3U); request.put(address(module)); request.put(released ? 1U : 0U); request.put(last);
    auto response = io_host(crypto_callback, request); Reader result{response.data};
    if (result.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual CNG provider-reference retirement has no C# receipt."); result.finish();
    if (!released) throw Fatal(last, "Actual selected CNG provider reference did not retire.");
}
void crypto_retire() {
    if (!io_owner().crypto) return; auto& owner = *io_owner().crypto;
    // A prepared declaration with no caller owns no native provider reference.
    // Retiring that declaration must not construct a provider for cleanup.
    if (!owner.load_entered) { owner.retired = true; return; }
    if (!owner.hashes.empty() || !owner.algorithms.empty() || !owner.pending.empty() || !owner.retired || owner.provider)
        throw Fatal(ERROR_BUSY, "Original CNG hash/algorithm/provider lifetimes remain after native module retirement.");
}
