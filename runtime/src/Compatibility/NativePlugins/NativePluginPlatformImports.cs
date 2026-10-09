namespace OpenNV.Runtime.Compatibility.NativePlugins;

// These are real Windows/CRT exports. Naming a platform owner never supplies
// TESForm, Script, Main, a game singleton, a hook or an imported file capability.
internal static class NativePluginPlatformImports
{
    private static readonly IReadOnlySet<string> Kernel = new HashSet<string>(StringComparer.Ordinal)
    {
        "VirtualAlloc", "VirtualFree", "VirtualProtect", "VirtualQuery", "FlushInstructionCache",
        "GetCurrentThreadId", "GetCurrentProcessId", "GetCurrentProcess", "GetSystemTimeAsFileTime",
        "GetTickCount", "GetTickCount64", "QueryPerformanceCounter", "QueryPerformanceFrequency",
        "EnterCriticalSection", "LeaveCriticalSection", "InitializeCriticalSection", "InitializeCriticalSectionEx",
        "InitializeCriticalSectionAndSpinCount", "DeleteCriticalSection", "TryEnterCriticalSection", "InitializeSListHead",
        "AcquireSRWLockExclusive", "AcquireSRWLockShared", "ReleaseSRWLockExclusive", "ReleaseSRWLockShared",
        "SleepConditionVariableSRW", "WakeAllConditionVariable", "WakeConditionVariable", "Sleep",
        "GetModuleHandleA", "GetModuleHandleW", "GetModuleFileNameA", "GetModuleFileNameW", "DisableThreadLibraryCalls",
        "GetLastError", "SetLastError", "IsDebuggerPresent", "IsProcessorFeaturePresent", "SetUnhandledExceptionFilter",
        "UnhandledExceptionFilter", "TerminateProcess", "LocalFree", "LocalAlloc", "FormatMessageA", "FormatMessageW",
        "MultiByteToWideChar", "WideCharToMultiByte", "GetLocaleInfoEx", "GetACP", "GetCPInfo", "GetStringTypeW",
    };
    private static readonly IReadOnlySet<string> Runtime = new HashSet<string>(StringComparer.Ordinal)
    {
        "_cexit", "_invalid_parameter_noinfo_noreturn", "abort", "_invoke_watson", "_initterm", "_initterm_e",
        "_seh_filter_dll", "_configure_narrow_argv", "_initialize_narrow_environment", "_initialize_onexit_table",
        "_register_onexit_function", "_execute_onexit_table", "_crt_atexit", "_errno", "terminate", "_get_errno", "_get_doserrno",
        "malloc", "calloc", "free", "realloc", "_aligned_malloc", "_aligned_realloc", "_aligned_free", "_callnewh",
        "memcpy", "memmove", "memset", "memcmp", "strlen", "strchr", "strrchr", "strstr", "wcsrchr", "wcslen",
        "strcpy_s", "strcat_s", "_stricmp", "_strnicmp", "strncmp", "_strdup", "tolower", "isalnum", "_wcsicmp", "_mbsicmp",
        "strtol", "strtoul", "strtof", "strtod", "strtoull", "_time64", "_localtime64_s", "___lc_codepage_func",
        "_CIfmod", "_CIatan2", "_fdclass", "_dclass", "_ldclass", "_ldsign", "_fdsign", "_dsign",
        "_libm_sse2_acos_precise", "_libm_sse2_cos_precise", "_libm_sse2_sin_precise", "_libm_sse2_sqrt_precise",
        "_libm_sse2_pow_precise", "lroundf", "modf", "ldexp", "ceil",
        "__stdio_common_vsprintf", "__stdio_common_vsprintf_s", "__stdio_common_vsnprintf_s", "__stdio_common_vsscanf",
        "__std_type_info_destroy_list", "_CxxThrowException", "__CxxFrameHandler3", "__std_exception_copy", "__std_exception_destroy",
        "_except_handler4_common", "__std_terminate", "__current_exception", "__current_exception_context", "__RTDynamicCast", "_purecall",
    };
    private static readonly IReadOnlySet<string> Cpp = new HashSet<string>(StringComparer.Ordinal)
    {
        "??1_Lockit@std@@QAE@XZ", "??0_Lockit@std@@QAE@H@Z", "?_Getgloballocale@locale@std@@CAPAV_Locimp@12@XZ",
        "?_Init@locale@std@@CAPAV_Locimp@12@_N@Z", "?_Xregex_error@std@@YAXW4error_type@regex_constants@1@@Z",
        "?_Raise_handler@std@@3P6AXABVexception@stdext@@@ZA", "?id@?$ctype@D@std@@2V0locale@2@A",
        "??_7_Facet_base@std@@6B@", "?id@?$collate@D@std@@2V0locale@2@A", "??_7facet@locale@std@@6B@",
        "_Strcoll", "?_Getcat@?$ctype@D@std@@SAIPAPBVfacet@locale@2@PBV42@@Z", "?tolower@?$ctype@D@std@@QBEPBDPADPBD@Z",
        "?tolower@?$ctype@D@std@@QBEDD@Z", "??0facet@locale@std@@IAE@I@Z", "?_Decref@facet@locale@std@@UAEPAV_Facet_base@3@XZ",
        "?_Incref@facet@locale@std@@UAEXXZ", "??Bid@locale@std@@QAEIXZ", "?_Getcoll@_Locinfo@std@@QBE?AU_Collvec@@XZ",
        "??1_Locinfo@std@@QAE@XZ", "??0_Locinfo@std@@QAE@PBD@Z", "_Strxfrm", "?_Xbad_alloc@std@@YAXXZ",
        "?_Xlength_error@std@@YAXPBD@Z", "?_Xout_of_range@std@@YAXPBD@Z",
    };
    internal static string? Owner(string library, string name)
    {
        if (NativePluginIoImports.Unowned.Contains(name) || NativePluginCrtImports.IsFileImport(library, name)) return null;
        if ((library is "kernel32.dll" or "kernelbase.dll" || library.StartsWith("api-ms-win-core-", StringComparison.Ordinal)) && Kernel.Contains(name))
            return "actual-Windows-x86-export:" + library + "!" + name;
        if (library == "user32.dll" && name == "MessageBoxA") return "actual-Windows-x86-dialog-export:user32.dll!MessageBoxA";
        if ((library.StartsWith("api-ms-win-crt-", StringComparison.Ordinal) || library is "ucrtbase.dll" or "vcruntime140.dll") && Runtime.Contains(name))
            return "actual-selected-x86-CRT-export:" + library + "!" + name;
        if (library == "msvcp140.dll" && Cpp.Contains(name)) return "actual-selected-x86-C++-runtime-export:" + library + "!" + name;
        return null;
    }
}
