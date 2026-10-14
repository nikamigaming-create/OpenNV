// A selected model-path PUSH owns only its pointer input. Its native module
// CString borrow is data, not an executable function or a menu receiver.
struct SourceModelOperand {
    std::uint64_t module = 0, generation = 0, lease = 0;
    std::uint32_t instruction = 0, initial = 0, pointer = 0, load = 0, first_return = 0, second_return = 0;
    std::uint32_t capacity = 0, output = 0, old_output = 0, stage = 0;
    bool failed = false, data_borrow = false, first_sdk_returned = false;
    std::vector<std::uint8_t> initial_buffer;
};
std::unique_ptr<SourceModelOperand> source_model_operand;

std::array<std::uint8_t, 5> source_model_instruction(SourceModelOperand& site) {
    if (!original_callable_pages || site.generation != state->generation || !site.lease)
        throw Fatal(ERROR_INVALID_STATE, "Model input lost its selected-image generation/lease.");
    (void)original_callable_pages->source_code_protection(site.instruction, 5);
    std::array<std::uint8_t, 5> result{}; SIZE_T read = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(site.instruction), result.data(), result.size(), &read) || read != result.size())
        throw Fatal(GetLastError(), "Model input has no complete native instruction readback.");
    return result;
}
void source_model_buffer(SourceModelOperand& site) {
    auto& module = nvse_module_record(site.module);
    if (!site.data_borrow || !module.borrows || site.pointer < address(module.image))
        throw Fatal(ERROR_INVALID_HANDLE, "Model input lost its retained original-module data borrow.");
    for (std::uint64_t cursor = site.pointer; cursor < static_cast<std::uint64_t>(site.pointer) + site.capacity;) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!VirtualQuery(reinterpret_cast<void*>(static_cast<std::uintptr_t>(cursor)), &memory, sizeof(memory)) ||
            memory.AllocationBase != module.image || memory.Type != MEM_IMAGE || memory.State != MEM_COMMIT ||
            (memory.Protect != PAGE_READONLY && memory.Protect != PAGE_READWRITE && memory.Protect != PAGE_WRITECOPY))
            throw Fatal(ERROR_INVALID_DATA, "Model CString changed its original module/data-page identity.");
        const auto next = static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(memory.BaseAddress)) + memory.RegionSize;
        if (next <= cursor) throw Fatal(ERROR_INVALID_DATA, "Model CString region made no progress.");
        cursor = std::min(next, static_cast<std::uint64_t>(site.pointer) + site.capacity);
    }
    std::vector<std::uint8_t> observed(site.capacity); SIZE_T read = 0;
    if (!ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(site.pointer), observed.data(), observed.size(), &read) || read != observed.size() ||
        observed != site.initial_buffer)
        throw Fatal(ERROR_NOT_SUPPORTED, "The actual model buffer has an unowned source-copy writer.");
}
void source_model_receipt(SourceModelOperand& site, std::uint32_t action, const void* caller, const void* pointer,
    SIZE_T extent, DWORD access, PDWORD output_cell, DWORD before, DWORD after, BOOL result, DWORD last) {
    Bytes receipt; receipt.put(action); receipt.put(address(caller)); receipt.put(address(pointer)); receipt.put(static_cast<std::uint32_t>(extent));
    receipt.put(access); receipt.put(address(output_cell)); receipt.put(before); receipt.put(after); receipt.put(static_cast<std::uint32_t>(result)); receipt.put(last);
    receipt.put(original_callable_pages->source_code_protection(site.instruction, 5));
    const auto range = result && (action == 1 || action == 3) ? original_callable_pages->source_code_range(address(pointer), 4, false) : std::array<std::uint32_t, 2>{0, 0};
    receipt.put(range[0]); receipt.put(range[1]);
    if (nvse_host(NvseHostCall::source_model_operand, receipt) != 1)
        throw Fatal(ERROR_INVALID_DATA, "Model input lacks its actual C# SDK/store-prefix receipt.");
}
bool source_model_operand_request(const void* pointer, SIZE_T bytes) {
    if (!source_model_operand || !bytes) return false;
    const auto start = static_cast<std::uint64_t>(address(pointer)); const auto end = start + bytes;
    const auto instruction = static_cast<std::uint64_t>(source_model_operand->instruction);
    return start < instruction + 5 && instruction < end;
}
BOOL source_model_operand_protect(LPVOID pointer, SIZE_T bytes, DWORD access, PDWORD old_access,
    const void* caller, DWORD incoming) {
    auto& owner = nvse_owner(); auto& site = *source_model_operand; auto& module = nvse_module_record(site.module);
    const auto tib = reinterpret_cast<const NT_TIB*>(NtCurrentTeb()); const auto output_address = static_cast<std::uint64_t>(address(old_access));
    if (site.failed || site.stage > 1 || site.module != state->module_id || site.generation != state->generation ||
        module.image != state->module || owner.phase != NvsePhase::loading || owner.invocation_depth != 1 || state->calls.size() != 1 ||
        address(pointer) != site.instruction + 1 || bytes != 4 || !old_access ||
        output_address < reinterpret_cast<std::uintptr_t>(tib->StackLimit) || output_address + sizeof(DWORD) > reinterpret_cast<std::uintptr_t>(tib->StackBase))
        throw Fatal(ERROR_NOT_SUPPORTED, "Model protection has no exact current Load/helper/operand/stack-output owner.");
    try {
        source_model_buffer(site); const auto before = nvse_copy(old_access);
        const auto first = site.stage == 0;
        if (address(caller) != address(module.image) + (first ? site.first_return : site.second_return) ||
            (first ? access != PAGE_EXECUTE_READWRITE : access != site.old_output || address(old_access) != site.output || before != site.old_output))
            throw Fatal(ERROR_NOT_SUPPORTED, "Model protection changed its selected source caller or original output cell.");
        const auto instruction = source_model_instruction(site); std::uint32_t word = 0; std::memcpy(&word, instruction.data() + 1, 4);
        if (instruction[0] != 0x68 || word != (first ? site.initial : site.pointer))
            throw Fatal(ERROR_INVALID_DATA, "The actual model DWORD store lost its complete source PUSH readback.");
        if (first) {
            site.output = address(old_access);
            source_model_receipt(site, 0, caller, pointer, bytes, access, old_access, before, before, FALSE, incoming);
        } else source_model_receipt(site, 2, caller, pointer, bytes, access, old_access, before, before, FALSE, incoming);
        SetLastError(incoming); const auto result = VirtualProtect(pointer, bytes, access, old_access); const auto last = GetLastError();
        const auto after = nvse_copy(old_access);
        if (result) original_callable_pages->source_code_protection_changed(address(pointer), 4, after, access);
        source_model_receipt(site, first ? 1U : 3U, caller, pointer, bytes, access, old_access, before, after, result, last);
        if (first) { site.old_output = after; site.stage = 1; site.first_sdk_returned = result != 0; }
        else {
            site.stage = 2;
            if (!result || !site.first_sdk_returned) { site.failed = true; retain_callback_fault(last ? last : ERROR_INVALID_STATE, "Original model protection retained a failed SDK result after its stored pointer."); }
        }
        SetLastError(last); return result;
    }
    catch (const CallablePageFailure& failure) { site.failed = true; throw Fatal(failure.code, failure.what()); }
    catch (...) { site.failed = true; throw; }
}
void source_model_operand_bind(const Frame& frame, Reader& reader) {
    const auto module_id = reader.get<std::uint64_t>(); auto& module = nvse_module_record(module_id);
    if (source_model_operand || !module.supplemental || module.runtime->phase != NvsePhase::queried_true || module.calls || module.borrows == UINT32_MAX)
        throw Fatal(ERROR_INVALID_STATE, "Model input publication needs one returned, idle original source module.");
    auto site = std::make_unique<SourceModelOperand>(); site->module = module_id; site->generation = state->generation;
    site->instruction = reader.get<std::uint32_t>(); site->initial = reader.get<std::uint32_t>(); site->load = reader.get<std::uint32_t>();
    site->first_return = reader.get<std::uint32_t>(); site->second_return = reader.get<std::uint32_t>();
    const auto buffer = reader.get<std::uint32_t>(); site->capacity = reader.get<std::uint32_t>();
    if (!site->initial || !site->capacity || site->capacity > max_payload - 256 || reader.data.size() - reader.at != site->capacity)
        throw Fatal(ERROR_INVALID_DATA, "Model buffer declaration lost its exact bounded source extent.");
    site->initial_buffer.assign(reader.data.begin() + static_cast<std::ptrdiff_t>(reader.at), reader.data.end()); reader.at = reader.data.size(); reader.finish();
    NvseExecutionScope scope(module_id);
    const auto* base = reinterpret_cast<const std::uint8_t*>(module.image);
    const auto dos = nvse_copy(reinterpret_cast<const IMAGE_DOS_HEADER*>(base));
    const auto nt = nvse_copy(reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos.e_lfanew));
    if (dos.e_magic != IMAGE_DOS_SIGNATURE || nt.Signature != IMAGE_NT_SIGNATURE || nt.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC ||
        buffer >= nt.OptionalHeader.SizeOfImage || site->capacity > nt.OptionalHeader.SizeOfImage - buffer ||
        site->load >= nt.OptionalHeader.SizeOfImage || address(base + site->load) != module.runtime->load ||
        site->first_return >= nt.OptionalHeader.SizeOfImage || site->second_return >= nt.OptionalHeader.SizeOfImage)
        throw Fatal(ERROR_INVALID_DATA, "Model producer leaves its actual selected original image.");
    nvse_executable(const_cast<std::uint8_t*>(base + site->first_return)); nvse_executable(const_cast<std::uint8_t*>(base + site->second_return));
    site->pointer = address(base + buffer); ++module.borrows; site->data_borrow = true;
    source_model_operand = std::move(site); auto& retained = *source_model_operand;
    source_model_buffer(retained);
    try { retained.lease = original_callable_pages->acquire_model_operand(retained.instruction, retained.initial); }
    catch (const CallablePageFailure& failure) { retained.failed = true; throw Fatal(failure.code, failure.what()); }
    Bytes result; result.put(retained.lease); result.put(retained.instruction); result.put(retained.pointer);
    result.put(original_callable_pages->source_code_protection(retained.instruction, 5)); reply(frame, ERROR_SUCCESS, "", result);
}
void source_model_operand_retire(const Frame& frame, Reader& reader) {
    const auto module = reader.get<std::uint64_t>(), lease = reader.get<std::uint64_t>(); reader.finish();
    if (!source_model_operand || source_model_operand->module != module || source_model_operand->lease != lease || source_model_operand->failed)
        throw Fatal(ERROR_INVALID_HANDLE, "Model operand retirement changed or failed its actual source lease.");
    auto& site = *source_model_operand; auto& owner = nvse_module_record(module);
    if (owner.calls || !site.data_borrow || !owner.borrows || site.stage == 1)
        throw Fatal(ERROR_BUSY, "Model operand retirement retains an original store or lacks its module-data borrow.");
    source_model_buffer(site); const auto instruction = source_model_instruction(site); std::uint32_t word = 0; std::memcpy(&word, instruction.data() + 1, 4);
    if (instruction[0] != 0x68 || word != (site.stage == 0 ? site.initial : site.pointer))
        throw Fatal(ERROR_INVALID_DATA, "Model operand changed before its actual isolated-field retirement.");
    try { original_callable_pages->release_site(site.lease); }
    catch (const CallablePageFailure& failure) { site.failed = true; throw Fatal(failure.code, failure.what()); }
    --owner.borrows; site.data_borrow = false; site.stage = 3;
    Bytes result; result.put(1U); result.put(frame.header.id); reply(frame, ERROR_SUCCESS, "", result);
}
