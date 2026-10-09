// Include after the SteamRuntime helpers and before steam_provider.
constexpr std::uint32_t steam_source_callback_event = 0x600;
void steam_callback_prefix_bytes(Bytes& bytes, const SteamRuntime* owner) {
    bytes.put(owner ? static_cast<std::uint32_t>(owner->callback_prefix.size()) : 0U);
    if (!owner) return;
    for (const auto& value : owner->callback_prefix) {
        bytes.put(value.sequence); bytes.put(value.token); bytes.put(value.id); bytes.put(value.flags);
        bytes.put(value.alternate ? 1U : 0U); bytes.put(value.io_failure ? 1U : 0U); bytes.put(value.api_call);
        bytes.put(value.payload_copied ? 1U : 0U); bytes.put(value.handler_returned ? 1U : 0U);
        bytes.put(static_cast<std::uint32_t>(value.payload.size()));
        for (auto byte : value.payload) bytes.put(byte);
    }
}
void steam_source_callback(SteamSourceCallback* object, void* payload, bool alternate, bool io_failure, std::uint64_t api_call) noexcept {
    try {
        auto& owner = steam_owner();
        const auto found = std::find_if(owner.callbacks.begin(), owner.callbacks.end(),
            [object](const auto& item) { return item.second.get() == object; });
        if (GetCurrentThreadId() != state->thread || state->calls.empty() || found == owner.callbacks.end() ||
            found->second.get() != object || !object->live || object->callback_id != static_cast<std::int32_t>(object->source_id))
            throw Fatal(ERROR_INVALID_STATE, "Actual callback did not enter its registered current native member/thread.");
        if (owner.callback_prefix.size() >= 65536 || owner.next_delivery == std::numeric_limits<std::uint64_t>::max())
            throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Actual source callback transaction extent exhausted.");
        SteamSourceCallbackPrefix prefix{}; prefix.sequence = ++owner.next_delivery; prefix.token = object->token;
        prefix.id = object->source_id; prefix.flags = object->flags; prefix.alternate = alternate;
        prefix.io_failure = io_failure; prefix.api_call = api_call;
        owner.callback_prefix.push_back(std::move(prefix)); auto& entered = owner.callback_prefix.back();
        steam_readable(payload, object->payload_size);
        const auto* source = static_cast<const unsigned char*>(payload);
        entered.payload.assign(source, source + object->payload_size); entered.payload_copied = true;
        Bytes bytes; bytes.put(owner.session); bytes.put(object->token); bytes.put(entered.sequence);
        bytes.put(object->source_id); bytes.put(static_cast<std::uint32_t>(object->flags));
        bytes.put(alternate ? 1U : 0U); bytes.put(io_failure ? 1U : 0U); bytes.put(api_call); bytes.put(object->payload_size);
        for (auto byte : entered.payload) bytes.put(byte);
        const auto result = perform_callback(Kind::nvse_callback, steam_source_callback_event, bytes);
        if (callback_fault.load()) return;
        if (result != 1) throw Fatal(ERROR_INVALID_DATA, "Actual C# source callback did not return its current receiver invocation.");
        entered.handler_returned = true;
    }
    catch (const Fatal& failure) { retain_callback_fault(failure.code, failure.what()); }
    catch (...) { retain_callback_fault(ERROR_INVALID_DATA, "Actual source callback preparation/receiver failed."); }
}
void __fastcall steam_source_run_many(SteamSourceCallback* object, void*, void* payload, bool io_failure, std::uint64_t api_call) {
    steam_source_callback(object, payload, true, io_failure, api_call);
}
void __fastcall steam_source_run_one(SteamSourceCallback* object, void*, void* payload) {
    steam_source_callback(object, payload, false, false, 0);
}
int __fastcall steam_source_size(SteamSourceCallback* object, void*) {
    if (!state || !state->steam || GetCurrentThreadId() != state->thread || state->calls.empty() ||
        std::none_of(state->steam->callbacks.begin(), state->steam->callbacks.end(),
            [object](const auto& item) { return item.second.get() == object && item.second->live; })) {
        retain_callback_fault(ERROR_INVALID_HANDLE, "Callback size query lost its actual current member/thread."); return 0;
    }
    return static_cast<int>(object->payload_size);
}
void* steam_source_callback_table[] = { reinterpret_cast<void*>(&steam_source_run_many),
    reinterpret_cast<void*>(&steam_source_run_one), reinterpret_cast<void*>(&steam_source_size) };
std::uint64_t steam_register_source_callback(SteamRuntime& owner, const std::string& argument, CallReceipt& receipt, std::uint32_t& result) {
    std::uint32_t id = 0, size = 0;
    if (argument == "1101") { id = 1101; size = 24; }
    else if (argument == "1102") { id = 1102; size = 16; }
    else if (argument == "1103") { id = 1103; size = 152; }
    else throw Fatal(ERROR_NOT_SUPPORTED, "Original callback has no source constructor/handler extent.");
    if (owner.phase != SteamPhase::loaded || std::any_of(owner.callbacks.begin(), owner.callbacks.end(),
        [id](const auto& item) { return item.second->source_id == id; }) || owner.next_callback == std::numeric_limits<std::uint64_t>::max())
        throw Fatal(ERROR_INVALID_STATE, "Original callback construction was already attempted or followed source Init.");
    auto member = std::make_unique<SteamSourceCallback>(); member->table = steam_source_callback_table;
    member->source_id = id; member->payload_size = size; member->token = ++owner.next_callback;
    const auto token = member->token; auto* object = member.get(); owner.callbacks.emplace(token, std::move(member));
    // Native ownership precedes the foreign call, so a failed constructor
    // retains its actual object rather than inventing a returned registration.
    owner.prefix.push_back(SteamForeignPrefix{SteamCallStep::source_export, SteamReturnKind::none});
    auto& prefix = owner.prefix.back(); prefix.callable_path = owner.path; prefix.callable_sha = owner.sha;
    (void)OpenNVInvokeSteamRegisterGuarded(steam_export(owner, "SteamAPI_RegisterCallback"), object, id, &receipt);
    prefix.abi = receipt;
    if (receipt.exception_code || receipt.stack_delta || receipt.preserved_registers != 7)
        throw Fatal(receipt.exception_code ? receipt.exception_code : ERROR_INVALID_DATA, "Actual Steam callback registration failed its ABI.");
    prefix.returned = true; result = object->flags;
    if (object->flags & 1U && object->callback_id != static_cast<std::int32_t>(id))
        throw Fatal(ERROR_INVALID_DATA, "Actual provider registered the member under another callback ID.");
    return token;
}
void steam_unregister_source_callback(SteamRuntime& owner, std::uint64_t token, CallReceipt& receipt) {
    const auto found = owner.callbacks.find(token);
    if (found == owner.callbacks.end() || owner.phase != SteamPhase::shutdown_returned)
        throw Fatal(ERROR_INVALID_HANDLE, "Actual member retirement lost its source shutdown/object lifetime.");
    auto& member = *found->second;
    // Original CCallback destructor tests the actual registered flag; a
    // constructor that returned with flag zero has no Unregister API call.
    if (member.flags & 1U) {
        (void)steam_invoke(owner, SteamCallStep::source_export, SteamReturnKind::none,
            steam_export(owner, "SteamAPI_UnregisterCallback"), Abi::cdecl_call, nullptr, 1,
            static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(&member)), receipt);
        if (member.flags & 1U) throw Fatal(ERROR_INVALID_STATE, "Actual callback provider retained the member's registered flag after Unregister.");
    }
    member.live = false; owner.callbacks.erase(found);
}
