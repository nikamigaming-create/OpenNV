constexpr std::uint32_t parallel_hardware_callback = 31;
constexpr const char* parallel_hardware_library = "msvcp140_atomic_wait.dll";
constexpr const char* parallel_hardware_name = "__std_parallel_algorithms_hw_threads";

void parallel_hardware_prepare(PluginIoRuntime& io, Reader& reader) {
    const auto count = reader.get<std::uint32_t>();
    if (count > 1) throw Fatal(ERROR_INVALID_DATA, "Parallel hardware query has more than one source provider.");
    if (!count) return;
    auto owner = std::make_unique<ParallelHardwareProvider>();
    owner->path = io_from_utf8(reader.text()); owner->sha = reader.text(); owner->declaration = reader.text();
    owner->rva = reader.get<std::uint32_t>();
    if (owner->path.empty() || owner->sha.size() != 64 || owner->declaration.empty() || !owner->rva)
        throw Fatal(ERROR_INVALID_DATA, "Parallel hardware query lost its selected source path/hash/export.");
    io.parallel_hardware = std::move(owner);
}
void* parallel_hardware_import_entry(const std::string& library, const std::string& name, void* prior) {
    if (library != parallel_hardware_library || name != parallel_hardware_name) return nullptr;
    auto& io = io_owner();
    if (!io.parallel_hardware || io.parallel_hardware->retired)
        throw Fatal(ERROR_NOT_SUPPORTED, "Parallel hardware query has no retained original import owner.");
    auto& owner = *io.parallel_hardware;
    HMODULE actual_module = nullptr;
    const auto flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | (owner.retained ? GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT : 0);
    if (!prior || !GetModuleHandleExW(flags, reinterpret_cast<LPCWSTR>(prior), &actual_module))
        throw Fatal(GetLastError(), "Parallel hardware IAT lost its genuine Windows provider image.");
    if (!owner.retained) owner.retained = actual_module;
    if (actual_module != owner.retained)
        throw Fatal(ERROR_INVALID_DATA, "Parallel hardware source imports changed their provider instance.");
    std::vector<wchar_t> path(32768);
    const auto length = GetModuleFileNameW(actual_module, path.data(), static_cast<DWORD>(path.size()));
    if (!length || length >= path.size()) throw Fatal(GetLastError(), "Parallel hardware provider path is absent/truncated.");
    crt_require_provider_file(owner.path, path.data());
    const auto actual = reinterpret_cast<void*>(GetProcAddress(actual_module, parallel_hardware_name));
    MEMORY_BASIC_INFORMATION memory{};
    if (!actual || actual != prior || static_cast<std::uint64_t>(address(actual_module)) + owner.rva != address(actual) ||
        VirtualQuery(actual, &memory, sizeof(memory)) != sizeof(memory) || memory.AllocationBase != actual_module ||
        memory.State != MEM_COMMIT || memory.Type != MEM_IMAGE || memory.Protect & (PAGE_GUARD | PAGE_NOACCESS))
        throw Fatal(ERROR_INVALID_DATA, "Parallel hardware source IAT differs from its original direct provider export.");
    const auto protection = memory.Protect & 0xffU;
    if (protection != PAGE_EXECUTE && protection != PAGE_EXECUTE_READ && protection != PAGE_EXECUTE_READWRITE && protection != PAGE_EXECUTE_WRITECOPY)
        throw Fatal(ERROR_INVALID_DATA, "Parallel hardware export is not executable in its retained image.");
    owner.actual = actual;
    // The original provider owns its cache and actual hardware_concurrency.
    // No thread count, CPU topology or serial fallback is supplied by OpenNV.
    return prior;
}
void parallel_hardware_receipt(std::uint32_t stage, const ParallelHardwareProvider& owner) {
    Bytes request; request.put(stage); request.put(address(owner.retained)); request.put(address(owner.actual)); request.put(owner.rva);
    request.text(io_utf8(owner.path)); request.text(owner.sha);
    crt_owned_callback(parallel_hardware_callback, request);
}
void parallel_hardware_publish() {
    if (!io_owner().parallel_hardware) return;
    auto& owner = *io_owner().parallel_hardware;
    if (!owner.retained || !owner.actual || owner.published || owner.retired)
        throw Fatal(ERROR_INVALID_STATE, "Parallel hardware publication has no genuine first mapped export prefix.");
    parallel_hardware_receipt(1, owner); owner.published = true;
}
void* parallel_hardware_dynamic_entry(HMODULE module, const std::string& name) {
    if (!io_owner().parallel_hardware || module != io_owner().parallel_hardware->retained) return nullptr;
    const auto& owner = *io_owner().parallel_hardware;
    if (!owner.published || owner.retired || name != parallel_hardware_name)
        throw Fatal(ERROR_NOT_SUPPORTED, "Parallel provider lookup has no actual hardware-query owner; work callbacks remain unowned.");
    return owner.actual;
}
void parallel_hardware_retire() {
    if (!io_owner().parallel_hardware) return;
    auto& owner = *io_owner().parallel_hardware;
    if (owner.retired) return;
    if (!owner.retained) { owner.retired = true; return; }
    if (owner.retiring) throw Fatal(ERROR_INVALID_STATE, "Parallel hardware provider retirement cannot replay an entered failure.");
    owner.retiring = true;
    if (!FreeLibrary(owner.retained)) throw Fatal(GetLastError(), "Parallel hardware provider reference did not retire.");
    if (owner.published) parallel_hardware_receipt(2, owner);
    owner.retained = nullptr; owner.retired = true;
}
void parallel_hardware_require_retired() {
    if (io_owner().parallel_hardware && !io_owner().parallel_hardware->retired)
        throw Fatal(ERROR_BUSY, "Parallel hardware provider still owns its actual loader reference.");
}
