// Include after callable_pages.inc. No original executable is mapped as code.
// OriginalCallablePages emits only its first-party seven-byte jump; this owner
// retains the same borrowed address and a genuine typed COMMAND_ARGS target.
std::unique_ptr<EngineCommandRuntime> engine_commands;

void engine_command_validate(const EngineCommandBinding& binding, bool retired = false) {
    if (!state || binding.generation != state->generation || binding.module != state->module_id ||
        !binding.published || (binding.retired && !retired) || binding.failed)
        throw Fatal(ERROR_INVALID_HANDLE, "Borrowed engine command has no current first-party generation/callable owner.");
    MEMORY_BASIC_INFORMATION observed{};
    if (!VirtualQuery(reinterpret_cast<void*>(binding.source), &observed, sizeof(observed)) ||
        observed.State != MEM_COMMIT || observed.Type != MEM_PRIVATE || observed.Protect != PAGE_EXECUTE_READ ||
        static_cast<std::uint64_t>(binding.source) + 7 > reinterpret_cast<std::uintptr_t>(observed.BaseAddress) + observed.RegionSize)
        throw Fatal(ERROR_INVALID_DATA, "Borrowed engine command lost its actual executable first-party page.");
    std::array<std::uint8_t, 7> bytes{}; SIZE_T transferred = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(binding.source), bytes.data(), bytes.size(), &transferred) || transferred != bytes.size())
        throw Fatal(GetLastError(), "Borrowed engine callable bytes have no complete readback.");
    if (retired) {
        for (const auto value : bytes) if (value != 0xcc) throw Fatal(ERROR_INVALID_DATA, "Borrowed engine callable did not acquire its retired trap extent.");
    } else {
        std::uint32_t target = 0; std::memcpy(&target, bytes.data() + 1, sizeof(target));
        if (bytes[0] != 0xb8 || bytes[5] != 0xff || bytes[6] != 0xe0 || target != binding.target ||
            owned_thunk(reinterpret_cast<void*>(target)) != target)
            throw Fatal(ERROR_INVALID_DATA, "Borrowed engine callable differs from its retained first-party publication.");
    }
}
EngineCommandBinding* engine_command_owned(std::uint32_t source) {
    if (!engine_commands) return nullptr;
    for (const auto& binding : engine_commands->bindings) if (binding->source == source && !binding->retired) return binding.get();
    return nullptr;
}
bool engine_command_invoke(std::size_t slot, const std::array<std::uint32_t, 8>& arguments) {
    return nvse_adapter([&] {
        nvse_owner();
        if (!engine_commands || engine_commands->failed || slot >= engine_commands->bindings.size())
            throw Fatal(ERROR_INVALID_HANDLE, "Engine Boolean command has no retained public-ABI callable slot.");
        auto& binding = *engine_commands->bindings[slot]; engine_command_validate(binding);
        if (binding.busy) throw Fatal(ERROR_BUSY, "Engine Boolean command cannot replay an unfinished semantic invocation.");
        binding.busy = true;
        try {
            Bytes request; request.put(binding.id); request.put(binding.source);
            for (const auto argument : arguments) request.put(argument);
            const auto result = nvse_host(NvseHostCall::engine_command_execute, request);
            if (result > 1) throw Fatal(ERROR_INVALID_DATA, "C# engine Boolean command returned a noncanonical public bool.");
            engine_command_validate(binding); binding.busy = false; return result != 0;
        } catch (...) { binding.failed = engine_commands->failed = true; throw; }
    });
}

using EngineCommandFunction = bool(__cdecl*)(NvseParamInfo*, void*, void*, void*, void*, void*, double*, std::uint32_t*);
template<std::size_t Slot> bool __cdecl engine_command_entry(NvseParamInfo* parameters, void* data,
    void* subject, void* containing, void* script, void* events, double* result, std::uint32_t* offset) {
    return engine_command_invoke(Slot, {address(parameters), address(data), address(subject), address(containing),
        address(script), address(events), address(result), address(offset)});
}
template<std::size_t... Slots> constexpr std::array<EngineCommandFunction, sizeof...(Slots)> engine_command_entries(std::index_sequence<Slots...>) {
    return {&engine_command_entry<Slots>...};
}
const auto engine_command_functions = engine_command_entries(std::make_index_sequence<engine_command_lifetime_limit>{});

