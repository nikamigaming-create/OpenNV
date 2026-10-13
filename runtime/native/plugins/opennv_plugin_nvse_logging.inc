// The public xNVSE logging table has no version word. Its borrowed path is
// stored in the same read-only allocation and lifetime as the core table.
const char* __fastcall nvse_plugin_log_path() {
    return nvse_adapter([] {
        auto& owner = nvse_owner();
        if (!owner.logging_path || owner.logging_path_length > 255U)
            throw Fatal(ERROR_INVALID_DATA, "Logging path lost its original character extent.");
        Bytes bytes; bytes.put(address(owner.logging_path)); bytes.put(owner.logging_path_length);
        nvse_bytes(bytes, owner.logging_path, owner.logging_path_length);
        if (nvse_host(NvseHostCall::logging_get_path, bytes) != 1)
            throw Fatal(ERROR_INVALID_DATA, "Logging getter lost its C# source/path lifetime.");
        return owner.logging_path;
    });
}
