#pragma once
#include <windows.h>
#include <array>
#include <cstdint>
#include <map>
#include <set>
#include <utility>

namespace opennv_domain {
struct CngSharedPointer {
    std::uint64_t section = 0, view = 0;
    std::uint32_t offset = 0, length = 0;
    explicit operator bool() const { return section != 0; }
};
struct CngSharedSource {
    // Kind 1: admitted game/value heap. Kind 2: genuine source/private mapping.
    std::uint32_t kind = 0, handle = 0, base = 0, length = 0, maximum = 0, offset = 0;
    std::uint64_t object = 0, view = 0;
};
struct CngSharedClientView { CngSharedSource source; std::uint64_t section; };
struct CngSharedClient {
    std::map<std::pair<std::uint32_t, std::uint64_t>, CngSharedClientView> views;
    std::map<std::uint64_t, CngSharedPointer> objects;
    bool detach_entered = false, detach_returned = false, detach_active = false;
    std::uint64_t detach_call = 0;
};
struct CngSharedServiceView {
    std::uint64_t id = 0;
    void* base = nullptr;
    std::uint32_t length = 0, offset = 0;
    bool release_entered = false;
    std::set<std::uint64_t> calls, hashes;
};
struct CngSharedServiceSection {
    std::uint64_t id = 0;
    HANDLE handle = nullptr;
    std::uint32_t maximum = 0;
    bool release_entered = false;
    std::map<std::uint64_t, CngSharedServiceView> views;
};
struct CngSharedService {
    std::map<std::uint64_t, CngSharedServiceSection> sections;
    std::map<std::uint64_t, CngSharedPointer> hash_objects;
};
}
