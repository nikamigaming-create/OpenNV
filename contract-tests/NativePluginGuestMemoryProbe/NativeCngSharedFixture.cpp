#include <windows.h>
#include <bcrypt.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// Independently authored public-ABI caller; these buffers contain no game data.
namespace {
HANDLE section = nullptr;
PUCHAR objects = nullptr, output = nullptr;
BCRYPT_ALG_HANDLE algorithm = nullptr;
BCRYPT_HASH_HANDLE detach_hash = nullptr;
constexpr ULONG data_bytes = 140013;
const UCHAR expected[32] = {
    0x60, 0x69, 0x4d, 0x37, 0x8d, 0x4d, 0xed, 0x3b, 0xb2, 0x47, 0x0d, 0xaa, 0xf4, 0x10, 0xbe, 0x43,
    0x9d, 0x57, 0xa9, 0xae, 0xba, 0x95, 0xed, 0x8e, 0x3b, 0x93, 0x4d, 0x68, 0x5c, 0xe1, 0xa0, 0xe8
};
bool retire() {
    if (detach_hash) {
        if (BCryptDestroyHash(detach_hash) < 0) return false;
        detach_hash = nullptr;
    }
    if (algorithm) {
        if (BCryptCloseAlgorithmProvider(algorithm, 0) < 0) return false;
        algorithm = nullptr;
    }
    if (output) { if (!UnmapViewOfFile(output)) return false; output = nullptr; }
    if (objects) { if (!UnmapViewOfFile(objects)) return false; objects = nullptr; }
    if (section) { if (!CloseHandle(section)) return false; section = nullptr; }
    return true;
}
}

extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
    return reason == DLL_PROCESS_DETACH ? (retire() ? TRUE : FALSE) : TRUE;
}

extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core,
    opennv_domain::NvsePluginInfo* info) {
    if (!core || core->is_editor || !info) return false;
    info->info_version = 1;
    info->name = "OpenNV authored shared-buffer CNG caller";
    info->version = 1;
    return true;
}

extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    if (!core || !core->get_handle || core->get_handle() == opennv_domain::nvse_invalid_handle) return false;
    section = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, 262144, nullptr);
    if (!section) return false;
    // This independently authored ABI case chooses its own original view
    // layout. It does not establish collision-free placement for game buffers.
    objects = static_cast<PUCHAR>(MapViewOfFileEx(section, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, 196608,
        reinterpret_cast<void*>(0x30000000u)));
    output = static_cast<PUCHAR>(MapViewOfFileEx(section, FILE_MAP_READ | FILE_MAP_WRITE, 0, 196608, 65536,
        reinterpret_cast<void*>(0x30030000u)));
    if (!objects || !output || objects == output) return false;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0 || !algorithm) return false;
    auto* object_length = reinterpret_cast<PULONG>(output);
    auto* copied = reinterpret_cast<PULONG>(output + sizeof(ULONG));
    *object_length = *copied = 0;
    if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, output, sizeof(ULONG), copied, 0) < 0 ||
        *copied != sizeof(ULONG) || !*object_length || *object_length > 4096) return false;
    for (ULONG at = 0; at < *object_length; ++at) objects[at] = 0xa5;
    BCRYPT_HASH_HANDLE hash = nullptr;
    if (BCryptCreateHash(algorithm, &hash, objects, *object_length, nullptr, 0, 0) < 0 || !hash) return false;
    bool object_changed = false;
    for (ULONG at = 0; at < *object_length; ++at) object_changed = object_changed || objects[at] != 0xa5;
    if (!object_changed) return false;
    for (ULONG at = 0; at < data_bytes; ++at) objects[8192 + at] = static_cast<UCHAR>(at * 37 + 11);
    if (BCryptHashData(hash, objects + 8192, data_bytes, 0) < 0 || BCryptFinishHash(hash, output + 64, 32, 0) < 0) return false;
    for (ULONG at = 0; at < 32; ++at) if (output[64 + at] != expected[at]) return false;
    if (BCryptDestroyHash(hash) < 0) return false;
    // Keep an independent genuine hash/object view for real DllMain detach.
    if (BCryptCreateHash(algorithm, &detach_hash, objects + 4096, *object_length, nullptr, 0, 0) < 0 || !detach_hash) return false;
    return BCryptHashData(detach_hash, objects + 8192, 17, 0) >= 0;
}
