#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <random>
#include <type_traits>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// Authored x86 callers of the public Windows and installed MSVC declarations.
// The original imports, provider code, entropy source and exceptions are real.
namespace {
using TryShared = BOOLEAN (WINAPI*)(PSRWLOCK);
using RandomDevice = unsigned int (__cdecl*)();
using StartupInfo = void (WINAPI*)(LPSTARTUPINFOW);
using StandardHandle = HANDLE (WINAPI*)(DWORD);
static_assert(sizeof(void*) == 4 && sizeof(BOOLEAN) == 1 && sizeof(unsigned int) == 4,
    "Exercise the actual x86 byte/uint32 contracts.");
static_assert(std::is_same_v<decltype(&TryAcquireSRWLockShared), TryShared>);
static_assert(std::is_same_v<decltype(&std::_Random_device), RandomDevice>);
static_assert(std::is_same_v<decltype(&GetStartupInfoW), StartupInfo> && sizeof(STARTUPINFOW) == 68);
static_assert(std::is_same_v<decltype(&GetStdHandle), StandardHandle>);

constexpr unsigned int query_proof = 18;
constexpr unsigned int random_samples = 16;
SRWLOCK retained_lock = SRWLOCK_INIT;
DWORD retained_thread = 0;
bool retained_shared = false;
volatile unsigned int observed_random = 0;
HMODULE source_module = nullptr;

template<std::size_t Size> bool CheckStage(const char (&stage)[Size], bool passed) {
    static_assert(Size <= 64, "Authored caller diagnostics have a fixed bounded extent.");
    const char* result = passed ? "=pass\n" : "=fail\n";
    char record[Size + 5]{};
    for (std::size_t at = 0; at < Size - 1; ++at) record[at] = stage[at];
    for (std::size_t at = 0; at < 6; ++at) record[Size - 1 + at] = result[at];
    const auto incoming_error = GetLastError();
    DWORD written = 0;
    const auto emitted = WriteFile(GetStdHandle(STD_ERROR_HANDLE), record, static_cast<DWORD>(sizeof(record)), &written, nullptr);
    SetLastError(incoming_error);
    // A failed diagnostic is also a failed authored caller. The test never
    // substitutes a missing receipt or promotes a failed source stage.
    return passed && emitted && written == sizeof(record);
}

bool ImageBytes(HMODULE module, const void* pointer, std::size_t count, bool executable = false) {
    const auto first = reinterpret_cast<std::uintptr_t>(pointer);
    if (!module || !pointer || !count || count > UINT32_MAX - first) return false;
    const auto end = first + count;
    for (auto cursor = first; cursor < end;) {
        MEMORY_BASIC_INFORMATION region{};
        if (VirtualQuery(reinterpret_cast<const void*>(cursor), &region, sizeof(region)) != sizeof(region) ||
            region.AllocationBase != module || region.State != MEM_COMMIT || region.Type != MEM_IMAGE ||
            region.Protect & (PAGE_GUARD | PAGE_NOACCESS)) return false;
        const auto protection = region.Protect & 0xffU;
        const bool code = protection == PAGE_EXECUTE_READ || protection == PAGE_EXECUTE_READWRITE || protection == PAGE_EXECUTE_WRITECOPY;
        if (executable ? !code : !(code || protection == PAGE_READONLY || protection == PAGE_READWRITE || protection == PAGE_WRITECOPY)) return false;
        const auto region_first = reinterpret_cast<std::uintptr_t>(region.BaseAddress);
        if (region.RegionSize > UINT32_MAX - region_first) return false;
        const auto next = region_first + region.RegionSize;
        if (next <= cursor) return false;
        cursor = next < end ? next : end;
    }
    return true;
}
bool SourceRva(DWORD rva, std::size_t count, DWORD extent) {
    const auto first = reinterpret_cast<std::uintptr_t>(source_module);
    return rva <= extent && count <= extent - rva && rva <= UINT32_MAX - first &&
        ImageBytes(source_module, reinterpret_cast<const void*>(first + rva), count);
}
bool SourceName(DWORD rva, DWORD extent, const char* expected) {
    const auto* base = reinterpret_cast<const unsigned char*>(source_module);
    for (std::size_t at = 0; at < 1024; ++at) {
        if (at > UINT32_MAX - rva || !SourceRva(rva + static_cast<DWORD>(at), 1, extent)) return false;
        const auto actual = base[rva + at];
        const auto wanted = static_cast<unsigned char>(expected[at]);
        if (actual != wanted) return false;
        if (!actual) return true;
    }
    return false;
}
const DWORD* RandomImportSlot() {
    const auto* base = reinterpret_cast<const unsigned char*>(source_module);
    if (!ImageBytes(source_module, base, sizeof(IMAGE_DOS_HEADER))) return nullptr;
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto first = reinterpret_cast<std::uintptr_t>(source_module);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0 ||
        static_cast<DWORD>(dos->e_lfanew) > UINT32_MAX - first) return nullptr;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS32*>(first + static_cast<DWORD>(dos->e_lfanew));
    if (!ImageBytes(source_module, nt, sizeof(*nt))) return nullptr;
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_I386 ||
        nt->FileHeader.SizeOfOptionalHeader < static_cast<WORD>(sizeof(IMAGE_OPTIONAL_HEADER32)) ||
        nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC || nt->OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_IMPORT) return nullptr;
    const auto extent = nt->OptionalHeader.SizeOfImage;
    const auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    constexpr auto descriptor_size = static_cast<DWORD>(sizeof(IMAGE_IMPORT_DESCRIPTOR));
    constexpr auto entry_size = static_cast<DWORD>(sizeof(DWORD));
    if (directory.Size < descriptor_size || !SourceRva(directory.VirtualAddress, directory.Size, extent)) return nullptr;
    for (DWORD descriptor = 0; descriptor < directory.Size / descriptor_size; ++descriptor) {
        const auto at = descriptor * descriptor_size;
        const auto* declaration = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + directory.VirtualAddress + at);
        if (!declaration->Name && !declaration->FirstThunk && !declaration->OriginalFirstThunk) return nullptr;
        if (!SourceName(declaration->Name, extent, "MSVCP140.dll") && !SourceName(declaration->Name, extent, "msvcp140.dll")) continue;
        if (!declaration->OriginalFirstThunk || !declaration->FirstThunk) return nullptr;
        for (DWORD index = 0; index < extent / entry_size; ++index) {
            const auto displacement = index * entry_size;
            if (displacement > UINT32_MAX - declaration->OriginalFirstThunk || displacement > UINT32_MAX - declaration->FirstThunk) return nullptr;
            const auto lookup = declaration->OriginalFirstThunk + displacement, slot = declaration->FirstThunk + displacement;
            if (!SourceRva(lookup, sizeof(DWORD), extent) || !SourceRva(slot, sizeof(DWORD), extent)) return nullptr;
            const auto name = *reinterpret_cast<const DWORD*>(base + lookup);
            if (!name) return nullptr;
            if (name & IMAGE_ORDINAL_FLAG32) continue;
            if (name > UINT32_MAX - sizeof(WORD)) return nullptr;
            if (SourceName(name + sizeof(WORD), extent, "?_Random_device@std@@YAIXZ")) return reinterpret_cast<const DWORD*>(base + slot);
        }
        return nullptr;
    }
    return nullptr;
}
bool OriginalRandomCallable(HMODULE provider) {
    const auto actual = reinterpret_cast<void*>(GetProcAddress(provider, "?_Random_device@std@@YAIXZ"));
    const auto* slot = RandomImportSlot();
    if (!CheckStage("provider:Random_device-original-IAT", actual && slot &&
        *slot == reinterpret_cast<DWORD>(actual) && ImageBytes(provider, actual, 1, true))) return false;
    const auto callable = reinterpret_cast<void*>(&std::_Random_device);
    if (callable == actual) return CheckStage("provider:Random_device-direct-export", true);
    // The installed header declares this external function without dllimport.
    // Its genuine linker thunk is a single indirect x86 JMP through the same
    // named source IAT slot. No arbitrary jump, address or substituted callback
    // qualifies. The actual cdecl caller below invokes this compiler callable.
    if (!ImageBytes(source_module, callable, 6, true)) return CheckStage("provider:Random_device-source-thunk", false);
    const auto* code = static_cast<const unsigned char*>(callable);
    DWORD operand = 0;
    for (unsigned int at = 0; at < sizeof(operand); ++at) operand |= static_cast<DWORD>(code[2 + at]) << (at * 8);
    return CheckStage("provider:Random_device-source-thunk", code[0] == 0xff && code[1] == 0x25 &&
        operand == reinterpret_cast<DWORD>(slot));
}

