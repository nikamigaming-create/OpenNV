constexpr std::uint32_t window_process_callback = 29;
void window_process_close_all();
WindowProcessRuntime& window_process_owner(bool caller = true) {
    if (!state || !state->window_process || GetCurrentThreadId() != state->thread ||
        !state->window_process->bound || state->window_process->retired)
        throw Fatal(ERROR_INVALID_STATE, "Original window/process API lacks its actual retained source producer.");
    if (caller && (!state->module || !state->nvse || state->calls.empty()))
        throw Fatal(ERROR_INVALID_STATE, "Original window/process API has no entered original caller/module.");
    return *state->window_process;
}
struct WindowReply { std::uint32_t result = 0, error = 0; std::vector<std::uint8_t> bytes; };
WindowReply window_process_call(WindowApi api, std::uint32_t target, DWORD incoming, const Bytes& arguments) {
    window_process_owner();
    Bytes request; request.put(1U); request.put(static_cast<std::uint32_t>(api)); request.put(target); request.put(incoming);
    request.put(static_cast<std::uint32_t>(arguments.data.size())); request.data.insert(request.data.end(), arguments.data.begin(), arguments.data.end());
    auto response = io_host(window_process_callback, request); Reader reader{response.data};
    WindowReply receipt; receipt.result = reader.get<std::uint32_t>(); receipt.error = reader.get<std::uint32_t>();
    const auto count = reader.get<std::uint32_t>(); const auto* bytes = static_cast<const std::uint8_t*>(reader.bytes(count));
    if (count) receipt.bytes.assign(bytes, bytes + count); reader.finish(); return receipt;
}
void window_process_output(const WindowReply& receipt, void* pointer, std::uint32_t count, std::uint32_t offset = 0) {
    if (receipt.bytes.size() != static_cast<std::size_t>(count) + offset)
        throw Fatal(ERROR_INVALID_DATA, "Window SDK output lost its complete original caller extent.");
    if (count) expression_write(pointer, receipt.bytes.data() + offset, count);
}
std::uint32_t window_process_scalar(WindowApi api, std::uint32_t target, DWORD incoming, const Bytes& arguments = {}) {
    const auto reply_value = window_process_call(api, target, incoming, arguments);
    if (!reply_value.bytes.empty()) throw Fatal(ERROR_INVALID_DATA, "Scalar window SDK result has extra output bytes.");
    SetLastError(reply_value.error); return reply_value.result;
}
HWND WINAPI window_set_active(HWND value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return reinterpret_cast<HWND>(window_process_scalar(WindowApi::set_active, address(value), incoming)); }); }
HWND WINAPI window_foreground() { const auto incoming = GetLastError(); return nvse_adapter([&] { return reinterpret_cast<HWND>(window_process_scalar(WindowApi::foreground, 0, incoming)); }); }
BOOL WINAPI window_set_foreground(HWND value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return static_cast<BOOL>(window_process_scalar(WindowApi::set_foreground, address(value), incoming)); }); }
HWND WINAPI window_active() { const auto incoming = GetLastError(); return nvse_adapter([&] { return reinterpret_cast<HWND>(window_process_scalar(WindowApi::active, 0, incoming)); }); }
HWND WINAPI window_focus(HWND value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return reinterpret_cast<HWND>(window_process_scalar(WindowApi::focus, address(value), incoming)); }); }
BOOL WINAPI window_top(HWND value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return static_cast<BOOL>(window_process_scalar(WindowApi::bring_top, address(value), incoming)); }); }
BOOL WINAPI window_show(HWND value, int command) { const auto incoming = GetLastError(); return nvse_adapter([&] { Bytes args; args.put(command); return static_cast<BOOL>(window_process_scalar(WindowApi::show, address(value), incoming, args)); }); }
BOOL WINAPI window_visible(HWND value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return static_cast<BOOL>(window_process_scalar(WindowApi::visible, address(value), incoming)); }); }
int window_text_output(WindowApi api, HWND window, LPSTR buffer, int capacity, DWORD incoming) {
    if (capacity < 0 || static_cast<std::uint32_t>(capacity) > max_payload - 256U || !buffer)
        throw Fatal(ERROR_NOT_SUPPORTED, "Window ANSI output requires its genuine bounded nonnull caller buffer.");
    expression_writable(buffer, static_cast<std::uint32_t>(capacity)); Bytes args; args.put(capacity); args.put(GetACP()); nvse_bytes(args, buffer, static_cast<std::uint32_t>(capacity));
    const auto receipt = window_process_call(api, address(window), incoming, args);
    window_process_output(receipt, buffer, static_cast<std::uint32_t>(capacity), 8); SetLastError(receipt.error); return static_cast<int>(receipt.result);
}
int WINAPI window_text(HWND window, LPSTR buffer, int capacity) { const auto incoming = GetLastError(); return nvse_adapter([&] { return window_text_output(WindowApi::text, window, buffer, capacity, incoming); }); }
int WINAPI window_class(HWND window, LPSTR buffer, int capacity) { const auto incoming = GetLastError(); return nvse_adapter([&] { return window_text_output(WindowApi::window_class, window, buffer, capacity, incoming); }); }
BOOL window_rect_output(WindowApi api, HWND window, LPRECT value, DWORD incoming) {
    expression_writable(value, sizeof(RECT)); Bytes args; nvse_bytes(args, value, sizeof(RECT));
    const auto receipt = window_process_call(api, address(window), incoming, args);
    window_process_output(receipt, value, sizeof(RECT)); SetLastError(receipt.error); return static_cast<BOOL>(receipt.result);
}
BOOL WINAPI window_client(HWND window, LPRECT value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return window_rect_output(WindowApi::client, window, value, incoming); }); }
BOOL WINAPI window_rectangle(HWND window, LPRECT value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return window_rect_output(WindowApi::rectangle, window, value, incoming); }); }
BOOL window_point_output(WindowApi api, HWND window, LPPOINT value, DWORD incoming) {
    expression_writable(value, sizeof(POINT)); Bytes args; nvse_bytes(args, value, sizeof(POINT));
    const auto receipt = window_process_call(api, address(window), incoming, args);
    window_process_output(receipt, value, sizeof(POINT)); SetLastError(receipt.error); return static_cast<BOOL>(receipt.result);
}
BOOL WINAPI window_cursor(LPPOINT value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return window_point_output(WindowApi::cursor, nullptr, value, incoming); }); }
BOOL WINAPI window_client_screen(HWND window, LPPOINT value) { const auto incoming = GetLastError(); return nvse_adapter([&] { return window_point_output(WindowApi::client_screen, window, value, incoming); }); }
BOOL WINAPI window_set_cursor(int x, int y) { const auto incoming = GetLastError(); return nvse_adapter([&] { Bytes args; args.put(x); args.put(y); return static_cast<BOOL>(window_process_scalar(WindowApi::set_cursor, 0, incoming, args)); }); }
BOOL WINAPI window_clip(const RECT* value) { const auto incoming = GetLastError(); return nvse_adapter([&] { Bytes args; if (value) nvse_bytes(args, value, sizeof(RECT)); return static_cast<BOOL>(window_process_scalar(WindowApi::clip, 0, incoming, args)); }); }
DWORD WINAPI window_thread_process(HWND window, LPDWORD process) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        Bytes args; if (process) { expression_writable(process, sizeof(DWORD)); nvse_bytes(args, process, sizeof(DWORD)); }
        const auto receipt = window_process_call(WindowApi::thread_process, address(window), incoming, args);
        window_process_output(receipt, process, process ? static_cast<std::uint32_t>(sizeof(DWORD)) : 0U); SetLastError(receipt.error); return receipt.result;
    });
}
BOOL WINAPI window_keyboard_state(PBYTE buffer) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        expression_writable(buffer, 256); Bytes args; nvse_bytes(args, buffer, 256);
        const auto receipt = window_process_call(WindowApi::keyboard_state, 0, incoming, args);
        window_process_output(receipt, buffer, 256); SetLastError(receipt.error); return static_cast<BOOL>(receipt.result);
    });
}
HKL WINAPI window_keyboard_layout(DWORD thread) { const auto incoming = GetLastError(); return nvse_adapter([&] { Bytes args; args.put(thread); return reinterpret_cast<HKL>(window_process_scalar(WindowApi::keyboard_layout, 0, incoming, args)); }); }
UINT WINAPI window_virtual_key(UINT code, UINT kind, HKL layout) { const auto incoming = GetLastError(); return nvse_adapter([&] { Bytes args; args.put(code); args.put(kind); args.put(address(layout)); return window_process_scalar(WindowApi::virtual_key, 0, incoming, args); }); }
int WINAPI window_unicode(UINT key, UINT scan, const BYTE* keyboard, LPWSTR characters, int capacity, UINT flags, HKL layout) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        if (capacity < 0 || static_cast<std::uint32_t>(capacity) > (max_payload - 512U) / 2U || !keyboard || !characters)
            throw Fatal(ERROR_NOT_SUPPORTED, "Unicode conversion requires complete original virtual-key/output buffers.");
        const auto count = static_cast<std::uint32_t>(capacity) * 2U;
        const auto key_start = static_cast<std::uint64_t>(address(keyboard)), out_start = static_cast<std::uint64_t>(address(characters));
        if (count && key_start < out_start + count && out_start < key_start + 256U)
            throw Fatal(ERROR_NOT_SUPPORTED, "Overlapping Unicode input/output has no cross-thread original alias owner.");
        nvse_readable(keyboard, 256); expression_writable(characters, count);
        Bytes args; args.put(key); args.put(scan); args.put(0U); args.put(capacity); args.put(flags); args.put(address(layout));
        nvse_bytes(args, keyboard, 256); nvse_bytes(args, characters, count);
        const auto receipt = window_process_call(WindowApi::unicode, 0, incoming, args);
        window_process_output(receipt, characters, count, 280); SetLastError(receipt.error); return static_cast<int>(receipt.result);
    });
}
BOOL WINAPI window_post(HWND window, UINT message, WPARAM first, LPARAM second) { const auto incoming = GetLastError(); return nvse_adapter([&] { Bytes args; args.put(message); args.put(first); args.put(second); return static_cast<BOOL>(window_process_scalar(WindowApi::post, address(window), incoming, args)); }); }
BOOL WINAPI window_attach(DWORD first, DWORD second, BOOL attach) { const auto incoming = GetLastError(); return nvse_adapter([&] { Bytes args; args.put(first); args.put(second); args.put(attach); return static_cast<BOOL>(window_process_scalar(WindowApi::attach, 0, incoming, args)); }); }
UINT WINAPI window_send_input(UINT count, LPINPUT values, int size) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        static_assert(sizeof(INPUT) == 28, "Preserve the source public x86 INPUT union.");
        if (size != static_cast<int>(sizeof(INPUT)) || count > (max_payload - 256U) / sizeof(INPUT) || count && !values)
            throw Fatal(ERROR_NOT_SUPPORTED, "SendInput lacks its complete original x86 union/caller extent.");
        Bytes args; args.put(count); args.put(size); if (count) nvse_bytes(args, values, count * static_cast<UINT>(sizeof(INPUT)));
        return window_process_scalar(WindowApi::send_input, 0, incoming, args);
    });
}
BOOL WINAPI window_enumerate(WNDENUMPROC function, LPARAM parameter) {
    const auto incoming = GetLastError(); return nvse_adapter([&] {
        auto& owner = window_process_owner(); nvse_executable(reinterpret_cast<void*>(function));
        if (owner.enumeration) throw Fatal(ERROR_BUSY, "Original EnumWindows callback owner is already entered.");
        owner.enumeration = function; owner.enumeration_parameter = parameter; owner.enumeration_parent = state->calls.back();
        try { const auto result = window_process_scalar(WindowApi::enumerate, 0, incoming); owner.enumeration = nullptr; owner.enumeration_parent = 0; return static_cast<BOOL>(result); }
        catch (...) { owner.enumeration = nullptr; owner.enumeration_parent = 0; throw; }
    });
}
void window_process_dispatch(const Frame& frame) {
    Reader reader{frame.payload}; const auto stage = reader.get<std::uint32_t>();
    if (stage == 1) {
        if (state->module || !state->calls.empty() || state->window_process) throw Fatal(ERROR_INVALID_STATE, "Window producer repeated after original/native admission.");
        auto owner = std::make_unique<WindowProcessRuntime>(); owner->window = reader.get<std::uint32_t>();
        owner->thread = reader.get<std::uint32_t>(); owner->process = reader.get<std::uint32_t>(); owner->source = reader.text(); reader.finish();
        const auto no_window = !owner->window && !owner->thread && !owner->process;
        if ((!no_window && (!owner->window || !owner->thread || !owner->process)) || owner->source.size() != 64 || !state->io)
            throw Fatal(ERROR_INVALID_DATA, "Source/window producer lacks exact living product identities.");
        owner->bound = true; Bytes receipt; receipt.put(owner->window); receipt.put(owner->thread); receipt.put(owner->process);
        state->window_process = std::move(owner); reply(frame, ERROR_SUCCESS, "", receipt); return;
    }
    if (stage != 2) throw Fatal(ERROR_NOT_SUPPORTED, "Window operation has no admitted source/callback producer.");
    auto& owner = window_process_owner(); const auto module = reader.get<std::uint64_t>();
    const auto window = reader.get<std::uint32_t>(), incoming = reader.get<std::uint32_t>(); reader.finish();
    if (module != state->module_id || !owner.enumeration || !owner.enumeration_parent || state->calls.back() != owner.enumeration_parent ||
        state->calls.size() >= max_call_depth || !frame.header.parent)
        throw Fatal(ERROR_INVALID_STATE, "EnumWindows callback lost its actual original enumeration/parent/module.");
    state->calls.push_back(frame.header.id); CallReceipt called{};
    SetLastError(incoming); InvokeGuarded(reinterpret_cast<void*>(owner.enumeration), Abi::stdcall_call, nullptr,
        window, static_cast<std::uint32_t>(owner.enumeration_parameter), &called); const auto error = GetLastError(); state->calls.pop_back();
    if (state->first_guarded_exception) std::rethrow_exception(state->first_guarded_exception);
    native_exception_raise(called, "original EnumWindows callback", address(reinterpret_cast<void*>(owner.enumeration)),
        callback_fault.load(), state->callback_reason, state->callback_reason_truncated);
    if (callback_fault.load() || called.stack_delta || called.preserved_registers != 7)
        throw Fatal(callback_fault.load() ? callback_fault.load() : ERROR_INVALID_DATA, "Original window callback violated its actual ABI/correlated caller.");
    Bytes receipt; receipt.put(called.result); receipt.put(error); reply(frame, ERROR_SUCCESS, "", receipt);
}
