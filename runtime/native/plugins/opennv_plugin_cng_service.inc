// Included after the domain's actual Frame/Reader/reply and file identity owner.
// Compiled in both images, but admitted only by the service-only dispatch gate.
CngSystemService& cng_service_owner() {
    if (!state || GetCurrentThreadId() != state->thread || !state->cng_service ||
        !state->cng_service->prepared || state->cng_service->retired)
        throw Fatal(ERROR_INVALID_STATE, "CNG service has no admitted current provider/process owner.");
    return *state->cng_service;
}
bool cng_presence(Reader& reader) {
    const auto value = reader.get<std::uint32_t>();
    if (value > 1) throw Fatal(ERROR_INVALID_DATA, "CNG pointer presence is not Boolean.");
    return value != 0;
}
void cng_service_system_path(const std::wstring& path, const wchar_t* filename) {
    wchar_t directory[32768]{};
    const auto length = GetSystemDirectoryW(directory, static_cast<UINT>(std::size(directory)));
    if (!length || length >= std::size(directory)) throw Fatal(GetLastError(), "CNG service has no actual x86 system directory.");
    const std::wstring expected = std::wstring(directory, length) + L"\\" + filename;
    // Canonical SysWOW64 and redirected System32 spellings may name one file.
    crt_require_provider_file(path, expected.c_str());
}
void cng_service_prepare(const Frame& frame, Reader& reader) {
#ifndef OPENNV_SYSTEM_CNG_SERVICE
    (void)frame; (void)reader;
    throw Fatal(ERROR_NOT_SUPPORTED, "Original-module domain cannot construct a broader-authority Windows provider.");
#else
    if (state->cng_service) throw Fatal(ERROR_INVALID_STATE, "CNG service preparation cannot replay an entered prefix.");
    auto owner = std::make_unique<CngSystemService>();
    owner->provider_path = io_from_utf8(reader.text()); owner->provider_sha = reader.text();
    owner->primitives_path = io_from_utf8(reader.text()); owner->primitives_sha = reader.text(); reader.finish();
    if (owner->provider_sha.size() != 64 || owner->primitives_sha.size() != 64)
        throw Fatal(ERROR_INVALID_DATA, "CNG service lacks retained exact Windows image identities.");
    cng_service_system_path(owner->provider_path, L"bcrypt.dll");
    cng_service_system_path(owner->primitives_path, L"bcryptPrimitives.dll");
    state->cng_service = std::move(owner); auto& current = *state->cng_service;
    current.prepare_entered = true;
    try {
        // Applies only to this first-party service. The original child token and
        // original input ACLs are unchanged. Non-Microsoft custom providers fail.
        PROCESS_MITIGATION_BINARY_SIGNATURE_POLICY required{}; required.MicrosoftSignedOnly = 1;
        if (!SetProcessMitigationPolicy(ProcessSignaturePolicy, &required, sizeof(required)))
            throw Fatal(GetLastError(), "CNG service could not enforce Microsoft image admission.");
        PROCESS_MITIGATION_BINARY_SIGNATURE_POLICY observed{};
        if (!GetProcessMitigationPolicy(GetCurrentProcess(), ProcessSignaturePolicy, &observed, sizeof(observed)))
            throw Fatal(GetLastError(), "CNG service signature policy did not read back.");
        if (!observed.MicrosoftSignedOnly) throw Fatal(ERROR_INVALID_DATA, "CNG service signature policy readback is not enforced.");
        current.provider = LoadLibraryExW(io_native_path(current.provider_path).c_str(), nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!current.provider) throw Fatal(GetLastError(), "Selected system CNG frontend did not initialize in its service.");
        std::vector<wchar_t> mapped(32768); auto count = GetModuleFileNameW(current.provider, mapped.data(), static_cast<DWORD>(mapped.size()));
        if (!count || count >= mapped.size()) throw Fatal(GetLastError(), "CNG provider has no complete actual mapped path.");
        crt_require_provider_file(current.provider_path, mapped.data());
        // Frontend admission does not imply an eager primitives constructor.
        // Retain this service's actual independent backend loader reference.
        current.primitives = LoadLibraryExW(io_native_path(current.primitives_path).c_str(), nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!current.primitives) throw Fatal(GetLastError(), "Selected CNG service primitives did not initialize.");
        count = GetModuleFileNameW(current.primitives, mapped.data(), static_cast<DWORD>(mapped.size()));
        if (!count || count >= mapped.size()) throw Fatal(GetLastError(), "CNG primitives lost its complete mapped path.");
        crt_require_provider_file(current.primitives_path, mapped.data());
        current.open = function_pointer<decltype(current.open)>(GetProcAddress(current.provider, "BCryptOpenAlgorithmProvider"));
        current.property = function_pointer<decltype(current.property)>(GetProcAddress(current.provider, "BCryptGetProperty"));
        current.create = function_pointer<decltype(current.create)>(GetProcAddress(current.provider, "BCryptCreateHash"));
        current.data = function_pointer<decltype(current.data)>(GetProcAddress(current.provider, "BCryptHashData"));
        current.finish = function_pointer<decltype(current.finish)>(GetProcAddress(current.provider, "BCryptFinishHash"));
        current.destroy = function_pointer<decltype(current.destroy)>(GetProcAddress(current.provider, "BCryptDestroyHash"));
        current.close = function_pointer<decltype(current.close)>(GetProcAddress(current.provider, "BCryptCloseAlgorithmProvider"));
        if (!current.open || !current.property || !current.create || !current.data || !current.finish || !current.destroy || !current.close)
            throw Fatal(ERROR_PROC_NOT_FOUND, "CNG service selected image lacks a reached public SDK export.");
        current.prepared = true;
        Bytes receipt; receipt.put(1U); receipt.put(1U); receipt.text(io_utf8(current.provider_path)); receipt.text(current.provider_sha);
        receipt.text(io_utf8(current.primitives_path)); receipt.text(current.primitives_sha);
        reply(frame, ERROR_SUCCESS, "", receipt);
    } catch (const Fatal& failure) { current.prepare_error = failure.code; throw; }
      catch (...) { current.prepare_error = ERROR_INVALID_DATA; throw; }
#endif
}
CngInvocation& cng_invocation(CngSystemService& owner, std::uint64_t id) {
    const auto found = owner.invocations.find(id);
    if (!id || found == owner.invocations.end()) throw Fatal(ERROR_INVALID_HANDLE, "CNG invocation is foreign or retired.");
    return found->second;
}
LPCWSTR cng_string(CngTransfer& buffer) {
    if (!buffer.present) return nullptr;
    if (buffer.length < sizeof(wchar_t) || buffer.length % sizeof(wchar_t))
        throw Fatal(ERROR_INVALID_DATA, "CNG wide-string extent has no complete terminator.");
    // Preserve raw UTF-16 code units, including unpaired surrogates. Do not turn
    // an SDK string into a guessed UTF-8 identifier.
    auto* value = reinterpret_cast<wchar_t*>(buffer.pointer());
    const auto count = buffer.length / sizeof(wchar_t);
    for (std::size_t at = 0; at < count; ++at)
        if (!value[at] && at != count - 1) throw Fatal(ERROR_INVALID_DATA, "CNG wide-string contains bytes beyond its first terminator.");
    if (value[count - 1]) throw Fatal(ERROR_INVALID_DATA, "CNG wide-string terminator is missing.");
    return value;
}
void cng_service_execute(CngSystemService& owner, CngInvocation& call) {
    if (call.entered) throw Fatal(ERROR_INVALID_STATE, "An entered Windows CNG invocation cannot replay.");
    for (const auto& buffer : call.buffers)
        if (buffer.written != buffer.length) throw Fatal(ERROR_HANDLE_EOF, "CNG invocation has incomplete source buffer bytes.");
    const auto name = cng_string(call.buffers[0]); const auto implementation = cng_string(call.buffers[1]);
    auto& input_bytes = call.buffers[2]; auto& output_bytes = call.buffers[3];
    auto algorithm = owner.algorithms.end(); auto hash = owner.hashes.end();
    if (call.operation != 1) {
        algorithm = owner.algorithms.find(call.handle); hash = owner.hashes.find(call.handle);
        if ((algorithm != owner.algorithms.end()) == (hash != owner.hashes.end()))
            throw Fatal(ERROR_INVALID_HANDLE, "CNG SDK target is not one live service-owned capability.");
        if ((call.operation == 3 || call.operation == 7) && algorithm == owner.algorithms.end() ||
            call.operation >= 4 && call.operation <= 6 && hash == owner.hashes.end())
            throw Fatal(ERROR_INVALID_HANDLE, "CNG SDK target has the wrong genuine object kind.");
    }
    if (call.operation < 1 || call.operation > 7 || call.operation == 1 && call.handle ||
        call.operation != 3 && call.object_bytes || call.operation != 1 && call.buffers[1].present ||
        call.operation != 1 && call.operation != 2 && call.buffers[0].present ||
        call.operation != 3 && call.operation != 4 && input_bytes.present ||
        call.operation != 2 && call.operation != 5 && output_bytes.present ||
        call.operation != 1 && call.operation != 3 && call.destination_present ||
        call.operation != 2 && call.copied_present)
        throw Fatal(ERROR_INVALID_DATA, "CNG invocation mixes unrelated ABI arguments.");
    cng_shared_service_before(owner, call);
    const auto created_id = call.operation == 1 || call.operation == 3 ? ++owner.next_handle : 0;
    if ((call.operation == 1 || call.operation == 3) && !created_id)
        throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "CNG actual handle identity exhausted.");
    BCRYPT_ALG_HANDLE new_algorithm = nullptr; BCRYPT_HASH_HANDLE new_hash = nullptr;
    call.entered = true; SetLastError(call.caller_error);
    switch (call.operation) {
    case 1: call.status = owner.open(call.destination_present ? &new_algorithm : nullptr, name, implementation, call.flags); break;
    case 2: {
        ULONG copied = call.copied; auto* size_slot = call.shared_copied ?
            reinterpret_cast<ULONG*>(cng_shared_service_pointer(owner, call.shared_copied)) : &copied;
        call.status = owner.property(algorithm != owner.algorithms.end() ? algorithm->second.handle : hash->second.handle,
            name, output_bytes.pointer(), output_bytes.length, call.copied_present ? size_slot : nullptr, call.flags);
        call.copied = call.copied_present ? *size_slot : copied; break;
    }
    case 3: call.status = owner.create(algorithm->second.handle, call.destination_present ? &new_hash : nullptr,
        cng_shared_service_pointer(owner, call.shared_object), call.object_bytes, input_bytes.pointer(), input_bytes.length, call.flags); break;
    case 4: call.status = owner.data(hash->second.handle, input_bytes.pointer(), input_bytes.length, call.flags); break;
    case 5: call.status = owner.finish(hash->second.handle, output_bytes.pointer(), output_bytes.length, call.flags); break;
    case 6: call.status = owner.destroy(hash->second.handle); break;
    case 7: call.status = owner.close(algorithm->second.handle, call.flags); break;
    default: throw Fatal(ERROR_INVALID_FUNCTION, "CNG operation has no actual SDK consumer.");
    }
    call.last_error = GetLastError(); call.completed = true;
    if (call.status >= 0) {
        if (call.operation == 1) {
            if (!new_algorithm || !call.destination_present) throw Fatal(ERROR_INVALID_HANDLE, "Successful Windows algorithm construction returned no owned handle.");
            // If allocation fails here the real acquired handle is retained by
            // the terminating service process, not acknowledged as a result.
            owner.algorithms.emplace(created_id, CngAlgorithmLease{new_algorithm, call.flags}); call.created = created_id;
        } else if (call.operation == 3) {
            if (!new_hash || !call.destination_present) throw Fatal(ERROR_INVALID_HANDLE, "Successful Windows hash construction returned no owned handle.");
            owner.hashes.emplace(created_id, CngHashLease{new_hash, call.handle}); call.created = created_id;
        } else if (call.operation == 6) owner.hashes.erase(hash);
        else if (call.operation == 7) owner.algorithms.erase(algorithm);
    }
    cng_shared_service_after(owner, call);
}
void cng_service_retire(CngSystemService& owner, const Frame& frame, bool abandoned) {
    Bytes receipt;
    bool more = false;
    if (abandoned) {
        // Parent may request this only after it observed exact original-child
        // closure. These are real emergency calls, never orderly caller receipts.
        std::uint32_t count = 0; receipt.put(count);
        constexpr std::uint32_t budget = (max_payload - 256) / 16;
        for (auto at = owner.hashes.begin(); at != owner.hashes.end() && count < budget;) {
            if (!owner.emergency_attempted.insert(at->first).second) { ++at; continue; }
            const auto status = owner.destroy(at->second.handle);
            receipt.put(6U); receipt.put(at->first); receipt.put(status);
            ++count;
            if (status >= 0) { cng_shared_service_hash_abandoned(owner, at->first); at = owner.hashes.erase(at); } else ++at;
        }
        const auto hashes_pending = std::any_of(owner.hashes.begin(), owner.hashes.end(), [&](const auto& item) {
            return owner.emergency_attempted.count(item.first) == 0;
        });
        // Hashes retain their algorithm provider until every hash close was
        // attempted; failed hashes prevent closing their provider as cleanup.
        for (auto at = owner.algorithms.begin(); at != owner.algorithms.end() && count < budget && !hashes_pending;) {
            if (owner.emergency_attempted.count(at->first)) { ++at; continue; }
            const auto referenced = std::any_of(owner.hashes.begin(), owner.hashes.end(), [&](const auto& item) { return item.second.algorithm == at->first; });
            if (referenced) { ++at; continue; }
            owner.emergency_attempted.insert(at->first);
            const auto status = owner.close(at->second.handle, 0);
            receipt.put(7U); receipt.put(at->first); receipt.put(status);
            ++count;
            if (status >= 0) at = owner.algorithms.erase(at); else ++at;
        }
        more = hashes_pending || std::any_of(owner.algorithms.begin(), owner.algorithms.end(), [&](const auto& item) {
            return owner.emergency_attempted.count(item.first) == 0 &&
                !std::any_of(owner.hashes.begin(), owner.hashes.end(), [&](const auto& hash) { return hash.second.algorithm == item.first; });
        });
        std::memcpy(receipt.data.data(), &count, sizeof(count));
        for (const auto& invocation : owner.invocations) cng_shared_service_release_invocation(owner, invocation.second);
        owner.invocations.clear();
        cng_shared_service_abandon(owner);
    } else {
        receipt.put(0U);
        if (!owner.invocations.empty()) throw Fatal(ERROR_BUSY, "CNG provider has unpublished invocation/result buffers.");
    }
    receipt.put(more ? 1U : 0U);
    if (more || !owner.hashes.empty() || !owner.algorithms.empty()) {
        receipt.put(0U); receipt.put(static_cast<std::uint32_t>(owner.hashes.size()));
        receipt.put(static_cast<std::uint32_t>(owner.algorithms.size()));
        receipt.put(0U); receipt.put(0U); reply(frame, ERROR_SUCCESS, "", receipt); return;
    }
    cng_shared_service_require_retired(owner);
    const auto provider_released = !owner.provider || FreeLibrary(owner.provider);
    auto error = provider_released ? ERROR_SUCCESS : GetLastError();
    if (provider_released) owner.provider = nullptr;
    const auto primitives_released = provider_released && (!owner.primitives || FreeLibrary(owner.primitives));
    if (primitives_released) owner.primitives = nullptr;
    else if (provider_released) error = GetLastError();
    const auto released = provider_released && primitives_released;
    if (released) owner.retired = true;
    receipt.put(released ? 1U : 0U); receipt.put(0U); receipt.put(0U); receipt.put(released ? 1U : 0U); receipt.put(error);
    reply(frame, ERROR_SUCCESS, "", receipt);
}
void cng_system_service(const Frame& frame) {
#ifndef OPENNV_SYSTEM_CNG_SERVICE
    (void)frame;
    throw Fatal(ERROR_NOT_SUPPORTED, "CNG service operations cannot execute in an original-module child.");
#else
    Reader reader{frame.payload}; const auto step = static_cast<CngServiceStep>(reader.get<std::uint32_t>());
    if (step == CngServiceStep::prepare) { cng_service_prepare(frame, reader); return; }
    if (step == CngServiceStep::shared_bind || step == CngServiceStep::shared_release || step == CngServiceStep::shared_begin) {
        cng_shared_service_step(frame, reader, step); return; }
    auto& owner = cng_service_owner(); Bytes receipt;
    if (step == CngServiceStep::begin) {
        CngInvocation call; call.id = ++owner.next_invocation;
        if (!call.id) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "CNG invocation identity exhausted.");
        call.operation = reader.get<std::uint32_t>(); call.handle = reader.get<std::uint64_t>();
        call.flags = reader.get<std::uint32_t>(); call.caller_error = reader.get<std::uint32_t>();
        call.object_bytes = reader.get<std::uint32_t>(); call.destination_present = cng_presence(reader);
        call.copied_present = cng_presence(reader); call.copied = reader.get<std::uint32_t>();
        for (auto& buffer : call.buffers) { const auto present = cng_presence(reader); const auto count = reader.get<std::uint32_t>(); buffer.allocate(present, count); }
        reader.finish(); const auto id = call.id; owner.invocations.emplace(id, std::move(call)); receipt.put(id);
    } else if (step == CngServiceStep::write) {
        auto& call = cng_invocation(owner, reader.get<std::uint64_t>()); const auto role = reader.get<std::uint32_t>();
        const auto offset = reader.get<std::uint32_t>(); const auto count = reader.get<std::uint32_t>(); const auto* bytes = reader.bytes(count); reader.finish();
        if (call.entered || role >= call.buffers.size()) throw Fatal(ERROR_INVALID_STATE, "CNG source upload targets an entered invocation or foreign buffer.");
        auto& buffer = call.buffers[role];
        if (!buffer.present || offset != buffer.written || count > buffer.length - buffer.written)
            throw Fatal(ERROR_INVALID_DATA, "CNG source upload skips/repeats or exceeds original bytes.");
        if (count) std::memcpy(buffer.pointer() + offset, bytes, count); buffer.written += count; receipt.put(buffer.written);
    } else if (step == CngServiceStep::execute) {
        auto& call = cng_invocation(owner, reader.get<std::uint64_t>()); reader.finish(); cng_service_execute(owner, call);
        receipt.put(call.status); receipt.put(call.last_error); receipt.put(call.created);
        receipt.put(call.copied_present ? 1U : 0U); receipt.put(call.copied); receipt.put(call.buffers[3].length);
    } else if (step == CngServiceStep::read) {
        auto& call = cng_invocation(owner, reader.get<std::uint64_t>()); const auto offset = reader.get<std::uint32_t>();
        const auto count = reader.get<std::uint32_t>(); reader.finish(); auto& buffer = call.buffers[3];
        if (!call.completed || !buffer.present || offset > buffer.length || count > buffer.length - offset || count > max_payload - 64)
            throw Fatal(ERROR_INVALID_DATA, "CNG result read has no complete bounded Windows output extent.");
        receipt.put(count); receipt.data.insert(receipt.data.end(), buffer.pointer() + offset, buffer.pointer() + offset + count);
    } else if (step == CngServiceStep::release) {
        const auto id = reader.get<std::uint64_t>(); reader.finish(); auto& call = cng_invocation(owner, id);
        if (!call.completed) throw Fatal(ERROR_INVALID_STATE, "Unexecuted CNG invocation cannot publish a completed release.");
        cng_shared_service_release_invocation(owner, call); owner.invocations.erase(id); receipt.put(1U);
    } else if (step == CngServiceStep::retire || step == CngServiceStep::abandon_after_client_exit) {
        reader.finish(); cng_service_retire(owner, frame, step == CngServiceStep::abandon_after_client_exit); return;
    } else throw Fatal(ERROR_INVALID_FUNCTION, "CNG service suboperation has no owner.");
    reply(frame, ERROR_SUCCESS, "", receipt);
#endif
}
void cng_system_service_require_retired() {
    if (state->cng_service) cng_shared_service_require_retired(*state->cng_service);
    if (state->cng_service && (!state->cng_service->retired || state->cng_service->provider || state->cng_service->primitives ||
        !state->cng_service->hashes.empty() || !state->cng_service->algorithms.empty() || !state->cng_service->invocations.empty()))
        throw Fatal(ERROR_BUSY, "CNG service still owns genuine provider/object/result resources.");
}
