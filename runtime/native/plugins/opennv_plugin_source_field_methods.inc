// Include after source object/global/CALL owners. All emitted code is the
// shared first-party jump producer and authored ABI thunks, never retail code.
std::unique_ptr<SourceFieldMethods> source_field_methods;

SourceFieldMethod* source_field_method_find(std::uint32_t entry) {
    if (!source_field_methods) return nullptr;
    for (auto& method : source_field_methods->methods) if (method->entry == entry) return method.get();
    return nullptr;
}
void source_field_method_require(const SourceFieldMethod& method, bool retired = false) {
    if (!state || GetCurrentThreadId() != state->thread || method.generation != state->generation ||
        method.module != state->module_id || !method.published || method.retired != retired || method.failed)
        throw Fatal(ERROR_INVALID_STATE, "Source field callable lost its actual generation/thread/publication.");
    MEMORY_BASIC_INFORMATION memory{};
    if (!original_callable_pages) throw Fatal(ERROR_INVALID_STATE, "Source field callable lost its source reservation owner.");
    const auto image = original_callable_pages->source_image_identity();
    if (!VirtualQuery(reinterpret_cast<void*>(method.entry), &memory, sizeof(memory)) ||
        memory.AllocationBase != reinterpret_cast<void*>(image[0]) ||
        memory.State != MEM_COMMIT || memory.Type != MEM_PRIVATE || memory.Protect != PAGE_EXECUTE_READ ||
        static_cast<std::uint64_t>(method.entry) + 7 > reinterpret_cast<std::uintptr_t>(memory.BaseAddress) + memory.RegionSize)
        throw Fatal(ERROR_INVALID_DATA, "Source field callable lost its shared first-party executable extent.");
    std::array<std::uint8_t,7> bytes{}; SIZE_T copied = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(method.entry), bytes.data(), bytes.size(), &copied) || copied != bytes.size())
        throw Fatal(GetLastError(), "Source field callable lacks its complete native byte readback.");
    if (retired) {
        for (const auto value : bytes) if (value != 0xcc) throw Fatal(ERROR_INVALID_DATA, "Retired field method lacks its actual trap bytes.");
        return;
    }
    std::uint32_t target = 0; std::memcpy(&target,bytes.data()+1,4);
    if (bytes[0] != 0xb8 || bytes[5] != 0xff || bytes[6] != 0xe0 || target != method.target ||
        owned_thunk(reinterpret_cast<void*>(target)) != target)
        throw Fatal(ERROR_INVALID_DATA, "Source field callable changed its actual first-party code target.");
}
std::uint32_t source_field_method_invoke(std::size_t slot,void* receiver,std::uint32_t abi) {
    return nvse_adapter([&] {
        nvse_owner();
        if (!source_field_methods || source_field_methods->failed || slot >= source_field_methods->methods.size())
            throw Fatal(ERROR_INVALID_HANDLE, "Source field method has no retained authored ABI slot.");
        auto& method = *source_field_methods->methods[slot]; source_field_method_require(method);
        if (method.busy || method.abi != abi) throw Fatal(ERROR_BUSY, "Source field callable reentered or changed its real ABI.");
        std::uint64_t object_id = 0;
        if (abi == 3) {
            auto& object = source_object_pointer(receiver);
            if (object.kind != method.receiver_kind) throw Fatal(ERROR_INVALID_DATA, "Source method has a different actual receiver class.");
            object_id = object.id;
        }
        else if (abi != 1 || receiver || method.receiver_kind) throw Fatal(ERROR_INVALID_DATA, "Source cdecl method acquired an unowned implicit receiver.");
        method.busy = true;
        try {
            Bytes request; request.put(method.id); request.put(method.entry); request.put(object_id); request.put(address(receiver));
            const auto result = nvse_host(NvseHostCall::source_field_method_execute,request);
            if (method.callbacks == std::numeric_limits<std::uint32_t>::max())
                throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Source method callback counter exhausted after its actual return.");
            ++method.callbacks; source_field_method_require(method);
            if (object_id) source_object(object_id);
            method.busy = false; return result;
        }
        catch (...) { method.failed = source_field_methods->failed = true; throw; }
    });
}
using SourceFieldCdecl = std::uint32_t(__cdecl*)();
using SourceFieldThiscall = std::uint32_t(__fastcall*)(void*,void*);
template<std::size_t Slot> std::uint32_t __cdecl source_field_cdecl_entry() {
    return source_field_method_invoke(Slot,nullptr,1);
}
template<std::size_t Slot> std::uint32_t __fastcall source_field_thiscall_entry(void* receiver,void*) {
    return source_field_method_invoke(Slot,receiver,3);
}
template<std::size_t... Slots> constexpr std::array<SourceFieldCdecl,sizeof...(Slots)> source_field_cdecl_entries(std::index_sequence<Slots...>) {
    return {&source_field_cdecl_entry<Slots>...};
}
template<std::size_t... Slots> constexpr std::array<SourceFieldThiscall,sizeof...(Slots)> source_field_thiscall_entries(std::index_sequence<Slots...>) {
    return {&source_field_thiscall_entry<Slots>...};
}
const auto source_field_cdecl_functions = source_field_cdecl_entries(std::make_index_sequence<source_field_method_limit>{});
const auto source_field_thiscall_functions = source_field_thiscall_entries(std::make_index_sequence<source_field_method_limit>{});

