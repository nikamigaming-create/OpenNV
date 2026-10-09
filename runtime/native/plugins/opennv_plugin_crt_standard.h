#pragma once
#include <windows.h>
#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace opennv_domain {
struct CppRuntimeExport { std::string name; std::uint32_t rva = 0; bool code = false; void* actual = nullptr; };
struct CppRuntimeProvider {
    std::wstring path; std::string sha, declaration;
    HMODULE retained = nullptr;
    std::vector<CppRuntimeExport> exports;
    bool published = false, retiring = false, retired = false;
    ~CppRuntimeProvider() { if (retained) FreeLibrary(retained); }
};
struct CrtStandardStream {
    unsigned index = 0; void* pointer = nullptr; int descriptor = -1;
    HANDLE handle = INVALID_HANDLE_VALUE; std::uint64_t route = 0;
    bool prepared = false, published = false, retired = false, entered = false, close_entered = false;
    int close_result = 0, close_errno = 0;
    unsigned long close_doserrno = 0;
    DWORD close_error = 0;
};
struct CrtDescriptor {
    std::uint64_t provider = 0, route = 0; void* stream = nullptr;
    int descriptor = -1; HANDLE handle = INVALID_HANDLE_VALUE;
    HANDLE identity = INVALID_HANDLE_VALUE;
    bool writable = false, independent = false, close_entered = false;
};
struct PluginCrtStandardRuntime {
    std::map<unsigned, CrtStandardStream> streams;
    std::map<int, CrtDescriptor> descriptors;
    bool publication_entered = false, published = false;
    ~PluginCrtStandardRuntime() {
        for (const auto& row : descriptors)
            if (row.second.identity != INVALID_HANDLE_VALUE) CloseHandle(row.second.identity);
    }
};
}
