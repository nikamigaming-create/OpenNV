#pragma once
#include <windows.h>
#include <cstdint>
#include <map>
#include <memory>
#include <vector>

namespace opennv_domain {
// Native Windows ABI declarations, independent of game objects and instructions.
struct MutexUnicodeString {
    USHORT length = 0, maximum = 0;
    PWSTR buffer = nullptr;
};
struct MutexObjectAttributes {
    ULONG length = sizeof(MutexObjectAttributes);
    HANDLE root = nullptr;
    MutexUnicodeString* name = nullptr;
    ULONG attributes = 0;
    void* descriptor = nullptr;
    void* quality = nullptr;
};
static_assert(sizeof(MutexUnicodeString) == 8, "Original namespace caller is Windows x86.");
static_assert(sizeof(MutexObjectAttributes) == 24, "Windows x86 OBJECT_ATTRIBUTES has its complete native layout.");
using MutexCreateMutant = LONG (NTAPI*)(PHANDLE, ACCESS_MASK, MutexObjectAttributes*, BOOLEAN);
using MutexOpenMutant = LONG (NTAPI*)(PHANDLE, ACCESS_MASK, MutexObjectAttributes*);
using MutexStatusError = ULONG (NTAPI*)(LONG);
struct PluginMutexNamespaceDirectory {
    std::uint64_t capability = 0;
    HANDLE handle = nullptr;
    bool close_entered = false, closed = false;
    BOOL close_result = FALSE;
    DWORD close_error = 0;
};
struct PluginMutexNamespaceRuntime {
    HMODULE windows = nullptr; // Borrowed process-lifetime ntdll, not a loader ref.
    MutexCreateMutant create = nullptr;
    MutexOpenMutant open = nullptr;
    MutexStatusError error = nullptr;
    std::map<std::uint64_t, PluginMutexNamespaceDirectory> directories;
};
struct PluginMutexNamespaceCall {
    std::uint64_t capability = 0;
    HANDLE directory = nullptr;
    std::vector<wchar_t> relative;
    bool used = false;
    LONG status = 0;
};
}

// The implementation includes live in the companion's one anonymous namespace.
// These prototypes deliberately have that same internal linkage.
namespace {
struct MutexSignature;
struct Reader;
void mutex_namespace_prepare(MutexSignature&);
void mutex_namespace_accept(MutexSignature&, Reader&);
HANDLE mutex_namespace_construct(MutexSignature&);
void mutex_namespace_retire();
void mutex_namespace_require_retired();
}
