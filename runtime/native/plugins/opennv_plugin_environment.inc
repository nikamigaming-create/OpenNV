constexpr std::uint32_t io_environment_callback = 25;

void environment_require_frame(const std::string& name, const std::string* submitted, std::uint32_t capacity) {
    const auto extent = static_cast<std::uint64_t>(name.size()) + (submitted ? submitted->size() : 0ULL) + capacity;
    if (extent > max_payload - 256) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Environment strings/output exceed the complete entered IPC frame.");
}
void environment_bytes(Bytes& request, const std::string& bytes) {
    if (bytes.size() > max_payload - 256) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Environment caller string exceeds its complete IPC extent.");
    request.put(static_cast<std::uint32_t>(bytes.size())); request.data.insert(request.data.end(), bytes.begin(), bytes.end());
}
std::string environment_output(const char* buffer, std::uint32_t capacity) {
    std::string result;
    for (std::uint32_t at = 0; at < capacity; ++at) {
        const auto byte = nvse_copy(buffer + at); if (!byte) return result; result.push_back(byte);
    }
    throw Fatal(ERROR_INVALID_DATA, "Actual environment output lacks a terminator inside the caller extent.");
}
void environment_emit(CrtProvider* provider, std::uint32_t operation, const std::string& name,
    const std::string* submitted, std::uint32_t capacity, std::uint32_t required,
    std::int64_t result, const CrtStatus& status, DWORD last, const std::string* output_bytes) {
    Bytes request; request.put(provider ? provider->id : 0ULL); request.put(operation);
    environment_bytes(request, name); request.put(submitted ? 1U : 0U); if (submitted) environment_bytes(request, *submitted);
    request.put(capacity); request.put(required); request.put(result); request.put(status.error); request.put(status.dos); request.put(last);
    request.put(output_bytes ? 1U : 0U); if (output_bytes) environment_bytes(request, *output_bytes);
    // Windows environment reads can exist without any selected CRT import.
    // Under loader detach both kinds share the actual ordered deferred queue.
    crt_owned_callback(io_environment_callback, request);
}
void environment_provider_register(CrtProvider& provider) {
    if (io_owner().loader_retiring && !provider.registered)
        throw Fatal(ERROR_NOT_SUPPORTED, "First CRT environment use under loader detach has no previously registered provider.");
    if (!io_owner().loader_retiring) crt_register(provider);
}
int __cdecl crt_getenv_s(std::size_t* required, char* buffer, std::size_t capacity, const char* name) {
    const auto incoming = GetLastError();
    return nvse_adapter([&] {
        if (!required || !name || !buffer && capacity != 0)
            throw Fatal(ERROR_NOT_SUPPORTED, "CRT environment invalid-parameter handler ownership is absent.");
        if (capacity > max_payload - 256) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Environment caller output exceeds the entered callback extent.");
        expression_writable(required, sizeof(std::size_t)); if (capacity) expression_writable(buffer, static_cast<std::uint32_t>(capacity));
        const auto source_name = nvse_string(name, false); environment_require_frame(source_name, nullptr, static_cast<std::uint32_t>(capacity));
        auto& provider = crt_active_provider(); environment_provider_register(provider);
        const auto entry = crt_export<int (__cdecl*)(std::size_t*, char*, std::size_t, const char*)>(provider, "getenv_s");
        SetLastError(incoming); const auto result = entry(required, buffer, capacity, name);
        const auto last = GetLastError(); const auto status = crt_status(provider);
        const auto actual_required = nvse_copy(required); std::string returned; const std::string* actual = nullptr;
        if (!result && buffer && capacity && actual_required) { returned = environment_output(buffer, static_cast<std::uint32_t>(capacity)); actual = &returned; }
        environment_emit(&provider, 1, source_name, nullptr, static_cast<std::uint32_t>(capacity),
            static_cast<std::uint32_t>(actual_required), result, status, last, actual);
        crt_restore_error(provider, status); SetLastError(last); return result;
    });
}
int __cdecl crt_putenv_s(const char* name, const char* value) {
    const auto incoming = GetLastError();
    return nvse_adapter([&] {
        if (!name || !value) throw Fatal(ERROR_NOT_SUPPORTED, "CRT environment invalid-parameter handler ownership is absent.");
        const auto source_name = nvse_string(name, false); const auto source_value = nvse_string(value, true);
        environment_require_frame(source_name, &source_value, 0);
        auto& provider = crt_active_provider(); environment_provider_register(provider);
        const auto entry = crt_export<int (__cdecl*)(const char*, const char*)>(provider, "_putenv_s");
        SetLastError(incoming); const auto result = entry(name, value);
        const auto last = GetLastError(); const auto status = crt_status(provider);
        // The genuine selected UCRT changes its own process-local environment.
        // Parent captures the actual return and submitted bytes without inferring
        // an identical Windows or Unicode environment view.
        environment_emit(&provider, 2, source_name, &source_value, 0, 0, result, status, last, nullptr);
        crt_restore_error(provider, status); SetLastError(last); return result;
    });
}
DWORD WINAPI io_get_environment_a(LPCSTR name, LPSTR buffer, DWORD capacity) {
    const auto incoming = GetLastError();
    return nvse_adapter([&] {
        if (!name || !buffer && capacity) throw Fatal(ERROR_NOT_SUPPORTED, "Windows environment null caller parameter ownership is absent.");
        if (capacity > max_payload - 256) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Windows environment output exceeds its entered callback extent.");
        const auto source_name = nvse_string(name, false); environment_require_frame(source_name, nullptr, capacity);
        if (capacity) expression_writable(buffer, capacity);
        SetLastError(incoming); const auto result = GetEnvironmentVariableA(name, buffer, capacity); const auto last = GetLastError();
        std::string returned; const std::string* actual = nullptr;
        if (buffer && result && result < capacity) { returned = environment_output(buffer, capacity); actual = &returned; }
        environment_emit(nullptr, 3, source_name, nullptr, capacity, result, result, CrtStatus{}, last, actual);
        SetLastError(last); return result;
    });
}
