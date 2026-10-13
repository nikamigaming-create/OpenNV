// The genuine Windows entry stays in the original x86 IAT. Windows owns its
// BOOLEAN/stdcall return, lock contention, calling-thread release and native
// breakpoint exception, standard handles and STARTUPINFOW bytes. The source caller owns storage. These system
// images are dependencies of the companion's own Windows process and remain
// live until that process exits, after the source module has retired.
bool synchronization_import_name(const std::string& name) {
    return name == "TryAcquireSRWLockShared" || name == "DebugBreak" || name == "GetStartupInfoW" || name == "GetStdHandle";
}
void require_native_standard_handles(const PluginIoRuntime& owner) {
    const auto incoming_error = GetLastError();
    // GetStdHandle remains the genuine Windows export. Validate the actual
    // process table before publishing that capability: standard input has no
    // producer, and both output handles belong to this private I/O lifetime.
    // The retained first-party transport handles never become source channels.
    if (owner.stdin_file != INVALID_HANDLE_VALUE || GetStdHandle(STD_INPUT_HANDLE) != nullptr ||
        owner.stdout_file == nullptr || owner.stdout_file == INVALID_HANDLE_VALUE ||
        owner.stderr_file == nullptr || owner.stderr_file == INVALID_HANDLE_VALUE ||
        GetStdHandle(STD_OUTPUT_HANDLE) != owner.stdout_file || GetStdHandle(STD_ERROR_HANDLE) != owner.stderr_file ||
        owner.stdout_file == input || owner.stdout_file == output || owner.stderr_file == input || owner.stderr_file == output ||
        GetFileType(owner.stdout_file) != FILE_TYPE_DISK || GetFileType(owner.stderr_file) != FILE_TYPE_DISK)
        throw Fatal(ERROR_NOT_SUPPORTED, "Actual Windows standard handles lack private channel ownership or expose native transport.");
    SetLastError(incoming_error);
}
void require_native_startup_info(const PluginIoRuntime& owner) {
    const auto incoming_error = GetLastError();
    require_native_standard_handles(owner);
    STARTUPINFOW actual{};
    GetStartupInfoW(&actual);
    // Observe the actual Windows structure, after real SetStdHandle publication
    // and before original DLL entry. The source never receives a replacement
    // structure or the companion's retained binary protocol channel.
    if (actual.cb != sizeof(actual) || actual.cbReserved2 || actual.lpReserved2 ||
        actual.hStdInput != nullptr || actual.hStdOutput != owner.stdout_file || actual.hStdError != owner.stderr_file)
        throw Fatal(ERROR_NOT_SUPPORTED, "Actual Windows startup information retains unowned input, protocol channels or inherited CRT descriptors.");
    SetLastError(incoming_error);
}
void* synchronization_import_entry(const std::string& library, const std::string& name, void* prior) {
    const bool platform = library == "kernel32.dll" || library == "kernelbase.dll" || library.rfind("api-ms-win-core-", 0) == 0;
    if (!platform || !synchronization_import_name(name)) return nullptr;
    if (!io_owner().non_io_imports.count(library + "!" + name))
        throw Fatal(ERROR_NOT_SUPPORTED, "Windows synchronization/exception import lacks its exact source declaration.");
    const auto incoming_error = GetLastError();
    const auto kernel = GetModuleHandleW(L"kernel32.dll"), base = GetModuleHandleW(L"kernelbase.dll");
    if (!kernel || !base) throw Fatal(ERROR_INVALID_HANDLE, "Windows synchronization/exception provider is not live.");
    const auto kernel_entry = reinterpret_cast<void*>(GetProcAddress(kernel, name.c_str()));
    const auto base_entry = reinterpret_cast<void*>(GetProcAddress(base, name.c_str()));
    if (!prior || (prior != kernel_entry && prior != base_entry))
        throw Fatal(ERROR_INVALID_DATA, "Windows synchronization/exception IAT has an altered or foreign provider address.");
    HMODULE actual = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(prior), &actual))
        throw Fatal(GetLastError(), "Windows synchronization/exception entry has no current provider image.");
    MEMORY_BASIC_INFORMATION region{};
    if (VirtualQuery(prior, &region, sizeof(region)) != sizeof(region) || region.State != MEM_COMMIT ||
        region.Type != MEM_IMAGE || region.AllocationBase != actual || region.Protect & (PAGE_NOACCESS | PAGE_GUARD))
        throw Fatal(ERROR_INVALID_ADDRESS, "Windows synchronization/exception entry lacks its actual executable image extent.");
    const auto protection = region.Protect & 0xffU;
    if (protection != PAGE_EXECUTE && protection != PAGE_EXECUTE_READ && protection != PAGE_EXECUTE_READWRITE && protection != PAGE_EXECUTE_WRITECOPY)
        throw Fatal(ERROR_INVALID_ADDRESS, "Windows synchronization/exception export is not executable.");
    if (name == "GetStartupInfoW") require_native_startup_info(io_owner());
    else if (name == "GetStdHandle") require_native_standard_handles(io_owner());
    SetLastError(incoming_error);
    return prior;
}
void* synchronization_dynamic_entry(HMODULE module, const std::string& name) {
    if (!synchronization_import_name(name)) return nullptr;
    const auto incoming_error = GetLastError();
    const auto kernel = GetModuleHandleW(L"kernel32.dll"), base = GetModuleHandleW(L"kernelbase.dll");
    const auto library = module == kernel ? "kernel32.dll" : module == base ? "kernelbase.dll" : nullptr;
    if (!library) return nullptr;
    const auto actual = reinterpret_cast<void*>(GetProcAddress(module, name.c_str()));
    auto* result = synchronization_import_entry(library, name, actual);
    SetLastError(incoming_error);
    return result;
}
