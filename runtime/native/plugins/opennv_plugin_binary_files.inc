constexpr std::uint32_t binary_begin_event = 0x450, binary_output_slice_event = 0x451,
    binary_output_ack_event = 0x452, binary_buffer_slice_event = 0x453,
    binary_buffer_ack_event = 0x454, binary_end_event = 0x455;

BinaryFileRuntime& binary_files_owner() {
    if (!state->nvse || !state->binary_files) nvse_unowned("Binary member: selected source method/class owner absent");
    return *state->binary_files;
}
BinaryFileBinding& binary_file(void* pointer) {
    auto& owner = binary_files_owner();
    for (const auto& binding : owner.bindings) if (binding->live && binding->pointer == address(pointer)) {
        auto& contributor = source_object(binding->contributor);
        const auto image = state->arena.inspect(binding->image), buffer = state->arena.inspect(binding->buffer);
        if (contributor.kind != 2 || contributor.pointer != binding->contributor_pointer ||
            nvse_copy(reinterpret_cast<const std::uint32_t*>(contributor.pointer + 0x10)) != binding->pointer ||
            address(image.base) != binding->pointer || image.length != binding->extent || image.access != GuestAccess::read_only ||
            buffer.length != binding->capacity || buffer.access != GuestAccess::read_only ||
            nvse_copy(reinterpret_cast<const std::uint32_t*>(binding->pointer)) != address(owner.table.base) ||
            nvse_copy(reinterpret_cast<const std::uint32_t*>(binding->pointer + 0x20)) != address(buffer.base) ||
            nvse_copy(reinterpret_cast<const std::uint32_t*>(binding->pointer + 0x24)) == 0)
            throw Fatal(ERROR_INVALID_DATA, "Binary source child/table/buffer/CRT publication differs from its actual retained constructor.");
        return *binding;
    }
    throw Fatal(ERROR_INVALID_HANDLE, "Original binary receiver has no actual retained source-class lifetime.");
}
void binary_request_prefix(Bytes& request, std::uint64_t caller, const BinaryFileBinding& file) {
    request.put(caller); request.put(file.id); request.put(file.pointer);
    request.put(file.contributor); request.put(file.contributor_pointer);
}
std::uint32_t binary_file_call(void* receiver, BinaryMethod method, void* destination,
    std::uint32_t first, std::uint32_t second) {
    auto& owner = binary_files_owner(); auto& file = binary_file(receiver);
    if (file.busy || !std::any_of(owner.methods.begin(), owner.methods.end(), [&](const auto& declared) { return declared.method == method; }))
        throw Fatal(ERROR_BUSY, "Binary member overlaps an unfinished output or lacks a selected source method.");
    const auto caller = source_object_caller();
    if (owner.calls == std::numeric_limits<std::uint64_t>::max()) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Binary call identity exhausted.");
    file.busy = true; ++owner.calls;
    // On any failure busy remains true. The consumed backend/native prefix may
    // retire only through terminal exact-child closure, never through replay.
    Bytes request; binary_request_prefix(request, caller, file);
    request.put(static_cast<std::uint32_t>(method)); request.put(first); request.put(second);
    auto response = expression_host(binary_begin_event, request); Reader reader{response.data};
    const auto result = reader.get<std::uint32_t>(); const auto transfer = reader.get<std::uint64_t>();
    const auto output_bytes = reader.get<std::uint32_t>(), image_bytes = reader.get<std::uint32_t>(), buffer_bytes = reader.get<std::uint32_t>();
    const auto* before = static_cast<const std::uint8_t*>(reader.bytes(image_bytes));
    const auto* after = static_cast<const std::uint8_t*>(reader.bytes(image_bytes)); reader.finish();
    const auto is_read = method == BinaryMethod::read || method == BinaryMethod::read_inherited;
    if (!transfer || image_bytes != file.extent || buffer_bytes > file.capacity ||
        (is_read ? output_bytes != result || output_bytes > first : output_bytes != 0))
        throw Fatal(ERROR_INVALID_DATA, "Binary source result changed its exact image/output/buffer extent.");
    for (std::uint32_t at = 0; at < image_bytes; ++at)
        if (before[at] != after[at] && !((at >= 4 && at < 16) || (at >= 0x14 && at < 0x20) || at == 0x2c || (at >= 0x150 && at < 0x158)))
            throw Fatal(ERROR_INVALID_DATA, "Binary member changed a class field outside its admitted buffer/cursor/procedure owner.");
    state->arena.replace_read_only(file.image, before, after, image_bytes);
    std::uint32_t position = 0;
    while (position < buffer_bytes) {
        const auto count = std::min(buffer_bytes - position, (max_payload - 64) / 2);
        Bytes next; binary_request_prefix(next, caller, file); next.put(transfer); next.put(position); next.put(count);
        auto block = expression_host(binary_buffer_slice_event, next); Reader block_reader{block.data};
        if (block_reader.get<std::uint32_t>() != count) throw Fatal(ERROR_INVALID_DATA, "Source binary buffer slice changed its actual extent.");
        const auto* old_bytes = block_reader.bytes(count); const auto* new_bytes = block_reader.bytes(count); block_reader.finish();
        state->arena.replace_read_only_range(file.buffer, position, old_bytes, new_bytes, count);
        Bytes ack; binary_request_prefix(ack, caller, file); ack.put(transfer); ack.put(position); ack.put(count);
        if (expression_scalar(binary_buffer_ack_event, ack) != count)
            throw Fatal(ERROR_INVALID_DATA, "Actual native binary buffer lacks its ordered write receipt.");
        position += count;
    }
    position = 0;
    if (output_bytes) expression_writable(destination, output_bytes);
    while (position < output_bytes) {
        const auto count = std::min(output_bytes - position, max_payload - 64);
        Bytes next; binary_request_prefix(next, caller, file); next.put(transfer); next.put(position); next.put(count);
        auto block = expression_host(binary_output_slice_event, next); Reader block_reader{block.data};
        if (block_reader.get<std::uint32_t>() != count) throw Fatal(ERROR_INVALID_DATA, "Source binary output slice changed its original actual extent.");
        const auto* bytes = block_reader.bytes(count); block_reader.finish();
        auto* target = static_cast<std::uint8_t*>(destination) + position; expression_write(target, bytes, count);
        std::vector<std::uint8_t> observed(count); SIZE_T read = 0;
        if (!ReadProcessMemory(GetCurrentProcess(), target, observed.data(), count, &read) || read != count)
            throw Fatal(ERROR_INVALID_ADDRESS, "Actual native binary destination has no complete read-back receipt.");
        Bytes ack; binary_request_prefix(ack, caller, file); ack.put(transfer); ack.put(position); ack.put(count);
        ack.data.insert(ack.data.end(), observed.begin(), observed.end());
        if (expression_scalar(binary_output_ack_event, ack) != count)
            throw Fatal(ERROR_INVALID_DATA, "Actual native binary output differs from its source-produced bytes.");
        position += count;
    }
    Bytes end; binary_request_prefix(end, caller, file); end.put(transfer);
    if (expression_scalar(binary_end_event, end) != 1) throw Fatal(ERROR_INVALID_DATA, "Binary member has no complete native/source lifetime receipt.");
    file.busy = false; ++owner.completed; return result;
}

