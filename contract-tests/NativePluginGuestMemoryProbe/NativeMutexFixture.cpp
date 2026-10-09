#include <windows.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// Authored public-ABI consumer. No original program instructions or names.
namespace {
HANDLE detach_mutex = nullptr;
const char name_a[] = "Local\\OpenNV.Authored.NativeMutex.Lifetime";
const wchar_t name_w[] = L"Local\\OpenNV.Authored.NativeMutex.Lifetime";
}
extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_DETACH && detach_mutex) {
        const auto released = ReleaseMutex(detach_mutex);
        const auto closed = CloseHandle(detach_mutex); if (closed) detach_mutex = nullptr;
        return released && closed;
    }
    return TRUE;
}
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core,
    opennv_domain::NvsePluginInfo* info) {
    if (!core || !info || core->is_editor) return false;
    info->info_version = 1; info->name = "OpenNV authored Windows mutex caller"; info->version = 1; return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    if (!core || !core->get_handle || core->get_handle() == opennv_domain::nvse_invalid_handle) return false;
    SECURITY_ATTRIBUTES ordinary{sizeof(SECURITY_ATTRIBUTES), nullptr, FALSE};
    detach_mutex = CreateMutexA(&ordinary, TRUE, name_a);
    if (!detach_mutex || GetLastError() == ERROR_ALREADY_EXISTS) return false;
    const auto second = CreateMutexW(nullptr, TRUE, name_w);
    if (!second || second == detach_mutex || GetLastError() != ERROR_ALREADY_EXISTS) return false;
    const auto alias_a = OpenMutexA(MUTEX_ALL_ACCESS, FALSE, name_a);
    const auto alias_w = OpenMutexW(MUTEX_ALL_ACCESS, FALSE, name_w);
    if (!alias_a || !alias_w || WaitForSingleObject(second, 0) != WAIT_OBJECT_0 ||
        WaitForSingleObjectEx(alias_a, 0, FALSE) != WAIT_OBJECT_0) return false;
    // Creation's initial owner plus two genuine recursive waits require three
    // releases. Creating another reference does not create another acquisition.
    if (!ReleaseMutex(detach_mutex) || !ReleaseMutex(second) || !ReleaseMutex(alias_a)) return false;
    SetLastError(0);
    if (ReleaseMutex(alias_w) || GetLastError() != ERROR_NOT_OWNER) return false;
    if (!CloseHandle(second) || !CloseHandle(alias_a) || !CloseHandle(alias_w)) return false;
    const auto first = CreateMutexExA(nullptr, nullptr, CREATE_MUTEX_INITIAL_OWNER, MUTEX_ALL_ACCESS);
    const auto other = CreateMutexExW(nullptr, nullptr, 0, MUTEX_ALL_ACCESS);
    const auto independent = CreateMutexW(nullptr, FALSE, nullptr);
    if (!first || !other || !independent || !CloseHandle(independent)) return false;
    const HANDLE handles[] = {first, other};
    if (WaitForMultipleObjects(2, handles, FALSE, 0) != WAIT_OBJECT_0 || !ReleaseMutex(first)) return false;
    if (WaitForMultipleObjects(2, handles, TRUE, 0) != WAIT_OBJECT_0 || !ReleaseMutex(first) || !ReleaseMutex(other)) return false;
    if (WaitForMultipleObjectsEx(2, handles, FALSE, 0, FALSE) != WAIT_OBJECT_0 || !ReleaseMutex(first)) return false;
    if (!ReleaseMutex(first) || !CloseHandle(first) || !CloseHandle(other)) return false;
    // The genuine original image's detach performs the remaining acquisition
    // release and CloseHandle. These are not host-forced cleanup calls.
    return WaitForSingleObject(detach_mutex, 0) == WAIT_OBJECT_0;
}
