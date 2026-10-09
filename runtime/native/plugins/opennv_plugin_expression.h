#pragma once
#include "opennv_plugin_nvse.h"
#include "opennv_plugin_guest_arena.h"
#include <memory>
#include <cstdarg>
#include <utility>
#include <cmath>

namespace opennv_domain {
// Exact public callable prefix; opaque handles have no game object layout.
// Slice keeps its unowned compiler layout explicit. Element arguments use
// pointer-shaped reference ABI here; the value owner validates their storage
// and requires its exact original allocation/free contract for string outputs.
struct NvseExpressionUtilities {
    void* (__stdcall* create)(const NvseParamInfo*, void*, void*, void*, void*, void*, double*, std::uint32_t*);
    void (__fastcall* destroy)(void*);
    bool (__fastcall* extract)(void*);
    std::uint8_t (__fastcall* count)(void*);
    void* (__fastcall* argument)(void*, std::uint32_t);
    std::uint8_t (__fastcall* type)(void*);
    double (__fastcall* number)(void*);
    bool (__fastcall* boolean)(void*);
    std::uint32_t (__fastcall* form_id)(void*);
    void* (__fastcall* form)(void*);
    const char* (__fastcall* string)(void*);
    std::uint32_t (__fastcall* array)(void*);
    std::uint32_t (__fastcall* actor_value)(void*);
    void* (__fastcall* variable)(void*);
    const void* (__fastcall* pair)(void*);
    const void* (__fastcall* slice)(void*);
    std::uint32_t (__fastcall* animation)(void*);
    void (__fastcall* expected_return)(void*, std::uint8_t);
    void (__fastcall* assign_element)(void*, void*);
    void (__fastcall* element)(void*, void*);
    bool (__fastcall* convert)(void*, std::uint8_t);
    bool (__fastcall* extract_v)(void*, va_list);
    void (__fastcall* error)(void*, const char*, va_list);
};
static_assert(sizeof(NvseExpressionUtilities) == 92);
struct NvseExpressionToken {
    std::uint64_t id = 0, left = 0, right = 0;
    std::uint32_t type = 0, offset = 0, string_offset = 0, pair_offset = 0;
    double number = 0;
    std::uint64_t local_context = 0;
    std::uint32_t local_entry = 0, local_index = 0, cached_string = 0;
    std::string text;
};
struct NvseExpressionCaller {
    std::uint64_t id;
    std::uint32_t opcode, start, end;
    std::array<std::uint32_t, 8> frame;
    std::uint32_t created = 0, destroyed = 0, tokens = 0;
};
struct NvseExpressionEvaluator {
    std::uint64_t id = 0, caller = 0;
    GuestRegion marker{}, tokens_region{}, pairs_region{};
    bool extracted = false;
    std::uint32_t source_start = 0;
    std::vector<NvseExpressionToken> tokens;
    std::vector<std::uint64_t> roots;
};
struct NvseExpressionRuntime {
    std::uint32_t declared_bytes = 0, callable_bytes = 0, array_pointer = 0;
    std::uint32_t initializations = 0, created = 0, destroyed = 0;
    GuestArena arena;
    std::vector<std::unique_ptr<NvseExpressionEvaluator>> evaluators;
    std::vector<NvseExpressionCaller*> callers;
};
}
