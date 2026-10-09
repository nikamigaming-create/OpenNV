// First-party adaptation of the public variable-token view. The pointer is an
// existing, ordered event-list cell; this owner allocates no replacement cell.
bool expression_local_type(std::uint32_t type) { return type >= 11U && type <= 14U; }
SourceLocalCell expression_local_read(NvseExpressionEvaluator& evaluator, const NvseExpressionToken& token) {
    auto& caller = expression_caller(evaluator.caller);
    if (!expression_local_type(token.type) || !token.local_context || !state->source_locals || state->source_locals->calls.empty())
        throw Fatal(ERROR_INVALID_HANDLE, "Expression local has no current source-produced cell lifetime.");
    auto& context = source_local_context(token.local_context);
    const auto& active = state->source_locals->calls.back();
    if (active.context != &context || active.caller != caller.id || !context.active ||
        caller.frame[5] != address(context.event_list.base) || token.local_entry >= context.count)
        throw Fatal(ERROR_INVALID_DATA, "Expression local escaped its actual caller/event-list extent.");
    source_local_sync(false);
    source_local_validate(context);
    const auto cell = source_local_cell(context, token.local_entry);
    if (cell.id != token.local_index) throw Fatal(ERROR_INVALID_DATA, "Expression local ID differs from its original ordinal.");
    for (std::uint32_t index = 0; index < token.local_entry; ++index)
        if (context.declarations[index].id == cell.id)
            throw Fatal(ERROR_INVALID_DATA, "Expression local does not select the first original matching ID.");
    const auto* pointer = static_cast<const SourceLocalCell*>(context.cells.base) + token.local_entry;
    Bytes request; request.put(caller.id); request.put(evaluator.id); request.put(address(evaluator.marker.base));
    request.put(token.id); request.put(token.type); request.put(context.id); request.put(token.local_entry);
    request.put(cell.id); request.put(address(pointer)); request.put(cell.value);
    auto response = expression_host(0x106, request); Reader receipt{response.data};
    const auto actual_pointer = receipt.get<std::uint32_t>(); const auto actual_bits = receipt.get<std::uint64_t>(); receipt.finish();
    const auto current = source_local_cell(context, token.local_entry);
    if (actual_pointer != address(pointer) || actual_bits != current.value)
        throw Fatal(ERROR_INVALID_DATA, "Expression local read lacks its current C# cell/publication receipt.");
    return current;
}
void* expression_local_pointer(NvseExpressionEvaluator& evaluator, const NvseExpressionToken& token) {
    (void)expression_local_read(evaluator, token);
    auto& context = source_local_context(token.local_context);
    return const_cast<void*>(source_local_owner().arena.read(context.cells.id, token.local_entry * 24U, 24U));
}
double expression_local_number(NvseExpressionEvaluator& evaluator, const NvseExpressionToken& token) {
    const auto cell = expression_local_read(evaluator, token);
    if (token.type != 11U && token.type != 13U) return 0.0;
    double value = 0; std::memcpy(&value, &cell.value, sizeof(value)); return value;
}
bool expression_local_boolean(NvseExpressionEvaluator& evaluator, const NvseExpressionToken& token) {
    const auto cell = expression_local_read(evaluator, token);
    double value = 0; std::memcpy(&value, &cell.value, sizeof(value)); return value != 0.0;
}
std::uint32_t expression_local_form_id(NvseExpressionEvaluator& evaluator, const NvseExpressionToken& token) {
    const auto cell = expression_local_read(evaluator, token);
    return token.type == 12U ? static_cast<std::uint32_t>(cell.value) : 0U;
}
std::uint32_t expression_local_handle(NvseExpressionEvaluator& evaluator, const NvseExpressionToken& token) {
    const auto cell = expression_local_read(evaluator, token);
    double value = 0; std::memcpy(&value, &cell.value, sizeof(value));
    if (!std::isfinite(value) || value < 0 || value > UINT32_MAX || value != std::trunc(value))
        throw Fatal(ERROR_NOT_SUPPORTED, "Expression local handle conversion exceeds the exact UInt32 domain.");
    return static_cast<std::uint32_t>(value);
}
const char* expression_local_string(NvseExpressionEvaluator& evaluator, const NvseExpressionToken& token) {
    (void)expression_local_read(evaluator, token);
    if (token.type != 13U) return static_cast<const char*>(expression_owner().arena.read(evaluator.marker.id, 8, 1));
    // ResolveVariable caches the string object at extraction. A later direct
    // cell write cannot silently retarget that existing object lifetime.
    const auto* text = values_get_string(token.cached_string);
    return text ? text : static_cast<const char*>(expression_owner().arena.read(evaluator.marker.id, 8, 1));
}
