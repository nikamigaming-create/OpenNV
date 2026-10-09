#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include "opennv_float_host.h"

extern "C" void opennv_fistp_fixture_save(void* state);
extern "C" void opennv_fistp_fixture_restore(const void* state);
extern "C" void opennv_fistp_fixture_environment(std::uint32_t control, std::uint32_t slots, std::uint32_t mxcsr);
extern "C" void opennv_fistp_fixture_pending();
using Call = int(*)(std::uint32_t, std::uint32_t, OpenNVHostFistp32*, std::uint32_t);
struct alignas(16) State { std::array<std::uint8_t, 512> bytes{}; };
struct EnvironmentLease {
    State saved;
    EnvironmentLease() { opennv_fistp_fixture_save(saved.bytes.data()); }
    ~EnvironmentLease() { opennv_fistp_fixture_restore(saved.bytes.data()); }
};
static void require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) { std::cerr << "Expected exactly one existing first-party adapter DLL path.\n"; return 2; }
    HMODULE module = LoadLibraryExW(argv[1], nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!module) { std::cerr << "The actual first-party adapter DLL could not be loaded.\n"; return 2; }
    int exit = 1; int executed = 0; State original_environment; bool owns_environment = false;
    try {
        const auto call = reinterpret_cast<Call>(GetProcAddress(module, "opennv_host_fistp32"));
        require(call != nullptr, "Actual first-party native FISTP export is absent.");
        EnvironmentLease environment;
        original_environment = environment.saved; owns_environment = true;
        std::uint32_t mxcsr{}; std::memcpy(&mxcsr, environment.saved.bytes.data() + 24, sizeof(mxcsr));
        int rows = 0;
        const auto observed = [&](std::uint32_t operation, std::uint32_t input) {
            OpenNVHostFistp32 value{};
            require(call(operation, input, &value, static_cast<std::uint32_t>(sizeof(value))) == 0, "Actual native ABI call failed.");
            require(value.abi == 1 && value.bytes == 48 && value.process == GetCurrentProcessId() && value.thread == GetCurrentThreadId() &&
                value.input == input && value.operation == operation && value.reserved == 0 && value.reserved_tail == 0, "Native ABI lost actual caller/operand identity.");
            return value;
        };
        const auto row = [&](const char* name) { ++rows; std::cout << "OPENNV_FISTP_NATIVE_CASE " << name << " PASS\n"; };
        const int positive[] = { 2, 1, 2, 1 }, negative[] = { -2, -2, -1, -1 };
        for (std::uint32_t mode = 0; mode < 4; ++mode) {
            for (int sign = 0; sign < 2; ++sign) {
                const auto control = 0x037fu | (mode << 10);
                // Deliberately DIFFERENT SSE rounding proves no MXCSR alias.
                opennv_fistp_fixture_environment(control, 0, (mxcsr & ~0x6000u) | ((mode ^ 1u) << 13));
                const auto value = observed(1, sign ? 0xbfc00000u : 0x3fc00000u);
                require(value.outcome == 2 && value.value == (sign ? negative[mode] : positive[mode]) &&
                    value.control_before == control && value.control_after == control && value.tag_before == 0 && value.tag_after == 0 &&
                    (value.status_after & 0x3fu) == 0x20u && (value.status_before & 0x3800u) == (value.status_after & 0x3800u),
                    "Actual FISTP did not consume x87 rounding or preserve stack/control/precision status.");
                std::cout << "OPENNV_FISTP_NATIVE_ROUND mode=" << mode << " sign=" << sign << " PASS\n"; ++rows;
            }
        }
        opennv_fistp_fixture_environment(0x037f, 0, mxcsr);
        auto value = observed(1, 0x7fc00000);
        require(value.outcome == 2 && value.value == INT32_MIN && (value.status_after & 0x3f) == 1, "Masked NaN lost integer indefinite/IE."); row("masked-nan");
        value = observed(1, 0xcf000000);
        require(value.outcome == 2 && value.value == INT32_MIN && (value.status_before & 1) != 0 && (value.status_after & 0x3f) == 1,
            "Exact INT_MIN or preserved pre-existing invalid flag changed."); row("exact-min-sticky-invalid");
        opennv_fistp_fixture_environment(0x037f, 0, mxcsr);
        value = observed(1, 0x4f000000);
        require(value.outcome == 2 && value.value == INT32_MIN && (value.status_after & 0x3f) == 1, "Masked overflow lost indefinite/IE."); row("masked-overflow");
        opennv_fistp_fixture_environment(0x037f, 0, mxcsr);
        value = observed(1, 1);
        require(value.outcome == 2 && value.value == 0 && (value.status_after & 0x3f) == 0x22, "Original denormal FLD/FISTP lost DE/PE."); row("denormal-status");
        opennv_fistp_fixture_environment(0x037f, 0, mxcsr);
        value = observed(2, 1);
        require(value.outcome == 2 && value.argument == 1 && value.value == 0 && (value.status_at_conversion & 2) != 0 &&
            (value.status_after & 0x3f) == ((value.status_at_conversion & 0x3f) | 0x22),
            "Source argument transport flags/bits were hidden by the FISTP result."); row("source-argument-status");
        const auto refused = [&](std::uint8_t outcome) {
            const auto result = observed(1, 0x3fc00000);
            require(result.outcome == outcome && result.value == INT32_MIN && result.control_before == result.control_after &&
                result.status_before == result.status_after && result.tag_before == result.tag_after, "Native refusal altered or silently consumed caller environment.");
        };
        opennv_fistp_fixture_environment(0x037f, 8, mxcsr); refused(6); row("full-stack-refused");
        opennv_fistp_fixture_environment(0x035f, 0, mxcsr); refused(4); row("unmasked-refused");
        opennv_fistp_fixture_pending(); refused(3); row("pending-exception-refused");
        opennv_fistp_fixture_environment(0x037f, 3, mxcsr);
        State before, after; opennv_fistp_fixture_save(before.bytes.data());
        value = observed(1, 0x3fc00000); opennv_fistp_fixture_save(after.bytes.data());
        require(value.outcome == 2 && value.value == 2 && value.tag_before == value.tag_after && value.tag_before != 0,
            "Available slot in a partially occupied stack was refused or destroyed.");
        for (int index = 0; index < 3; ++index)
            require(std::memcmp(before.bytes.data() + 32 + index * 16, after.bytes.data() + 32 + index * 16, 10) == 0, "Existing x87 register was overwritten.");
        row("occupied-registers-preserved");
        OpenNVHostFistp32 invalid; std::memset(&invalid, 0x5a, sizeof(invalid)); const auto original = invalid;
        require(call(1, 0, &invalid, 47) == -1 && std::memcmp(&invalid, &original, sizeof(invalid)) == 0, "Wrong ABI extent entered or mutated output."); row("wrong-extent-refused");
        require(call(3, 0, &invalid, 48) == -1 && std::memcmp(&invalid, &original, sizeof(invalid)) == 0, "Unknown ABI operation entered or mutated output."); row("wrong-operation-refused");
        require(rows == 19, "Native audit did not execute all 19 independent cases.");
        executed = rows; exit = 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; }
    if (owns_environment) {
        State retired; opennv_fistp_fixture_save(retired.bytes.data());
        if (std::memcmp(original_environment.bytes.data(), retired.bytes.data(), 5) != 0 ||
            std::memcmp(original_environment.bytes.data() + 24, retired.bytes.data() + 24, 4) != 0) {
            std::cerr << "Actual original x87 control/status/tag or MXCSR did not restore.\n"; exit = 1;
        }
        for (int index = 0; index < 8; ++index)
            if (std::memcmp(original_environment.bytes.data() + 32 + index * 16, retired.bytes.data() + 32 + index * 16, 10) != 0) {
                std::cerr << "Actual original x87 register did not restore.\n"; exit = 1;
            }
    }
    if (!FreeLibrary(module)) { std::cerr << "Actual first-party DLL lease did not retire.\n"; return 1; }
    if (exit == 0 && executed == 19)
        std::cout << "OPENNV_FISTP_ISA_NATIVE_PASS cases=19 zeroExecutedRefused=true realCallingThread=true mxcsrIndependent=true sourceArgumentPrefix=true originalEnvironmentRestored=true dllRetired=true productGameplay=UNEXECUTED\n";
    return exit;
}
