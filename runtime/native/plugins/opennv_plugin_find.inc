// After actual io_host/path/native destination helpers; no pseudo search HANDLE.
constexpr std::uint32_t io_find_begin_callback = 11, io_find_candidate_callback = 12,
    io_find_result_callback = 13, io_find_close_callback = 14, io_find_backend_callback = 15;
PluginFindRuntime& find_owner() {
    auto& io = io_owner(); if (!io.find) io.find = std::make_unique<PluginFindRuntime>(); return *io.find;
}
PluginFindSearch& find_handle(HANDLE handle) {
    if (handle == INVALID_HANDLE_VALUE || !handle) throw Fatal(ERROR_INVALID_HANDLE, "Find call has no actual search handle.");
    for (const auto& search : find_owner().searches)
        if (search.second->public_handle == handle && !search.second->faulted) return *search.second;
    throw Fatal(ERROR_INVALID_HANDLE, "Find call has a foreign/retired actual search lifetime.");
}
std::uint64_t find_time(const FILETIME& time) {
    return static_cast<std::uint64_t>(time.dwHighDateTime) << 32 | time.dwLowDateTime;
}
std::wstring find_name(const char* text) { return io_ansi(text); }
std::wstring find_name(const wchar_t* text) { return io_wide(text); }
void find_backend(PluginFindSearch& search, std::uint32_t source, std::uint32_t operation, HANDLE handle, bool success, DWORD last) {
    Bytes request; request.put(search.id); request.put(source); request.put(operation); request.put(address(handle));
    request.put(success ? 1U : 0U); request.put(last);
    auto response = io_host(io_find_backend_callback, request); Reader result{response.data};
    if (result.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual native directory backend lost its source/call receipt."); result.finish();
}
template<class Data> bool find_candidate(PluginFindSearch& search, std::uint32_t source_index, const Data& data, bool wide) {
    auto& source = search.sources.at(source_index); Bytes request; request.put(search.id); request.put(source_index);
    request.put(address(source.handle)); request.put(wide ? 1U : 0U); request.put(data.dwFileAttributes);
    request.put(find_time(data.ftCreationTime)); request.put(find_time(data.ftLastAccessTime)); request.put(find_time(data.ftLastWriteTime));
    request.put(static_cast<std::uint64_t>(data.nFileSizeHigh) << 32 | data.nFileSizeLow);
    request.put(data.dwReserved0); request.put(data.dwReserved1);
    request.text(io_utf8(find_name(data.cFileName))); request.text(io_utf8(find_name(data.cAlternateFileName)));
    auto response = io_host(io_find_candidate_callback, request); Reader result{response.data}; const auto admitted = result.get<std::uint32_t>(); result.finish();
    if (admitted > 1) throw Fatal(ERROR_INVALID_DATA, "Actual native find candidate has an invalid C# winner disposition."); return admitted != 0;
}
void find_result(PluginFindSearch& search, std::uint32_t operation, HANDLE handle, bool success, DWORD last) {
    Bytes request; request.put(search.id); request.put(operation); request.put(address(handle)); request.put(success ? 1U : 0U); request.put(last);
    auto response = io_host(io_find_result_callback, request); Reader result{response.data};
    if (result.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual native find destination/public handle lacks its complete result receipt."); result.finish();
}
void find_emit_close(const PluginFindCloseEvent& event) {
    if (io_owner().loader_retiring) { find_owner().pending.push_back(event); return; }
    Bytes request; request.put(event.id); request.put(event.public_handle); request.put(static_cast<std::uint32_t>(event.handles.size()));
    for (const auto& handle : event.handles) { request.put(handle.handle); request.put(handle.result); request.put(handle.error); }
    auto response = io_host(io_find_close_callback, request); Reader result{response.data};
    if (result.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual native find close lacks its complete backend receipt."); result.finish();
}
bool find_close_actual(PluginFindSearch& search, HANDLE public_handle) {
    PluginFindCloseEvent event{search.id, address(public_handle), {}}; bool success = true; DWORD failure = ERROR_SUCCESS, observed_last = ERROR_SUCCESS;
    // Reserve before any actual close so publication cannot lose a closed
    // prefix if allocation throws. The same handle is never closed twice.
    event.handles.reserve(search.sources.size());
    for (auto& source : search.sources) if (source.handle != INVALID_HANDLE_VALUE) {
        const auto handle = source.handle; const auto closed = FindClose(handle); const auto last = GetLastError(); observed_last = last;
        const PluginFindClose receipt{address(handle), closed ? 1U : 0U, last}; event.handles.push_back(receipt);
        if (closed) source.handle = INVALID_HANDLE_VALUE;
        else { success = false; failure = last; find_owner().failures.push_back(receipt); search.faulted = true; }
    }
    if (!event.handles.empty()) find_emit_close(event);
    if (success) find_owner().searches.erase(search.id);
    SetLastError(success ? observed_last : failure); return success;
}
HANDLE find_open(const std::wstring& search_text, PluginFindSearch*& published) {
    Bytes request; request.text(io_utf8(search_text)); auto response = io_host(io_find_begin_callback, request); Reader result{response.data};
    auto search = std::make_unique<PluginFindSearch>(); search->id = result.get<std::uint64_t>(); search->pattern = io_from_utf8(result.text());
    const auto count = result.get<std::uint32_t>(); search->sources.reserve(count);
    for (std::uint32_t at = 0; at < count; ++at) search->sources.push_back({io_native_path(io_from_utf8(result.text()))}); result.finish();
    if (!search->id || search->pattern.empty() || !count || find_owner().searches.count(search->id))
        throw Fatal(ERROR_INVALID_DATA, "Native search has no exact selected source pattern/directory lifetime.");
    const auto id = search->id; published = search.get(); find_owner().searches.emplace(id, std::move(search));
    return INVALID_HANDLE_VALUE;
}
HANDLE find_first_api(const std::wstring& path, WIN32_FIND_DATAW* data) { return FindFirstFileW(path.c_str(), data); }
HANDLE find_first_api(const std::wstring& path, WIN32_FIND_DATAA* data) {
    const auto bytes = io_native_ansi(path); return FindFirstFileA(bytes.c_str(), data);
}
BOOL find_next_api(HANDLE handle, WIN32_FIND_DATAW* data) { return FindNextFileW(handle, data); }
BOOL find_next_api(HANDLE handle, WIN32_FIND_DATAA* data) { return FindNextFileA(handle, data); }
template<class Data> bool find_advance(PluginFindSearch& search, Data* destination, bool wide, DWORD& last) {
    expression_writable(destination, sizeof(Data));
    for (; search.current < search.sources.size(); ++search.current) {
        auto& source = search.sources[search.current]; Data data = nvse_copy(destination); BOOL found = FALSE;
        if (!source.attempted) {
            source.attempted = true; const auto path = source.directory + L"\\" + search.pattern;
            source.handle = find_first_api(path, &data); last = GetLastError(); found = source.handle != INVALID_HANDLE_VALUE;
            if (found && search.public_handle == INVALID_HANDLE_VALUE) search.public_handle = source.handle;
            find_backend(search, static_cast<std::uint32_t>(search.current), 1, source.handle, found != FALSE, last);
        }
        else if (!source.exhausted) {
            found = find_next_api(source.handle, &data); last = GetLastError();
            find_backend(search, static_cast<std::uint32_t>(search.current), 2, source.handle, found != FALSE, last);
        }
        while (found) {
            if (find_candidate(search, static_cast<std::uint32_t>(search.current), data, wide)) {
                expression_write(destination, &data, sizeof(Data)); const auto observed = nvse_copy(destination);
                if (std::memcmp(&observed, &data, sizeof(Data))) throw Fatal(ERROR_INVALID_DATA, "Actual native find destination lost its OS byte equality.");
                return true;
            }
            data = nvse_copy(destination); found = find_next_api(source.handle, &data); last = GetLastError();
            find_backend(search, static_cast<std::uint32_t>(search.current), 2, source.handle, found != FALSE, last);
        }
        if (last != ERROR_FILE_NOT_FOUND && last != ERROR_PATH_NOT_FOUND && last != ERROR_NO_MORE_FILES) {
            search.faulted = true; return false;
        }
        source.exhausted = true;
    }
    return false;
}
template<class Data> HANDLE find_first_actual(const std::wstring& search_text, Data* destination, bool wide) {
    PluginFindSearch* search = nullptr; (void)find_open(search_text, search); DWORD last = ERROR_SUCCESS;
    const auto delivered = find_advance(*search, destination, wide, last); const auto id = search->id;
    if (delivered) { const auto handle = search->public_handle; find_result(*search, 1, handle, true, last); SetLastError(last); return handle; }
    // Keep the constructor owner until close/result receipts have both been
    // consumed. No successful virtual search is made from an OS absence.
    PluginFindCloseEvent event{id, address(INVALID_HANDLE_VALUE), {}}; event.handles.reserve(search->sources.size());
    for (auto& source : search->sources) if (source.handle != INVALID_HANDLE_VALUE) {
        const auto handle = source.handle; const auto closed = FindClose(handle); const auto error = GetLastError();
        const PluginFindClose receipt{address(handle), closed ? 1U : 0U, error}; event.handles.push_back(receipt);
        if (closed) source.handle = INVALID_HANDLE_VALUE;
        else { find_owner().failures.push_back(receipt); search->faulted = true; }
    }
    if (!event.handles.empty()) find_emit_close(event);
    // Public virtual-namespace absence follows FindFirst's documented error;
    // the distinct actual backend EOF/error is already retained above.
    if (last == ERROR_NO_MORE_FILES) last = ERROR_FILE_NOT_FOUND;
    find_result(*search, 1, INVALID_HANDLE_VALUE, false, last);
    find_owner().searches.erase(id); SetLastError(last); return INVALID_HANDLE_VALUE;
}
HANDLE WINAPI io_find_first_w(LPCWSTR search_text, LPWIN32_FIND_DATAW destination) { return nvse_adapter([&] { return find_first_actual(io_wide(search_text), destination, true); }); }
HANDLE WINAPI io_find_first_a(LPCSTR search_text, LPWIN32_FIND_DATAA destination) { return nvse_adapter([&] {
    if (!AreFileApisANSI()) throw Fatal(ERROR_NOT_SUPPORTED, "OEM file-API name conversion lacks its selected namespace owner.");
    return find_first_actual(io_ansi(search_text), destination, false); }); }
template<class Data> BOOL find_next_actual(HANDLE handle, Data* destination, bool wide) {
    auto& search = find_handle(handle); DWORD last = ERROR_SUCCESS;
    if (search.current == search.sources.size()) {
        expression_writable(destination, sizeof(Data)); const auto result = find_next_api(handle, destination); last = GetLastError();
        const auto source = std::find_if(search.sources.begin(), search.sources.end(), [&](const auto& entry) { return entry.handle == handle; });
        if (source == search.sources.end()) throw Fatal(ERROR_INVALID_HANDLE, "Completed directory lost its actual primary backend.");
        find_backend(search, static_cast<std::uint32_t>(source - search.sources.begin()), 2, handle, result != FALSE, last);
        if (result) throw Fatal(ERROR_INVALID_DATA, "Completed source directory changed after its actual exhaustion.");
        find_result(search, 2, handle, false, last); SetLastError(last); return FALSE;
    }
    const auto delivered = find_advance(search, destination, wide, last); find_result(search, 2, handle, delivered, last);
    SetLastError(last); return delivered ? TRUE : FALSE;
}
BOOL WINAPI io_find_next_w(HANDLE handle, LPWIN32_FIND_DATAW destination) { return nvse_adapter([&] { return find_next_actual(handle, destination, true); }); }
BOOL WINAPI io_find_next_a(HANDLE handle, LPWIN32_FIND_DATAA destination) { return nvse_adapter([&] {
    if (!AreFileApisANSI()) throw Fatal(ERROR_NOT_SUPPORTED, "OEM file-API name conversion lacks its selected namespace owner.");
    return find_next_actual(handle, destination, false); }); }
BOOL WINAPI io_find_close(HANDLE handle) { return nvse_adapter([&] { auto& search = find_handle(handle); return find_close_actual(search, handle) ? TRUE : FALSE; }); }
void find_flush_detach() {
    if (!io_owner().find) return; auto& owner = find_owner();
    for (const auto& event : owner.pending) find_emit_close(event); owner.pending.clear();
}
void find_retire() {
    if (!io_owner().find) return; auto& owner = find_owner();
    if (!owner.searches.empty() || !owner.pending.empty() || !owner.failures.empty())
        throw Fatal(ERROR_BUSY, "Original native directory search/close lifetimes remain unretired.");
}
void* io_find_entry(const std::string& name) {
    struct Entry { const char* name; void* function; };
    const Entry entries[] = {
        {"FindFirstFileW", reinterpret_cast<void*>(io_find_first_w)}, {"FindFirstFileA", reinterpret_cast<void*>(io_find_first_a)},
        {"FindNextFileW", reinterpret_cast<void*>(io_find_next_w)}, {"FindNextFileA", reinterpret_cast<void*>(io_find_next_a)},
        {"FindClose", reinterpret_cast<void*>(io_find_close)},
    };
    for (const auto& entry : entries) if (name == entry.name) return entry.function; return nullptr;
}