__declspec(noinline) bool CallTryShared(TryShared function, PSRWLOCK nativeLock, BOOLEAN& returned) {
    DWORD before = 0, after = 0, raw = 0, actual_bx = 0, actual_si = 0, actual_di = 0;
    BOOLEAN byte_return = 0;
    __asm {
        mov before, esp
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        push nativeLock
        call function
        mov byte_return, al
        mov raw, eax
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    returned = byte_return;
    // The upper EAX bits are unspecified by BOOLEAN and are never required to
    // be zero. Only AL supplies the caller's result. A wrong stack is recovered
    // solely to return a failed authored proof, never to continue gameplay.
    return before == after && actual_bx == 0x1234abcd && actual_si == 0x3456cdef &&
        actual_di == 0x5678ef01 && byte_return == static_cast<BOOLEAN>(raw & 0xffU);
}
__declspec(noinline) bool CallRandomDevice(RandomDevice function, unsigned int& returned) {
    DWORD before = 0, after = 0, raw = 0, actual_bx = 0, actual_si = 0, actual_di = 0;
    __asm {
        mov before, esp
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        call function
        mov raw, eax
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    returned = raw;
    return before == after && actual_bx == 0x1234abcd && actual_si == 0x3456cdef && actual_di == 0x5678ef01;
}
__declspec(noinline) bool ActualStartupInfo() {
    const StartupInfo function = &GetStartupInfoW;
    STARTUPINFOW actual{};
    DWORD before = 0, after = 0, actual_bx = 0, actual_si = 0, actual_di = 0;
    __asm {
        mov before, esp
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        lea eax, actual
        push eax
        call function
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    if (!CheckStage("startup:stdcall4-and-registers", before == after && actual_bx == 0x1234abcd && actual_si == 0x3456cdef && actual_di == 0x5678ef01)) return false;
    return CheckStage("startup:actual-structure-and-channels", actual.cb == sizeof(actual) && !actual.cbReserved2 && !actual.lpReserved2 && !actual.hStdInput &&
        actual.hStdInput == GetStdHandle(STD_INPUT_HANDLE) && actual.hStdOutput == GetStdHandle(STD_OUTPUT_HANDLE) &&
        actual.hStdError == GetStdHandle(STD_ERROR_HANDLE) &&
        GetFileType(actual.hStdOutput) == FILE_TYPE_DISK && GetFileType(actual.hStdError) == FILE_TYPE_DISK);
}
__declspec(noinline) bool CallStandardHandle(DWORD channel, HANDLE& returned) {
    const StandardHandle function = &GetStdHandle;
    DWORD before = 0, after = 0, raw = 0, actual_bx = 0, actual_si = 0, actual_di = 0;
    __asm {
        mov before, esp
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        push channel
        call function
        mov raw, eax
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    returned = reinterpret_cast<HANDLE>(raw);
    return before == after && actual_bx == 0x1234abcd && actual_si == 0x3456cdef && actual_di == 0x5678ef01;
}
bool ActualStandardHandles() {
    HANDLE stdin_handle = INVALID_HANDLE_VALUE, stdout_handle = INVALID_HANDLE_VALUE, stderr_handle = INVALID_HANDLE_VALUE;
    if (!CheckStage("standard:stdin-stdcall4-and-registers", CallStandardHandle(STD_INPUT_HANDLE, stdin_handle)) ||
        !CheckStage("standard:stdout-stdcall4-and-registers", CallStandardHandle(STD_OUTPUT_HANDLE, stdout_handle)) ||
        !CheckStage("standard:stderr-stdcall4-and-registers", CallStandardHandle(STD_ERROR_HANDLE, stderr_handle))) return false;
    return CheckStage("standard:actual-null-input-private-output", stdin_handle == nullptr &&
        stdout_handle && stdout_handle != INVALID_HANDLE_VALUE && stderr_handle && stderr_handle != INVALID_HANDLE_VALUE &&
        GetFileType(stdout_handle) == FILE_TYPE_DISK && GetFileType(stderr_handle) == FILE_TYPE_DISK);
}
bool OriginalProviders() {
    const auto windows = GetModuleHandleW(L"kernel32.dll"), cpp = GetModuleHandleW(L"msvcp140.dll");
    return CheckStage("provider:live-images", windows && cpp) &&
        CheckStage("provider:TryAcquireSRWLockShared", reinterpret_cast<void*>(GetProcAddress(windows, "TryAcquireSRWLockShared")) == reinterpret_cast<void*>(&TryAcquireSRWLockShared)) &&
        CheckStage("provider:DebugBreak", reinterpret_cast<void*>(GetProcAddress(windows, "DebugBreak")) == reinterpret_cast<void*>(&DebugBreak)) &&
        CheckStage("provider:GetStartupInfoW", reinterpret_cast<void*>(GetProcAddress(windows, "GetStartupInfoW")) == reinterpret_cast<void*>(&GetStartupInfoW)) &&
        CheckStage("provider:GetStdHandle", reinterpret_cast<void*>(GetProcAddress(windows, "GetStdHandle")) == reinterpret_cast<void*>(&GetStdHandle)) &&
        CheckStage("provider:Random_device", OriginalRandomCallable(cpp));
}
bool TrySharedBehavior() {
    SRWLOCK lock = SRWLOCK_INIT;
    const auto thread = GetCurrentThreadId();
    BOOLEAN returned = 0;
    AcquireSRWLockExclusive(&lock);
    const auto contended = CallTryShared(&TryAcquireSRWLockShared, &lock, returned);
    ReleaseSRWLockExclusive(&lock);
    if (!CheckStage("srw:contended-byte-and-stdcall4", contended && returned == 0 && thread == GetCurrentThreadId())) return false;
    for (unsigned int attempt = 0; attempt < 2; ++attempt) {
        const auto abi = CallTryShared(&TryAcquireSRWLockShared, &lock, returned);
        if (returned) ReleaseSRWLockShared(&lock);
        if (!CheckStage("srw:retry-byte-and-same-thread-release", abi && returned != 0 && thread == GetCurrentThreadId())) return false;
        // Real exclusive acquisition after shared release also proves that the
        // source caller has no remaining shared acquisition on this lock.
        AcquireSRWLockExclusive(&lock); ReleaseSRWLockExclusive(&lock);
    }
    return true;
}
bool RandomBehavior() {
    for (unsigned int sample = 0; sample < random_samples; ++sample) {
        unsigned int returned = 0;
        if (!CheckStage("random:uint32-cdecl-and-registers", CallRandomDevice(&std::_Random_device, returned))) return false;
        observed_random = returned;
    }
    // Every uint32 value and repetition is legal. No deterministic output or
    // uniqueness condition replaces the genuine provider's entropy semantics.
    return true;
}
LONG BreakFilter(EXCEPTION_POINTERS* pointers, DWORD thread, DWORD* observed) {
    if (!pointers || !pointers->ExceptionRecord || !pointers->ContextRecord ||
        pointers->ExceptionRecord->ExceptionCode != EXCEPTION_BREAKPOINT ||
        !pointers->ExceptionRecord->ExceptionAddress || !pointers->ContextRecord->Eip ||
        GetCurrentThreadId() != thread) return EXCEPTION_CONTINUE_SEARCH;
    *observed = pointers->ExceptionRecord->ExceptionCode;
    return EXCEPTION_EXECUTE_HANDLER;
}
__declspec(noinline) bool CaughtBreak() {
    const auto thread = GetCurrentThreadId();
    DWORD observed = 0;
    __try { DebugBreak(); }
    __except (BreakFilter(GetExceptionInformation(), thread, &observed)) { return observed == EXCEPTION_BREAKPOINT; }
    return false;
}
}
extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) source_module = instance;
    if (reason == DLL_PROCESS_DETACH && retained_shared) {
        if (GetCurrentThreadId() != retained_thread) { DebugBreak(); return FALSE; }
        ReleaseSRWLockShared(&retained_lock); retained_shared = false;
        AcquireSRWLockExclusive(&retained_lock); ReleaseSRWLockExclusive(&retained_lock);
        retained_thread = 0;
    }
    if (reason == DLL_PROCESS_DETACH) source_module = nullptr;
    return TRUE;
}
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core, opennv_domain::NvsePluginInfo* info) {
    if (!CheckStage("query:arguments", core && info && core->nvse_version && core->runtime_version && !core->is_editor) ||
        !CheckStage("query:no-debugger", !IsDebuggerPresent())) return false;
    if (!CheckStage("query:providers", OriginalProviders()) || !CheckStage("query:startup-info", ActualStartupInfo()) ||
        !CheckStage("query:standard-handles", ActualStandardHandles()) || !CheckStage("query:srw-lock", TrySharedBehavior()) ||
        !CheckStage("query:random-device", RandomBehavior())) return false;
#if defined(OPENNV_PLATFORM_BREAK_UNHANDLED)
    // This reaches the existing genuine native exception receipt, rather than
    // converting an original breakpoint into a successful bool or a no-op.
    if (!CheckStage("query:enter-unhandled-breakpoint", true)) return false;
    DebugBreak(); return false;
#else
    if (!CheckStage("query:caught-breakpoint", CaughtBreak()) || !CheckStage("query:complete", true)) return false;
    info->info_version = 1; info->name = "OpenNV actual synchronization and entropy"; info->version = query_proof; return true;
#endif
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    if (!CheckStage("load:arguments", core && core->get_handle && core->get_handle() != opennv_domain::nvse_invalid_handle && !retained_shared)) return false;
    if (!CheckStage("load:providers", OriginalProviders()) || !CheckStage("load:startup-info", ActualStartupInfo()) ||
        !CheckStage("load:standard-handles", ActualStandardHandles()) || !CheckStage("load:srw-lock", TrySharedBehavior()) ||
        !CheckStage("load:random-device", RandomBehavior()) || !CheckStage("load:caught-breakpoint", CaughtBreak())) return false;
    BOOLEAN returned = 0;
    const auto abi = CallTryShared(&TryAcquireSRWLockShared, &retained_lock, returned);
    if (!CheckStage("load:retained-shared-acquisition", abi && returned != 0)) { if (returned) ReleaseSRWLockShared(&retained_lock); return false; }
    retained_thread = GetCurrentThreadId(); retained_shared = true;
    // The source module's original DllMain releases this acquisition during
    // actual FreeLibrary, on its own original calling thread.
    return CheckStage("load:complete", true);
}
