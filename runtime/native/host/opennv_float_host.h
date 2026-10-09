#pragma once
#include <cstddef>
#include <cstdint>

// First-party public Win64/Intel ISA bridge. No selected game's addresses or
// executable layout is part of this ABI. A refused call has no integer result.
struct OpenNVHostFistp32 {
    std::uint32_t abi;
    std::uint32_t bytes;
    std::uint32_t process;
    std::uint32_t thread;
    std::uint32_t input;
    std::int32_t value;
    std::uint16_t control_before;
    std::uint16_t status_before;
    std::uint16_t control_after;
    std::uint16_t status_after;
    std::uint8_t tag_before;
    std::uint8_t tag_after;
    std::uint8_t outcome;
    std::uint8_t operation;
    std::uint32_t reserved;
    std::uint32_t argument;
    std::uint16_t status_at_conversion;
    std::uint16_t reserved_tail;
};
static_assert(sizeof(OpenNVHostFistp32) == 48);
static_assert(offsetof(OpenNVHostFistp32, control_before) == 24);
static_assert(offsetof(OpenNVHostFistp32, outcome) == 34);
static_assert(offsetof(OpenNVHostFistp32, reserved) == 36);
static_assert(offsetof(OpenNVHostFistp32, argument) == 40);

// operation: 0 observes only; 1 performs FLD m32real + FISTP m32int;
// 2 first performs the admitted Float32 argument load/store transport.
// outcome: 1 observed, 2 converted, 3 pending exception, 4 unmasked
// exception policy, 5 reserved precision, 6 occupied next x87 stack slot.
extern "C" int opennv_host_fistp32(std::uint32_t operation, std::uint32_t input,
    OpenNVHostFistp32* result, std::uint32_t bytes);
