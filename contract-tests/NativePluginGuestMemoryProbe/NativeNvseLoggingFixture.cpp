#include <windows.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// Independently authored caller of the public, versionless fastcall table.
namespace {
const char* borrowed_path = nullptr;
char query_path[256]{};
bool observe(const opennv_domain::NvseCoreInterface* core) {
    const auto* table = static_cast<const opennv_domain::NvseLoggingInterface*>(core->query_interface(9));
    if (!table || !table->get_plugin_log_path) return false;
    const auto* first = table->get_plugin_log_path();
    const auto* second = table->get_plugin_log_path();
    if (!first || first != second || (borrowed_path && borrowed_path != first)) return false;
    unsigned int index = 0;
    for (; first[index] && index < 255U; ++index) {
        if (borrowed_path && first[index] != query_path[index]) return false;
        query_path[index] = first[index];
    }
    if (first[index] || (borrowed_path && query_path[index])) return false;
    query_path[index] = '\0'; borrowed_path = first; return true;
}
}
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core, opennv_domain::NvsePluginInfo* info) {
    if (!core || !info || !observe(core)) return false;
    *info = {1, "AuthoredLoggingCaller", 1}; return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    if (!core || !observe(core)) return false;
    // Every byte through the terminator is written below. The unused suffix
    // needs no zero fill or CRT initialization in this entry-only caller.
    char file[320]; unsigned int index = 0;
    for (; borrowed_path[index]; ++index) file[index] = borrowed_path[index];
    constexpr char name[] = "opennv_logging_fixture.log";
    for (unsigned int next = 0; next < sizeof(name); ++next) file[index + next] = name[next];
    const auto handle = CreateFileA(file, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) return false;
    constexpr char message[] = "authored logging caller\r\n"; DWORD written = 0;
    const auto result = WriteFile(handle, message, sizeof(message) - 1, &written, nullptr);
    const auto closed = CloseHandle(handle);
    return result && closed && written == sizeof(message) - 1;
}
extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
