// These calls observe the real restricted x86 child. They never substitute
// Godot, a retail PID, fake module rows, or source-object metadata for Windows.
std::uint64_t window_process_enter(std::uint32_t api, HANDLE handle, std::uint32_t first,
    std::uint32_t second, std::uint32_t third, std::uint32_t count) {
    auto& owner = window_process_owner();
    if (owner.pending) throw Fatal(ERROR_BUSY, "Process SDK call repeats an entered native prefix.");
    Bytes args; args.put(2U); args.put(1U); args.put(api); args.put(address(handle));
    args.put(first); args.put(second); args.put(third); args.put(count);
    auto response = io_host(window_process_callback, args); Reader reader{response.data}; const auto id = reader.get<std::uint64_t>(); reader.finish();
    if (!id) throw Fatal(ERROR_INVALID_DATA, "Process SDK call has no C# source/caller admission.");
    owner.pending = true; return id;
}
void window_process_complete(std::uint64_t id, std::int64_t result, DWORD error, HANDLE handle, std::uint32_t count) {
    auto& owner = window_process_owner();
    if (!owner.pending) throw Fatal(ERROR_INVALID_STATE, "Process SDK result has no actual entered prefix.");
    Bytes args; args.put(2U); args.put(2U); args.put(id); args.put(result); args.put(error); args.put(address(handle)); args.put(count);
    auto response = io_host(window_process_callback, args); Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual process SDK result lost its authoritative correlated completion.");
    reader.finish(); owner.pending = false;
}
WindowProcessHandle& window_process_handle(HANDLE handle, std::uint32_t kind = 0) {
    auto& owner = window_process_owner(); const auto found = owner.handles.find(address(handle));
    if (found == owner.handles.end() || found->second.value != handle || kind && found->second.kind != kind)
        throw Fatal(ERROR_INVALID_HANDLE, "Original query handle is foreign, closed, reused or another kernel kind.");
    return found->second;
}
HANDLE WINAPI window_open_thread(DWORD access, BOOL inherit, DWORD thread) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        const auto id = window_process_enter(1, nullptr, access, static_cast<std::uint32_t>(inherit), thread, 0);
        SetLastError(incoming); const auto handle = OpenThread(access, inherit, thread); const auto error = GetLastError();
        if (handle && !window_process_owner().handles.emplace(address(handle), WindowProcessHandle{handle, 1}).second)
            throw Fatal(ERROR_INVALID_HANDLE, "Actual OpenThread repeats a live native query handle.");
        window_process_complete(id, address(handle), error, handle, 0); SetLastError(error); return handle;
    });
}
BOOL WINAPI window_process_times(HANDLE process, LPFILETIME created, LPFILETIME exited, LPFILETIME kernel, LPFILETIME user) {
    const auto incoming = GetLastError(); return nvse_adapter([&]() -> BOOL {
        expression_writable(created, sizeof(FILETIME)); expression_writable(exited, sizeof(FILETIME));
        expression_writable(kernel, sizeof(FILETIME)); expression_writable(user, sizeof(FILETIME));
        const auto id = window_process_enter(2, process, 0, 0, 0, 32);
        SetLastError(incoming); const auto result = GetProcessTimes(process, created, exited, kernel, user); const auto error = GetLastError();
        window_process_complete(id, result, error, process, result ? 32U : 0U); SetLastError(error); return result;
    });
}
BOOL WINAPI window_read_process(HANDLE process, LPCVOID source, LPVOID buffer, SIZE_T size, SIZE_T* count) {
    const auto incoming = GetLastError(); return nvse_adapter([&]() -> BOOL {
        if (size > max_payload - 256U) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Actual process memory request exceeds its bounded caller output.");
        if (size) expression_writable(buffer, static_cast<std::uint32_t>(size)); if (count) expression_writable(count, sizeof(SIZE_T));
        const auto id = window_process_enter(3, process, address(source), static_cast<std::uint32_t>(size), count ? 1U : 0U, static_cast<std::uint32_t>(size));
        SetLastError(incoming);
        const auto result = ReadProcessMemory(process, source, buffer, size, count); const auto error = GetLastError();
        // Forward the genuine optional NULL exactly. Failed partial byte count
        // stays unobserved when the original caller supplied no count slot.
        const auto observed = count ? nvse_copy(count) : result ? size : 0U;
        window_process_complete(id, result, error, process, static_cast<std::uint32_t>(observed)); SetLastError(error); return result;
    });
}
HANDLE WINAPI window_toolhelp(DWORD flags, DWORD process) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        const auto id = window_process_enter(4, nullptr, flags, process, 0, 0);
        SetLastError(incoming); const auto handle = CreateToolhelp32Snapshot(flags, process); const auto error = GetLastError();
        if (handle != INVALID_HANDLE_VALUE && !window_process_owner().handles.emplace(address(handle), WindowProcessHandle{handle, 4}).second)
            throw Fatal(ERROR_INVALID_HANDLE, "Actual snapshot repeats a live native kernel handle.");
        window_process_complete(id, address(handle), error, handle, 0); SetLastError(error); return handle;
    });
}
BOOL window_module_entry(HANDLE snapshot, LPMODULEENTRY32W entry, bool first, DWORD incoming) {
    static_assert(sizeof(MODULEENTRY32W) == 1064, "Preserve the complete public x86 module entry.");
    window_process_handle(snapshot, 4); expression_writable(entry, sizeof(MODULEENTRY32W));
    if (nvse_copy(entry).dwSize != sizeof(MODULEENTRY32W)) throw Fatal(ERROR_NOT_SUPPORTED, "Module entry has an unknown original public SDK extent.");
    const auto id = window_process_enter(first ? 5U : 6U, snapshot, 0, 0, 0, sizeof(MODULEENTRY32W));
    SetLastError(incoming); const auto result = first ? Module32FirstW(snapshot, entry) : Module32NextW(snapshot, entry); const auto error = GetLastError();
    window_process_complete(id, result, error, snapshot, result ? static_cast<std::uint32_t>(sizeof(MODULEENTRY32W)) : 0U); SetLastError(error); return result;
}
BOOL WINAPI window_module_first(HANDLE snapshot, LPMODULEENTRY32W entry) { const auto incoming = GetLastError(); return nvse_adapter([&] { return window_module_entry(snapshot, entry, true, incoming); }); }
BOOL WINAPI window_module_next(HANDLE snapshot, LPMODULEENTRY32W entry) { const auto incoming = GetLastError(); return nvse_adapter([&] { return window_module_entry(snapshot, entry, false, incoming); }); }
bool window_process_close_handle(HANDLE handle, BOOL& returned) {
    if (!state->window_process || !state->window_process->handles.count(address(handle))) return false;
    const auto incoming = GetLastError(); window_process_handle(handle);
    const auto id = window_process_enter(7, handle, 0, 0, 0, 0);
    SetLastError(incoming); const auto result = CloseHandle(handle); const auto error = GetLastError();
    if (result) window_process_owner().handles.erase(address(handle));
    window_process_complete(id, result, error, handle, 0); returned = result; SetLastError(error); return true;
}
void window_process_close_all() {
    auto& owner = window_process_owner();
    if (owner.pending || owner.enumeration) throw Fatal(ERROR_BUSY, "Actual process/window call remains before independent resource retirement.");
    std::vector<HANDLE> handles; for (const auto& row : owner.handles) handles.push_back(row.second.value);
    std::exception_ptr failure;
    for (const auto handle : handles) {
        try { BOOL result = FALSE; if (!window_process_close_handle(handle, result) || !result) throw Fatal(GetLastError(), "Actual original query handle failed independent CloseHandle."); }
        catch (...) { if (!failure) failure = std::current_exception(); }
    }
    if (failure) std::rethrow_exception(failure);
}
void window_process_flush_detach(const Frame& frame) {
    if (!state->window_process) return;
    state->calls.push_back(frame.header.id);
    try { window_process_close_all(); state->calls.pop_back(); }
    catch (...) { state->calls.pop_back(); throw; }
}
void window_process_retire() {
    if (!state->window_process || state->window_process->retired) return;
    auto& owner = window_process_owner(false);
    if (owner.pending || owner.enumeration || !owner.handles.empty()) throw Fatal(ERROR_BUSY, "Original query/Toolhelp/callback resources remain at native retirement.");
    std::exception_ptr failure;
    for (auto& row : owner.providers) {
        if (!row.second) continue;
        if (!FreeLibrary(row.first)) { if (!failure) failure = std::make_exception_ptr(Fatal(GetLastError(), "Actual window/process export reference failed independent FreeLibrary.")); continue; }
        row.second = false;
    }
    if (failure) std::rethrow_exception(failure); owner.retired = true;
}
