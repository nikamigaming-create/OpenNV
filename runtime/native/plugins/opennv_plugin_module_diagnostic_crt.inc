// Only this source-selected producer creates a peer's diagnostic FILE. Other
// private services still enter io_owner's independent primary-policy guard.
PluginIoRuntime& crt_io_owner() {
    if (nvse_primary_services()) return io_owner();
    if (!state || !state->io || GetCurrentThreadId() != state->thread || !state->module_set || state->calls.empty())
        throw Fatal(ERROR_INVALID_THREAD_ID, "Module CRT lost its actual shared thread/call/resource lifetime.");
    auto& module = nvse_module_record(state->module_id);
    if (!module.diagnostic || !module.diagnostic->bound || module.image != state->module)
        throw Fatal(ERROR_NOT_SUPPORTED, "Additional original DLL has no source-bound diagnostic CRT owner.");
    return *state->io;
}
Bytes crt_host(std::uint32_t operation, const Bytes& payload) {
    auto& io = crt_io_owner();
    if (!nvse_primary_services() && operation != 5 && operation != 6 && operation != 7 && operation != 8 && operation != 24)
        throw Fatal(ERROR_NOT_SUPPORTED, "Diagnostic FILE cannot borrow another private callback policy.");
    if (io.loader_retiring) throw Fatal(ERROR_NOT_SUPPORTED, "Diagnostic route cannot call its parent while the original loader lock is entered.");
    return io_host_bound(io, operation, payload);
}
std::unique_ptr<ModuleDiagnosticCrt> module_diagnostic_read(Reader& reader) {
    const auto present = reader.get<std::uint32_t>();
    if (!present) return {};
    if (present != 1) throw Fatal(ERROR_INVALID_DATA, "Module diagnostic source presence is invalid.");
    auto result = std::make_unique<ModuleDiagnosticCrt>();
    result->sha = reader.text(); result->stack = reader.text(); result->family = reader.text();
    result->query = reader.get<std::uint32_t>(); result->producer = reader.get<std::uint32_t>(); result->stream = reader.get<std::uint32_t>();
    result->name_rva = reader.get<std::uint32_t>(); result->mode_rva = reader.get<std::uint32_t>();
    result->first_return = reader.get<std::uint32_t>(); result->retry_return = reader.get<std::uint32_t>();
    result->sharing = reader.get<int>(); result->retry_limit = reader.get<std::uint32_t>(); result->name = reader.text(); result->mode = reader.text();
    if (result->sha.size() != 64 || result->stack.size() != 64 || result->family.empty() || !result->query || !result->producer || !result->stream ||
        !result->name_rva || !result->mode_rva || result->first_return <= result->producer || result->retry_return <= result->first_return ||
        result->sharing != 0x20 || !result->retry_limit || result->retry_limit > 32 || result->mode != "w" || result->name.empty() || result->name.size() > 96 ||
        result->name.find_first_of("/\\:") != std::string::npos)
        throw Fatal(ERROR_INVALID_DATA, "Module diagnostic lost its bounded source path/share/global declaration.");
    return result;
}
void module_diagnostic_bind(NvseModuleRecord& module, const IMAGE_NT_HEADERS32& nt, const std::string& sha, const std::string& stack) {
    if (!module.diagnostic) return;
    auto& source = *module.diagnostic;
    if (!module.supplemental || source.sha != sha || source.stack != stack || !state->io || !state->io->crt ||
        state->io->crt->providers.size() != 1 || !state->io->crt->providers[0]->retained)
        throw Fatal(ERROR_NOT_SUPPORTED, "Module diagnostic lost its returned original attach/shared provider source.");
    const auto base = reinterpret_cast<unsigned char*>(module.image);
    const auto extent = nt.OptionalHeader.SizeOfImage;
    for (const auto rva : {source.query, source.producer, source.first_return, source.retry_return}) {
        if (rva >= extent) throw Fatal(ERROR_INVALID_ADDRESS, "Diagnostic caller leaves its actual source image.");
        NvseExecutionScope entered(module.id); nvse_executable(base + rva);
    }
    if (source.stream > extent || sizeof(void*) > extent - source.stream || source.name_rva >= extent || source.mode_rva >= extent ||
        nvse_string(reinterpret_cast<char*>(base + source.name_rva), false) != source.name ||
        nvse_string(reinterpret_cast<char*>(base + source.mode_rva), false) != source.mode)
        throw Fatal(ERROR_INVALID_DATA, "Diagnostic mapped source operands changed after original attach.");
    auto** cell = reinterpret_cast<void**>(base + source.stream); expression_writable(cell, sizeof(void*));
    if (nvse_copy(cell)) throw Fatal(ERROR_NOT_SUPPORTED, "Diagnostic has a preceding actual FILE publisher before its selected Query.");
    source.bound = true;
}
class CrtDiagnosticOpen {
    ModuleDiagnosticCrt* owner_ = nullptr;
public:
    CrtOpenProducer declaration;
    CrtDiagnosticOpen(const void* original_return, const std::wstring& path, const std::wstring& mode, int sharing) {
        if (nvse_primary_services()) return;
        (void)crt_io_owner(); auto& module = nvse_module_record(state->module_id); auto& source = *module.diagnostic;
        declaration.caller = address(original_return); declaration.cell = address(module.image) + source.stream;
        declaration.value = address(nvse_copy(reinterpret_cast<void**>(declaration.cell)));
        declaration.attempt = source.attempt; declaration.sharing = sharing;
        const auto expected_path = source.attempt == 0 ? source.name : source.name + std::to_string(source.attempt - 1);
        const auto expected_return = address(module.image) + (source.attempt == 0 ? source.first_return : source.retry_return);
        if (source.entered || module.runtime->phase != NvsePhase::querying || sharing != source.sharing || io_utf8(mode) != source.mode ||
            io_utf8(path) != expected_path || declaration.caller != expected_return || declaration.value || source.attempt > source.retry_limit ||
            (source.attempt != 0 && (!source.previous_returned || source.previous_file)))
            throw Fatal(ERROR_NOT_SUPPORTED, "Diagnostic open changed its original Query/caller/null-prefix/path/mode/share order.");
        source.entered = true; owner_ = &source;
    }
    CrtDiagnosticOpen(const CrtDiagnosticOpen&) = delete;
    CrtDiagnosticOpen& operator=(const CrtDiagnosticOpen&) = delete;
    void returned(void* pointer) {
        if (!owner_) return;
        owner_->previous_file = address(pointer); owner_->previous_returned = true;
        ++owner_->attempt;
    }
    ~CrtDiagnosticOpen() { if (owner_) owner_->entered = false; }
};
