#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

namespace {
char game_path[1024]{};
wchar_t file_path[1024]{};
char profile_path[1024]{};
DWORD caller_error = 0;
HANDLE last_closed = INVALID_HANDLE_VALUE;
bool make_paths(const char* source) {
    unsigned int length = 0, folder = 0;
    while (source[length]) { if (source[length] == '\\') folder = length + 1; if (++length >= 900) return false; }
    if (!folder) return false;
    for (unsigned int at = 0; at < folder; ++at) { profile_path[at] = source[at]; file_path[at] = static_cast<wchar_t>(source[at]); }
    const char file[] = "OpenNV-module-read.cfg", profile[] = "OpenNV-module-profile.ini";
    for (unsigned int at = 0; at < sizeof(file); ++at) file_path[folder + at] = static_cast<wchar_t>(file[at]);
    for (unsigned int at = 0; at < sizeof(profile); ++at) profile_path[folder + at] = profile[at];
    return true;
}
}
// /ENTRY targets this complete pure leaf. The source reader verifies the
// actual executable body and TLS vector before Windows is allowed to enter it.
extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core, opennv_domain::NvsePluginInfo* info) {
    if (!core || !info) return false;
    info->info_version = 1; info->name = "OpenNV authored module read caller"; info->version = 1; return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    return core && GetModuleFileNameA(nullptr, game_path, sizeof(game_path)) && make_paths(game_path);
}
extern "C" DWORD __stdcall ModuleReadError() { return caller_error; }
extern "C" DWORD __stdcall ModuleReadProbe(DWORD mode) {
    if (mode == 0) {
        SetLastError(0x12345678);
        const auto file = CreateFileW(file_path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
        caller_error = GetLastError(); if (file == INVALID_HANDLE_VALUE) return 1;
        LARGE_INTEGER extent{};
        if (!GetFileSizeEx(file, &extent) || extent.QuadPart != 17) return 2;
        if (!CloseHandle(file)) return 3; last_closed = file; return 0;
    }
    if (mode == 1) {
        wchar_t missing[1024]; unsigned int at = 0;
        while (file_path[at]) { missing[at] = file_path[at]; ++at; }
        missing[at++] = L'-'; missing[at++] = L'x'; missing[at] = 0;
        SetLastError(0x23456789);
        const auto file = CreateFileW(missing, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
        caller_error = GetLastError(); return file == INVALID_HANDLE_VALUE && caller_error == ERROR_FILE_NOT_FOUND ? 0U : 1U;
    }
    if (mode == 2 || mode == 3) {
        struct Buffer { DWORD first; char output[12]; DWORD last; } value{};
        value.first = 0x11223344; value.last = 0x55667788;
        for (auto& byte : value.output) byte = '!';
        SetLastError(0x3456789a);
        const auto copied = GetPrivateProfileStringA("Example", mode == 2 ? "Answer" : nullptr,
            "43", value.output, sizeof(value.output), profile_path);
        caller_error = GetLastError();
        if (value.first != 0x11223344 || value.last != 0x55667788) return 1;
        if (mode == 2) return copied == 2 && value.output[0] == '4' && value.output[1] == '2' && value.output[2] == 0 ? 0U : 2U;
        // The SDK owns multi-SZ enumeration and nSize-2 truncation. Keep the
        // returned bytes and count; no host parsing supplies the answer.
        return copied == 10 && value.output[10] == 0 && value.output[11] == 0 ? 0U : 3U;
    }
    if (mode == 4 || mode == 6) {
        struct Buffer { DWORD first; char output[5]; char sentinel[3]; DWORD last; } value{};
        value.first = 0x11223344; value.last = 0x55667788;
        for (auto& byte : value.output) byte = '!'; for (auto& byte : value.sentinel) byte = '#';
        SetLastError(0x456789ab);
        const auto copied = GetModuleFileNameA(mode == 6 ? reinterpret_cast<HMODULE>(1) : nullptr, value.output, sizeof(value.output));
        caller_error = GetLastError();
        return copied == 5 && caller_error == ERROR_INSUFFICIENT_BUFFER && value.output[4] == 0 &&
            value.first == 0x11223344 && value.last == 0x55667788 && value.sentinel[0] == '#' ? 0U : 1U;
    }
    if (mode == 5) {
        (void)CreateFileW(file_path, GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        return 1; // The module's original requested write must refuse before the SDK.
    }
    if (mode == 7) { (void)CloseHandle(last_closed); return 1; }
    return 9;
}
