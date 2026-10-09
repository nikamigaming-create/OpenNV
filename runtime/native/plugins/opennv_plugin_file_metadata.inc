constexpr std::uint32_t io_file_metadata_callback = 26;
void io_file_metadata_result(std::uint64_t route, HANDLE file, std::uint32_t api, std::uint32_t information_class,
    const void* caller_buffer, std::uint32_t length, BOOL returned, DWORD last) {
    Bytes request; request.put(route); request.put(address(file)); request.put(api); request.put(information_class); request.put(length);
    request.put(returned ? 1U : 0U); request.put(last); request.put(returned ? 1U : 0U); request.put(returned ? length : 0U);
    if (returned && length) {
        const auto at = request.data.size(); request.data.resize(at + length);
        nvse_readable(caller_buffer, length); std::memcpy(request.data.data() + at, caller_buffer, length);
    }
    crt_owned_callback(io_file_metadata_callback, request);
}
BOOL io_attributes_extended(const std::wstring& path, GET_FILEEX_INFO_LEVELS level, void* destination, DWORD incoming) {
    if (level != GetFileExInfoStandard) throw Fatal(ERROR_NOT_SUPPORTED, "File attributes have an unowned information-class caller layout.");
    expression_writable(destination, sizeof(WIN32_FILE_ATTRIBUTE_DATA));
    const auto route = io_resolve(io_attributes_api, 5, path);
    BOOL returned = FALSE; DWORD last = route.error;
    if (!route.error) { SetLastError(incoming); returned = GetFileAttributesExW(route.path.c_str(), level, destination); last = GetLastError(); }
    io_file_metadata_result(route.id, nullptr, io_attributes_api, static_cast<std::uint32_t>(level), destination,
        sizeof(WIN32_FILE_ATTRIBUTE_DATA), returned, last);
    io_completed(route, io_attributes_api, returned ? ERROR_SUCCESS : last); SetLastError(last); return returned;
}
BOOL WINAPI io_attributes_ex_w(LPCWSTR path, GET_FILEEX_INFO_LEVELS level, LPVOID destination) {
    const auto incoming = GetLastError();
    return nvse_adapter([&] { return io_attributes_extended(io_wide(path), level, destination, incoming); });
}
BOOL WINAPI io_attributes_ex_a(LPCSTR path, GET_FILEEX_INFO_LEVELS level, LPVOID destination) {
    const auto incoming = GetLastError();
    return nvse_adapter([&] {
        if (!AreFileApisANSI()) throw Fatal(ERROR_NOT_SUPPORTED, "OEM file-API byte conversion has no admitted private path owner.");
        return io_attributes_extended(io_ansi(path), level, destination, incoming);
    });
}
bool io_owned_information_class(FILE_INFO_BY_HANDLE_CLASS information_class) {
    switch (information_class) {
    case FileBasicInfo: case FileStandardInfo: case FileNameInfo: case FileStreamInfo: case FileCompressionInfo:
    case FileAttributeTagInfo: case FileIoPriorityHintInfo: case FileRemoteProtocolInfo: case FileStorageInfo:
    case FileAlignmentInfo: case FileIdInfo: case FileCaseSensitiveInfo: case FileNormalizedNameInfo: return true;
    default: return false;
    }
}
BOOL WINAPI io_information_by_handle_ex(HANDLE file, FILE_INFO_BY_HANDLE_CLASS information_class, LPVOID destination, DWORD length) {
    const auto incoming = GetLastError();
    return nvse_adapter([&] {
        if (!io_owned_information_class(information_class))
            throw Fatal(ERROR_NOT_SUPPORTED, "File enumeration/set-information/unknown class lacks its winning-directory/format owner.");
        if (length > max_payload - 256) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Metadata caller output exceeds its entered callback extent.");
        const auto owned = io_file(file); if (length) expression_writable(destination, length);
        SetLastError(incoming); const auto returned = GetFileInformationByHandleEx(file, information_class, destination, length); const auto last = GetLastError();
        io_file_metadata_result(owned.capability, file, 13, static_cast<std::uint32_t>(information_class), destination, length, returned, last);
        SetLastError(last); return returned;
    });
}
