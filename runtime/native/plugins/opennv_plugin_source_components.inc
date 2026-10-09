// First-party component tables retain the real class extents and ABI. Every
// absent method refuses when reached; null vptrs or successful empty callbacks
// are never substituted for a source component implementation.
template<std::uint32_t Role, std::size_t Index> [[noreturn]] void __fastcall source_component_unowned(void*, void*) {
    const auto text = std::string("Source component ") + std::to_string(Role) + " method " + std::to_string(Index * 4) + " has no authoritative engine owner";
    nvse_unowned(text.c_str());
}
template<std::uint32_t Role, std::size_t... Index> std::array<void*, sizeof...(Index)> source_component_table(std::index_sequence<Index...>) {
    return {reinterpret_cast<void*>(&source_component_unowned<Role, Index>)...};
}
const char* __fastcall source_quest_editor(void* pointer, void*) {
    return nvse_adapter([&]() -> const char* {
        if (source_object_method(pointer, 0x138) != 1) throw Fatal(ERROR_INVALID_DATA, "Actual Quest editor-name owner refused publication.");
        return reinterpret_cast<const char*>(source_object_pointer(pointer).editor);
    });
}
std::uint32_t source_object_install_tables(SourceObjectRuntime& owner, std::uint32_t kind, std::uint64_t image_id) {
    if (kind == 2) return 0;
    if (!owner.vtable.id) {
        auto functions = source_form_table(std::make_index_sequence<78>{});
        functions[0x8c / 4] = reinterpret_cast<void*>(&source_form_type); functions[0xf0 / 4] = reinterpret_cast<void*>(&source_form_reference);
        functions[0x100 / 4] = reinterpret_cast<void*>(&source_form_actor); functions[0x130 / 4] = reinterpret_cast<void*>(&source_form_editor);
        owner.vtable = owner.arena.allocate(static_cast<std::uint32_t>(sizeof(functions)), GuestAccess::read_only, functions.data(), static_cast<std::uint32_t>(sizeof(functions)));
    }
    if (kind == 3 && !owner.quest_vtable.id) {
        auto functions = source_form_table(std::make_index_sequence<79>{});
        functions[0x8c / 4] = reinterpret_cast<void*>(&source_form_type); functions[0xf0 / 4] = reinterpret_cast<void*>(&source_form_reference);
        functions[0x100 / 4] = reinterpret_cast<void*>(&source_form_actor); functions[0x130 / 4] = reinterpret_cast<void*>(&source_form_editor);
        functions[0x138 / 4] = reinterpret_cast<void*>(&source_quest_editor);
        owner.quest_vtable = owner.arena.allocate(static_cast<std::uint32_t>(sizeof(functions)), GuestAccess::read_only, functions.data(), static_cast<std::uint32_t>(sizeof(functions)));
        const auto scriptable = source_component_table<1>(std::make_index_sequence<4>{});
        const auto icon = source_component_table<2>(std::make_index_sequence<7>{});
        const auto name = source_component_table<3>(std::make_index_sequence<4>{});
        owner.components[0] = owner.arena.allocate(static_cast<std::uint32_t>(sizeof(scriptable)), GuestAccess::read_only, scriptable.data(), static_cast<std::uint32_t>(sizeof(scriptable)));
        owner.components[1] = owner.arena.allocate(static_cast<std::uint32_t>(sizeof(icon)), GuestAccess::read_only, icon.data(), static_cast<std::uint32_t>(sizeof(icon)));
        owner.components[2] = owner.arena.allocate(static_cast<std::uint32_t>(sizeof(name)), GuestAccess::read_only, name.data(), static_cast<std::uint32_t>(sizeof(name)));
    }
    const auto table = address(kind == 3 ? owner.quest_vtable.base : owner.vtable.base);
    state->arena.write(image_id, 0, &table, 4);
    if (kind == 3) {
        const std::array<std::uint32_t, 3> offsets{24, 36, 48};
        for (std::size_t index = 0; index < offsets.size(); ++index) {
            const auto component = address(owner.components[index].base); state->arena.write(image_id, offsets[index], &component, 4);
        }
    }
    return table;
}