std::uint32_t __fastcall binary_read(void* receiver, void*, void* destination, std::uint32_t count) {
    return nvse_adapter([&] { return binary_file_call(receiver, BinaryMethod::read, destination, count, 0); });
}
std::uint32_t __fastcall binary_read_inherited(void* receiver, void*, void* destination, std::uint32_t count) {
    return nvse_adapter([&] { return binary_file_call(receiver, BinaryMethod::read_inherited, destination, count, 0); });
}
void __fastcall binary_seek(void* receiver, void*, std::uint32_t argument, std::uint32_t origin) {
    nvse_adapter([&] { (void)binary_file_call(receiver, BinaryMethod::seek, nullptr, argument, origin); });
}
void __fastcall binary_seek_current(void* receiver, void*, std::int32_t delta) {
    nvse_adapter([&] { (void)binary_file_call(receiver, BinaryMethod::seek_current, nullptr, static_cast<std::uint32_t>(delta), 0); });
}
std::uint32_t __fastcall binary_size(void* receiver, void*) {
    return nvse_adapter([&] { return binary_file_call(receiver, BinaryMethod::size, nullptr, 0, 0); });
}
std::uint32_t __fastcall binary_cursor(void* receiver, void*) {
    return nvse_adapter([&] { return binary_file_call(receiver, BinaryMethod::cursor, nullptr, 0, 0); });
}
bool __fastcall binary_flush_read_buffer(void* receiver, void*) {
    return nvse_adapter([&] { return binary_file_call(receiver, BinaryMethod::flush_read_buffer, nullptr, 0, 0) != 0; });
}
void __fastcall binary_select_procedures(void* receiver, void*, bool alternate) {
    nvse_adapter([&] { (void)binary_file_call(receiver, BinaryMethod::select_procedures, nullptr, alternate ? 1U : 0U, 0); });
}
std::uint32_t __cdecl binary_read_procedure(void* receiver, void* destination, std::uint32_t count) {
    return nvse_adapter([&] { return binary_file_call(receiver, BinaryMethod::read_inherited, destination, count, 0); });
}
[[noreturn]] std::uint32_t __cdecl binary_write_procedure(void*, const void*, std::uint32_t) {
    nvse_unowned("Binary write procedure: original input has no writable source/backend owner");
}
[[noreturn]] std::uint32_t __cdecl binary_alternate_read(void*, void*, std::uint32_t, const std::uint32_t*, std::uint32_t) {
    nvse_unowned("Binary alternate read: original component/endian transform owner absent");
}
[[noreturn]] std::uint32_t __cdecl binary_alternate_write(void*, const void*, std::uint32_t, const std::uint32_t*, std::uint32_t) {
    nvse_unowned("Binary alternate write: original component/endian/writable source owner absent");
}
template<std::size_t Index> [[noreturn]] void __fastcall binary_unowned(void*, void*) {
    const auto text = std::string("Original binary virtual method ") + std::to_string(Index * 4) + " has no complete source runtime owner";
    nvse_unowned(text.c_str());
}
template<std::size_t... Index> std::array<void*, sizeof...(Index)> binary_unowned_table(std::index_sequence<Index...>) {
    return {reinterpret_cast<void*>(&binary_unowned<Index>)...};
}
void* binary_method_entry(BinaryMethod method) {
    switch (method) {
    case BinaryMethod::read: return reinterpret_cast<void*>(&binary_read);
    case BinaryMethod::read_inherited: return reinterpret_cast<void*>(&binary_read_inherited);
    case BinaryMethod::seek: return reinterpret_cast<void*>(&binary_seek);
    case BinaryMethod::seek_current: return reinterpret_cast<void*>(&binary_seek_current);
    case BinaryMethod::size: return reinterpret_cast<void*>(&binary_size);
    case BinaryMethod::cursor: return reinterpret_cast<void*>(&binary_cursor);
    case BinaryMethod::flush_read_buffer: return reinterpret_cast<void*>(&binary_flush_read_buffer);
    case BinaryMethod::select_procedures: return reinterpret_cast<void*>(&binary_select_procedures);
    default: throw Fatal(ERROR_NOT_SUPPORTED, "Binary source method has no first-party callable ABI.");
    }
}

