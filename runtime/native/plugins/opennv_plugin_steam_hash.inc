// First-party original-file hashing is C# authority. The restricted x86 child
// only identifies its actually opened retained handle and correlates the real
// source callback; no BCrypt import, admission, SDK handle or digest default.
constexpr std::uint32_t steam_source_hash_event = 0x601;
std::uint64_t steam_source_time(const FILETIME& value) {
    return (static_cast<std::uint64_t>(value.dwHighDateTime) << 32) | value.dwLowDateTime;
}
SteamSourceFileIdentity steam_source_identity(HANDLE source_input) {
    SetLastError(ERROR_SUCCESS); const auto type = GetFileType(source_input);
    if (type != FILE_TYPE_DISK) throw Fatal(GetLastError() ? GetLastError() : ERROR_INVALID_DATA, "Platform source is not an actual disk file.");
    BY_HANDLE_FILE_INFORMATION ordinary{}; FILE_ID_INFO extended{};
    if (!GetFileInformationByHandle(source_input, &ordinary))
        throw Fatal(GetLastError(), "Native original-file BY_HANDLE_FILE_INFORMATION query failed.");
    if (!GetFileInformationByHandleEx(source_input, FileIdInfo, &extended, sizeof(extended)))
        throw Fatal(GetLastError(), "Native original-file 128-bit identity query failed.");
    SteamSourceFileIdentity result;
    result.type = type; result.attributes = ordinary.dwFileAttributes;
    result.created = steam_source_time(ordinary.ftCreationTime); result.written = steam_source_time(ordinary.ftLastWriteTime);
    result.volume = ordinary.dwVolumeSerialNumber;
    result.index = (static_cast<std::uint64_t>(ordinary.nFileIndexHigh) << 32) | ordinary.nFileIndexLow;
    result.bytes = (static_cast<std::uint64_t>(ordinary.nFileSizeHigh) << 32) | ordinary.nFileSizeLow;
    result.extended_volume = extended.VolumeSerialNumber;
    std::memcpy(&result.id_low, extended.FileId.Identifier, sizeof(result.id_low));
    std::memcpy(&result.id_high, extended.FileId.Identifier + sizeof(result.id_low), sizeof(result.id_high));
    if ((result.attributes & FILE_ATTRIBUTE_DIRECTORY) || !result.bytes || result.bytes > 512ULL * 1024 * 1024)
        throw Fatal(ERROR_INVALID_DATA, "Actual original source has no bounded image-file extent.");
    return result;
}
bool steam_source_same_identity(const SteamSourceFileIdentity& a, const SteamSourceFileIdentity& b) {
    return a.type == b.type && a.attributes == b.attributes && a.created == b.created && a.written == b.written &&
        a.volume == b.volume && a.index == b.index && a.bytes == b.bytes && a.extended_volume == b.extended_volume &&
        a.id_low == b.id_low && a.id_high == b.id_high;
}
void steam_source_identity_bytes(Bytes& bytes, const SteamSourceFileIdentity& value) {
    bytes.put(value.type); bytes.put(value.attributes); bytes.put(value.created); bytes.put(value.written);
    bytes.put(value.volume); bytes.put(value.index); bytes.put(value.bytes); bytes.put(value.extended_volume);
    bytes.put(value.id_low); bytes.put(value.id_high);
}
SteamSourceFileIdentity steam_source_identity_reply(Reader& reader) {
    SteamSourceFileIdentity value;
    value.type = reader.get<std::uint32_t>(); value.attributes = reader.get<std::uint32_t>();
    value.created = reader.get<std::uint64_t>(); value.written = reader.get<std::uint64_t>(); value.volume = reader.get<std::uint32_t>();
    value.index = reader.get<std::uint64_t>(); value.bytes = reader.get<std::uint64_t>(); value.extended_volume = reader.get<std::uint64_t>();
    value.id_low = reader.get<std::uint64_t>(); value.id_high = reader.get<std::uint64_t>(); return value;
}
std::string steam_hash_source(HANDLE source_input, const std::wstring& path, SteamSourceFileKind kind) {
    if (!state || !state->steam || state->calls.empty() || GetCurrentThreadId() != state->thread ||
        state->steam->source_request != state->calls.back())
        throw Fatal(ERROR_INVALID_STATE, "Original source hashing has no genuine entered platform frame/file lease.");
    auto& runtime = *state->steam;
    if (runtime.source_hashes.size() >= 65) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Platform source image denominator exceeded.");
    auto retained = std::make_unique<SteamSourceHash>(); auto* actual = retained.get();
    retained->ordinal = runtime.source_hashes.size() + 1; retained->request = runtime.source_request;
    retained->operation = static_cast<std::uint32_t>(runtime.attempted); retained->kind = kind; retained->path = path;
    runtime.source_hashes.push_back(std::move(retained)); auto& owner = *actual;
    try {
        owner.before = steam_source_identity(source_input); owner.before_copied = true; owner.phase = SteamSourceHashPhase::identified;
        Bytes request; request.put(1U); request.put(runtime.session); request.put(owner.ordinal); request.put(owner.operation);
        request.put(static_cast<std::uint32_t>(kind)); request.text(steam_utf8(path)); steam_source_identity_bytes(request, owner.before);
        owner.phase = SteamSourceHashPhase::callback_entered;
        const auto callback_before = state->next_callback;
        auto reply_bytes = perform_callback_bytes(Kind::nvse_callback, steam_source_hash_event, request);
        if (state->next_callback == callback_before + 1 && state->next_callback < (1ULL << 63))
            owner.callback = (1ULL << 63) | state->next_callback;
        if (callback_fault.load()) throw Fatal(callback_fault.load(), "Original-file hashing retained its genuine managed callback refusal.");
        // The helper owns this exact reply identity. No original game/NVSE
        // object or invented Main invocation is needed to hash our source file.
        if (!owner.callback) throw Fatal(ERROR_INVALID_DATA, "Original-file hashing lost its single actual callback allocation.");
        const auto actual_callback = owner.callback;
        Reader reply_reader{reply_bytes.data};
        const auto schema = reply_reader.get<std::uint32_t>(); const auto generation = reply_reader.get<std::uint64_t>();
        const auto session = reply_reader.get<std::uint64_t>(), ordinal = reply_reader.get<std::uint64_t>();
        const auto waiting = reply_reader.get<std::uint64_t>(), callback = reply_reader.get<std::uint64_t>();
        const auto operation = reply_reader.get<std::uint32_t>(), actual_kind = reply_reader.get<std::uint32_t>();
        const auto checked_identity = steam_source_identity_reply(reply_reader); const auto sha = reply_reader.text(); reply_reader.finish();
        if (schema != 1 || generation != state->generation || session != runtime.session || ordinal != owner.ordinal ||
            waiting != owner.request || callback != actual_callback || operation != owner.operation || actual_kind != static_cast<std::uint32_t>(kind) ||
            !steam_source_same_identity(owner.before, checked_identity) || sha.size() != 64 ||
            std::any_of(sha.begin(), sha.end(), [](char value) { return !((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')); }))
            throw Fatal(ERROR_INVALID_DATA, "C# source hash reply lost its actual waiting call/kernel file identity.");
        owner.sha = sha; owner.phase = SteamSourceHashPhase::managed_returned;
        owner.after = steam_source_identity(source_input); owner.after_copied = true; owner.phase = SteamSourceHashPhase::rechecked;
        if (!steam_source_same_identity(owner.before, owner.after))
            throw Fatal(ERROR_INVALID_DATA, "Actual native original-file handle changed across C# retained-file hashing.");
        owner.phase = SteamSourceHashPhase::complete; return sha;
    }
    catch (const Fatal& failure) { owner.error = failure.code; owner.phase = SteamSourceHashPhase::failed; throw; }
    catch (...) { owner.error = ERROR_INVALID_DATA; owner.phase = SteamSourceHashPhase::failed; throw; }
}
void steam_source_hash_prefix_bytes(Bytes& bytes, const SteamRuntime* owner) {
    bytes.put(owner ? static_cast<std::uint32_t>(owner->source_hashes.size()) : 0U);
    if (!owner) return;
    for (const auto& retained : owner->source_hashes) {
        const auto& value = *retained;
        bytes.put(value.ordinal); bytes.put(value.request); bytes.put(value.callback); bytes.put(value.operation);
        bytes.put(static_cast<std::uint32_t>(value.kind)); bytes.put(static_cast<std::uint32_t>(value.phase));
        bytes.put(value.before_copied ? 1U : 0U); if (value.before_copied) steam_source_identity_bytes(bytes, value.before);
        bytes.put(value.after_copied ? 1U : 0U); if (value.after_copied) steam_source_identity_bytes(bytes, value.after);
        bytes.text(value.sha); bytes.put(value.error);
    }
}
