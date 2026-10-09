constexpr std::uint32_t crt_support_callback = 24;

PluginCrtSupportRuntime& crt_support_owner() {
    auto& owner = io_owner();
    if (!owner.crt_support) owner.crt_support = std::make_unique<PluginCrtSupportRuntime>();
    return *owner.crt_support;
}
void crt_owned_callback(std::uint32_t operation, const Bytes& payload) {
    auto& owner = crt_support_owner();
    // One ordered queue contains ordinary FILE, alias and environment receipts.
    // Detach cannot reverse close/lock/buffer order by using separate queues.
    if (io_owner().loader_retiring) { owner.pending.push_back({operation, payload.data}); return; }
    auto response = io_host(operation, payload); Reader receipt{response.data};
    if (receipt.get<std::uint32_t>() != 1) throw Fatal(ERROR_INVALID_DATA, "Actual CRT result has no complete correlated parent receipt.");
    receipt.finish();
}
void crt_defer_heap_receipt(const NvseHeapEvent& event) {
    if (!io_owner().loader_retiring || event.caller != 0)
        throw Fatal(ERROR_INVALID_STATE, "Deferred heap result lost its actual original-loader/caller lifetime.");
    Bytes request; request.put(event.capability); request.put(event.pointer); request.put(event.length);
    request.put(event.operation); request.put(event.abi); request.put(event.receiver);
    crt_support_owner().pending.push_back({value_heap_event, request.data, true});
}
CrtSupportState& crt_support_state(void* pointer) {
    const auto stream = crt_stream(pointer);
    auto& allocation = crt_support_owner().streams[address(pointer)];
    if (!allocation) allocation = std::make_shared<CrtSupportState>();
    auto& entry = *allocation;
    if (!entry.stream) { entry.provider = stream.provider; entry.route = stream.route; entry.stream = pointer; entry.thread = GetCurrentThreadId(); }
    if (entry.provider != stream.provider || entry.route != stream.route || entry.stream != pointer || entry.thread != GetCurrentThreadId())
        throw Fatal(ERROR_INVALID_HANDLE, "CRT support state has a foreign FILE/provider/thread lifetime.");
    return entry;
}
class CrtSupportCall {
    std::shared_ptr<CrtSupportState> retained_;
public:
    explicit CrtSupportCall(void* pointer, bool closing = false) {
        auto& value = crt_support_state(pointer);
        if (value.active_calls) throw Fatal(ERROR_BUSY, "Reentrant original code cannot reuse a FILE while its actual SDK/result call is entered.");
        if (closing && value.lock_depth) throw Fatal(ERROR_BUSY, "A FILE cannot close while its explicit caller lock remains owned.");
        retained_ = crt_support_owner().streams.at(address(pointer)); ++value.active_calls;
    }
    CrtSupportCall(const CrtSupportCall&) = delete;
    CrtSupportCall& operator=(const CrtSupportCall&) = delete;
    ~CrtSupportCall() { --retained_->active_calls; }
};
CrtBufferLease crt_support_buffer(char* pointer, std::size_t length) {
    if (!pointer || !length) return {};
    if (length > std::numeric_limits<std::uint32_t>::max()) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "CRT borrowed-buffer extent overflows x86.");
    expression_writable(pointer, static_cast<std::uint32_t>(length));
    if (state->value_heap) {
        auto& heap = values_heap_owner();
        for (const auto& value : heap.allocations) {
            const auto start = address(value.second.region.base); const auto offset = static_cast<std::uint64_t>(address(pointer)) - start;
            if (address(pointer) >= start && offset <= value.second.region.length && length <= value.second.region.length - offset) {
                heap.shared_heap.inspect(value.second.region.id);
                return {pointer, static_cast<std::uint32_t>(length), CrtBufferOwner::shared_heap, value.second.region.id};
            }
        }
    }
    MEMORY_BASIC_INFORMATION observed{};
    if (state->module && VirtualQuery(pointer, &observed, sizeof(observed)) && observed.Type == MEM_IMAGE &&
        observed.AllocationBase == state->module) {
        const auto* base = reinterpret_cast<const std::uint8_t*>(state->module);
        const auto dos = nvse_copy(reinterpret_cast<const IMAGE_DOS_HEADER*>(base));
        if (dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew <= 0) throw Fatal(ERROR_BAD_EXE_FORMAT, "CRT caller module header changed.");
        const auto image = nvse_copy(reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos.e_lfanew));
        const auto relative = static_cast<std::uint64_t>(address(pointer)) - address(state->module);
        if (image.Signature != IMAGE_NT_SIGNATURE || image.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC ||
            relative > image.OptionalHeader.SizeOfImage || length > image.OptionalHeader.SizeOfImage - relative)
            throw Fatal(ERROR_INVALID_ADDRESS, "CRT module buffer exceeds the genuine retained original image.");
        auto* end = pointer + length - 1; MEMORY_BASIC_INFORMATION tail{};
        if (!VirtualQuery(end, &tail, sizeof(tail)) || tail.Type != MEM_IMAGE || tail.AllocationBase != state->module)
            throw Fatal(ERROR_INVALID_ADDRESS, "CRT module buffer crosses another native owner.");
        return {pointer, static_cast<std::uint32_t>(length), CrtBufferOwner::original_module, 0};
    }
    throw Fatal(ERROR_NOT_SUPPORTED, "Nonnull CRT buffer has no retained original image or authoritative shared-heap allocation lifetime.");
}
void crt_support_require_releasable(std::uint64_t allocation) {
    if (!state->io || !state->io->crt_support) return;
    for (const auto& row : state->io->crt_support->streams)
        if (row.second->buffer.owner == CrtBufferOwner::shared_heap && row.second->buffer.allocation == allocation)
            throw Fatal(ERROR_BUSY, "A genuine UCRT FILE still borrows this native allocation.");
}
void crt_support_validate(void* pointer, bool closing = false) {
    if (!state->io || !state->io->crt_support) return;
    const auto found = state->io->crt_support->streams.find(address(pointer));
    if (found == state->io->crt_support->streams.end()) return;
    auto& value = *found->second;
    if (value.thread != GetCurrentThreadId() || value.stream != pointer)
        throw Fatal(ERROR_INVALID_THREAD_ID, "CRT buffer/lock operation changed its actual calling thread.");
    if (closing && value.lock_depth) throw Fatal(ERROR_BUSY, "A FILE cannot retire while its explicit caller lock remains owned.");
    if (value.buffer.buffer) {
        const auto current = crt_support_buffer(static_cast<char*>(value.buffer.buffer), value.buffer.length);
        if (current.owner != value.buffer.owner || current.allocation != value.buffer.allocation)
            throw Fatal(ERROR_INVALID_DATA, "Retained CRT caller buffer changed allocation identity.");
    }
    if (value.base_cell) expression_writable(value.base_cell, sizeof(char*));
    if (value.pointer_cell) expression_writable(value.pointer_cell, sizeof(char*));
    if (value.count_cell) expression_writable(value.count_cell, sizeof(int));
}
void crt_support_report(const CrtSupportState& value, CrtSupportKind kind, std::int64_t result, DWORD last,
    bool position_available = false, std::uint64_t position = 0) {
    Bytes request; request.put(value.provider); request.put(value.route); request.put(address(value.stream));
    request.put(static_cast<std::uint32_t>(kind)); request.put(result); request.put(last);
    request.put(address(value.buffer.buffer)); request.put(value.buffer.length); request.put(static_cast<std::uint32_t>(value.buffer.owner)); request.put(value.buffer.allocation);
    request.put(address(value.base_cell)); request.put(address(value.pointer_cell)); request.put(address(value.count_cell));
    request.put(value.base_cell ? address(nvse_copy(value.base_cell)) : 0U);
    request.put(value.pointer_cell ? address(nvse_copy(value.pointer_cell)) : 0U);
    request.put(value.count_cell ? nvse_copy(value.count_cell) : 0);
    request.put(value.lock_depth); request.put(position_available ? 1U : 0U); request.put(position);
    if (kind == CrtSupportKind::close) crt_owned_callback(crt_support_callback, request);
    else { CrtSupportCall entered(value.stream); crt_owned_callback(crt_support_callback, request); }
}
void crt_support_closed(void* pointer, int result, DWORD last) {
    if (!io_owner().crt_support) return;
    auto& entries = io_owner().crt_support->streams; const auto found = entries.find(address(pointer));
    if (found == entries.end()) return;
    auto retired = *found->second; retired.buffer = {}; retired.base_cell = nullptr; retired.pointer_cell = nullptr; retired.count_cell = nullptr;
    // FILE is already invalid, including fclose's flush-failure result. Never
    // read an exposed cell after close or retry the pointer on a callback fault.
    entries.erase(found); crt_support_report(retired, CrtSupportKind::close, result, last);
}
void crt_support_report_preserving_error(const CrtSupportState& value, CrtSupportKind kind, std::int64_t result, DWORD last,
    bool position_available = false, std::uint64_t position = 0) {
    auto& provider = crt_provider(value.provider); const auto status = crt_status(provider);
    crt_support_report(value, kind, result, last, position_available, position);
    // A correlated callback may enter another genuine FILE on this thread.
    // Preserve this SDK call's errno/doserrno as well as its Windows last error.
    crt_restore_error(provider, status); SetLastError(last);
}
void crt_support_flush_detach() {
    if (!io_owner().crt_support) return; auto& pending = io_owner().crt_support->pending;
    while (!pending.empty()) {
        const auto entry = pending.front(); Bytes payload; payload.data = entry.payload;
        if (entry.heap) {
            if (entry.operation != value_heap_event || values_caller_id() != 0 || values_scalar(value_heap_event, payload) != 1)
                throw Fatal(ERROR_INVALID_DATA, "Deferred heap result lacks its actual correlated zero-caller lifetime receipt.");
        }
        else crt_owned_callback(entry.operation, payload);
        pending.erase(pending.begin());
    }
}
void crt_support_require_retired() {
    if (io_owner().crt_support && (!io_owner().crt_support->streams.empty() || !io_owner().crt_support->pending.empty()))
        throw Fatal(ERROR_BUSY, "Actual UCRT caller buffer/alias/lock/result owners remain at retirement.");
}
