// Idle publication is distinct from a plugin write. The native owner checks
// the full old image before changing protection and restores read-only pages.
void source_object_refresh(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>();
    const auto count = reader.get<std::uint32_t>();
    if (!state->nvse || module != state->module_id || !state->calls.empty() || !count || count > (max_payload - 64) / 2)
        throw Fatal(ERROR_BUSY, "Source object refresh lacks an idle complete generation owner.");
    const auto* before = static_cast<const std::uint8_t*>(reader.bytes(count));
    const auto* after = static_cast<const std::uint8_t*>(reader.bytes(count)); reader.finish();
    auto& object = source_object(id);
    std::uint32_t form = 0;
    if (count >= 16) std::memcpy(&form, after + 12, 4);
    if (count != object.extent || (object.kind != 2 &&
        (std::memcmp(before, after, 4) ||
            after[4] != (object.kind == 1 ? 17 : 71) ||
            form != object.form)) || (object.kind == 3 &&
        (count != 108 || std::memcmp(before + 24, after + 24, 4) ||
            std::memcmp(before + 36, after + 36, 4) || std::memcmp(before + 48, after + 48, 4))))
        throw Fatal(ERROR_INVALID_DATA, "Source refresh changed class, vtable, form or full extent.");
    state->arena.replace_read_only(object.image_id, before, after, count);
    // inspect validates actual protection and allocation lifetime again.
    (void)source_object(id);
    Bytes response; response.put(id); response.put(object.pointer); response.put(count);
    reply(frame, ERROR_SUCCESS, "", response);
}
