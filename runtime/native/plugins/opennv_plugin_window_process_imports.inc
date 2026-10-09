void* window_process_entry(const std::string& name) {
#define WINDOW_ENTRY(source, owner) if (name == source) return reinterpret_cast<void*>(owner)
    WINDOW_ENTRY("SetActiveWindow", window_set_active); WINDOW_ENTRY("GetForegroundWindow", window_foreground);
    WINDOW_ENTRY("SetForegroundWindow", window_set_foreground); WINDOW_ENTRY("GetWindowTextA", window_text);
    WINDOW_ENTRY("GetClientRect", window_client); WINDOW_ENTRY("GetWindowRect", window_rectangle);
    WINDOW_ENTRY("SendInput", window_send_input); WINDOW_ENTRY("GetCursorPos", window_cursor);
    WINDOW_ENTRY("ClientToScreen", window_client_screen); WINDOW_ENTRY("ClipCursor", window_clip);
    WINDOW_ENTRY("EnumWindows", window_enumerate); WINDOW_ENTRY("GetClassNameA", window_class);
    WINDOW_ENTRY("GetWindowThreadProcessId", window_thread_process); WINDOW_ENTRY("GetActiveWindow", window_active);
    WINDOW_ENTRY("SetFocus", window_focus); WINDOW_ENTRY("SetCursorPos", window_set_cursor);
    WINDOW_ENTRY("BringWindowToTop", window_top); WINDOW_ENTRY("ShowWindow", window_show);
    WINDOW_ENTRY("PostMessageA", window_post); WINDOW_ENTRY("AttachThreadInput", window_attach);
    WINDOW_ENTRY("IsWindowVisible", window_visible); WINDOW_ENTRY("GetKeyboardLayout", window_keyboard_layout);
    WINDOW_ENTRY("GetKeyboardState", window_keyboard_state); WINDOW_ENTRY("MapVirtualKeyExA", window_virtual_key);
    WINDOW_ENTRY("ToUnicodeEx", window_unicode);
#undef WINDOW_ENTRY
    return nullptr;
}
void* window_process_sdk_entry(const std::string& name) {
    if (name == "OpenThread") return reinterpret_cast<void*>(window_open_thread);
    if (name == "GetProcessTimes") return reinterpret_cast<void*>(window_process_times);
    if (name == "ReadProcessMemory") return reinterpret_cast<void*>(window_read_process);
    if (name == "CreateToolhelp32Snapshot") return reinterpret_cast<void*>(window_toolhelp);
    if (name == "Module32FirstW") return reinterpret_cast<void*>(window_module_first);
    if (name == "Module32NextW") return reinterpret_cast<void*>(window_module_next);
    return nullptr;
}
void* window_process_import_entry(const std::string& library, const std::string& name, void* prior) {
    const bool user = library == "user32.dll";
    const bool kernel = library == "kernel32.dll" || library == "kernelbase.dll" || library.rfind("api-ms-win-core-", 0) == 0;
    auto* replacement = user ? window_process_entry(name) : kernel ? window_process_sdk_entry(name) : nullptr;
    if (!replacement) return nullptr;
    auto& owner = window_process_owner(false);
    if (user && !owner.window) throw Fatal(ERROR_NOT_SUPPORTED, "Original USER32 import has no genuine product-window producer.");
    if (!io_owner().non_io_imports.count(library + "!" + name))
        throw Fatal(ERROR_NOT_SUPPORTED, "Window/process import lacks its exact selected original declaration.");
    const auto first = GetModuleHandleW(user ? L"user32.dll" : L"kernel32.dll");
    const auto second = user ? nullptr : GetModuleHandleW(L"kernelbase.dll");
    const auto expected_first = first ? GetProcAddress(first, name.c_str()) : nullptr;
    const auto expected_second = second ? GetProcAddress(second, name.c_str()) : nullptr;
    if (!prior || (prior != reinterpret_cast<void*>(expected_first) && prior != reinterpret_cast<void*>(expected_second)))
        throw Fatal(ERROR_INVALID_DATA, "Window/process IAT pointer is altered or not the actual Windows export.");
    HMODULE mapped = nullptr; const auto flags = GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS;
    if (!GetModuleHandleExW(flags, reinterpret_cast<LPCWSTR>(prior), &mapped))
        throw Fatal(GetLastError(), "Actual Windows export could not retain its original native provider reference.");
    if (owner.providers.count(mapped)) { if (!FreeLibrary(mapped)) throw Fatal(GetLastError(), "Repeated Windows export reference failed independent closure."); }
    else owner.providers.emplace(mapped, true);
    return replacement;
}
void* window_process_dynamic_entry(HMODULE module, const std::string& name) {
    if (!state->window_process) return nullptr;
    auto* user_entry = window_process_entry(name); auto* kernel_entry = window_process_sdk_entry(name);
    if (!user_entry && !kernel_entry) return nullptr;
    const auto user = GetModuleHandleW(L"user32.dll"), kernel = GetModuleHandleW(L"kernel32.dll"), base = GetModuleHandleW(L"kernelbase.dll");
    const auto library = module == user ? "user32.dll" : module == kernel ? "kernel32.dll" : module == base ? "kernelbase.dll" : "";
    if (!library[0]) throw Fatal(ERROR_NOT_SUPPORTED, "Dynamic window/process export targets an unowned original provider.");
    auto* original = reinterpret_cast<void*>(GetProcAddress(module, name.c_str()));
    return window_process_import_entry(library, name, original);
}
