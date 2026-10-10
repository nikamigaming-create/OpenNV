// First-party data-only TLS carrier. The Windows loader creates its actual
// compiler/static TLS index and current-thread storage. No game/mod code,
// source callback, original data or imported provider is built into this DLL.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>

#pragma section(".tls", read, write)
#pragma section(".tls$ZZZ", read, write)
#pragma section(".CRT$XLA", read)
#pragma section(".CRT$XLZ", read)
#pragma section(".rdata$T", read)
extern "C" {
__declspec(allocate(".tls")) char _tls_start = 0;
__declspec(allocate(".tls$ZZZ")) char _tls_end = 0;
__declspec(allocate(".CRT$XLA")) PIMAGE_TLS_CALLBACK _xl_a = nullptr;
__declspec(allocate(".CRT$XLZ")) PIMAGE_TLS_CALLBACK _xl_z = nullptr;
unsigned long _tls_index = 0;
__declspec(allocate(".rdata$T")) extern const IMAGE_TLS_DIRECTORY32 _tls_used = {
    reinterpret_cast<DWORD>(&_tls_start), reinterpret_cast<DWORD>(&_tls_end),
    reinterpret_cast<DWORD>(&_tls_index), reinterpret_cast<DWORD>(&_xl_a + 1), 0, 0
};
}
#pragma comment(linker, "/INCLUDE:__tls_used")
extern "C" {
__declspec(thread) __declspec(align(16)) unsigned char engine_tls_data[65536]{};
}

extern "C" std::uint32_t __cdecl OpenNVEngineTlsVersion() { return 1; }
extern "C" std::uint32_t __cdecl OpenNVEngineTlsIndex() { return _tls_index; }
extern "C" std::uint32_t __cdecl OpenNVEngineTlsExtent() {
    return static_cast<std::uint32_t>(_tls_used.EndAddressOfRawData - _tls_used.StartAddressOfRawData + _tls_used.SizeOfZeroFill);
}
extern "C" void* __cdecl OpenNVEngineTlsData() { return engine_tls_data; }
// The DATA export names this symbol's actual PE raw-template address. The
// companion joins it to IMAGE_TLS_DIRECTORY32 and the compiler-computed
// living thread variable. No TEB offset or assembly-symbol relocation is
// guessed, and a raw image address never substitutes for current TLS data.
