#pragma once
#include <windows.h>
#include <bcrypt.h>
#include "opennv_plugin_cng_shared.h"
#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <iterator>
#include <map>
#include <memory>
#include <stdexcept>
#include <set>
#include <string>
#include <utility>
#include <vector>

namespace opennv_domain {
// This executable owns Windows handles. No Windows handle value is a wire ID.
enum class CngServiceStep : std::uint32_t {
    prepare = 0, begin = 1, write = 2, execute = 3, read = 4,
    release = 5, retire = 6, abandon_after_client_exit = 7, publish_local = 8,
    shared_bind = 9, shared_release = 10, shared_begin = 11, detach_scope = 12
};
enum class CngBuffer : std::uint32_t { name = 0, implementation = 1, input = 2, output = 3 };
struct CngTransfer {
    bool present = false;
    std::uint32_t length = 0, written = 0;
    std::vector<std::uint8_t> bytes;
    PUCHAR shared = nullptr;
    void allocate(bool exists, std::uint32_t size) {
        present = exists; length = size; written = exists ? 0U : size;
        // Non-null, zero-length is distinct from a null pointer to the SDK.
        if (exists) bytes.resize(size ? size : 1);
    }
    PUCHAR pointer() { return shared ? shared : present ? bytes.data() : nullptr; }
};
struct CngInvocation {
    std::uint64_t id = 0, handle = 0;
    std::uint32_t operation = 0, flags = 0, caller_error = 0, object_bytes = 0;
    bool destination_present = false, copied_present = false, entered = false, completed = false;
    std::uint32_t copied = 0, last_error = 0;
    NTSTATUS status = 0;
    std::uint64_t created = 0;
    std::array<CngTransfer, 4> buffers;
    std::array<CngSharedPointer, 4> shared_buffers{};
    CngSharedPointer shared_object{}, shared_copied{};
};
struct CngAlgorithmLease { BCRYPT_ALG_HANDLE handle; std::uint32_t flags; };
struct CngHashLease { BCRYPT_HASH_HANDLE handle; std::uint64_t algorithm; };
struct CngSystemService {
    HMODULE provider = nullptr, primitives = nullptr;
    bool prepare_entered = false, prepared = false, retired = false;
    DWORD prepare_error = ERROR_SUCCESS;
    std::uint64_t next_invocation = 0, next_handle = 0;
    std::wstring provider_path, primitives_path;
    std::string provider_sha, primitives_sha;
    std::map<std::uint64_t, CngAlgorithmLease> algorithms;
    std::map<std::uint64_t, CngHashLease> hashes;
    std::map<std::uint64_t, CngInvocation> invocations;
    std::unique_ptr<CngSharedService> shared;
    std::set<std::uint64_t> emergency_attempted;
    decltype(&BCryptOpenAlgorithmProvider) open = nullptr;
    decltype(&BCryptGetProperty) property = nullptr;
    decltype(&BCryptCreateHash) create = nullptr;
    decltype(&BCryptHashData) data = nullptr;
    decltype(&BCryptFinishHash) finish = nullptr;
    decltype(&BCryptDestroyHash) destroy = nullptr;
    decltype(&BCryptCloseAlgorithmProvider) close = nullptr;
    // A failed generation is terminated by the existing exact-child owner.
    // Destruction is not a fabricated orderly Windows cleanup receipt.
};
struct CngImportedHandle {
    std::uint64_t service_generation, id, algorithm;
    bool hash, live;
};
struct CngSystemClient {
    std::uint64_t service_generation = 0;
    bool entered = false, retired = false;
    // Retired token addresses remain allocated until exact client process exit,
    // so reuse cannot silently turn an old handle into a new capability.
    std::map<std::uint32_t, std::unique_ptr<CngImportedHandle>> tokens;
    std::unique_ptr<CngSharedClient> shared;
};
}