SourceFieldMethod& source_field_method_prepare(std::uint32_t entry,std::uint32_t abi,std::uint32_t receiver_kind) {
    nvse_owner();
    if (state->calls.size() != 1 || state->nvse->invocation_depth != 1 || state->nvse->phase != NvsePhase::loaded_true ||
        (abi == 1 ? receiver_kind != 0 : abi != 3 || receiver_kind < 1 || receiver_kind > 3))
        throw Fatal(ERROR_INVALID_STATE, "Source field method requires its loaded outer typed caller and actual zero-argument ABI.");
    if (source_field_methods && (source_field_methods->failed ||
        std::any_of(source_field_methods->methods.begin(),source_field_methods->methods.end(),[](const auto& method){return method->busy;})))
        throw Fatal(ERROR_BUSY, "Source field declaration overlaps an entered/failed actual call.");
    Bytes declaration; declaration.put(entry); declaration.put(abi); declaration.put(receiver_kind);
    const auto id = nvse_host(NvseHostCall::source_field_method_declare,declaration);
    if (!id) throw Fatal(ERROR_NOT_SUPPORTED, "Source field method lacks an admitted C# semantic producer.");
    if (auto* existing = source_field_method_find(entry)) {
        if (existing->id != id || existing->abi != abi || existing->receiver_kind != receiver_kind || existing->retired)
            throw Fatal(ERROR_INVALID_DATA, "Source field declaration replayed a differently typed or retired entry.");
        source_field_method_require(*existing); return *existing;
    }
    if (!source_field_methods) source_field_methods = std::make_unique<SourceFieldMethods>();
    if (source_field_methods->methods.size() >= source_field_method_limit)
        throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Source field callable lifetime quota is exhausted.");
    auto method = std::make_unique<SourceFieldMethod>(); method->id = id; method->entry = entry; method->abi = abi;
    method->receiver_kind = receiver_kind; method->generation = state->generation; method->module = state->module_id;
    method->slot = static_cast<std::uint32_t>(source_field_methods->methods.size());
    method->target = abi == 1 ? owned_thunk(reinterpret_cast<void*>(source_field_cdecl_functions[method->slot])) :
        owned_thunk(reinterpret_cast<void*>(source_field_thiscall_functions[method->slot]));
    source_field_methods->methods.push_back(std::move(method)); auto& actual = *source_field_methods->methods.back();
    try {
        if (!original_callable_pages) throw Fatal(ERROR_INVALID_STATE, "Source field callable has no actual pre-entry source image reservation.");
        actual.lease = original_callable_pages->acquire({{entry,reinterpret_cast<void*>(actual.target)}});
        actual.published = true; source_field_method_require(actual);
        Bytes publication; publication.put(actual.id); publication.put(actual.entry); publication.put(actual.lease); publication.put(actual.target);
        if (nvse_host(NvseHostCall::source_field_method_publish,publication) != 1)
            throw Fatal(ERROR_INVALID_DATA, "Source field callable lacks its genuine parent native readback.");
        return actual;
    }
    catch (const CallablePageFailure& failure) { actual.failed = source_field_methods->failed = true; throw Fatal(failure.code,failure.what()); }
    catch (...) { actual.failed = source_field_methods->failed = true; throw; }
}
void source_field_methods_operation(const Frame& frame) {
    Reader request{frame.payload}; const auto module = request.get<std::uint64_t>(); const auto action = request.get<std::uint32_t>();
    if (!state->nvse || module != state->module_id || state->nvse->phase != NvsePhase::loaded_true || !state->calls.empty())
        throw Fatal(ERROR_INVALID_STATE, "Source field operation lost its idle genuinely loaded native module.");
    state->calls.push_back(frame.header.id); state->nvse->interface_calls = 0; bool call_owned = true;
    try {
        NvseInvocation invocation(*state->nvse); Bytes receipt;
        if (action == 0) {
            const auto entry = request.get<std::uint32_t>(), abi = request.get<std::uint32_t>(), kind = request.get<std::uint32_t>(); request.finish();
            const auto& method = source_field_method_prepare(entry,abi,kind); receipt.put(method.id); receipt.put(method.entry);
        }
        else if (action == 1) {
            const auto id = request.get<std::uint32_t>(), entry = request.get<std::uint32_t>();
            const auto object_id = request.get<std::uint64_t>(); const auto receiver = request.get<std::uint32_t>(); request.finish();
            auto* method = source_field_method_find(entry);
            if (!method || method->id != id || method->busy || method->failed || source_field_methods->failed)
                throw Fatal(ERROR_INVALID_HANDLE, "Source field call lacks its exact retained callable.");
            source_field_method_require(*method);
            if (method->abi == 3 ? source_object(object_id).pointer != receiver : object_id != 0 || receiver != 0)
                throw Fatal(ERROR_INVALID_HANDLE, "Source field caller lost its genuine native object or cdecl identity.");
            const auto before = method->callbacks; CallReceipt actual{};
            InvokeSourceCallSiteGuarded(reinterpret_cast<void*>(entry),reinterpret_cast<void*>(receiver),&actual);
            try {
                nvse_check_call(actual,"source field method",entry); source_field_method_require(*method);
                if (method->callbacks - before != 1) throw Fatal(ERROR_INVALID_DATA, "Source field native callback cardinality changed.");
                if (object_id) source_object(object_id);
            }
            catch (...) { method->failed = source_field_methods->failed = true; throw; }
            receipt.put(frame.header.id); receipt.put(actual.result); receipt.put(actual.stack_delta); receipt.put(actual.preserved_registers);
            receipt.put(method->callbacks - before);
        }
        else throw Fatal(ERROR_NOT_SUPPORTED, "Unknown source field method operation.");
        state->calls.pop_back(); call_owned = false; reply(frame,ERROR_SUCCESS,"",receipt);
    }
    catch (...) { if (call_owned) state->calls.pop_back(); throw; }
}
void source_field_methods_retire(const Frame& frame) {
    if (!source_field_methods) return;
    if (!state->calls.empty() || source_field_methods->failed ||
        std::any_of(source_field_methods->methods.begin(),source_field_methods->methods.end(),[](const auto& value){return value->busy || value->failed;}))
        throw Fatal(ERROR_BUSY, "Source field retirement retains an entered/failed native invocation.");
    for (auto& method : source_field_methods->methods) {
        if (method->retired) continue;
        try {
            source_field_method_require(*method); original_callable_pages->release(method->lease);
            method->retired = true; source_field_method_require(*method,true);
            state->calls.push_back(frame.header.id);
            try {
                Bytes receipt; receipt.put(method->id); receipt.put(method->entry); receipt.put(method->lease);
                if (nvse_host(NvseHostCall::source_field_method_retire,receipt) != 1)
                    throw Fatal(ERROR_INVALID_DATA, "Source field trap retirement lacks its actual parent readback.");
                state->calls.pop_back();
            }
            catch (...) { state->calls.pop_back(); throw; }
        }
        catch (const CallablePageFailure& failure) { method->failed = source_field_methods->failed = true; throw Fatal(failure.code,failure.what()); }
        catch (...) { method->failed = source_field_methods->failed = true; throw; }
    }
}
