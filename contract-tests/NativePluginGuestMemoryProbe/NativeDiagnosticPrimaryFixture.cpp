#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <locale>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// Authored primary: genuine Microsoft provider dependency, pure module entry,
// and no diagnostic path. The original peer must supply its own source policy.
extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface*, opennv_domain::NvsePluginInfo* info) {
    if (!info) return false;
    const auto& current = std::locale::classic(); (void)current;
    info->info_version = 1; info->name = "Authored shared diagnostic provider"; info->version = 1; return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) { return core != nullptr; }
