#pragma once
#include <windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace opennv_domain {
enum class CryptoOperation : std::uint32_t {
    open_algorithm = 1, get_property = 2, create_hash = 3, hash_data = 4,
    finish_hash = 5, destroy_hash = 6, close_algorithm = 7
};
struct PluginCryptoAlgorithm {
    std::uint64_t id;
    BCRYPT_ALG_HANDLE handle;
    std::uint32_t flags;
};
struct PluginCryptoHash {
    std::uint64_t id, algorithm;
    BCRYPT_HASH_HANDLE handle;
    void* object;
    std::uint32_t object_bytes, flags;
};
struct PluginCryptoEvent {
    CryptoOperation operation;
    std::uint64_t lifetime, algorithm;
    std::uint32_t handle, flags, requested, result_bytes;
    NTSTATUS status;
    std::wstring name, implementation;
    bool implementation_present;
    std::uint32_t object, object_bytes;
    bool result_available = false;
};
struct PluginCryptoRuntime {
    std::wstring path;
    std::string sha, declaration;
    HMODULE provider = nullptr;
    bool load_entered = false;
    DWORD load_error = ERROR_SUCCESS;
    bool registered = false, retired = false;
    std::uint64_t next_lifetime = 0;
    std::map<std::uint32_t, PluginCryptoAlgorithm> algorithms;
    std::map<std::uint32_t, PluginCryptoHash> hashes;
    std::vector<PluginCryptoEvent> pending;
    decltype(&BCryptOpenAlgorithmProvider) open = nullptr;
    decltype(&BCryptGetProperty) property = nullptr;
    decltype(&BCryptCreateHash) create = nullptr;
    decltype(&BCryptHashData) data = nullptr;
    decltype(&BCryptFinishHash) finish = nullptr;
    decltype(&BCryptDestroyHash) destroy = nullptr;
    decltype(&BCryptCloseAlgorithmProvider) close = nullptr;
    ~PluginCryptoRuntime() {
        // Terminal process cleanup is not an orderly provider/hash receipt.
        // Retain the genuine provider through every best-effort native close.
        if (provider) {
            if (destroy) for (const auto& hash : hashes) (void)destroy(hash.second.handle);
            if (close) for (const auto& algorithm : algorithms) (void)close(algorithm.second.handle, 0);
            (void)FreeLibrary(provider);
        }
    }
};
}
