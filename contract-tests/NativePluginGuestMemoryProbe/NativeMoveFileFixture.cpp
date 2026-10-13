#include <windows.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

namespace {
bool path(const opennv_domain::NvseCoreInterface* core, const wchar_t* leaf, wchar_t (&value)[1024]) {
    if (!core || !core->runtime_directory) return false;
    const auto* root = core->runtime_directory(); if (!root) return false;
    const auto copied = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, root, -1, value, 1024);
    if (!copied) return false;
    unsigned int at = static_cast<unsigned int>(copied - 1);
    while (*leaf) { if (at + 1 >= 1024) return false; value[at++] = *leaf++; }
    value[at] = 0; return true;
}
bool bytes(const wchar_t* name, const char* expected, DWORD length) {
    const auto file = CreateFileW(name, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    char actual[16]{}; DWORD read = 0;
    bool good = ReadFile(file, actual, sizeof(actual), &read, nullptr) && read == length;
    for (DWORD at = 0; at < length; ++at) good = good && actual[at] == expected[at];
    return CloseHandle(file) && good;
}
bool absent(const wchar_t* name) {
    const auto file = CreateFileW(name, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    const auto last = GetLastError();
    if (file != INVALID_HANDLE_VALUE) { CloseHandle(file); return false; }
    return last == ERROR_FILE_NOT_FOUND;
}
}

extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core, opennv_domain::NvsePluginInfo* info) {
    if (!core || !info || core->is_editor) return false;
    info->info_version = 1; info->name = "OpenNV authored paired file-move caller"; info->version = 1;
    return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    wchar_t source[1024], target[1024], renamed[1024], missing[1024];
    if (!path(core, L"OpenNVMove-source.cfg", source) || !path(core, L"OpenNVMove-target.cfg", target) ||
        !path(core, L"OpenNVMove-\u03a9.cfg", renamed) || !path(core, L"OpenNVMove-missing.cfg", missing)) return false;
    if (MoveFileExW(source, target, 0)) return false;
    const auto conflict = GetLastError();
    if (conflict != ERROR_ALREADY_EXISTS && conflict != ERROR_FILE_EXISTS) return false;
    if (!bytes(source, "SOURCE", 6) || !bytes(target, "TARGET", 6)) return false;
    char narrowSource[1024], narrowTarget[1024];
    if (!WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, source, -1, narrowSource, 1024, nullptr, nullptr) ||
        !WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, target, -1, narrowTarget, 1024, nullptr, nullptr)) return false;
    if (!MoveFileExA(narrowSource, narrowTarget, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) ||
        !absent(source) || !bytes(target, "SOURCE", 6)) return false;
    if (!MoveFileExW(target, renamed, MOVEFILE_WRITE_THROUGH) || !absent(target) || !bytes(renamed, "SOURCE", 6)) return false;
    if (MoveFileExW(missing, renamed, MOVEFILE_REPLACE_EXISTING) || GetLastError() != ERROR_FILE_NOT_FOUND) return false;
    return bytes(renamed, "SOURCE", 6);
}