void engine_command_admit(void* pointer, std::uint32_t descriptor) {
    nvse_load_only(); auto& runtime = nvse_owner();
    if (!pointer) return;
    if (!descriptor || state->calls.size() != 1 || runtime.invocation_depth != 1)
        throw Fatal(ERROR_BUSY, "Engine callable publication requires the genuine outer original Load frame.");
    Bytes declaration; declaration.put(address(pointer)); declaration.put(descriptor); declaration.put(1U);
    const auto id = nvse_host(NvseHostCall::engine_command_declare, declaration);
    if (!id) throw Fatal(ERROR_NOT_SUPPORTED, "Borrowed engine command source behavior was refused.");
    if (auto* existing = engine_command_owned(address(pointer))) {
        if (existing->id != id) throw Fatal(ERROR_INVALID_DATA, "Engine source declaration changed its retained callable identity.");
        engine_command_validate(*existing); return;
    }
    if (!engine_commands) engine_commands = std::make_unique<EngineCommandRuntime>();
    if (engine_commands->failed || engine_commands->bindings.size() >= engine_command_lifetime_limit)
        throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Engine command callable lifetime budget is failed/exhausted.");
    auto binding = std::make_unique<EngineCommandBinding>();
    binding->id = id; binding->generation = state->generation; binding->module = state->module_id;
    binding->source = address(pointer); binding->slot = static_cast<std::uint32_t>(engine_commands->bindings.size());
    binding->target = owned_thunk(reinterpret_cast<void*>(engine_command_functions[binding->slot]));
    engine_commands->bindings.push_back(std::move(binding)); auto& retained = *engine_commands->bindings.back();
    try {
        // This narrowly admitted publication occurs inside the one outer Load,
        // before any engine-leaf execution. Idle publication restrictions for
        // the other source families are not relaxed.
        if (std::any_of(engine_commands->bindings.begin(), engine_commands->bindings.end(), [](const auto& candidate) { return candidate->busy; }))
            throw Fatal(ERROR_BUSY, "Engine callable publication overlaps an actual engine semantic call.");
        if (!original_callable_pages) original_callable_pages = std::make_unique<OriginalCallablePages>();
        retained.callable_lease = original_callable_pages->acquire({{retained.source, reinterpret_cast<void*>(retained.target)}});
        retained.published = true; engine_command_validate(retained);
        Bytes publication; publication.put(retained.id); publication.put(retained.source);
        publication.put(retained.callable_lease); publication.put(retained.target);
        if (nvse_host(NvseHostCall::engine_command_publish, publication) != 1)
            throw Fatal(ERROR_INVALID_DATA, "Engine callable lacks its genuine C# executable-memory readback receipt.");
    } catch (const CallablePageFailure& error) {
        retained.failed = engine_commands->failed = true; throw Fatal(error.code, error.what());
    } catch (...) { retained.failed = engine_commands->failed = true; throw; }
}
void engine_command_handler(void* pointer, std::uint32_t descriptor, const std::string& context) {
    if (!pointer) return;
    MEMORY_BASIC_INFORMATION memory{};
    if (VirtualQuery(pointer, &memory, sizeof(memory)) && memory.AllocationBase == state->module && memory.Type == MEM_IMAGE) {
        nvse_executable(pointer, false, context); return;
    }
    engine_command_admit(pointer, descriptor);
}
void engine_command_executable(void* pointer) {
    if (auto* binding = engine_command_owned(address(pointer))) { engine_command_validate(*binding); return; }
    nvse_executable(pointer);
}
bool engine_command_zero_parameter_caller(std::uint32_t execute, std::uint32_t parameters, std::uint32_t return_type) {
    auto* binding = engine_command_owned(execute);
    if (!binding || parameters || return_type) return false;
    engine_command_validate(*binding); return true;
}
void engine_commands_validate_all() {
    if (!engine_commands) return;
    for (const auto& binding : engine_commands->bindings) if (!binding->retired) engine_command_validate(*binding);
}
void engine_commands_retire(const Frame& frame) {
    if (!engine_commands) return;
    if (!state->calls.empty() || engine_commands->failed ||
        std::any_of(engine_commands->bindings.begin(), engine_commands->bindings.end(), [](const auto& binding) { return binding->busy || binding->failed; }))
        throw Fatal(ERROR_BUSY, "Engine command retirement retains an entered/failed invocation or publication.");
    for (auto& binding : engine_commands->bindings) {
        if (binding->retired) continue;
        try {
            engine_command_validate(*binding); source_callables_release(binding->callable_lease);
            engine_command_validate(*binding, true);
            state->calls.push_back(frame.header.id);
            try {
                Bytes request; request.put(binding->id); request.put(binding->source); request.put(binding->callable_lease);
                if (nvse_host(NvseHostCall::engine_command_retire, request) != 1)
                    throw Fatal(ERROR_INVALID_DATA, "Engine command trap retirement lacks its independent C# readback.");
                state->calls.pop_back(); binding->retired = true;
            } catch (...) { state->calls.pop_back(); throw; }
        } catch (...) { binding->failed = engine_commands->failed = true; throw; }
    }
    // The shared page owner keeps retired addresses reserved to process closure.
    // Binding slots are never recycled, and original DLL pointers cannot rebind.
}
