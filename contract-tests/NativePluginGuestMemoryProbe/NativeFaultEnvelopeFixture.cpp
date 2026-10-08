#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "../../runtime/native/plugins/opennv_plugin_domain.h"
#include <cstdlib>
#include <cstring>
#include <string>

using namespace opennv_domain;
static_assert(sizeof(void*) == 4, "Build the authored transport fixture as x86.");
static_assert(OPENNV_FAULT_ENVELOPE_MODE >= 0 && OPENNV_FAULT_ENVELOPE_MODE <= 6,
    "Each executable has one explicit first-party corruption mode.");

static bool transfer(HANDLE pipe, void* bytes, DWORD length, bool write) {
    auto* cursor = static_cast<unsigned char*>(bytes);
    while (length != 0) {
        DWORD count = 0;
        const auto ok = write ? WriteFile(pipe, cursor, length, &count, nullptr) :
            ReadFile(pipe, cursor, length, &count, nullptr);
        if (!ok || count == 0) return false;
        cursor += count; length -= count;
    }
    return true;
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 3 || std::wstring(argv[1]) != L"--generation") return 2;
    wchar_t* end = nullptr;
    const auto generation = _wcstoui64(argv[2], &end, 10);
    if (generation == 0 || !end || *end != L'\0') return 2;
    const auto input = GetStdHandle(STD_INPUT_HANDLE);
    const auto output = GetStdHandle(STD_OUTPUT_HANDLE);
    FrameHeader request{};
    if (!transfer(input, &request, sizeof(request), false) || request.magic != protocol_magic ||
        request.version != protocol_version || request.kind != static_cast<std::uint32_t>(Kind::request) ||
        request.operation != static_cast<std::uint32_t>(Operation::hello) || request.generation != generation ||
        request.id != 1 || request.parent != 0 || request.length != 0 || request.reserved != 0) return 2;
    const char* reason = OPENNV_FAULT_ENVELOPE_MODE == 6 ? "" : "authored-fault-envelope-fixture";
    const auto reason_length = static_cast<std::uint32_t>(std::strlen(reason));
    std::uint32_t code = OPENNV_FAULT_ENVELOPE_MODE == 5 ? 0U : ERROR_INVALID_DATA;
    FrameHeader fault{protocol_magic, protocol_version, static_cast<std::uint32_t>(Kind::fault),
        request.operation, generation, request.id, request.parent, 8U + reason_length, 0};
    if (OPENNV_FAULT_ENVELOPE_MODE == 1) ++fault.id;
    if (OPENNV_FAULT_ENVELOPE_MODE == 2) ++fault.parent;
    if (OPENNV_FAULT_ENVELOPE_MODE == 3) ++fault.operation;
    if (OPENNV_FAULT_ENVELOPE_MODE == 4) ++fault.generation;
    auto length = reason_length;
    if (!transfer(output, &fault, sizeof(fault), true) || !transfer(output, &code, sizeof(code), true) ||
        !transfer(output, &length, sizeof(length), true) ||
        !transfer(output, const_cast<char*>(reason), reason_length, true)) return 2;
    return 3;
}
