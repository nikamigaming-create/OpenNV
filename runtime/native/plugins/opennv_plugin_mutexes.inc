// First-party Win32 adaptation. All actual mutex calls run on the original
// restricted child thread; Windows, not an IPC stand-in, owns blocking/acquisition.
constexpr std::uint32_t mutex_callback = 22;
PluginMutexRuntime& mutex_owner() {
    auto& io = io_owner();
    if (!state->nvse || !state->module || state->calls.empty())
        throw Fatal(ERROR_INVALID_STATE, "Mutex call has no entered original image/caller owner.");
    if (!io.mutexes) io.mutexes = std::make_unique<PluginMutexRuntime>();
    return *io.mutexes;
}
PluginMutexHandle& mutex_handle(HANDLE handle) {
    auto& owner = mutex_owner();
    for (auto& row : owner.handles) if (row.second.live && row.second.handle == handle) return row.second;
    throw Fatal(ERROR_INVALID_HANDLE, "Mutex call received an unowned, retired or foreign native handle.");
}
Bytes mutex_host(const Bytes& payload, bool loader_scope = false) {
    auto& io = io_owner();
    if (!loader_scope) return io_host(mutex_callback, payload);
    if (!mutex_detach_callback_allowed())
        throw Fatal(ERROR_INVALID_STATE, "Mutex loader callback is not before/after its actual FreeLibrary scope.");
    Bytes request; request.put(state->module_id); request.data.insert(request.data.end(), payload.data.begin(), payload.data.end());
    auto response = perform_callback_bytes(Kind::io_callback, mutex_callback, request);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), "Mutex original detach ownership callback failed with retained prefix.");
    ++io.callbacks; return response;
}
struct MutexName {
    const void* pointer = nullptr;
    std::vector<std::uint8_t> raw, wide;
    std::uint32_t code_page = 0;
    bool is_wide = false;
};
MutexName mutex_name(const void* pointer, bool wide) {
    MutexName name; name.pointer = pointer; name.is_wide = wide;
    if (!pointer) return name;
    const auto* bytes = static_cast<const std::uint8_t*>(pointer);
    bool terminated = false;
    for (std::uint32_t at = 0; at < 260; ++at) {
        if (wide) {
            const auto character = nvse_copy(reinterpret_cast<const std::uint16_t*>(bytes + at * 2U));
            name.raw.push_back(static_cast<std::uint8_t>(character)); name.raw.push_back(static_cast<std::uint8_t>(character >> 8));
            if (!character) { terminated = true; break; }
        } else {
            const auto character = nvse_copy(bytes + at); name.raw.push_back(character);
            if (!character) { terminated = true; break; }
        }
    }
    if (!terminated) throw Fatal(ERROR_NOT_SUPPORTED, "Mutex source name lacks a complete public bounded declaration.");
    if (wide) name.wide = name.raw;
    else {
        name.code_page = GetACP();
        const auto count = MultiByteToWideChar(name.code_page, 0, reinterpret_cast<LPCSTR>(name.raw.data()),
            static_cast<int>(name.raw.size()), nullptr, 0);
        if (!count) throw Fatal(GetLastError(), "Mutex source ANSI name has no genuine Windows conversion.");
        std::vector<wchar_t> converted(static_cast<std::size_t>(count));
        if (MultiByteToWideChar(name.code_page, 0, reinterpret_cast<LPCSTR>(name.raw.data()),
            static_cast<int>(name.raw.size()), converted.data(), count) != count)
            throw Fatal(GetLastError(), "Mutex ANSI source conversion changed its exact extent.");
        const auto* begin = reinterpret_cast<const std::uint8_t*>(converted.data()); name.wide.assign(begin, begin + converted.size() * 2);
    }
    return name;
}
struct MutexSignature {
    MutexApi api;
    DWORD incoming;
    LPSECURITY_ATTRIBUTES security = nullptr;
    BOOL inherit = FALSE;
    DWORD flags = 0, access = 0, timeout = 0;
    BOOL all = FALSE, alertable = FALSE;
    MutexName name;
    std::vector<PluginMutexHandle> handles;
};
void mutex_bytes(Bytes& data, const std::vector<std::uint8_t>& bytes) {
    data.put(static_cast<std::uint32_t>(bytes.size())); data.data.insert(data.data.end(), bytes.begin(), bytes.end());
}
std::uint64_t mutex_begin(const MutexSignature& signature) {
    auto& owner = mutex_owner(); auto& io = io_owner();
    if (owner.pending || owner.detach_entered || io.loader_retiring)
        throw Fatal(ERROR_INVALID_STATE, "Mutex construction/wait cannot enter after an unfinished SDK/loader prefix.");
    SECURITY_ATTRIBUTES attributes{}; static_assert(sizeof(attributes) == 12, "Mutex SECURITY_ATTRIBUTES is the public x86 layout.");
    if (signature.security) attributes = nvse_copy(signature.security);
    if (signature.security && (attributes.nLength != sizeof(attributes) || attributes.lpSecurityDescriptor || attributes.bInheritHandle) || signature.inherit || signature.alertable)
        throw Fatal(ERROR_NOT_SUPPORTED, "Custom security, inheritance or alertable mutex callbacks have no genuine owner.");
    Bytes data; data.put(1U); data.put(static_cast<std::uint32_t>(signature.api)); data.put(signature.incoming);
    data.put(address(signature.security)); data.put(attributes.nLength); data.put(address(attributes.lpSecurityDescriptor));
    data.put(signature.inherit || attributes.bInheritHandle ? 1U : 0U); data.put(signature.flags); data.put(signature.access);
    data.put(signature.timeout); data.put(signature.all ? 1U : 0U); data.put(signature.alertable ? 1U : 0U);
    data.put(address(signature.name.pointer)); data.put(signature.name.is_wide ? 1U : 0U); data.put(signature.name.code_page);
    mutex_bytes(data, signature.name.raw); mutex_bytes(data, signature.name.wide);
    data.put(static_cast<std::uint32_t>(signature.handles.size()));
    for (const auto& handle : signature.handles) { data.put(handle.capability); data.put(address(handle.handle)); }
    auto response = mutex_host(data); Reader reader{response.data}; const auto id = reader.get<std::uint64_t>(); reader.finish();
    if (!id) throw Fatal(ERROR_INVALID_DATA, "Mutex SDK call has no C# generation/caller identity.");
    owner.pending = true; owner.sdk_call = id; return id;
}
void mutex_complete(std::uint64_t id, std::uint32_t result, DWORD error, bool construction) {
    auto& owner = mutex_owner();
    if (!owner.pending || owner.sdk_call != id) throw Fatal(ERROR_INVALID_STATE, "Mutex SDK result lacks its entered native prefix.");
    Bytes data; data.put(2U); data.put(id); data.put(result); data.put(error); data.put(construction ? 1U : 0U);
    auto response = mutex_host(data); Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Mutex result lacks exact C# Windows/state publication."); reader.finish();
    owner.pending = false; owner.sdk_call = 0; SetLastError(error);
}
template<class Call> HANDLE mutex_construct(MutexSignature& signature, Call call) {
    const auto id = mutex_begin(signature); auto& owner = mutex_owner();
    // Allocate the strong slot before Windows can create an object. If result
    // publication fails, the child still retains the actual returned handle.
    auto found = owner.handles.emplace(id, PluginMutexHandle{id, nullptr, false});
    if (!found.second) throw Fatal(ERROR_INVALID_STATE, "Mutex construction repeats a generation capability.");
    SetLastError(signature.incoming); const auto handle = call(); const auto error = GetLastError();
    found.first->second.handle = handle; found.first->second.live = handle != nullptr;
    mutex_complete(id, address(handle), error, true);
    if (!handle) owner.handles.erase(found.first);
    SetLastError(error); return handle;
}
HANDLE WINAPI mutex_create_a(LPSECURITY_ATTRIBUTES security, BOOL initial, LPCSTR name) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        MutexSignature signature{MutexApi::create_a, incoming}; signature.security = security; signature.flags = initial ? 1U : 0U;
        signature.name = mutex_name(name, false); return mutex_construct(signature, [&] { return CreateMutexA(security, initial, name); });
    });
}
HANDLE WINAPI mutex_create_w(LPSECURITY_ATTRIBUTES security, BOOL initial, LPCWSTR name) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        MutexSignature signature{MutexApi::create_w, incoming}; signature.security = security; signature.flags = initial ? 1U : 0U;
        signature.name = mutex_name(name, true); return mutex_construct(signature, [&] { return CreateMutexW(security, initial, name); });
    });
}
HANDLE WINAPI mutex_create_ex_a(LPSECURITY_ATTRIBUTES security, LPCSTR name, DWORD flags, DWORD access) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        MutexSignature signature{MutexApi::create_ex_a, incoming}; signature.security = security; signature.flags = flags; signature.access = access;
        signature.name = mutex_name(name, false); return mutex_construct(signature, [&] { return CreateMutexExA(security, name, flags, access); });
    });
}
HANDLE WINAPI mutex_create_ex_w(LPSECURITY_ATTRIBUTES security, LPCWSTR name, DWORD flags, DWORD access) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        MutexSignature signature{MutexApi::create_ex_w, incoming}; signature.security = security; signature.flags = flags; signature.access = access;
        signature.name = mutex_name(name, true); return mutex_construct(signature, [&] { return CreateMutexExW(security, name, flags, access); });
    });
}
HANDLE WINAPI mutex_open_a(DWORD access, BOOL inherit, LPCSTR name) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        MutexSignature signature{MutexApi::open_a, incoming}; signature.inherit = inherit; signature.access = access;
        signature.name = mutex_name(name, false); return mutex_construct(signature, [&] { return OpenMutexA(access, inherit, name); });
    });
}
HANDLE WINAPI mutex_open_w(DWORD access, BOOL inherit, LPCWSTR name) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        MutexSignature signature{MutexApi::open_w, incoming}; signature.inherit = inherit; signature.access = access;
        signature.name = mutex_name(name, true); return mutex_construct(signature, [&] { return OpenMutexW(access, inherit, name); });
    });
}
BOOL mutex_mutate(HANDLE handle, MutexApi api, DWORD incoming) {
    auto& owner = mutex_owner(); auto& io = io_owner(); auto& value = mutex_handle(handle);
    if (io.loader_retiring) {
        if (!owner.detach_entered || !owner.in_free || owner.detach_returned || owner.call != state->calls.back() || owner.image != state->module)
            throw Fatal(ERROR_INVALID_STATE, "Mutex destructor operation has no actual entered original FreeLibrary owner.");
        // Queue allocation precedes the actual SDK side effect. No IPC is sent
        // while DllMain runs under the loader lock.
        owner.detached.push_back({static_cast<std::uint64_t>(owner.detached.size()) + 1, value.capability, GetCurrentThreadId(), api, handle, incoming});
        auto& event = owner.detached.back(); SetLastError(incoming);
        event.result = api == MutexApi::release ? ReleaseMutex(handle) : CloseHandle(handle); event.last_error = GetLastError(); event.completed = true;
        if (api == MutexApi::close && event.result) value.live = false;
        SetLastError(event.last_error); return event.result;
    }
    MutexSignature signature{api, incoming}; signature.handles.push_back(value);
    const auto id = mutex_begin(signature); SetLastError(incoming);
    const auto result = api == MutexApi::release ? ReleaseMutex(handle) : CloseHandle(handle); const auto error = GetLastError();
    if (api == MutexApi::close && result) value.live = false;
    mutex_complete(id, static_cast<std::uint32_t>(result), error, false); SetLastError(error); return result;
}
BOOL WINAPI mutex_release(HANDLE handle) {
    const auto incoming = GetLastError(); return nvse_adapter([&] { return mutex_mutate(handle, MutexApi::release, incoming); });
}
bool mutex_close_handle(HANDLE handle, BOOL& result) {
    const auto incoming = GetLastError();
    if (!state->io || !state->io->mutexes) return false;
    for (const auto& row : state->io->mutexes->handles) if (row.second.live && row.second.handle == handle) {
        result = mutex_mutate(handle, MutexApi::close, incoming); return true;
    }
    SetLastError(incoming); return false;
}
DWORD mutex_wait(MutexApi api, DWORD count, const HANDLE* handles, BOOL all, DWORD timeout, BOOL alertable, DWORD incoming) {
    if (!handles || count == 0 || count > MAXIMUM_WAIT_OBJECTS)
        throw Fatal(ERROR_NOT_SUPPORTED, "Mutex wait has no complete public source handle array.");
    nvse_readable(handles, count * sizeof(HANDLE)); std::vector<HANDLE> actual; actual.reserve(count);
    MutexSignature signature{api, incoming}; signature.all = all; signature.timeout = timeout; signature.alertable = alertable;
    for (DWORD at = 0; at < count; ++at) { const auto handle = nvse_copy(handles + at); actual.push_back(handle); signature.handles.push_back(mutex_handle(handle)); }
    const auto id = mutex_begin(signature); SetLastError(incoming);
    DWORD result;
    if (api == MutexApi::wait) result = WaitForSingleObject(actual[0], timeout);
    else if (api == MutexApi::wait_ex) result = WaitForSingleObjectEx(actual[0], timeout, alertable);
    else if (api == MutexApi::wait_many) result = WaitForMultipleObjects(count, actual.data(), all, timeout);
    else result = WaitForMultipleObjectsEx(count, actual.data(), all, timeout, alertable);
    const auto error = GetLastError(); mutex_complete(id, result, error, false); SetLastError(error); return result;
}
DWORD WINAPI mutex_wait_one(HANDLE handle, DWORD timeout) {
    const auto incoming = GetLastError(); return nvse_adapter([&] { return mutex_wait(MutexApi::wait, 1, &handle, FALSE, timeout, FALSE, incoming); });
}
DWORD WINAPI mutex_wait_one_ex(HANDLE handle, DWORD timeout, BOOL alertable) {
    const auto incoming = GetLastError(); return nvse_adapter([&] { return mutex_wait(MutexApi::wait_ex, 1, &handle, FALSE, timeout, alertable, incoming); });
}
DWORD WINAPI mutex_wait_many(DWORD count, const HANDLE* handles, BOOL all, DWORD timeout) {
    const auto incoming = GetLastError(); return nvse_adapter([&] { return mutex_wait(MutexApi::wait_many, count, handles, all, timeout, FALSE, incoming); });
}
DWORD WINAPI mutex_wait_many_ex(DWORD count, const HANDLE* handles, BOOL all, DWORD timeout, BOOL alertable) {
    const auto incoming = GetLastError(); return nvse_adapter([&] { return mutex_wait(MutexApi::wait_many_ex, count, handles, all, timeout, alertable, incoming); });
}
void* mutex_entry(const std::string& name) {
    struct Entry { const char* name; void* function; };
    const Entry entries[] = {
        {"CreateMutexA", reinterpret_cast<void*>(mutex_create_a)}, {"CreateMutexW", reinterpret_cast<void*>(mutex_create_w)},
        {"CreateMutexExA", reinterpret_cast<void*>(mutex_create_ex_a)}, {"CreateMutexExW", reinterpret_cast<void*>(mutex_create_ex_w)},
        {"OpenMutexA", reinterpret_cast<void*>(mutex_open_a)}, {"OpenMutexW", reinterpret_cast<void*>(mutex_open_w)},
        {"ReleaseMutex", reinterpret_cast<void*>(mutex_release)}, {"WaitForSingleObject", reinterpret_cast<void*>(mutex_wait_one)},
        {"WaitForSingleObjectEx", reinterpret_cast<void*>(mutex_wait_one_ex)}, {"WaitForMultipleObjects", reinterpret_cast<void*>(mutex_wait_many)},
        {"WaitForMultipleObjectsEx", reinterpret_cast<void*>(mutex_wait_many_ex)} };
    for (const auto& entry : entries) if (name == entry.name) return entry.function; return nullptr;
}
bool mutex_detach_callback_allowed() {
    if (!state || !state->io || !state->io->mutexes || state->calls.empty()) return false;
    const auto& owner = *state->io->mutexes;
    return owner.detach_entered && !owner.in_free && owner.call == state->calls.back();
}
void mutex_before_free(const Frame& frame, HMODULE image) {
    if (!state->io || !state->io->mutexes) return;
    auto& owner = *state->io->mutexes;
    if (owner.detach_entered || owner.pending || image != state->module || state->calls.empty() || state->calls.back() != frame.header.id)
        throw Fatal(ERROR_INVALID_STATE, "Mutex source detach cannot replay or abandon an entered SDK call.");
    owner.detach_entered = true; owner.call = frame.header.id; owner.image = image;
    Bytes data; data.put(3U); data.put(address(image)); data.put(0U); data.put(0U);
    auto response = mutex_host(data, true); Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Mutex detach has no exact C# source/caller authorization."); reader.finish();
}
BOOL mutex_original_free_library(HMODULE image) {
    if (!state->io || !state->io->mutexes) return FreeLibrary(image);
    auto& owner = *state->io->mutexes;
    if (!owner.detach_entered || owner.in_free || owner.detach_returned || owner.image != image)
        throw Fatal(ERROR_INVALID_STATE, "Actual FreeLibrary changed the admitted mutex source lifetime.");
    owner.in_free = true; const auto result = FreeLibrary(image); const auto error = GetLastError();
    owner.unload_result = result; owner.unload_error = error; owner.in_free = false; owner.detach_returned = true;
    SetLastError(error); return result;
}
void mutex_after_free(const Frame& frame, HMODULE image, BOOL result, DWORD error) {
    if (!state->io || !state->io->mutexes) return;
    const auto& owner = *state->io->mutexes;
    if (!owner.detach_returned || owner.call != frame.header.id || owner.image != image || owner.unload_result != result || owner.unload_error != error)
        throw Fatal(ERROR_INVALID_DATA, "Mutex detach lost its actual FreeLibrary return/status prefix.");
    Bytes data; data.put(4U); data.put(address(image)); data.put(static_cast<std::uint32_t>(result)); data.put(error);
    auto response = mutex_host(data, true); Reader reader{response.data};
    if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Mutex detach return lacks its C# source lifetime receipt."); reader.finish();
}
void mutex_flush_detach() {
    if (!state->io || !state->io->mutexes) return;
    auto& owner = *state->io->mutexes;
    if (!owner.detach_returned || owner.in_free || state->calls.empty() || state->calls.back() != owner.call)
        throw Fatal(ERROR_INVALID_STATE, "Deferred mutex results have no returned exact original loader scope.");
    for (; owner.flushed < owner.detached.size(); ++owner.flushed) {
        const auto& event = owner.detached[owner.flushed];
        if (!event.completed || event.thread != state->thread)
            throw Fatal(ERROR_INVALID_STATE, "Mutex destructor SDK entry did not return on its exact original thread.");
        Bytes data; data.put(5U); data.put(event.sequence); data.put(static_cast<std::uint32_t>(event.api));
        data.put(event.capability); data.put(address(event.handle)); data.put(event.incoming); data.put(static_cast<std::uint32_t>(event.result)); data.put(event.last_error);
        auto response = mutex_host(data); Reader reader{response.data};
        if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual mutex destructor result did not retire its C# state prefix."); reader.finish();
    }
}
void mutex_retire() {
    if (!state->io || !state->io->mutexes) return;
    const auto& owner = *state->io->mutexes;
    if (owner.pending || owner.flushed != owner.detached.size() ||
        owner.detach_entered && (!owner.detach_returned || owner.unload_result == FALSE) ||
        std::any_of(owner.handles.begin(), owner.handles.end(), [](const auto& row) { return row.second.live; }))
        throw Fatal(ERROR_BUSY, "Mutex retirement retains actual Windows handles or an entered/failed native prefix.");
}
