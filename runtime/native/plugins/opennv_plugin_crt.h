#pragma once
#include <windows.h>
#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <map>
#include <memory>
#include <set>
#include <string>
#include <vector>

namespace opennv_domain {
enum class CrtOperation : std::uint32_t {
    open = 1, close = 2, read = 3, write = 4, seek = 5, tell = 6, rewind = 7,
    flush = 8, put_character = 9, put_string = 10, formatted_write = 11,
    eof = 12, error = 13, clear_error = 14, make_directory = 15
};
struct CrtStatus { int error; unsigned long dos; bool available; int eof, stream_error; };
struct CrtProvider {
    std::uint64_t id = 0;
    std::wstring path;
    std::string sha, declaration;
    std::map<std::string, std::set<std::string>> imports;
    HMODULE retained = nullptr;
    bool registered = false;
    ~CrtProvider() { if (retained) FreeLibrary(retained); }
};
struct CrtStream {
    std::uint64_t provider, route;
    // An opaque actual FILE* returned by this same export provider. It is
    // neither a HANDLE nor allocated metadata pretending to be a CRT object.
    void* pointer;
    bool writable;
    std::wstring mode;
};
struct CrtEvent {
    std::uint64_t provider, route;
    std::uint32_t stream;
    CrtOperation operation;
    std::uint64_t argument, requested;
    std::int64_t result;
    bool retired;
    CrtStatus status;
};
struct PluginCrtRuntime {
    std::vector<std::unique_ptr<CrtProvider>> providers;
    std::map<std::uint32_t, CrtStream> streams;
    std::vector<CrtEvent> pending;
    std::vector<CrtEvent> failed_closes;
    ~PluginCrtRuntime() {
        // Terminal cleanup is not an orderly plugin/CRT receipt. Attempt each
        // surviving stream while its exact export provider is still retained.
        for (const auto& stream : streams) for (const auto& provider : providers)
            if (provider->id == stream.second.provider && provider->retained) {
                const auto close = GetProcAddress(provider->retained, "fclose");
                if (close) (void)reinterpret_cast<int (__cdecl*)(void*)>(close)(stream.second.pointer);
            }
    }
};
}
