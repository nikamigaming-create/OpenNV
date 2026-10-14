NvseModuleRecord& nvse_module_record(std::uint64_t id) {
    if (!state || !state->module_set) throw Fatal(ERROR_INVALID_STATE, "Co-resident module registry is absent.");
    const auto found = state->module_set->modules.find(id);
    if (found == state->module_set->modules.end() || found->second->retired || !found->second->image || !found->second->runtime)
        throw Fatal(ERROR_INVALID_HANDLE, "Co-resident original module has retired or lacks its actual image/runtime.");
    return *found->second;
}
void nvse_module_set_adopt_primary() {
    if (state->module_set) return;
    if (!state->module || !state->nvse || !state->module_id || !state->calls.empty())
        throw Fatal(ERROR_INVALID_STATE, "Module-set construction needs a genuine idle primary original module.");
    auto owner = std::make_unique<NvseModuleSet>();
    auto primary = std::make_unique<NvseModuleRecord>();
    primary->id = state->module_id; primary->image = state->module; primary->runtime = state->nvse;
    owner->primary = primary->id; owner->live = 1;
    owner->shared_tables = state->nvse->tables; owner->shared_tables_extent = state->nvse->tables_extent;
    // The process service, rather than any individual DLL destructor, now
    // owns this actual already-published interface allocation.
    primary->runtime->owns_tables = false;
    owner->images.emplace(address(primary->image), primary.get());
    owner->modules.emplace(primary->id, std::move(primary)); state->module_set = std::move(owner);
}
class NvseExecutionScope {
    HMODULE previous_image_;
    std::uint64_t previous_id_;
    std::shared_ptr<NvseRuntime> previous_runtime_;
    NvseModuleRecord* entered_ = nullptr;
public:
    explicit NvseExecutionScope(std::uint64_t id)
        : previous_image_(state->module), previous_id_(state->module_id), previous_runtime_(state->nvse) {
        if (!state->module_set) {
            if (id != state->module_id) throw Fatal(ERROR_INVALID_HANDLE, "Original invocation changed its sole module identity.");
            return;
        }
        auto& target = nvse_module_record(id);
        if (target.calls == UINT32_MAX) throw Fatal(ERROR_ARITHMETIC_OVERFLOW, "Original module call ownership exhausted.");
        ++target.calls; entered_ = &target;
        state->module = target.image; state->module_id = target.id; state->nvse = target.runtime;
    }
    NvseExecutionScope(const NvseExecutionScope&) = delete;
    NvseExecutionScope& operator=(const NvseExecutionScope&) = delete;
    ~NvseExecutionScope() {
        if (entered_) --entered_->calls;
        if (state->module_set) {
            const auto previous = state->module_set->modules.find(previous_id_);
            if (previous == state->module_set->modules.end() || previous->second->retired) {
                state->module = nullptr; state->module_id = 0; state->nvse.reset();
                for (const auto& item : state->module_set->modules) if (!item.second->retired) {
                    state->module = item.second->image; state->module_id = item.second->id; state->nvse = item.second->runtime; break;
                }
                return;
            }
        }
        state->module = previous_image_; state->module_id = previous_id_; state->nvse = previous_runtime_;
    }
};
class NvseCallbackModuleScope {
    std::unique_ptr<NvseExecutionScope> scope_;
public:
    explicit NvseCallbackModuleScope(const void* original_return) {
        if (!state->module_set || !original_return) return;
        MEMORY_BASIC_INFORMATION memory{};
        if (VirtualQuery(original_return, &memory, sizeof(memory)) != sizeof(memory) || memory.State != MEM_COMMIT || memory.Type != MEM_IMAGE)
            throw Fatal(ERROR_INVALID_ADDRESS, "Native callback entry lost its genuine executable caller image.");
        const auto found = state->module_set->images.find(address(memory.AllocationBase));
        // Internal adapters and system-provider frames keep their already-
        // entered module scope. A direct source DLL caller binds its own
        // retained image; no outer DLL is credited for another DLL's callback.
        if (found == state->module_set->images.end()) return;
        if (found->second->retired) throw Fatal(ERROR_INVALID_HANDLE, "Retired original code cannot reenter a shared host interface.");
        scope_ = std::make_unique<NvseExecutionScope>(found->second->id);
    }
};
struct NvseLoadingScope {
    std::uint32_t previous;
    explicit NvseLoadingScope(std::uint32_t handle) : previous(state->loading_handle) { state->loading_handle = handle; }
    ~NvseLoadingScope() { state->loading_handle = previous; }
};

void nvse_module_set_require_retirement(std::uint64_t id) {
    if (!state->module_set) return;
    auto& module = nvse_module_record(id);
    if (module.calls != 1 || module.borrows)
        throw Fatal(ERROR_BUSY, "Original module retirement still owns a native call or retained cross-image function.");
}
bool nvse_module_set_retire_record(std::uint64_t id) {
    if (!state->module_set) return true;
    auto& owner = *state->module_set; auto& module = nvse_module_record(id);
    owner.images.erase(address(module.image)); module.retired = true; module.image = nullptr;
    if (!owner.live) throw Fatal(ERROR_INVALID_DATA, "Original module registry lost its live cardinality.");
    --owner.live; module.runtime->phase = NvsePhase::retired;
    return owner.live == 0;
}
bool nvse_primary_services() {
    return !state->module_set || state->module_id == state->module_set->primary ||
        (state->module_set->live == 0 && state->module_id == 0);
}
void nvse_require_primary_return(const void* original_return) {
    if (!state->module_set) return;
    if (!nvse_primary_services()) throw Fatal(ERROR_NOT_SUPPORTED, "Additional original DLL has no module-bound private service owner.");
    MEMORY_BASIC_INFORMATION memory{};
    if (!original_return || !VirtualQuery(original_return, &memory, sizeof(memory)))
        throw Fatal(ERROR_INVALID_ADDRESS, "Private source service lost its current native caller image.");
    const auto found = state->module_set->images.find(address(memory.AllocationBase));
    if (found != state->module_set->images.end() && found->second->id != state->module_set->primary)
        throw Fatal(ERROR_NOT_SUPPORTED, "Foreign original code cannot borrow the primary DLL's private service policy.");
}
std::unique_ptr<NvseExecutionScope> nvse_request_module(const Frame& frame) {
    const auto operation = static_cast<Operation>(frame.header.operation);
    switch (operation) {
    case Operation::nvse_query: case Operation::nvse_load: case Operation::nvse_message:
    case Operation::nvse_serialization: case Operation::unload_nvse: break;
    default: return {};
    }
    Reader reader{frame.payload}; const auto id = reader.get<std::uint64_t>();
    return std::make_unique<NvseExecutionScope>(id);
}
void nvse_module_set_operation(const Frame&);
