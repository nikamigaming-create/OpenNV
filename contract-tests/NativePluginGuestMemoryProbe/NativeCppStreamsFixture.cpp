#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <fstream>
#include <iostream>
#include <locale>
#include <type_traits>
#include <stdio.h>
#include <io.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// First-party /MD caller. Windows constructs the real Microsoft runtime and
// this caller's actual C++ objects. The fixture contains no game assets/code.
namespace {
using ClearMethod = void (__thiscall std::ios::*)(std::ios_base::iostate, bool);
using SeekPositionMethod = std::istream& (__thiscall std::istream::*)(std::streampos);
using SeekOffsetMethod = std::istream& (__thiscall std::istream::*)(std::streamoff, std::ios_base::seekdir);
using TellPositionMethod = std::streampos (__thiscall std::istream::*)();
using StatePredicate = bool (__thiscall std::ios_base::*)() const noexcept;
using ExceptionMaskMethod = void (__thiscall std::ios_base::*)(std::ios_base::iostate);
static_assert(sizeof(void*) == 4 && sizeof(std::streamoff) == 8 && sizeof(std::streampos) == 24);
static_assert(sizeof(std::ios_base::iostate) == 4 && sizeof(std::ios_base::seekdir) == 4 && sizeof(bool) == 1);
static_assert(std::is_same_v<decltype(static_cast<ClearMethod>(&std::ios::clear)), ClearMethod>);
static_assert(std::is_same_v<decltype(static_cast<SeekPositionMethod>(&std::istream::seekg)), SeekPositionMethod>);
static_assert(std::is_same_v<decltype(static_cast<SeekOffsetMethod>(&std::istream::seekg)), SeekOffsetMethod>);
static_assert(std::is_same_v<decltype(&std::istream::tellg), TellPositionMethod>);
static_assert(std::is_same_v<decltype(&std::ios_base::eof), StatePredicate>);
static_assert(std::is_same_v<decltype(&std::ios_base::fail), StatePredicate>);
static_assert(std::is_same_v<decltype(&std::ios_base::bad), StatePredicate>);
static_assert(std::is_same_v<decltype(static_cast<ExceptionMaskMethod>(&std::ios_base::exceptions)), ExceptionMaskMethod>);
static_assert(std::is_trivially_copyable_v<std::streampos> && std::is_trivially_destructible_v<std::streampos>);

// Volatile member-pointer calls force the exact original IAT declarations in
// optimized and debug callers. MSVC owns every base adjustment/result object.
__declspec(noinline) void TypedClear(std::ios& stream, std::ios_base::iostate state = std::ios_base::goodbit, bool reraise = false) {
    static volatile ClearMethod imported = static_cast<ClearMethod>(&std::ios::clear);
    const ClearMethod method = imported; (stream.*method)(state, reraise);
}
__declspec(noinline) std::istream& TypedSeekPosition(std::istream& stream, std::streampos position) {
    static volatile SeekPositionMethod imported = static_cast<SeekPositionMethod>(&std::istream::seekg);
    const SeekPositionMethod method = imported; return (stream.*method)(position);
}
__declspec(noinline) std::istream& TypedSeekOffset(std::istream& stream, std::streamoff displacement, std::ios_base::seekdir way) {
    static volatile SeekOffsetMethod imported = static_cast<SeekOffsetMethod>(&std::istream::seekg);
    const SeekOffsetMethod method = imported; return (stream.*method)(displacement, way);
}
__declspec(noinline) std::streampos TypedTellPosition(std::istream& stream) {
    static volatile TellPositionMethod imported = &std::istream::tellg;
    const TellPositionMethod method = imported; return (stream.*method)();
}
__declspec(noinline) bool TypedEof(const std::ios_base& stream) {
    static volatile StatePredicate imported = &std::ios_base::eof;
    const StatePredicate method = imported; return (stream.*method)();
}
__declspec(noinline) bool TypedFail(const std::ios_base& stream) {
    static volatile StatePredicate imported = &std::ios_base::fail;
    const StatePredicate method = imported; return (stream.*method)();
}
__declspec(noinline) bool TypedBad(const std::ios_base& stream) {
    static volatile StatePredicate imported = &std::ios_base::bad;
    const StatePredicate method = imported; return (stream.*method)();
}
__declspec(noinline) void TypedExceptions(std::ios_base& stream, std::ios_base::iostate mask) {
    static volatile ExceptionMaskMethod imported = static_cast<ExceptionMaskMethod>(&std::ios_base::exceptions);
    const ExceptionMaskMethod method = imported; (stream.*method)(mask);
}

constexpr const char* clear_name = "?clear@?$basic_ios@DU?$char_traits@D@std@@@std@@QAEXH_N@Z";
constexpr const char* seek_position_name = "?seekg@?$basic_istream@DU?$char_traits@D@std@@@std@@QAEAAV12@V?$fpos@U_Mbstatet@@@2@@Z";
constexpr const char* seek_offset_name = "?seekg@?$basic_istream@DU?$char_traits@D@std@@@std@@QAEAAV12@_JH@Z";
constexpr const char* tell_position_name = "?tellg@?$basic_istream@DU?$char_traits@D@std@@@std@@QAE?AV?$fpos@U_Mbstatet@@@2@XZ";
constexpr const char* eof_name = "?eof@ios_base@std@@QBE_NXZ";
constexpr const char* fail_name = "?fail@ios_base@std@@QBE_NXZ";
constexpr const char* bad_name = "?bad@ios_base@std@@QBE_NXZ";
struct PositionCalls {
    void* clear = nullptr;
    void* seek_position = nullptr;
    void* seek_offset = nullptr;
    void* tell_position = nullptr;
    void* eof = nullptr;
    void* fail = nullptr;
    void* bad = nullptr;
    bool Bind() {
        const auto provider = GetModuleHandleW(L"msvcp140.dll");
        if (!provider) return false;
        clear = reinterpret_cast<void*>(GetProcAddress(provider, clear_name));
        seek_position = reinterpret_cast<void*>(GetProcAddress(provider, seek_position_name));
        seek_offset = reinterpret_cast<void*>(GetProcAddress(provider, seek_offset_name));
        tell_position = reinterpret_cast<void*>(GetProcAddress(provider, tell_position_name));
        eof = reinterpret_cast<void*>(GetProcAddress(provider, eof_name));
        fail = reinterpret_cast<void*>(GetProcAddress(provider, fail_name));
        bad = reinterpret_cast<void*>(GetProcAddress(provider, bad_name));
        return clear && seek_position && seek_offset && tell_position && eof && fail && bad;
    }
};
bool Preserved(DWORD before, DWORD after, DWORD actual_bx, DWORD actual_si, DWORD actual_di) {
    return before == after && actual_bx == 0x1234abcd && actual_si == 0x3456cdef && actual_di == 0x5678ef01;
}
__declspec(noinline) bool CallStatePredicate(void* function, const std::ios_base* stream, bool expected) {
    DWORD before = 0, after = 0, actual_bx = 0, actual_si = 0, actual_di = 0;
    unsigned char returned = 0;
    __asm {
        mov before, esp
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        mov ecx, stream
        call function
        mov returned, al
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    return Preserved(before, after, actual_bx, actual_si, actual_di) && returned == static_cast<unsigned char>(expected);
}
bool StateMatches(const PositionCalls& calls, const std::ios_base& stream, bool eof, bool fail, bool bad) {
    return CallStatePredicate(calls.eof, &stream, eof) && CallStatePredicate(calls.fail, &stream, fail) &&
        CallStatePredicate(calls.bad, &stream, bad) && TypedEof(stream) == eof && TypedFail(stream) == fail && TypedBad(stream) == bad;
}
__declspec(noinline) bool CallClear(void* function, std::ios* stream, std::ios_base::iostate state) {
    DWORD before = 0, after = 0, actual_bx = 0, actual_si = 0, actual_di = 0;
    __asm {
        mov before, esp
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        push 0
        push state
        mov ecx, stream
        call function
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    return Preserved(before, after, actual_bx, actual_si, actual_di);
}
__declspec(noinline) bool CallSeekOffset(void* function, std::istream* stream,
    std::streamoff displacement, std::ios_base::seekdir way) {
    DWORD before = 0, after = 0, actual_bx = 0, actual_si = 0, actual_di = 0, returned = 0;
    __asm {
        mov before, esp
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        push way
        push dword ptr displacement[4]
        push dword ptr displacement[0]
        mov ecx, stream
        call function
        mov returned, eax
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    return Preserved(before, after, actual_bx, actual_si, actual_di) && returned == reinterpret_cast<DWORD>(stream);
}
__declspec(noinline) bool CallSeekPosition(void* function, std::istream* stream, const std::streampos& position) {
    const void* raw_position = &position;
    enum { position_bytes = sizeof(std::streampos), position_words = sizeof(std::streampos) / sizeof(DWORD) };
    DWORD before = 0, after = 0, actual_bx = 0, actual_si = 0, actual_di = 0, returned = 0;
    __asm {
        mov before, esp
        sub esp, position_bytes
        mov esi, raw_position
        mov edi, esp
        mov ecx, position_words
        cld
        rep movsd
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        mov ecx, stream
        call function
        mov returned, eax
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    // Copy the complete compiler-created fpos argument. Its private fields and
    // conversion state are never interpreted or reconstructed by this caller.
    return Preserved(before, after, actual_bx, actual_si, actual_di) && returned == reinterpret_cast<DWORD>(stream);
}
struct GuardedPosition {
    DWORD head[2] = {0x13245768, 0x897a6b5c};
    std::streampos value = std::streampos(std::streamoff{-731});
    DWORD tail[2] = {0xacbdcedf, 0xfedcba98};
    bool Intact() const {
        return head[0] == 0x13245768 && head[1] == 0x897a6b5c && tail[0] == 0xacbdcedf && tail[1] == 0xfedcba98;
    }
};
__declspec(noinline) bool CallTellInto(void* function, std::istream* stream, GuardedPosition& guarded) {
    void* destination = &guarded.value;
    DWORD before = 0, after = 0, actual_bx = 0, actual_si = 0, actual_di = 0, returned = 0;
    __asm {
        mov before, esp
        mov ebx, 1234abcdh
        mov esi, 3456cdefh
        mov edi, 5678ef01h
        push destination
        mov ecx, stream
        call function
        mov returned, eax
        mov after, esp
        mov actual_bx, ebx
        mov actual_si, esi
        mov actual_di, edi
        mov esp, before
    }
    return Preserved(before, after, actual_bx, actual_si, actual_di) && returned == reinterpret_cast<DWORD>(destination) && guarded.Intact();
}
bool CallTellPosition(void* function, std::istream* stream, std::streampos& result) {
    // Keep the aligned C++ result outside the asm frame. EBX is available for
    // the nonvolatile-register proof even when MSVC aligns this object stack.
    GuardedPosition guarded;
    if (!CallTellInto(function, stream, guarded)) return false;
    result = guarded.value;
    return true;
}
bool ReadByte(std::istream& stream, char expected) {
    char actual = 0; stream.read(&actual, 1);
    return stream.gcount() == 1 && !TypedFail(stream) && actual == expected;
}
bool PositionSemantics(std::ifstream& input_stream) {
    PositionCalls calls;
    if (!calls.Bind() || !input_stream.good() || !StateMatches(calls, input_stream, false, false, false)) return false;
    auto* input = static_cast<std::istream*>(&input_stream);
    auto* ios = static_cast<std::ios*>(&input_stream);
    std::streampos position;
    if (!CallTellPosition(calls.tell_position, input, position) || static_cast<std::streamoff>(position) != 0) return false;
    const auto start = TypedTellPosition(input_stream);
    if (!CallSeekOffset(calls.seek_offset, input, 2, std::ios_base::beg) || !ReadByte(*input, 'C')) return false;
    const auto saved = TypedTellPosition(input_stream);
    if (static_cast<std::streamoff>(saved) != 3 || &TypedSeekOffset(input_stream, -2, std::ios_base::cur) != input || !ReadByte(*input, 'B')) return false;
    if (!CallSeekPosition(calls.seek_position, input, saved) || !ReadByte(*input, 'D') || !ReadByte(*input, 'E')) return false;
    if (!CallTellPosition(calls.tell_position, input, position) || static_cast<std::streamoff>(position) != 5) return false;

    char beyond_end = 0; input_stream.read(&beyond_end, 1);
    if (input_stream.gcount() != 0 || !StateMatches(calls, input_stream, true, true, false)) return false;
    if (!CallTellPosition(calls.tell_position, input, position) || static_cast<std::streamoff>(position) != -1) return false;
    // seekg removes eofbit but does not clear an existing failbit.
    if (!CallSeekOffset(calls.seek_offset, input, 0, std::ios_base::beg) || !StateMatches(calls, input_stream, false, true, false)) return false;
    if (!CallClear(calls.clear, ios, std::ios_base::goodbit) || !input_stream.good() || input_stream.gcount() != 0) return false;
    if (&TypedSeekPosition(input_stream, start) != input || !ReadByte(*input, 'A')) return false;
    input_stream.setstate(std::ios_base::eofbit);
    if (!CallSeekOffset(calls.seek_offset, input, 0, std::ios_base::beg) || !input_stream.good() || !ReadByte(*input, 'A')) return false;
    if (!CallSeekOffset(calls.seek_offset, input, -1, std::ios_base::beg) || !TypedFail(input_stream)) return false;
    if (!CallTellPosition(calls.tell_position, input, position) || static_cast<std::streamoff>(position) != -1) return false;
    TypedClear(input_stream);

    // Seeking a read-only source beyond EOF changes no file bytes. This actual
    // offset requires both signed 64-bit input words and the full fpos result.
    constexpr std::streamoff high_position = 0x100000003LL;
    if (!CallSeekOffset(calls.seek_offset, input, high_position, std::ios_base::beg) || !input_stream.good()) return false;
    if (!CallTellPosition(calls.tell_position, input, position) || static_cast<std::streamoff>(position) != high_position) return false;
    if (!CallSeekPosition(calls.seek_position, input, position) || TypedTellPosition(input_stream) != position) return false;
    if (!CallSeekPosition(calls.seek_position, input, saved) || !ReadByte(*input, 'D')) return false;

    TypedExceptions(input_stream, std::ios_base::failbit);
    bool caught_failure = false;
    try { TypedClear(input_stream, std::ios_base::failbit, false); }
    catch (const std::ios_base::failure& error) { caught_failure = error.what() && TypedFail(input_stream); }
    TypedExceptions(input_stream, std::ios_base::goodbit); TypedClear(input_stream);
    if (!caught_failure) return false;
    bool reraised_original = false;
    try { throw 0x31415926U; }
    catch (...) {
        TypedExceptions(input_stream, std::ios_base::failbit);
        try { TypedClear(input_stream, std::ios_base::failbit, true); }
        catch (unsigned int original) { reraised_original = original == 0x31415926U && TypedFail(input_stream); }
        TypedExceptions(input_stream, std::ios_base::goodbit); TypedClear(input_stream);
    }
    if (!reraised_original) return false;

    {
        std::istream no_buffer(nullptr);
        if (!CallClear(calls.clear, static_cast<std::ios*>(&no_buffer), std::ios_base::goodbit) || !StateMatches(calls, no_buffer, false, true, true)) return false;
        if (!CallTellPosition(calls.tell_position, &no_buffer, position) || static_cast<std::streamoff>(position) != -1) return false;
    }
    input_stream.close();
    TypedClear(input_stream);
    if (!CallSeekOffset(calls.seek_offset, input, 0, std::ios_base::beg) || !TypedFail(input_stream)) return false;
    if (!CallTellPosition(calls.tell_position, input, position) || static_cast<std::streamoff>(position) != -1) return false;
    return true;
}
}
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core, opennv_domain::NvsePluginInfo* info) {
    if (!core || !info || !core->nvse_version || !core->runtime_version || core->is_editor) return false;
    info->info_version = 1; info->name = "OpenNV actual C++ streams"; info->version = 3; return true;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    if (!core || !core->runtime_directory) return false;
    const std::string directory = core->runtime_directory();
    const std::string source = directory + "OpenNVCppAuthored.cfg", output = directory + "OpenNVCppAuthored.out";
    {
        std::ifstream input_stream(source, std::ios::binary);
        char bytes[5]{}; input_stream.read(bytes, sizeof(bytes));
        if (input_stream.gcount() != static_cast<std::streamsize>(sizeof(bytes)) || std::string(bytes, sizeof(bytes)) != "ABCDE") return false;
        TypedSeekOffset(input_stream, 0, std::ios_base::beg);
        if (!PositionSemantics(input_stream)) return false;
        std::ofstream output_stream(output, std::ios::binary | std::ios::app);
        output_stream.imbue(std::locale::classic());
        output_stream << "cpp:" << 37U << ':' << 125LL << ':' << 2.5 << '\n';
        output_stream.flush(); if (!output_stream.good()) return false;
        output_stream.close();
    }
    FILE* standard = __acrt_iob_func(2);
    if (!standard || standard != __acrt_iob_func(2)) return false;
    const int descriptor = _fileno(standard);
    if (descriptor != 2 || _get_osfhandle(descriptor) == -1) return false;
    const int alias = _dup(descriptor);
    if (alias < 3 || _write(alias, "descriptor\n", 11) < 0) return false;
    if (_close(alias)) return false;
    if (fputs("FILE\n", standard) < 0 || fflush(standard)) return false;
    std::cerr << "cerr:" << 91U << '\n'; std::cerr.flush();
    std::cerr << "position:64bit/hidden-fpos/eof/fail/exceptions/abi\n"; std::cerr.flush();
    return std::cerr.good();
}
