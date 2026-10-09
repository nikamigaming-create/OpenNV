#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <cstring>
#include <limits>
#include "opennv_float_host.h"

#if !defined(_M_X64)
#error The current calling-thread float host requires the Win64 AMD64 ABI.
#endif

extern "C" void opennv_host_fistp32_asm(OpenNVHostFistp32* result);

extern "C" int opennv_host_fistp32(std::uint32_t operation,
    std::uint32_t input, OpenNVHostFistp32* result, std::uint32_t bytes) {
    if (!result || bytes != sizeof(OpenNVHostFistp32) || operation > 2 || (!operation && input)) return -1;
    std::memset(result, 0, sizeof(*result));
    result->abi = 1;
    result->bytes = static_cast<std::uint32_t>(sizeof(*result));
    result->process = GetCurrentProcessId();
    result->thread = GetCurrentThreadId();
    result->input = input;
    result->argument = input;
    result->value = (std::numeric_limits<std::int32_t>::min)();
    result->operation = static_cast<std::uint8_t>(operation);
    // This is a synchronous call on the caller's real OS thread. No worker,
    // domain dispatch, CRT controlfp, MXCSR read, control-word write or reset.
    opennv_host_fistp32_asm(result);
    return 0;
}
