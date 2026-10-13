#include <windows.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// Independently authored caller. Its only code address is supplied by the
// private owned-source test; no original address or instruction body is built in.
namespace {
constexpr DWORD failure_output_seed = 0x13579U;
struct FailedProtection { BOOL result; DWORD output, error; };
FailedProtection failed_protection(void* pointer, DWORD access) {
    DWORD output = failure_output_seed;
    const auto result = VirtualProtect(pointer, 5, access, &output);
    const auto error = GetLastError();
    return {result, output, error};
}
unsigned int number(const char* value) {
    unsigned int result = 0;
    if (!value || !*value) return 0;
    for (; *value; ++value) {
        if (*value < '0' || *value > '9' || result > (0xffffffffU - (*value - '0')) / 10U) return 0;
        result = result * 10U + (*value - '0');
    }
    return result;
}
bool read(const opennv_domain::NvseCoreInterface* core, unsigned int& site, unsigned int& mode, unsigned int& neighbor) {
    char path[32768]; unsigned int count = 0;
    const auto* directory = core->runtime_directory();
    if (!directory) return false;
    for (; directory[count]; ++count) { if (count >= 32000) return false; path[count] = directory[count]; }
    constexpr char suffix[] = "Data\\NVSE\\nvse_config.ini";
    for (unsigned int index = 0; index < sizeof(suffix); ++index) path[count + index] = suffix[index];
    char text[32];
    if (!GetPrivateProfileStringA("SUPPRESSION", "Address", nullptr, text, sizeof(text), path)) return false;
    site = number(text);
    if (!GetPrivateProfileStringA("SUPPRESSION", "Mode", nullptr, text, sizeof(text), path)) return false;
    mode = number(text);
    if (GetPrivateProfileStringA("SUPPRESSION", "Neighbor", nullptr, text, sizeof(text), path)) neighbor = number(text);
    return site != 0 && (mode != 7 || neighbor != 0);
}
bool suppress_writable(unsigned int site) {
    // No source mutation is entered for this baseline. Win32 documents the
    // output only on success; compare the actual provider, never a sentinel.
    const auto baseline = failed_protection(reinterpret_cast<void*>(1U), PAGE_EXECUTE_READ);
    DWORD previous = 0;
    const auto opened = VirtualProtect(reinterpret_cast<void*>(site), 5, PAGE_EXECUTE_READWRITE, &previous);
    if (!opened) return false;
    auto* code = reinterpret_cast<volatile unsigned char*>(site);
    code[0] = 0x0f; code[1] = 0x1f; code[2] = 0x44; code[3] = 0; code[4] = 0;
    const auto failed = failed_protection(reinterpret_cast<void*>(static_cast<unsigned int>(opened)), previous);
    if (baseline.result || failed.result != baseline.result || failed.output != baseline.output || failed.error != baseline.error) return false;
    return FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(site), 5) != FALSE;
}
void __cdecl unbound_target() { }
}
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core, opennv_domain::NvsePluginInfo* info) {
    if (!core || !info) return false;
    *info = {1, "AuthoredSourceSuppressionCaller", 1}; return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    unsigned int site = 0, mode = 0, neighbor = 0;
    if (!core || !read(core, site, mode, neighbor)) return false;
    if (mode == 7) return suppress_writable(neighbor) && suppress_writable(site);
    const auto baseline = failed_protection(reinterpret_cast<void*>(1U), PAGE_EXECUTE_READ);
    auto* code = reinterpret_cast<volatile unsigned char*>(site); DWORD old = 0, restored = 0;
    const auto opened = VirtualProtect(reinterpret_cast<void*>(site), 5, PAGE_EXECUTE_READWRITE, &old);
    if (!opened) return false;
    if (mode == 3) {
        const auto delta = reinterpret_cast<unsigned int>(unbound_target) - site - 5U;
        code[0] = 0xe8;
        for (unsigned int index = 0; index < 4; ++index) code[index + 1] = static_cast<unsigned char>(delta >> (index * 8));
    }
    else {
        code[0] = mode == 1 ? 0xe9 : 0x0f;
        code[1] = 0x1f; code[2] = 0x44; code[3] = 0; code[4] = 0;
    }
    if (mode == 4 || mode == 6) {
        // A caller that uses the preceding SDK BOOL as its restoration address
        // must receive the actual failure. The source page remains writable.
        const auto failed = failed_protection(reinterpret_cast<void*>(static_cast<unsigned int>(opened)), old);
        if (baseline.result || failed.result != baseline.result || failed.output != baseline.output || failed.error != baseline.error) return false;
    }
    else if (mode == 5) {
        if (!FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(site), 5)) return false;
        if (!VirtualProtect(reinterpret_cast<void*>(site), 5, old, &restored)) return false;
    }
    else if (!VirtualProtect(reinterpret_cast<void*>(site), 5, old, &restored)) return false;
    if (!FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(site), 5)) return false;
    if (mode == 2 && !FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(site), 5)) return false;
    if (mode == 6) {
        code[4] = 1; // no new protection/write lease: cached identity must refuse
        if (!FlushInstructionCache(GetCurrentProcess(), reinterpret_cast<void*>(site), 5)) return false;
    }
    return mode == 4 || mode == 6 || restored == PAGE_EXECUTE_READWRITE;
}
extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
