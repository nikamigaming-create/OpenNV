#pragma once
#include "opennv_plugin_guest_arena.h"
#include <memory>
#include <cstddef>
#include <vector>

namespace opennv_domain {
// Public xNVSE 6.4.9 GameAPI.h/GameTypes.h storage declarations. These
// first-party objects retain actual campaign locals, not an Actor or Script.
#pragma pack(push, 4)
struct SourceLocalCell { std::uint32_t id, reserved_link; std::uint64_t value; std::uint8_t storage_flags, reserved_bytes[3]; std::uint32_t reserved_tail; };
struct SourceListNode { void* data; SourceListNode* next; };
struct SourceEventList {
    void* script;
    std::uint32_t reserved;
    SourceListNode* events;
    SourceListNode* variables;
    void* auxiliary;
};
#pragma pack(pop)
static_assert(sizeof(SourceLocalCell) == 24);
static_assert(offsetof(SourceLocalCell, storage_flags) == 16);
static_assert(offsetof(SourceLocalCell, value) == 8);
static_assert(sizeof(SourceListNode) == 8);
static_assert(sizeof(SourceEventList) == 20);
static_assert(offsetof(SourceEventList, variables) == 12);

struct SourceLocalDeclaration { std::uint32_t id, kind, storage_flags; std::uint64_t baseline; };
struct SourceLocalContext {
    std::uint64_t id = 0, script_id = 0;
    std::uint32_t script_pointer = 0;
    std::uint32_t count = 0, filled = 0;
    GuestRegion cells{}, nodes{}, empty_events{}, event_list{};
    std::vector<SourceLocalDeclaration> declarations;
    bool sealed = false, live = true;
    std::uint32_t active = 0;
};
struct SourceLocalCall { SourceLocalContext* context; std::uint64_t caller; };
struct SourceLocalRuntime {
    GuestArena arena;
    std::vector<std::unique_ptr<SourceLocalContext>> contexts;
    std::vector<SourceLocalCall> calls;
    std::uint64_t next_transfer = 0;
    bool synchronizing = false;
};
}
