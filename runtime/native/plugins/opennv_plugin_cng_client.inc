ImportedProvider* import_provider_for(HMODULE);
void* import_provider_export(ImportedProvider&, const std::string&);
bool import_provider_original_entry(HMODULE, const std::string&, const std::string&, void*);
// Included after actual io_host/nvse memory guards. Original caller buffers
// never become service addresses; all returned handles are opaque local leases.
constexpr std::uint32_t cng_client_category = 4, cng_transfer_bytes = 60000;
CngSystemClient& cng_client_owner() {
    auto& io = io_owner();
    if (!io.crypto || io.crypto->path.empty() || io.crypto->retired || io.loader_retiring && !cng_detach_active())
        throw Fatal(ERROR_INVALID_STATE, "CNG service caller lacks selected source or is under the original loader lock.");
    if (io.loader_retiring && (!io.cng_client || !io.cng_client->entered || !io.cng_client->service_generation))
        throw Fatal(ERROR_INVALID_STATE, "Original detach cannot construct a new CNG service.");
    if (!io.cng_client) io.cng_client = std::make_unique<CngSystemClient>();
    if (io.cng_client->retired) throw Fatal(ERROR_INVALID_STATE, "Retired CNG client cannot construct another service generation.");
    return *io.cng_client;
}
Bytes cng_client_request(CngServiceStep step, const Bytes& payload) {
    auto& owner = cng_client_owner(); owner.entered = true;
    Bytes request; request.put(cng_client_category); request.put(static_cast<std::uint32_t>(step));
    request.data.insert(request.data.end(), payload.data.begin(), payload.data.end());
    if (io_owner().loader_retiring) cng_detach_require_request(step, payload);
    auto response = io_owner().loader_retiring ? cng_detach_host(request) : io_host(crypto_callback, request); Reader reader{response.data};
    const auto generation = reader.get<std::uint64_t>();
    if (!generation || owner.service_generation && generation != owner.service_generation)
        throw Fatal(ERROR_INVALID_DATA, "CNG reply changed its actual service generation.");
    owner.service_generation = generation; Bytes result;
    result.data.assign(response.data.begin() + static_cast<std::ptrdiff_t>(reader.at), response.data.end()); return result;
}
template<class F> NTSTATUS cng_client_guard(F&& invoke) noexcept {
    try { return invoke(); }
    catch (const Fatal& failure) {
        std::fprintf(stderr, "OPENNV_CNG_CALLER_FAULT code=%lu owner=%s\n", static_cast<unsigned long>(failure.code), failure.what());
    }
    catch (const std::exception& failure) {
        std::fprintf(stderr, "OPENNV_CNG_CALLER_FAULT owner=%s\n", failure.what());
    } catch (...) { std::fputs("OPENNV_CNG_CALLER_FAULT owner=unowned native caller failure\n", stderr); }
    // An IPC/layout/ownership failure has no Windows NTSTATUS. Returning zero
    // through nvse_adapter would falsely publish STATUS_SUCCESS. Retire only
    // this exact child; its parent retains the original fault and service leases.
    std::fflush(stderr); TerminateProcess(GetCurrentProcess(), 3); ExitProcess(3);
}
struct CngCallerBuffer { const void* pointer = nullptr; std::uint32_t length = 0; };
struct CngCallerResult {
    NTSTATUS status; DWORD last_error; std::uint64_t created;
    bool copied_present; std::uint32_t copied, output_length;
};
bool cng_caller_overlap(const void* first, std::uint32_t first_length, const void* second, std::uint32_t second_length) {
    if (!first || !second || !first_length || !second_length) return false;
    const auto a = reinterpret_cast<std::uintptr_t>(first), b = reinterpret_cast<std::uintptr_t>(second);
    if (first_length > UINTPTR_MAX - a || second_length > UINTPTR_MAX - b)
        throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "CNG original buffer range wraps its actual x86 address domain.");
    return a < b + second_length && b < a + first_length;
}
void cng_caller_ranges(const std::array<CngCallerBuffer, 4>& buffers, const void* additional_output, std::uint32_t additional_length) {
    const auto& owner = cng_client_owner();
    for (std::size_t at = 0; at < buffers.size(); ++at) {
        const auto& buffer = buffers[at];
        if (at < 3 && cng_caller_overlap(buffer.pointer, buffer.length, buffers[3].pointer, buffers[3].length) ||
            cng_caller_overlap(buffer.pointer, buffer.length, additional_output, additional_length))
            throw Fatal(ERROR_NOT_SUPPORTED, "Aliased original CNG input/output buffers lack a genuine shared-address owner.");
        for (const auto& token : owner.tokens)
            if (cng_caller_overlap(buffer.pointer, buffer.length, token.second.get(), sizeof(CngImportedHandle)) ||
                cng_caller_overlap(additional_output, additional_length, token.second.get(), sizeof(CngImportedHandle)))
                throw Fatal(ERROR_NOT_SUPPORTED, "CNG caller attempted raw storage access to an opaque imported capability.");
    }
}
std::vector<std::uint8_t> cng_caller_string(LPCWSTR value) {
    if (!value) return {};
    std::vector<std::uint8_t> bytes;
    for (std::uint32_t at = 0;; ++at) {
        if (at >= (UINT32_MAX / sizeof(wchar_t)) - 1)
            throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "CNG source UTF-16 extent exceeds its original ULONG domain.");
        const auto unit = nvse_copy(value + at);
        bytes.push_back(static_cast<std::uint8_t>(unit)); bytes.push_back(static_cast<std::uint8_t>(unit >> 8));
        if (!unit) return bytes;
    }
}
CngImportedHandle& cng_caller_handle(void* value, int expected_kind = -1) {
    auto& owner = cng_client_owner(); const auto found = owner.tokens.find(address(value));
    if (!value || found == owner.tokens.end() || !found->second->live || found->second.get() != value ||
        found->second->service_generation != owner.service_generation ||
        expected_kind >= 0 && found->second->hash != (expected_kind != 0))
        throw Fatal(ERROR_INVALID_HANDLE, "CNG caller supplied a foreign/retired/pseudo or wrong-kind capability.");
    return *found->second;
}
void cng_client_upload(std::uint64_t id, std::uint32_t role, const CngCallerBuffer& buffer) {
    if (!buffer.pointer) return;
    for (std::uint32_t offset = 0; offset < buffer.length;) {
        const auto count = (std::min)(cng_transfer_bytes, buffer.length - offset);
        const auto* bytes = static_cast<const std::uint8_t*>(buffer.pointer) + offset; nvse_readable(bytes, count);
        Bytes request; request.put(id); request.put(role); request.put(offset); request.put(count);
        request.data.insert(request.data.end(), bytes, bytes + count);
        auto response = cng_client_request(CngServiceStep::write, request); Reader reader{response.data};
        if (reader.get<std::uint32_t>() != offset + count) throw Fatal(ERROR_INVALID_DATA, "CNG source upload was not acknowledged at its actual complete extent.");
        reader.finish(); offset += count;
    }
}
CngCallerResult cng_client_invoke(std::uint32_t operation, std::uint64_t target, ULONG flags, DWORD caller_error,
    ULONG object_bytes, bool destination_present, ULONG* copied, const std::array<CngCallerBuffer, 4>& buffers,
    void* output_destination, PUCHAR object = nullptr) {
    if (cng_shared_needed(target, buffers, copied, object, object_bytes))
        return cng_shared_invoke(operation, target, flags, caller_error, object_bytes, destination_present, copied, buffers, output_destination, object);
    if (output_destination && buffers[3].length) expression_writable(output_destination, buffers[3].length);
    if (copied) expression_writable(copied, sizeof(*copied));
    cng_caller_ranges(buffers, copied, copied ? sizeof(*copied) : 0U);
    Bytes begin; begin.put(operation); begin.put(target); begin.put(flags); begin.put(caller_error); begin.put(object_bytes);
    begin.put(destination_present ? 1U : 0U); begin.put(copied ? 1U : 0U); begin.put(copied ? nvse_copy(copied) : 0U);
    for (const auto& buffer : buffers) { begin.put(buffer.pointer ? 1U : 0U); begin.put(buffer.length); }
    auto started = cng_client_request(CngServiceStep::begin, begin); Reader start{started.data};
    const auto id = start.get<std::uint64_t>(); start.finish();
    if (!id) throw Fatal(ERROR_INVALID_DATA, "CNG service omitted its retained invocation identity.");
    for (std::uint32_t role = 0; role < buffers.size(); ++role) cng_client_upload(id, role, buffers[role]);
    Bytes request; request.put(id); auto executed = cng_client_request(CngServiceStep::execute, request); Reader result{executed.data};
    CngCallerResult receipt{result.get<NTSTATUS>(), result.get<DWORD>(), result.get<std::uint64_t>(), false, 0, 0};
    const auto copied_present = result.get<std::uint32_t>(); if (copied_present > 1) throw Fatal(ERROR_INVALID_DATA, "CNG result-size pointer changed presence.");
    receipt.copied_present = copied_present != 0; receipt.copied = result.get<std::uint32_t>(); receipt.output_length = result.get<std::uint32_t>(); result.finish();
    if (receipt.copied_present != (copied != nullptr) || receipt.output_length != buffers[3].length ||
        receipt.created && (receipt.status < 0 || operation != 1 && operation != 3))
        throw Fatal(ERROR_INVALID_DATA, "CNG service result changed the genuine original API signature.");
    // Copy actual provider buffer changes on both success and failed NTSTATUS;
    // BUFFER_TOO_SMALL, size queries and unrelated unchanged bytes stay distinct.
    if (output_destination) for (std::uint32_t offset = 0; offset < receipt.output_length;) {
        const auto count = (std::min)(cng_transfer_bytes, receipt.output_length - offset);
        Bytes read; read.put(id); read.put(offset); read.put(count);
        auto bytes = cng_client_request(CngServiceStep::read, read); Reader reader{bytes.data};
        if (reader.get<std::uint32_t>() != count) throw Fatal(ERROR_INVALID_DATA, "CNG service output extent changed before local publication.");
        const auto* source = reader.bytes(count); reader.finish(); auto* destination = static_cast<std::uint8_t*>(output_destination) + offset;
        expression_writable(destination, count); std::memcpy(destination, source, count); offset += count;
    }
    if (copied) *copied = receipt.copied;
    auto released = cng_client_request(CngServiceStep::release, request); Reader release{released.data};
    if (release.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "CNG result invocation did not retire after local publication.");
    release.finish(); return receipt;
}
void* cng_client_publish_handle(const CngCallerResult& result, bool hash, std::uint64_t algorithm) {
    if (result.status < 0 || !result.created) throw Fatal(ERROR_INVALID_HANDLE, "CNG capability publication lacks actual successful Windows construction.");
    auto& owner = cng_client_owner();
    auto token = std::make_unique<CngImportedHandle>(CngImportedHandle{owner.service_generation, result.created, algorithm, hash, true});
    auto* pointer = token.get(); const auto key = address(pointer);
    if (!key || !owner.tokens.emplace(key, std::move(token)).second)
        throw Fatal(ERROR_INVALID_HANDLE, "CNG local capability aliases a retained live or retired address.");
    return pointer;
}
void cng_client_publish_readback(void** destination, void* token) {
    const auto& active = cng_caller_handle(token); Bytes request;
    request.put(active.id); request.put(address(token)); request.put(address(destination));
    request.put(active.hash ? 1U : 0U); request.put(address(nvse_copy(destination)));
    auto response = cng_client_request(CngServiceStep::publish_local, request); Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "CNG original output slot has no correlated native readback receipt.");
    reader.finish();
}
NTSTATUS WINAPI cng_client_open(BCRYPT_ALG_HANDLE* destination, LPCWSTR algorithm, LPCWSTR implementation, ULONG flags) {
    const auto caller_error = GetLastError();
    return cng_client_guard([&] {
        if (destination) { expression_writable(destination, sizeof(*destination)); cng_shared_handle_destination(destination, sizeof(*destination)); }
        const auto name = cng_caller_string(algorithm), provider = cng_caller_string(implementation);
        const std::array<CngCallerBuffer, 4> buffers{{{algorithm, static_cast<std::uint32_t>(name.size())},
            {implementation, static_cast<std::uint32_t>(provider.size())}, {}, {}}};
        cng_caller_ranges(buffers, destination, destination ? sizeof(*destination) : 0U);
        const auto receipt = cng_client_invoke(1, 0, flags, caller_error, 0, destination != nullptr, nullptr,
            buffers, nullptr);
        if (receipt.status >= 0) {
            if (!destination) throw Fatal(ERROR_INVALID_DATA, "CNG algorithm result has no actual original destination.");
            *destination = cng_client_publish_handle(receipt, false, 0);
            cng_client_publish_readback(destination, *destination);
        }
        // Failed output-handle contents are SDK-undefined, and remain the
        // original bytes. No remote/garbage Windows pointer is copied locally.
        SetLastError(receipt.last_error); return receipt.status;
    });
}
NTSTATUS WINAPI cng_client_property(BCRYPT_HANDLE handle, LPCWSTR property, PUCHAR destination, ULONG length, ULONG* copied, ULONG flags) {
    const auto caller_error = GetLastError();
    return cng_client_guard([&] {
        const auto active = cng_caller_handle(handle); const auto name = cng_caller_string(property);
        const auto receipt = cng_client_invoke(2, active.id, flags, caller_error, 0, false, copied,
            {{{property, static_cast<std::uint32_t>(name.size())}, {}, {}, {destination, length}}}, destination);
        SetLastError(receipt.last_error); return receipt.status;
    });
}
NTSTATUS WINAPI cng_client_create(BCRYPT_ALG_HANDLE algorithm, BCRYPT_HASH_HANDLE* destination, PUCHAR object, ULONG object_bytes,
    PUCHAR secret, ULONG secret_bytes, ULONG flags) {
    const auto caller_error = GetLastError();
    return cng_client_guard([&] {
        if (cng_caller_overlap(object, object_bytes, destination, destination ? sizeof(*destination) : 0U))
            throw Fatal(ERROR_NOT_SUPPORTED, "Opaque CNG output handle aliases original hash-object bytes.");
        const auto active = cng_caller_handle(algorithm, 0); if (destination) { expression_writable(destination, sizeof(*destination)); cng_shared_handle_destination(destination, sizeof(*destination)); }
        const std::array<CngCallerBuffer, 4> buffers{{{}, {}, {secret, secret_bytes}, {}}};
        cng_caller_ranges(buffers, destination, destination ? sizeof(*destination) : 0U);
        const auto receipt = cng_client_invoke(3, active.id, flags, caller_error, object_bytes, destination != nullptr, nullptr,
            buffers, nullptr, object);
        if (receipt.status >= 0) {
            if (!destination) throw Fatal(ERROR_INVALID_DATA, "CNG hash result has no actual original destination.");
            *destination = cng_client_publish_handle(receipt, true, active.id);
            cng_client_publish_readback(destination, *destination);
        }
        SetLastError(receipt.last_error); return receipt.status;
    });
}
NTSTATUS WINAPI cng_client_data(BCRYPT_HASH_HANDLE handle, PUCHAR bytes, ULONG length, ULONG flags) {
    const auto caller_error = GetLastError();
    return cng_client_guard([&] {
        const auto active = cng_caller_handle(handle, 1);
        const auto receipt = cng_client_invoke(4, active.id, flags, caller_error, 0, false, nullptr,
            {{{}, {}, {bytes, length}, {}}}, nullptr); SetLastError(receipt.last_error); return receipt.status;
    });
}
NTSTATUS WINAPI cng_client_finish(BCRYPT_HASH_HANDLE handle, PUCHAR bytes, ULONG length, ULONG flags) {
    const auto caller_error = GetLastError();
    return cng_client_guard([&] {
        const auto active = cng_caller_handle(handle, 1);
        const auto receipt = cng_client_invoke(5, active.id, flags, caller_error, 0, false, nullptr,
            {{{}, {}, {}, {bytes, length}}}, bytes); SetLastError(receipt.last_error); return receipt.status;
    });
}
NTSTATUS WINAPI cng_client_destroy(BCRYPT_HASH_HANDLE handle) {
    const auto caller_error = GetLastError();
    return cng_client_guard([&] {
        auto& active = cng_caller_handle(handle, 1);
        const auto receipt = cng_client_invoke(6, active.id, 0, caller_error, 0, false, nullptr, {}, nullptr);
        if (receipt.status >= 0) { active.live = false; cng_shared_hash_destroyed(active.id); } SetLastError(receipt.last_error); return receipt.status;
    });
}
NTSTATUS WINAPI cng_client_close(BCRYPT_ALG_HANDLE handle, ULONG flags) {
    const auto caller_error = GetLastError();
    return cng_client_guard([&] {
        auto& active = cng_caller_handle(handle, 0);
        const auto receipt = cng_client_invoke(7, active.id, flags, caller_error, 0, false, nullptr, {}, nullptr);
        if (receipt.status >= 0) active.live = false; SetLastError(receipt.last_error); return receipt.status;
    });
}
void* cng_client_entry(const std::string& name) {
    struct Entry { const char* name; void* pointer; };
    const Entry entries[] = {
        {"BCryptOpenAlgorithmProvider", reinterpret_cast<void*>(cng_client_open)}, {"BCryptGetProperty", reinterpret_cast<void*>(cng_client_property)},
        {"BCryptCreateHash", reinterpret_cast<void*>(cng_client_create)}, {"BCryptHashData", reinterpret_cast<void*>(cng_client_data)},
        {"BCryptFinishHash", reinterpret_cast<void*>(cng_client_finish)}, {"BCryptDestroyHash", reinterpret_cast<void*>(cng_client_destroy)},
        {"BCryptCloseAlgorithmProvider", reinterpret_cast<void*>(cng_client_close)} };
    for (const auto& entry : entries) if (name == entry.name) return entry.pointer;
    return nullptr;
}
void cng_client_require_original_provider(HMODULE actual) {
    if (!actual || !io_owner().crypto) throw Fatal(ERROR_INVALID_HANDLE, "Original CNG import lacks its real mapped source module.");
    std::vector<wchar_t> mapped(32768); const auto count = GetModuleFileNameW(actual, mapped.data(), static_cast<DWORD>(mapped.size()));
    if (!count || count >= mapped.size()) throw Fatal(GetLastError(), "Original CNG import provider has no complete mapped filename.");
    crt_require_provider_file(io_owner().crypto->path, mapped.data());
}
void* cng_client_import_entry(const std::string& library, const std::string& name, void* original) {
    if (library != "bcrypt.dll") return nullptr;
    auto* replacement = cng_client_entry(name);
    if (!replacement || !original) throw Fatal(ERROR_NOT_SUPPORTED, "Original imported CNG arm has no genuine public callable owner.");
    HMODULE actual = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(original), &actual)) throw Fatal(GetLastError(), "Original CNG import pointer has no actual mapped owner.");
    if (import_provider_original_entry(actual, library, name, original)) return original;
    cng_client_require_original_provider(actual);
    if (reinterpret_cast<void*>(GetProcAddress(actual, name.c_str())) != original)
        throw Fatal(ERROR_INVALID_DATA, "Original CNG IAT pointer differs from the actual selected public export.");
    return replacement;
}
void* cng_client_dynamic_entry(HMODULE module, const std::string& name) {
    if (!io_owner().crypto || name.rfind("BCrypt", 0) != 0) return nullptr;
    const auto selected = GetModuleHandleW(L"bcrypt.dll"); if (!selected || module != selected) return nullptr;
    if (auto* provider = import_provider_for(module)) return import_provider_export(*provider, name);
    cng_client_require_original_provider(module); auto* entry = cng_client_entry(name);
    if (!entry) throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic original CNG export has no genuine callable service owner.");
    return entry;
}
void cng_client_flush_detach() {
    auto& io = io_owner(); if (!io.cng_client || !io.cng_client->entered || io.cng_client->retired) return;
    for (const auto& token : io.cng_client->tokens) if (token.second->live) return;
    cng_shared_flush_detach();
    Bytes request; auto response = cng_client_request(CngServiceStep::retire, request); Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 0 || reader.get<std::uint32_t>() != 0 || reader.get<std::uint32_t>() != 1 || reader.get<std::uint32_t>() != 0 ||
        reader.get<std::uint32_t>() != 0 || reader.get<std::uint32_t>() != 1 || reader.get<std::uint32_t>() != 0)
        throw Fatal(ERROR_INVALID_DATA, "CNG service did not genuinely release its provider after all caller handles.");
    reader.finish(); io.cng_client->retired = true;
}
void cng_client_require_retired() {
    const auto& io = io_owner();
    if (io.cng_client && io.cng_client->entered && !io.cng_client->retired)
        throw Fatal(ERROR_BUSY, "Original CNG client retains live service/import capabilities.");
}
