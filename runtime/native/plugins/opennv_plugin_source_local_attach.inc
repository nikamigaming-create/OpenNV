void source_local_attach_script(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>(), script_id = reader.get<std::uint64_t>(); reader.finish();
    if (!state->nvse || module != state->module_id || !state->calls.empty())
        throw Fatal(ERROR_INVALID_STATE, "Actual local Script attachment needs its idle current module.");
    auto& context = source_local_context(id); auto& script = source_object(script_id); source_local_validate(context);
    if (context.script_id || context.script_pointer || context.active || script.kind != 1)
        throw Fatal(ERROR_INVALID_DATA, "Prepared event list has a competing or non-Script native owner.");
    auto& owner = source_local_owner();
    const auto before = nvse_copy(static_cast<const SourceEventList*>(owner.arena.read(context.event_list.id, 0, 20)));
    auto after = before; after.script = reinterpret_cast<void*>(script.pointer);
    owner.arena.replace_read_only(context.event_list.id, &before, &after, 20);
    context.script_id = script.id; context.script_pointer = script.pointer; source_local_validate(context);
    Bytes response; response.put(id); response.put(address(context.event_list.base)); response.put(script.pointer);
    reply(frame, ERROR_SUCCESS, "", response);
}
