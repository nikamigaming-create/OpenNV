// Public Data v3/v4 callback ABI. Original DLL code is never copied here.
constexpr std::uint32_t data_callback = 0x500U;
void data_validate() {
    if (!state || !state->data || !state->data->input) return;
    const auto& owner = *state->data;
    if (nvse_copy(reinterpret_cast<const std::uint32_t*>(owner.input.get())) != owner.input_vtable)
        throw Fatal(ERROR_NOT_SUPPORTED, "DIHookControl direct vtable mutation has no owned replacement constructor.");
}
Bytes data_request(NvseDataCall call, const Bytes& arguments) {
    if (!state || !state->nvse || GetCurrentThreadId() != state->thread || state->calls.empty())
        throw Fatal(ERROR_INVALID_THREAD_ID, "Data callback lacks its actual module/thread/current call.");
    Bytes payload; payload.put(state->module_id); payload.put(state->nvse->handle); payload.put(static_cast<std::uint32_t>(call));
    payload.data.insert(payload.data.end(), arguments.data.begin(), arguments.data.end());
    auto result = perform_callback_bytes(Kind::nvse_callback, data_callback, payload);
    if (callback_fault.load()) throw Fatal(callback_fault.load(), state->callback_reason[0] ? state->callback_reason : "Data callback ownership failed.");
    return result;
}
void data_input_read(NvseDataRuntime& owner, const Bytes& bytes) {
    Reader reader{bytes.data}; const auto window = reader.get<std::uint64_t>(), sample = reader.get<std::uint64_t>();
    const auto revision = reader.get<std::int64_t>(); const auto count = reader.get<std::uint32_t>();
    if (!window || !sample || revision <= 0 || count != 266 || !owner.input ||
        (owner.window && owner.window != window) || sample < owner.sample || revision < owner.revision)
        throw Fatal(ERROR_INVALID_DATA, "DIHookControl has no current complete product-window/device extent.");
    std::array<NvseDiKey, 266> keys{};
    for (auto& key : keys) {
        key = reader.get<NvseDiKey>();
        if (key.raw > 1 || key.game > 1 || key.inserted > 1 || key.hold > 1 || key.tap > 1 || key.user_disabled > 1 || key.script_disabled > 1)
            throw Fatal(ERROR_INVALID_DATA, "DIHookControl boolean extent is invalid.");
    }
    reader.finish(); owner.input->keys = keys; owner.window = window; owner.sample = sample; owner.revision = revision;
}
void data_sync_if_bound() {
    if (!state || !state->data || !state->data->input || state->data->synchronizing) return;
    auto& owner = *state->data;
    data_validate();
    if (state->calls.empty()) throw Fatal(ERROR_INVALID_STATE, "DIHookControl exchange escaped its actual native invocation.");
    owner.synchronizing = true;
    try {
        Bytes payload; payload.put(address(owner.input.get())); payload.put(owner.sequence + 1); payload.put(266U);
        for (const auto& key : owner.input->keys) payload.put(key);
        const auto output_bytes = data_request(NvseDataCall::input_exchange, payload);
        data_input_read(owner, output_bytes); ++owner.sequence; owner.synchronizing = false;
    } catch (...) { owner.synchronizing = false; throw; }
}
void* __cdecl data_singleton(std::uint32_t id) {
    return nvse_adapter([&]() -> void* {
        nvse_owner(); Bytes args; args.put(id); auto bytes = data_request(NvseDataCall::singleton, args);
        Reader reader{bytes.data}; if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Data singleton admission changed."); reader.finish();
        if (id != 1) throw Fatal(ERROR_NOT_SUPPORTED, "Data native map singleton has no complete class producer.");
        auto& owner = *state->data;
        if (!owner.input) {
            owner.input = std::make_unique<NvseDiObject>();
            owner.input_vtable = nvse_copy(reinterpret_cast<const std::uint32_t*>(owner.input.get()));
            Bytes publication; publication.put(address(owner.input.get()));
            data_input_read(owner, data_request(NvseDataCall::input_create, publication));
        }
        return owner.input.get();
    });
}
void* __stdcall data_inventory_create(void* container, void* item, std::int32_t count, void* extra) {
    return nvse_adapter([&]() -> void* {
        nvse_owner(); Bytes args; args.put(address(container)); args.put(address(item)); args.put(count); args.put(address(extra));
        auto bytes = data_request(NvseDataCall::inventory_create, args); Reader reader{bytes.data};
        const auto id = reader.get<std::uint32_t>(), reference = reader.get<std::uint32_t>(), inventory = reader.get<std::uint32_t>(); reader.finish();
        if (!id || !reference || !inventory) throw Fatal(ERROR_INVALID_DATA, "InventoryCreateEntry has no actual constructor result.");
        nvse_readable(reinterpret_cast<void*>(reference), 4); nvse_readable(reinterpret_cast<void*>(inventory), 0x30);
        return reinterpret_cast<void*>(reference);
    });
}
void* __cdecl data_inventory_get(std::uint32_t id) {
    return nvse_adapter([&]() -> void* {
        nvse_owner(); Bytes args; args.put(id); auto bytes = data_request(NvseDataCall::inventory_get, args); Reader reader{bytes.data};
        const auto result = reader.get<std::uint32_t>(); reader.finish();
        if (result) nvse_readable(reinterpret_cast<void*>(result), 0x30); return reinterpret_cast<void*>(result);
    });
}
void* __cdecl data_inventory_self(void* inventory) {
    return nvse_adapter([&]() -> void* {
        nvse_owner(); Bytes args; args.put(address(inventory)); auto bytes = data_request(NvseDataCall::inventory_self, args); Reader reader{bytes.data};
        const auto result = reader.get<std::uint32_t>(); reader.finish(); return reinterpret_cast<void*>(result);
    });
}
std::uint32_t data_lambda(void* script, NvseDataCall call) {
    nvse_owner(); Bytes args; args.put(address(script)); auto bytes = data_request(call, args); Reader reader{bytes.data};
    const auto value = reader.get<std::uint32_t>(); reader.finish(); if (value > 1) throw Fatal(ERROR_INVALID_DATA, "Lambda result is not a bool."); return value;
}
void __cdecl data_lambda_save(void* script) { nvse_adapter([&] { (void)data_lambda(script, NvseDataCall::lambda_save); }); }
void __cdecl data_lambda_unsave(void* script) { nvse_adapter([&] { (void)data_lambda(script, NvseDataCall::lambda_unsave); }); }
bool __cdecl data_is_lambda(void* script) { return nvse_adapter([&] { return data_lambda(script, NvseDataCall::is_lambda) != 0; }); }
void* __cdecl data_function(std::uint32_t id) {
    return nvse_adapter([&]() -> void* {
        nvse_owner(); Bytes args; args.put(id); auto bytes = data_request(NvseDataCall::function, args); Reader reader{bytes.data};
        if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Data function admission changed."); reader.finish();
        switch (id) {
        case 2: return reinterpret_cast<void*>(data_inventory_get);
        case 3: return reinterpret_cast<void*>(data_inventory_self);
        case 7: return reinterpret_cast<void*>(data_inventory_create);
        case 8: return reinterpret_cast<void*>(data_lambda_save);
        case 9: return reinterpret_cast<void*>(data_lambda_unsave);
        case 10: return reinterpret_cast<void*>(data_is_lambda);
        default: throw Fatal(ERROR_NOT_SUPPORTED, "Data function has no genuine native caller/owner.");
        }
    });
}
void* __cdecl data_pointer(std::uint32_t id) {
    return nvse_adapter([&]() -> void* {
        nvse_owner(); Bytes args; args.put(id); (void)data_request(NvseDataCall::data, args);
        throw Fatal(ERROR_NOT_SUPPORTED, "Data pointer has no preload/current-state producer.");
    });
}
void __cdecl data_clear_cache() {
    nvse_adapter([&] {
        nvse_owner(); auto bytes = data_request(NvseDataCall::clear_cache, {}); Reader reader{bytes.data};
        if (reader.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Data cache invalidation lacks its actual consumer."); reader.finish();
    });
}
void* data_interface_address(std::uint32_t version) {
    if (version != 3 && version != 4) throw Fatal(ERROR_INVALID_DATA, "Selected public Data interface version is unreviewed.");
    if (!state->data) {
        state->data = std::make_unique<NvseDataRuntime>();
        state->data->table = {version, data_singleton, data_function, data_pointer, data_clear_cache};
    }
    if (state->data->table.version != version) throw Fatal(ERROR_INVALID_DATA, "Data source version changed within one generation.");
    return &state->data->table;
}
