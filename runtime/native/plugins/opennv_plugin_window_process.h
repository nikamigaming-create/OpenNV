#pragma once
#include <windows.h>
#include <tlhelp32.h>
#include <cstdint>
#include <map>
#include <set>
#include <string>
#include <vector>

namespace opennv_domain {
enum class WindowApi : std::uint32_t {
    set_active = 1, foreground, set_foreground, text, client, rectangle, send_input,
    cursor, client_screen, clip, enumerate, window_class, thread_process, active,
    focus, set_cursor, bring_top, show, post, attach, visible, keyboard_layout,
    keyboard_state, virtual_key, unicode
};
struct WindowProcessHandle { HANDLE value = nullptr; std::uint32_t kind = 0; };
struct WindowProcessRuntime {
    std::uint32_t window = 0, thread = 0, process = 0;
    std::string source;
    std::map<std::uint32_t, WindowProcessHandle> handles;
    std::map<HMODULE, bool> providers;
    WNDENUMPROC enumeration = nullptr;
    LPARAM enumeration_parameter = 0;
    std::uint64_t enumeration_parent = 0;
    bool bound = false, retired = false, pending = false;
    // A failed SDK/native receipt retains this owner until actual child closure.
};
}
