#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <errno.h>
#include <limits.h>
#include <stdint.h>
#include "../../runtime/native/plugins/opennv_plugin_nvse.h"

// Authored x86 caller of genuine imported UCRT entries. Volatile function
// pointers and /Oi- keep the six imports executable in optimized builds.
extern "C" __declspec(dllimport) void __cdecl _libm_sse2_tan_precise(void);
extern "C" int _fltused = 0;

namespace {
using Concat = errno_t (__cdecl*)(char*, size_t, const char*, size_t);
using Convert = int (__cdecl*)(const char*);
using Maximum = float (__cdecl*)(float, float);
using Unary = double (__cdecl*)(double);
unsigned invalid_calls = 0;
unsigned completed = 0;

void __cdecl returning_invalid(const wchar_t*, const wchar_t*, const wchar_t*, unsigned, uintptr_t) {
    ++invalid_calls;
}
union Float { uint32_t bits; float value; };
union Double { uint64_t bits; double value; };
bool equal(const char* actual, const char* expected) {
    while (*expected) if (*actual++ != *expected++) return false;
    return *actual == 0;
}
struct Guarded {
    uint32_t before = 0x639ab172U;
    char text[8] = {'a', 'b', 0};
    uint32_t after = 0xa87523c9U;
    bool intact() const { return before == 0x639ab172U && after == 0xa87523c9U; }
};
bool concat_cases() {
    Concat volatile concatenate = &strncat_s;
    Guarded bounded;
    bool good = concatenate(bounded.text, sizeof(bounded.text), "cdefgh", 3) == 0 &&
        equal(bounded.text, "abcde") && bounded.intact();
    Guarded exact;
    good = concatenate(exact.text, sizeof(exact.text), "cdefg", 5) == 0 &&
        equal(exact.text, "abcdefg") && exact.intact() && good;
    Guarded truncated;
    good = concatenate(truncated.text, sizeof(truncated.text), "cdefgh", _TRUNCATE) == STRUNCATE &&
        equal(truncated.text, "abcdefg") && truncated.intact() && good;
    Guarded range;
    const auto prior_calls = invalid_calls;
    errno = 0;
    const auto range_result = concatenate(range.text, sizeof(range.text), "cdefgh", 6);
    good = range_result == ERANGE && errno == ERANGE && range.text[0] == 0 &&
        range.intact() && invalid_calls == prior_calls + 1 && good;
    errno = 0;
    const auto null_result = concatenate(nullptr, sizeof(range.text), "x", 1);
    good = null_result == EINVAL && errno == EINVAL && invalid_calls == prior_calls + 2 && good;
    return good;
}
bool atoi_cases() {
    Convert volatile convert = &atoi;
    errno = 0;
    bool good = convert(" \t-2309tail") == -2309 && convert("+17") == 17 && convert("text") == 0 &&
        convert("2147483647") == INT_MAX && convert("-2147483648") == INT_MIN && errno == 0;
    errno = 0;
    const auto positive = convert("2147483648");
    good = positive == INT_MAX && errno == ERANGE && good;
    errno = 0;
    const auto negative = convert("-2147483649");
    good = negative == INT_MIN && errno == ERANGE && good;
    return good;
}
bool maximum_cases() {
    Maximum volatile maximum = &fmaxf;
    Float nan{0x7fc13579U}, negative_zero{0x80000000U}, infinity{0x7f800000U}, negative_infinity{0xff800000U};
    bool good = maximum(nan.value, 3.0f) == 3.0f && maximum(-2.0f, nan.value) == -2.0f &&
        maximum(infinity.value, negative_infinity.value) == infinity.value && maximum(-4.0f, -9.0f) == -4.0f;
    Float both_nan{}, zero_a{}, zero_b{}, same_negative_zero{};
    both_nan.value = maximum(nan.value, nan.value);
    zero_a.value = maximum(negative_zero.value, 0.0f);
    zero_b.value = maximum(0.0f, negative_zero.value);
    same_negative_zero.value = maximum(negative_zero.value, negative_zero.value);
    // The public contract does not prescribe the sign for unlike equal zeros.
    // Exercise both orders without replacing actual UCRT tie behavior.
    return good && (both_nan.bits & 0x7fffffffU) > 0x7f800000U &&
        (zero_a.bits & 0x7fffffffU) == 0 && (zero_b.bits & 0x7fffffffU) == 0 &&
        same_negative_zero.bits == 0x80000000U;
}
bool unary_cases() {
    Unary volatile floor_value = &floor;
    Unary volatile round_value = &round;
    Double negative_zero{0x8000000000000000ULL}, infinity{0x7ff0000000000000ULL}, nan{0x7ff8123456789abcULL};
    Double floor_zero{}, round_zero{}, floor_nan{}, round_nan{};
    floor_zero.value = floor_value(negative_zero.value); round_zero.value = round_value(negative_zero.value);
    floor_nan.value = floor_value(nan.value); round_nan.value = round_value(nan.value);
    return floor_value(1.75) == 1.0 && floor_value(-1.25) == -2.0 && round_value(0.5) == 1.0 &&
        round_value(-0.5) == -1.0 && round_value(2.5) == 3.0 && round_value(-2.5) == -3.0 &&
        floor_value(infinity.value) == infinity.value && round_value(infinity.value) == infinity.value &&
        floor_zero.bits == negative_zero.bits && round_zero.bits == negative_zero.bits &&
        (floor_nan.bits & 0x7fffffffffffffffULL) > 0x7ff0000000000000ULL &&
        (round_nan.bits & 0x7fffffffffffffffULL) > 0x7ff0000000000000ULL;
}
__declspec(noinline) bool tan_boundary(double input_value, uint64_t expected) {
    Double result{};
    uint32_t before = 0, after = 0, original_frame = 0, current_frame = 0;
    uint32_t registers = 0, guard = 0;
    __asm {
        push ebx
        push esi
        push edi
        push 05128a7ceh
        mov before, esp
        mov original_frame, ebp
        mov ebx, 0317abc29h
        mov esi, 0549e2801h
        mov edi, 076dab351h
        movsd xmm0, input_value
        call _libm_sse2_tan_precise
        movsd result, xmm0
        mov after, esp
        mov current_frame, ebp
        xor eax, eax
        cmp ebx, 0317abc29h
        jne register_done
        cmp esi, 0549e2801h
        jne register_done
        cmp edi, 076dab351h
        jne register_done
        mov eax, 1
    register_done:
        mov registers, eax
        pop eax
        mov guard, eax
        pop edi
        pop esi
        pop ebx
    }
    return result.bits == expected && before == after && original_frame == current_frame &&
        registers == 1 && guard == 0x5128a7ceU;
}
bool run_cases() {
    const auto old_handler = _set_thread_local_invalid_parameter_handler(returning_invalid);
    const int old_errno = errno;
    unsigned short old_control = 0;
    uint32_t old_mxcsr = 0;
    constexpr unsigned floating_environment_bytes = 512;
    __declspec(align(16)) unsigned char old_environment[floating_environment_bytes];
    __asm { fxsave old_environment }
    __asm { fnstcw old_control }
    __asm { stmxcsr old_mxcsr }
    completed = 0;
    if (concat_cases()) completed |= 1;
    if (atoi_cases()) completed |= 2;
    if (maximum_cases()) completed |= 4;
    if (unary_cases()) completed |= 8;
    bool rounding = true;
    for (unsigned mode = 0; mode < 4; ++mode) {
        const unsigned short control = static_cast<unsigned short>((old_control & ~0x0c00U) | (mode << 10));
        const uint32_t mxcsr = (old_mxcsr & ~0x6000U) | (mode << 13);
        __asm { fldcw control }
        __asm { ldmxcsr mxcsr }
        rounding = unary_cases() && rounding;
    }
    if (rounding) completed |= 16;
    __asm { fldcw old_control }
    __asm { ldmxcsr old_mxcsr }
    Double negative_zero{0x8000000000000000ULL};
    if (tan_boundary(0.0, 0) && tan_boundary(negative_zero.value, negative_zero.bits) &&
        tan_boundary(0.25, 0x3fd05785a43c4c56ULL)) completed |= 32;
    errno = old_errno;
    const auto installed = _set_thread_local_invalid_parameter_handler(old_handler);
    unsigned short restored_control = 0;
    uint32_t restored_mxcsr = 0;
    // Restore x87 status/stack and SSE state as well as rounding control.
    __asm { fxrstor old_environment }
    __asm { fnstcw restored_control }
    __asm { stmxcsr restored_mxcsr }
    return completed == 63 && installed == returning_invalid && errno == old_errno &&
        restored_control == old_control && restored_mxcsr == old_mxcsr;
}
}

extern "C" BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
extern "C" bool __cdecl NVSEPlugin_Query(const opennv_domain::NvseCoreInterface* core,
    opennv_domain::NvsePluginInfo* info) {
    if (!core || !info || core->is_editor) return false;
    info->info_version = 1; info->name = "OpenNV authored genuine pure CRT fixture";
    const auto good = run_cases(); info->version = completed; return good;
}
extern "C" bool __cdecl NVSEPlugin_Load(const opennv_domain::NvseCoreInterface* core) {
    return core && !core->is_editor && run_cases();
}
