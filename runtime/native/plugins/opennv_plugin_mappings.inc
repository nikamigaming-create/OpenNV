constexpr std::uint32_t mapping_callback = 20;
struct PluginMappingPlan { std::uint64_t id, object; std::wstring name; };
std::wstring mapping_local_name(const std::wstring& name) { return name.rfind(L"Local\\", 0) == 0 ? name.substr(6) : name; }
PluginMappingRuntime& mapping_owner() {
    auto& io = io_owner();
    if (!io.mappings) io.mappings = std::make_unique<PluginMappingRuntime>();
    return *io.mappings;
}
void mapping_ack(const Bytes& request) {
    auto response = io_host(mapping_callback, request); Reader result{response.data};
    if (result.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Native mapping operation has no complete correlated source/lifetime receipt.");
    result.finish();
}
PluginMappingPlan mapping_plan(std::uint32_t operation, std::uint64_t file, HANDLE handle, DWORD protection,
    std::uint64_t maximum, std::uint64_t effective, bool has_name, const std::wstring& name) {
    Bytes request; request.put(1U); request.put(operation); request.put(file); request.put(address(handle));
    request.put(protection); request.put(maximum); request.put(effective); request.put(has_name ? 1U : 0U); request.text(io_utf8(name));
    auto response = io_host(mapping_callback, request); Reader result{response.data};
    PluginMappingPlan plan{result.get<std::uint64_t>(), result.get<std::uint64_t>(), io_from_utf8(result.text())}; result.finish();
    if (!plan.id || (!has_name && (!plan.name.empty() || plan.object)))
        throw Fatal(ERROR_INVALID_DATA, "Mapping plan changed the original name/object source identity.");
    if (plan.object && !mapping_owner().objects.count(plan.object))
        throw Fatal(ERROR_INVALID_DATA, "C# named mapping decision has no matching genuine native object lifetime.");
    return plan;
}
void mapping_result(const PluginMappingPlan& plan, std::uint64_t object, HANDLE handle, DWORD error, bool success) {
    Bytes request; request.put(2U); request.put(plan.id); request.put(object); request.put(address(handle)); request.put(error); request.put(success ? 1U : 0U);
    mapping_ack(request);
}
void mapping_event(const PluginMappingEvent& event) {
    if (io_owner().loader_retiring) { mapping_owner().pending.push_back(event); return; }
    Bytes request; request.put(5U); request.put(event.operation); request.put(event.id); request.put(event.address);
    request.put(event.requested); request.put(event.success ? 1U : 0U); request.put(event.error); mapping_ack(request);
}
void mapping_collect(std::uint64_t object) {
    auto& owner = mapping_owner();
    if (std::none_of(owner.handles.begin(), owner.handles.end(), [&](const auto& handle) { return handle.second.object == object; }) &&
        std::none_of(owner.views.begin(), owner.views.end(), [&](const auto& view) { return view.second.object == object; }))
        owner.objects.erase(object);
}
HANDLE mapping_create(HANDLE file, LPSECURITY_ATTRIBUTES security, DWORD protection, DWORD high, DWORD low,
    bool has_name, const std::wstring& name) {
    if (security) throw Fatal(ERROR_NOT_SUPPORTED, "Mapping custom-security/inherited-handle ownership is unbound.");
    auto& owner = mapping_owner(); const auto page = protection & 0xffU;
    if ((page != PAGE_READONLY && page != PAGE_READWRITE && page != PAGE_WRITECOPY) || (protection & ~0xdc0000ffU))
        throw Fatal(ERROR_NOT_SUPPORTED, "Original executable/image/unknown mapping permissions have no data-only source owner.");
    std::uint64_t existing = 0;
    if (has_name && !name.empty()) for (const auto& item : owner.objects)
        if (!item.second.declared_name.empty() && mapping_local_name(item.second.declared_name) == mapping_local_name(name)) {
            if (existing) throw Fatal(ERROR_INVALID_DATA, "Original mapping name repeats distinct owned native objects.");
            existing = item.first;
        }
    std::uint64_t file_id = 0; bool writable = existing ? owner.objects.at(existing).writable : true;
    if (file != INVALID_HANDLE_VALUE) {
        // An existing named section ignores its new backing/maximum request.
        // Never invent a new file owner or query an ignored foreign HANDLE.
        if (existing) { const auto found = io_owner().files.find(address(file));
            if (found != io_owner().files.end() && found->second.handle == file) file_id = found->second.capability; }
        else { const auto input_file = io_file(file); file_id = input_file.capability; writable = input_file.writable; }
        if (!existing && !writable && page == PAGE_READWRITE) throw Fatal(ERROR_ACCESS_DENIED, "Writable mapping targets an original read-only file capability.");
    }
    const auto maximum = (static_cast<std::uint64_t>(high) << 32) | low;
    auto effective = maximum;
    if (!maximum && existing) effective = owner.objects.at(existing).maximum;
    else if (!maximum && file != INVALID_HANDLE_VALUE) {
        LARGE_INTEGER size{}; if (!GetFileSizeEx(file, &size) || size.QuadPart < 0)
            throw Fatal(GetLastError(), "Mapping has no actual source/private backing-file extent.");
        effective = static_cast<std::uint64_t>(size.QuadPart);
    }
    const auto plan = mapping_plan(1, file_id, file, protection, maximum, effective, has_name, name);
    if (plan.object != existing) throw Fatal(ERROR_INVALID_DATA, "Native/C# named mapping lifetimes disagree before the real Windows call.");
    const auto mapped = CreateFileMappingW(file, nullptr, protection, high, low, has_name ? plan.name.c_str() : nullptr);
    const auto last = GetLastError();
    if (!mapped) { mapping_result(plan, 0, nullptr, last, false); SetLastError(last); return nullptr; }
    const auto object = plan.object ? plan.object : plan.id;
    try {
        if (last == ERROR_ALREADY_EXISTS ? !plan.object : plan.object != 0)
            throw Fatal(ERROR_INVALID_DATA, "Actual named mapping existence differs from its source-owned object graph.");
        if (owner.handles.count(address(mapped)) || io_owner().files.count(address(mapped)))
            throw Fatal(ERROR_INVALID_HANDLE, "Actual mapping handle aliases a current source file/mapping capability.");
        if (!plan.object) {
            if (!effective) throw Fatal(ERROR_INVALID_DATA, "Actual mapping creation lacks a nonzero source/pagefile extent.");
            owner.objects.emplace(object, PluginMappingObject{object, file_id, effective, protection, writable, plan.name, has_name ? name : std::wstring{}});
        }
        owner.handles.emplace(address(mapped), PluginMappingHandle{plan.id, object, mapped, protection});
    }
    catch (...) { CloseHandle(mapped); mapping_collect(object); throw; }
    mapping_result(plan, object, mapped, last == ERROR_ALREADY_EXISTS ? last : ERROR_SUCCESS, true); SetLastError(last); return mapped;
}
HANDLE WINAPI mapping_create_w(HANDLE file, LPSECURITY_ATTRIBUTES security, DWORD protection, DWORD high, DWORD low, LPCWSTR name) {
    return nvse_adapter([&] { return mapping_create(file, security, protection, high, low, name != nullptr, io_wide(name, true)); });
}
HANDLE WINAPI mapping_create_a(HANDLE file, LPSECURITY_ATTRIBUTES security, DWORD protection, DWORD high, DWORD low, LPCSTR name) {
    return nvse_adapter([&] { return mapping_create(file, security, protection, high, low, name != nullptr, name ? io_ansi(name) : std::wstring{}); });
}
HANDLE mapping_open(DWORD access, BOOL inherit, const std::wstring& name) {
    if (inherit) throw Fatal(ERROR_NOT_SUPPORTED, "Cross-process inherited mapping handles have no selected child owner.");
    auto& owner = mapping_owner(); const auto plan = mapping_plan(2, 0, nullptr, access, 0, 0, true, name);
    const auto mapped = OpenFileMappingW(access, FALSE, plan.name.c_str()); const auto last = GetLastError();
    if (!mapped) { mapping_result(plan, 0, nullptr, last, false); SetLastError(last); return nullptr; }
    try {
        if (!plan.object || !owner.objects.count(plan.object))
            throw Fatal(ERROR_NOT_SUPPORTED, "Opened named mapping has no genuine selected object/extent owner.");
        if (owner.handles.count(address(mapped)) || io_owner().files.count(address(mapped)))
            throw Fatal(ERROR_INVALID_HANDLE, "OpenFileMapping aliases another live handle.");
        owner.handles.emplace(address(mapped), PluginMappingHandle{plan.id, plan.object, mapped, access});
    }
    catch (...) { CloseHandle(mapped); throw; }
    mapping_result(plan, plan.object, mapped, ERROR_SUCCESS, true); SetLastError(last); return mapped;
}
HANDLE WINAPI mapping_open_w(DWORD access, BOOL inherit, LPCWSTR name) {
    return nvse_adapter([&] { return mapping_open(access, inherit, io_wide(name)); });
}
HANDLE WINAPI mapping_open_a(DWORD access, BOOL inherit, LPCSTR name) {
    return nvse_adapter([&] { return mapping_open(access, inherit, io_ansi(name)); });
}
PluginMappingHandle mapping_handle(HANDLE handle) {
    auto& owner = mapping_owner(); const auto found = owner.handles.find(address(handle));
    if (found == owner.handles.end() || found->second.handle != handle || !owner.objects.count(found->second.object))
        throw Fatal(ERROR_INVALID_HANDLE, "MapViewOfFile has a foreign, closed or pre-entry-only mapping handle.");
    return found->second;
}
LPVOID mapping_view(HANDLE handle, DWORD access, DWORD high, DWORD low, SIZE_T bytes, LPVOID preferred) {
    auto& owner = mapping_owner(); const auto active = mapping_handle(handle); const auto object = owner.objects.at(active.object);
    if (access & ~static_cast<DWORD>(FILE_MAP_ALL_ACCESS))
        throw Fatal(ERROR_NOT_SUPPORTED, "Original executable/large-page/unknown view access has no data-view producer.");
    const auto offset = (static_cast<std::uint64_t>(high) << 32) | low;
    Bytes request; request.put(3U); request.put(active.id); request.put(address(handle)); request.put(access);
    request.put(offset); request.put(static_cast<std::uint32_t>(bytes)); request.put(address(preferred));
    auto response = io_host(mapping_callback, request); Reader decision{response.data}; const auto id = decision.get<std::uint64_t>(); decision.finish();
    if (!id) throw Fatal(ERROR_INVALID_DATA, "Native view has no correlated source mapping plan.");
    const auto base = MapViewOfFileEx(handle, access, high, low, bytes, preferred); const auto last = GetLastError();
    std::uint64_t logical = 0, region = 0; DWORD native_state = 0, protection = 0;
    if (base) {
        try {
            if (offset >= object.maximum) throw Fatal(ERROR_INVALID_DATA, "Actual view exceeds its genuine source mapping extent.");
            logical = bytes ? static_cast<std::uint64_t>(bytes) : object.maximum - offset;
            MEMORY_BASIC_INFORMATION memory{};
            if (!logical || logical > object.maximum - offset || logical > std::numeric_limits<std::uint32_t>::max() ||
                !VirtualQuery(base, &memory, sizeof(memory)) || memory.AllocationBase != base || memory.BaseAddress != base ||
                memory.Type != MEM_MAPPED || (memory.State != MEM_COMMIT && memory.State != MEM_RESERVE) || memory.RegionSize < logical)
                throw Fatal(ERROR_INVALID_DATA, "Actual view lacks a complete observed data-mapping extent/state.");
            const auto page = memory.Protect & 0xffU;
            if ((page != 0 && page != PAGE_READONLY && page != PAGE_READWRITE && page != PAGE_WRITECOPY) ||
                (!object.writable && page == PAGE_READWRITE))
                throw Fatal(ERROR_ACCESS_DENIED, "Actual mapping view violates the original read-only/data protection contract.");
            region = static_cast<std::uint64_t>(memory.RegionSize); native_state = memory.State; protection = memory.Protect;
            if (!owner.views.emplace(address(base), PluginMappingView{id, object.id, offset, logical, region, base, access, native_state, protection}).second)
                throw Fatal(ERROR_INVALID_ADDRESS, "Actual view address aliases another live mapping capability.");
        }
        catch (...) { UnmapViewOfFile(base); throw; }
    }
    if (base) cng_shared_retain_mapping_view(owner.views.at(address(base)), handle);
    Bytes completion; completion.put(4U); completion.put(id); completion.put(address(base)); completion.put(logical); completion.put(region);
    completion.put(native_state); completion.put(protection); completion.put(base ? ERROR_SUCCESS : last); completion.put(base ? 1U : 0U);
    mapping_ack(completion); SetLastError(last); return base;
}
LPVOID WINAPI mapping_view_default(HANDLE handle, DWORD access, DWORD high, DWORD low, SIZE_T bytes) {
    return nvse_adapter([&] { return mapping_view(handle, access, high, low, bytes, nullptr); });
}
LPVOID WINAPI mapping_view_at(HANDLE handle, DWORD access, DWORD high, DWORD low, SIZE_T bytes, LPVOID preferred) {
    return nvse_adapter([&] { return mapping_view(handle, access, high, low, bytes, preferred); });
}
PluginMappingView mapping_existing_view(LPCVOID base) {
    auto& owner = mapping_owner(); const auto found = owner.views.find(address(base));
    if (found == owner.views.end() || found->second.base != base || !owner.objects.count(found->second.object))
        throw Fatal(ERROR_INVALID_ADDRESS, "View member targets a foreign, retired or pre-entry-only address.");
    MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(base, &memory, sizeof(memory)) || memory.AllocationBase != base || memory.Type != MEM_MAPPED)
        throw Fatal(ERROR_INVALID_DATA, "Actual view mapping drifted outside its retained native lifetime.");
    return found->second;
}
BOOL WINAPI mapping_unmap(LPCVOID base) {
    return nvse_adapter([&]() -> BOOL {
        const auto active = mapping_existing_view(base); cng_shared_require_releasable(2U, active.id);
        const auto success = UnmapViewOfFile(base); const auto last = GetLastError();
        if (success) { cng_shared_forget(2U, active.id); cng_shared_close_mapping_view(mapping_owner().views.at(address(base)));
            mapping_owner().views.erase(address(base)); mapping_collect(active.object); }
        mapping_event({5, active.id, address(base), 0, success != FALSE, success ? ERROR_SUCCESS : last}); SetLastError(last); return success;
    });
}
BOOL WINAPI mapping_flush(LPCVOID base, SIZE_T bytes) {
    return nvse_adapter([&]() -> BOOL {
        const auto active = mapping_existing_view(base);
        if (bytes && static_cast<std::uint64_t>(bytes) > active.logical_bytes)
            throw Fatal(ERROR_INVALID_PARAMETER, "Mapped flush exceeds the exact source view extent.");
        const auto success = FlushViewOfFile(base, bytes); const auto last = GetLastError();
        mapping_event({6, active.id, address(base), static_cast<std::uint32_t>(bytes), success != FALSE, success ? ERROR_SUCCESS : last}); SetLastError(last); return success;
    });
}
bool mapping_close_handle(HANDLE handle, BOOL& result) {
    if (!io_owner().mappings) return false;
    auto& owner = *io_owner().mappings; const auto found = owner.handles.find(address(handle)); if (found == owner.handles.end()) return false;
    const auto active = found->second;
    if (active.handle != handle) throw Fatal(ERROR_INVALID_HANDLE, "Mapping close pointer/capability identity drifted.");
    result = CloseHandle(handle); const auto last = GetLastError();
    if (result) { owner.handles.erase(address(handle)); mapping_collect(active.object); }
    mapping_event({4, active.id, address(handle), 0, result != FALSE, result ? ERROR_SUCCESS : last}); SetLastError(last); return true;
}
void* mapping_entry(const std::string& name) {
    struct Entry { const char* name; void* function; };
    const Entry entries[] = {
        {"CreateFileMappingW", reinterpret_cast<void*>(mapping_create_w)}, {"CreateFileMappingA", reinterpret_cast<void*>(mapping_create_a)},
        {"OpenFileMappingW", reinterpret_cast<void*>(mapping_open_w)}, {"OpenFileMappingA", reinterpret_cast<void*>(mapping_open_a)},
        {"MapViewOfFile", reinterpret_cast<void*>(mapping_view_default)}, {"MapViewOfFileEx", reinterpret_cast<void*>(mapping_view_at)},
        {"UnmapViewOfFile", reinterpret_cast<void*>(mapping_unmap)}, {"FlushViewOfFile", reinterpret_cast<void*>(mapping_flush)} };
    for (const auto& entry : entries) if (name == entry.name) return entry.function;
    return nullptr;
}
void mapping_flush_detach() {
    if (!io_owner().mappings) return; auto& owner = *io_owner().mappings;
    while (!owner.pending.empty()) { mapping_event(owner.pending.front()); owner.pending.erase(owner.pending.begin()); }
}
void mapping_retire() {
    if (!io_owner().mappings) return; const auto& owner = *io_owner().mappings;
    if (!owner.objects.empty() || !owner.handles.empty() || !owner.views.empty() || !owner.pending.empty())
        throw Fatal(ERROR_BUSY, "Original mapping object/handle/view lifetimes remain after module retirement.");
}
