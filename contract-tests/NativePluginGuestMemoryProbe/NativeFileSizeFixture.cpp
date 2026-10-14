#include <windows.h>
#include <cstdint>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

namespace {
DWORD caller_error = 0;
constexpr std::uint64_t large_length = 0x100000013ULL;
#ifndef OPENNV_FILE_SIZE_PEER
HANDLE small_file = INVALID_HANDLE_VALUE, large_file = INVALID_HANDLE_VALUE;
bool path(const opennv_domain::NvseCoreInterface* core, const wchar_t* leaf, wchar_t (&out)[1024]) {
    if (!core || !core->runtime_directory) return false;
    const auto* root = core->runtime_directory(); if (!root) return false;
    const auto count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, root, -1, out, 1024);
    if (!count) return false;
    auto at = static_cast<unsigned int>(count - 1);
    while (*leaf) { if (at + 1 >= 1024) return false; out[at++] = *leaf++; }
    out[at] = 0; return true;
}
#endif
}

extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
#ifndef OPENNV_FILE_SIZE_PEER
    if (reason == DLL_PROCESS_DETACH) {
        if (small_file != INVALID_HANDLE_VALUE) { CloseHandle(small_file); small_file = INVALID_HANDLE_VALUE; }
        if (large_file != INVALID_HANDLE_VALUE) { CloseHandle(large_file); large_file = INVALID_HANDLE_VALUE; }
    }
#else
    (void)reason;
#endif
    return TRUE;
}
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core, opennv_domain::NvsePluginInfo* info) {
    if (!core || !info) return false;
    info->info_version = 1; info->name = "OpenNV authored shared file-size caller"; info->version = 1; return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
#ifdef OPENNV_FILE_SIZE_PEER
    return core != nullptr;
#else
    wchar_t smallPath[1024], largePath[1024];
    if (!path(core, L"OpenNVFileSize-small.cfg", smallPath) || !path(core, L"OpenNVFileSize-large.cfg", largePath)) return false;
    small_file = CreateFileW(smallPath, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    large_file = CreateFileW(largePath, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (small_file == INVALID_HANDLE_VALUE || large_file == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER position{}; position.QuadPart = 3;
    return SetFilePointerEx(small_file, position, nullptr, FILE_BEGIN) && SetFilePointerEx(large_file, position, nullptr, FILE_BEGIN);
#endif
}
extern "C" DWORD __stdcall FileSizeHandle(DWORD large) {
#ifdef OPENNV_FILE_SIZE_PEER
    (void)large; return 0;
#else
    return static_cast<DWORD>(reinterpret_cast<std::uintptr_t>(large ? large_file : small_file));
#endif
}
extern "C" DWORD __stdcall FileSizePosition(DWORD large) {
#ifdef OPENNV_FILE_SIZE_PEER
    (void)large; return 0;
#else
    LARGE_INTEGER zero{}, actual{};
    return SetFilePointerEx(large ? large_file : small_file, zero, &actual, FILE_CURRENT) && actual.QuadPart == 3 ? 3U : UINT32_MAX;
#endif
}
extern "C" DWORD __stdcall FileSizeLastError() { return caller_error; }

// Probe the imported original Win32 stdcall itself, including ESP and the
// four callee-preserved x86 registers, without replacing the SDK's result.
__declspec(naked) DWORD __cdecl probe(HANDLE native_handle, PLARGE_INTEGER output, BOOL* result) {
    __asm {
        push ebp
        mov ebp, esp
        push ebx
        push esi
        push edi
        mov ebx, 13579bdfh
        mov esi, 2468ace0h
        mov edi, 11223344h
        push output
        push native_handle
        call GetFileSizeEx
        mov edx, result
        mov dword ptr [edx], eax
        xor eax, eax
        lea edx, [ebp-12]
        cmp esp, edx
        jne broken
        cmp ebx, 13579bdfh
        jne broken
        cmp esi, 2468ace0h
        jne broken
        cmp edi, 11223344h
        jne broken
        jmp done
    broken:
        mov eax, 1
        lea esp, [ebp-12]
    done:
        pop edi
        pop esi
        pop ebx
        pop ebp
        ret
    }
}
extern "C" DWORD __stdcall FileSizeProbe(DWORD raw_handle, DWORD mode) {
    struct Buffer { std::uint64_t first; LARGE_INTEGER output; std::uint64_t last; } value{
        0x1212121234343434ULL, {}, 0x5656565678787878ULL };
    value.output.QuadPart = static_cast<LONGLONG>(0x2233445566778899ULL);
    SetLastError(0x12345678U); BOOL returned = FALSE;
    const auto abi = probe(reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(raw_handle)), &value.output, &returned);
    caller_error = GetLastError();
    if (abi || value.first != 0x1212121234343434ULL || value.last != 0x5656565678787878ULL) return 1;
    if (mode == 2) return !returned && caller_error == ERROR_INVALID_HANDLE &&
        static_cast<std::uint64_t>(value.output.QuadPart) == 0x2233445566778899ULL ? 0U : 2U;
    const auto expected = mode == 1 ? large_length : 17ULL;
    return returned && static_cast<std::uint64_t>(value.output.QuadPart) == expected ? 0U : 3U;
}
