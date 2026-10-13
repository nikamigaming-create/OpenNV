constexpr std::uint32_t io_move_callback = 30;

BOOL io_move(const std::wstring& source, const std::wstring& destination, DWORD flags, DWORD incoming) {
    Bytes request; request.put(1U); request.text(io_utf8(source)); request.text(io_utf8(destination)); request.put(flags);
    auto routes = io_host(io_move_callback, request); Reader reader{routes.data};
    const PluginIoRoute from{reader.get<std::uint64_t>(), reader.get<std::uint32_t>(), io_native_path(io_from_utf8(reader.text()))};
    const PluginIoRoute to{reader.get<std::uint64_t>(), reader.get<std::uint32_t>(), io_native_path(io_from_utf8(reader.text()))}; reader.finish();
    if (!from.id || !to.id || from.id == to.id || from.path.empty() || to.path.empty())
        throw Fatal(ERROR_INVALID_DATA, "MoveFileEx has no two actual admitted private paths.");
    SetLastError(incoming);
    const auto result = from.error || to.error ? FALSE : MoveFileExW(from.path.c_str(), to.path.c_str(), flags);
    const auto last = from.error ? from.error : to.error ? to.error : GetLastError();
    Bytes completed; completed.put(2U); completed.put(from.id); completed.put(to.id); completed.put(flags);
    completed.put(result ? 1U : 0U); completed.put(last);
    auto response = io_host(io_move_callback, completed); Reader returned{response.data};
    if (returned.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "MoveFileEx paired completion did not publish.");
    returned.finish(); SetLastError(last); return result;
}
BOOL WINAPI io_move_ex_a(LPCSTR source, LPCSTR destination, DWORD flags) {
    const auto incoming = GetLastError();
    return nvse_adapter([&] { return io_move(io_ansi(source), destination ? io_ansi(destination) : std::wstring{}, flags, incoming); });
}
BOOL WINAPI io_move_ex_w(LPCWSTR source, LPCWSTR destination, DWORD flags) {
    const auto incoming = GetLastError();
    return nvse_adapter([&] { return io_move(io_wide(source), io_wide(destination, true), flags, incoming); });
}