void binary_methods_configure(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>();
    const auto extent = reader.get<std::uint32_t>(), slots = reader.get<std::uint32_t>(), count = reader.get<std::uint32_t>();
    if (!state->nvse || state->binary_files || module != state->module_id || !state->calls.empty() ||
        (state->nvse->phase != NvsePhase::mapped && state->nvse->phase != NvsePhase::queried_true) ||
        extent < 0x158 || extent > max_payload / 4 || !slots || slots > 128 || !count || count > 128)
        throw Fatal(ERROR_INVALID_STATE, "Binary source methods require an exact idle original module before entry.");
    auto owner = std::make_unique<BinaryFileRuntime>(); owner->extent = extent; owner->slots = slots;
    auto table = binary_unowned_table(std::make_index_sequence<128>{}); std::vector<CallableEntry> calls;
    std::array<bool, 128> declared_slots{};
    for (std::uint32_t index = 0; index < count; ++index) {
        const BinaryMethodEntry method{reader.get<std::uint32_t>(), static_cast<BinaryMethod>(reader.get<std::uint32_t>()),
            static_cast<Abi>(reader.get<std::uint32_t>()), reader.get<std::uint32_t>()};
        if (method.abi != Abi::thiscall_call || (method.slot != std::numeric_limits<std::uint32_t>::max() &&
            (method.slot >= slots || declared_slots[method.slot])))
            throw Fatal(ERROR_INVALID_DATA, "Binary source virtual/member convention or slot is competing/unowned.");
        auto* entry = binary_method_entry(method.method); calls.push_back({method.address, entry}); owner->methods.push_back(method);
        if (method.slot != std::numeric_limits<std::uint32_t>::max()) {
            table[method.slot] = reinterpret_cast<void*>(method.address); declared_slots[method.slot] = true;
        }
    }
    owner->read = reader.get<std::uint32_t>(); owner->write = reader.get<std::uint32_t>();
    owner->alternate_read = reader.get<std::uint32_t>(); owner->alternate_write = reader.get<std::uint32_t>(); reader.finish();
    calls.push_back({owner->read, reinterpret_cast<void*>(&binary_read_procedure)});
    calls.push_back({owner->write, reinterpret_cast<void*>(&binary_write_procedure)});
    calls.push_back({owner->alternate_read, reinterpret_cast<void*>(&binary_alternate_read)});
    calls.push_back({owner->alternate_write, reinterpret_cast<void*>(&binary_alternate_write)});
    state->binary_files = std::move(owner); auto& actual = *state->binary_files;
    actual.callable_lease = source_callables_acquire(calls);
    actual.table = actual.tables.allocate(slots * 4, GuestAccess::read_only, table.data(), slots * 4);
    Bytes receipt; receipt.put(count); receipt.put(address(actual.table.base)); receipt.put(actual.read); receipt.put(actual.write);
    receipt.put(actual.alternate_read); receipt.put(actual.alternate_write); reply(frame, ERROR_SUCCESS, "", receipt);
}
void binary_file_bind(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); const auto id = reader.get<std::uint64_t>();
    auto binding = std::make_unique<BinaryFileBinding>(); binding->id = id;
    binding->contributor = reader.get<std::uint64_t>(); binding->contributor_pointer = reader.get<std::uint32_t>();
    binding->image = reader.get<std::uint64_t>(); binding->pointer = reader.get<std::uint32_t>(); binding->extent = reader.get<std::uint32_t>();
    binding->buffer = reader.get<std::uint64_t>(); const auto buffer_pointer = reader.get<std::uint32_t>(); binding->capacity = reader.get<std::uint32_t>(); reader.finish();
    auto& owner = binary_files_owner(); auto& contributor = source_object(binding->contributor);
    const auto image = state->arena.inspect(binding->image), buffer = state->arena.inspect(binding->buffer);
    if (module != state->module_id || !id || !state->calls.empty() || contributor.kind != 2 || contributor.pointer != binding->contributor_pointer ||
        image.length != owner.extent || binding->extent != owner.extent || image.access != GuestAccess::read_only || address(image.base) != binding->pointer ||
        buffer.access != GuestAccess::read_only || buffer.length != binding->capacity || address(buffer.base) != buffer_pointer ||
        std::any_of(owner.bindings.begin(), owner.bindings.end(), [&](const auto& old) {
            return old->id == id || old->pointer == binding->pointer || old->image == binding->image || old->buffer == binding->buffer || old->contributor == binding->contributor;
        })) throw Fatal(ERROR_INVALID_DATA, "Binary native child/class/source/buffer identity is incomplete or competing.");
    owner.bindings.push_back(std::move(binding)); (void)binary_file(reinterpret_cast<void*>(owner.bindings.back()->pointer));
    Bytes receipt; receipt.put(id); receipt.put(owner.bindings.back()->pointer); reply(frame, ERROR_SUCCESS, "", receipt);
}
void binary_file_retire(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(), id = reader.get<std::uint64_t>();
    const auto pointer = reader.get<std::uint32_t>(); reader.finish();
    auto& file = binary_file(reinterpret_cast<void*>(pointer));
    if (module != state->module_id || file.id != id || !state->calls.empty() || file.busy)
        throw Fatal(ERROR_BUSY, "Binary native receiver retirement overlaps its actual call/output lifetime.");
    file.live = false; Bytes receipt; receipt.put(id); receipt.put(0U); reply(frame, ERROR_SUCCESS, "", receipt);
}
void binary_files_retire_all() {
    if (!state->binary_files) return; auto& owner = *state->binary_files;
    if (!state->calls.empty() || std::any_of(owner.bindings.begin(), owner.bindings.end(), [](const auto& file) { return file->live || file->busy; }))
        throw Fatal(ERROR_BUSY, "Original binary classes still retain a genuine call/source/CRT constructor owner.");
    if (owner.table.id) { owner.tables.release(owner.table.id); owner.table.id = 0; }
    owner.tables.retire();
    if (owner.callable_lease) { source_callables_release(owner.callable_lease); owner.callable_lease = 0; }
    state->binary_files.reset();
}
