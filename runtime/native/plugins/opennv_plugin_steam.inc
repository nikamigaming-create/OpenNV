// Include after State, Bytes, Reader, reply and path_from_utf8. Only the
// actual selected original Steam DLL and its returned interfaces are called.

std::wstring steam_module_path(HMODULE module) {
    std::vector<wchar_t> text(32768); const auto count = GetModuleFileNameW(module, text.data(), static_cast<DWORD>(text.size()));
    if (!count || count >= text.size()) throw Fatal(GetLastError() ? GetLastError() : ERROR_BUFFER_OVERFLOW, "Platform callable module path failed.");
    return std::wstring(text.data(), count);
}
std::string steam_utf8(const std::wstring& value) {
    const auto count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (count <= 0) throw Fatal(GetLastError(), "Platform source path has no exact UTF8 identity.");
    std::string result(static_cast<std::size_t>(count), '\0');
    if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), count, nullptr, nullptr) != count)
        throw Fatal(GetLastError(), "Platform source path conversion drifted.");
    return result;
}
#include "opennv_plugin_steam_hash.inc"

std::string steam_sha(HANDLE source_input, const std::wstring& path, SteamSourceFileKind kind) { return steam_hash_source(source_input, path, kind); }
bool steam_same_file(HANDLE first, HANDLE second) {
    FILE_ID_INFO a{}, b{};
    if (!GetFileInformationByHandleEx(first, FileIdInfo, &a, sizeof(a)) || !GetFileInformationByHandleEx(second, FileIdInfo, &b, sizeof(b)))
        throw Fatal(GetLastError(), "Actual mapped platform source has no kernel file identity.");
    return a.VolumeSerialNumber == b.VolumeSerialNumber && std::memcmp(a.FileId.Identifier, b.FileId.Identifier, sizeof(a.FileId.Identifier)) == 0;
}
HANDLE steam_open_source(const std::wstring& path) {
    const auto source_input = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (source_input == INVALID_HANDLE_VALUE) throw Fatal(GetLastError(), "Actual platform source read-only lease failed.");
    return source_input;
}
SteamRuntime& steam_owner() {
    if (!state->steam || !state->steam->module || state->steam->phase == SteamPhase::retired || state->steam->phase == SteamPhase::failed)
        throw Fatal(ERROR_INVALID_STATE, "Source platform has no actual retained DLL lifetime.");
    return *state->steam;
}
void* steam_export(SteamRuntime& owner, const char* name) {
    const auto entry = GetProcAddress(owner.module, name);
    if (!entry) throw Fatal(GetLastError(), "Actual selected platform export is absent.");
    HMODULE actual = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(entry), &actual) || actual != owner.module)
        throw Fatal(ERROR_INVALID_DATA, "Platform export forwarded outside its exact selected original image.");
    return reinterpret_cast<void*>(entry);
}
void steam_readable(const void* pointer, std::size_t count) {
    MEMORY_BASIC_INFORMATION region{};
    if (!pointer || !VirtualQuery(pointer, &region, sizeof(region)) || region.State != MEM_COMMIT ||
        region.Protect & (PAGE_NOACCESS | PAGE_GUARD) ||
        !(region.Protect & (PAGE_READONLY | PAGE_READWRITE | PAGE_WRITECOPY | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)) ||
        reinterpret_cast<std::uintptr_t>(pointer) < reinterpret_cast<std::uintptr_t>(region.BaseAddress) ||
        reinterpret_cast<std::uintptr_t>(pointer) - reinterpret_cast<std::uintptr_t>(region.BaseAddress) > region.RegionSize ||
        count > region.RegionSize - (reinterpret_cast<std::uintptr_t>(pointer) - reinterpret_cast<std::uintptr_t>(region.BaseAddress)))
        throw Fatal(ERROR_INVALID_ADDRESS, "Actual Steam interface/table has no complete readable current memory.");
}
void* steam_method(void* pointer, std::uint32_t slot) {
    steam_readable(pointer, sizeof(void*)); auto** table = *reinterpret_cast<void***>(pointer);
    steam_readable(table, (static_cast<std::size_t>(slot) + 1) * sizeof(void*)); auto* method = table[slot];
    MEMORY_BASIC_INFORMATION region{};
    if (!method || !VirtualQuery(method, &region, sizeof(region)) || region.State != MEM_COMMIT || region.Protect & (PAGE_NOACCESS | PAGE_GUARD) ||
        !(region.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE | PAGE_EXECUTE_WRITECOPY)))
        throw Fatal(ERROR_INVALID_ADDRESS, "Actual Steam interface returned no current executable method.");
    return method;
}
SteamCallableSource& steam_callable(SteamRuntime& owner, void* method) {
    HMODULE actual = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, reinterpret_cast<LPCWSTR>(method), &actual))
        throw Fatal(GetLastError(), "Actual Steam method has no retained mapped callable image.");
    const auto known = owner.callable_sources.find(actual);
    if (known != owner.callable_sources.end()) {
        if (!FreeLibrary(actual)) throw Fatal(GetLastError(), "Temporary callable-provider reference failed retirement.");
        return *known->second;
    }
    auto source = std::make_unique<SteamCallableSource>(); source->retained = actual;
    auto* retained = source.get(); owner.callable_sources.emplace(actual, std::move(source));
    // Publish ownership before path/hash work can fail. The fault frame then
    // retains the actual module/input prefix rather than losing an allocation
    // whose metadata constructor did not return.
    retained->path = steam_module_path(actual);
    retained->input = steam_open_source(retained->path); retained->sha = steam_sha(retained->input, retained->path, SteamSourceFileKind::callable);
    return *retained;
}
std::uint32_t steam_invoke(SteamRuntime& owner, SteamCallStep step, SteamReturnKind kind,
    void* function, Abi abi, void* receiver, std::uint32_t arguments, std::uint32_t argument, CallReceipt& receipt,
    const SteamCallableSource* callable = nullptr) {
    if (arguments > 1 || (abi != Abi::cdecl_call && abi != Abi::thiscall_call)) throw Fatal(ERROR_INVALID_DATA, "Platform call has another unowned ABI/argument count.");
    if (owner.prefix.size() >= 16) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Actual platform call-prefix extent exceeded.");
    owner.prefix.push_back(SteamForeignPrefix{step, kind});
    owner.prefix.back().callable_path = callable ? callable->path : owner.path;
    owner.prefix.back().callable_sha = callable ? callable->sha : owner.sha;
    const auto result = OpenNVInvokeSteamGuarded(function, abi, receiver, arguments, argument, &receipt);
    auto& prefix = owner.prefix.back(); prefix.abi = receipt;
    prefix.result = kind == SteamReturnKind::none ? 0U : kind == SteamReturnKind::boolean_byte ? result & 0xffU :
        kind == SteamReturnKind::pointer_presence ? (result ? 1U : 0U) : result;
    if (receipt.exception_code || receipt.stack_delta || receipt.preserved_registers != 7)
        throw Fatal(receipt.exception_code ? receipt.exception_code : ERROR_INVALID_DATA, "Actual selected platform call failed its ABI boundary.");
    prefix.returned = true;
    return result;
}
void steam_prefix_bytes(Bytes& bytes, const SteamRuntime* owner) {
    bytes.put(owner ? static_cast<std::uint32_t>(owner->prefix.size()) : 0U);
    if (!owner) return;
    for (const auto& value : owner->prefix) {
        bytes.put(static_cast<std::uint32_t>(value.step)); bytes.put(static_cast<std::uint32_t>(value.kind));
        bytes.put(value.returned ? 1U : 0U); bytes.put(value.result); bytes.put(value.abi.stack_delta);
        bytes.put(value.abi.preserved_registers); bytes.put(value.abi.exception_code);
        bytes.text(steam_utf8(value.callable_path)); bytes.text(value.callable_sha);
    }
}
#include "opennv_plugin_steam_callbacks.inc"

void steam_fault_bytes(Bytes& bytes, const SteamRuntime* owner) {
    bytes.put(3U); bytes.put(owner ? owner->session : 0ULL);
    bytes.put(owner ? static_cast<std::uint32_t>(owner->attempted) : 0U);
    bytes.put(owner ? static_cast<std::uint32_t>(owner->phase) : static_cast<std::uint32_t>(SteamPhase::failed));
    bytes.put(owner && owner->module ? 1U : 0U); bytes.put(owner && owner->input != INVALID_HANDLE_VALUE ? 1U : 0U);
    bytes.put(owner ? owner->current_app : 0U); steam_prefix_bytes(bytes, owner);
    bytes.put(owner ? static_cast<std::uint32_t>(owner->callable_sources.size()) : 0U);
    if (owner) for (const auto& entry : owner->callable_sources) {
        const auto& value = *entry.second;
        bytes.text(value.path.empty() ? "" : steam_utf8(value.path)); bytes.text(value.sha);
        bytes.put(value.retained ? 1U : 0U); bytes.put(value.input != INVALID_HANDLE_VALUE ? 1U : 0U);
    }
    steam_callback_prefix_bytes(bytes, owner);
    bytes.put(owner ? static_cast<std::uint32_t>(owner->callbacks.size()) : 0U);
    if (owner) for (const auto& value : owner->callbacks) {
        bytes.put(value.first); bytes.put(value.second->source_id); bytes.put(static_cast<std::uint32_t>(value.second->flags));
        bytes.put(value.second->live ? 1U : 0U);
    }
    steam_source_hash_prefix_bytes(bytes, owner);
}
void steam_reply(const Frame& frame, SteamRuntime& owner, SteamOperation operation, std::uint64_t token,
    std::uint32_t result, const CallReceipt& call, const SteamCallableSource* callable = nullptr) {
    if (owner.sequence == std::numeric_limits<std::uint64_t>::max()) throw Fatal(ERROR_INVALID_DATA, "Platform source receipt sequence exhausted.");
    Bytes bytes; bytes.put(owner.session); bytes.put(++owner.sequence); bytes.put(static_cast<std::uint32_t>(operation)); bytes.put(token);
    const auto measured = !owner.prefix.empty();
    bytes.put(state->thread); bytes.put(result); bytes.put(measured ? 1U : 0U);
    bytes.put(measured ? call.stack_delta : 0); bytes.put(measured ? call.preserved_registers : 0U); bytes.put(measured ? call.exception_code : 0U);
    bytes.put(owner.current_app); bytes.put(static_cast<std::uint32_t>(owner.phase));
    bytes.text(callable ? steam_utf8(callable->path) : ""); bytes.text(callable ? callable->sha : "");
    steam_prefix_bytes(bytes, &owner); steam_callback_prefix_bytes(bytes, &owner);
    steam_source_hash_prefix_bytes(bytes, &owner);
    reply(frame, ERROR_SUCCESS, "", bytes);
}
void steam_provider(const Frame& frame) {
    if (GetCurrentThreadId() != state->thread || !state->calls.empty())
        throw Fatal(ERROR_BUSY, "Actual platform operation cannot replace an entered original call/thread owner.");
    Reader reader{frame.payload}; const auto operation = static_cast<SteamOperation>(reader.get<std::uint32_t>());
    if (operation == SteamOperation::load) {
        if (state->steam) { reply(frame, ERROR_BUSY, "Actual platform was already constructed/attempted."); return; }
        auto value = std::make_unique<SteamRuntime>(); value->path = path_from_utf8(reader.text()); const auto expected_sha = reader.text();
        value->user_slot = reader.get<std::uint32_t>(); value->achievement_slot = reader.get<std::uint32_t>(); value->store_slot = reader.get<std::uint32_t>();
        value->app_slot = reader.get<std::uint32_t>(); value->expected_app = reader.get<std::uint32_t>(); reader.finish();
        if (value->user_slot != 1 || value->achievement_slot != 7 || value->store_slot != 10 || value->app_slot != 9)
            throw Fatal(ERROR_NOT_SUPPORTED, "Selected platform interface layout is unowned.");
        state->steam = std::move(value); auto& owner = *state->steam;
        owner.source_request = frame.header.id; state->calls.push_back(frame.header.id);
        try {
            owner.input = steam_open_source(owner.path); owner.sha = steam_sha(owner.input, owner.path, SteamSourceFileKind::provider);
            if (owner.sha != expected_sha) throw Fatal(ERROR_INVALID_DATA, "Actual platform input differs from its retained selected original SHA256.");
            owner.module = LoadLibraryExW(owner.path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
            if (!owner.module) throw Fatal(GetLastError(), "Actual source platform loader failed.");
            const auto mapped = steam_module_path(owner.module); const auto mapped_input = steam_open_source(mapped);
            bool same = false;
            try { same = steam_same_file(owner.input, mapped_input); CloseHandle(mapped_input); }
            catch (...) { CloseHandle(mapped_input); throw; }
            if (!same) throw Fatal(ERROR_INVALID_DATA, "Mapped platform DLL is not the same selected kernel file.");
            for (const auto* name : {"SteamAPI_Init", "SteamAPI_Shutdown", "SteamAPI_IsSteamRunning", "SteamAPI_RunCallbacks", "SteamUser", "SteamUserStats", "SteamUtils", "SteamAPI_RegisterCallback", "SteamAPI_UnregisterCallback"})
                (void)steam_export(owner, name);
            owner.phase = SteamPhase::loaded;
            CallReceipt receipt{};
            state->calls.pop_back(); owner.source_request = 0;
            steam_reply(frame, owner, operation, 0, 1, receipt); return;
        }
        catch (...) {
            if (!state->calls.empty() && state->calls.back() == frame.header.id) state->calls.pop_back();
            owner.source_request = 0; owner.phase = SteamPhase::failed; throw;
        }
    }
    auto& owner = steam_owner(); const auto session = reader.get<std::uint64_t>(), token = reader.get<std::uint64_t>(); const auto argument = reader.text(); reader.finish();
    if (session != owner.session || operation < SteamOperation::initialize || operation > SteamOperation::unregister_callback)
        throw Fatal(ERROR_INVALID_DATA, "Platform operation belongs to another original child session.");
    owner.attempted = operation; owner.prefix.clear(); owner.callback_prefix.clear();
    const bool uses_interface = operation == SteamOperation::logged_on || operation == SteamOperation::set_achievement || operation == SteamOperation::store_statistics || operation == SteamOperation::release_interface ||
        operation == SteamOperation::application_id || operation == SteamOperation::request_statistics || operation == SteamOperation::unregister_callback;
    if (uses_interface != (token != 0) || (operation != SteamOperation::set_achievement && operation != SteamOperation::register_callback && !argument.empty()))
        throw Fatal(ERROR_INVALID_DATA, "Platform source operation changed its interface/argument category.");
    CallReceipt receipt{0, 0, 7, 0}; std::uint32_t result = 0; std::uint64_t reply_token = token; SteamCallableSource* callable = nullptr;
    if (operation == SteamOperation::release_interface) {
        if (owner.interfaces.erase(token) != 1) throw Fatal(ERROR_INVALID_HANDLE, "Original borrowed platform interface has no live child token.");
        steam_reply(frame, owner, operation, token, 1, receipt); return;
    }
    if (operation == SteamOperation::unload) {
        if (!owner.interfaces.empty() || !owner.callbacks.empty() ||
            (owner.phase != SteamPhase::shutdown_returned && owner.phase != SteamPhase::loaded))
            throw Fatal(ERROR_BUSY, "Actual platform shutdown/interfaces must retire before its export provider.");
        for (auto& entry : owner.callable_sources) {
            auto& source = *entry.second;
            if (source.retained) {
                if (!FreeLibrary(source.retained)) throw Fatal(GetLastError(), "Actual callable provider reference failed retirement.");
                source.retained = nullptr;
            }
            if (source.input != INVALID_HANDLE_VALUE) {
                if (!CloseHandle(source.input)) throw Fatal(GetLastError(), "Actual callable source input failed retirement.");
                source.input = INVALID_HANDLE_VALUE;
            }
        }
        owner.callable_sources.clear();
        if (!FreeLibrary(owner.module)) throw Fatal(GetLastError(), "Actual platform export reference failed retirement.");
        owner.module = nullptr;
        if (!CloseHandle(owner.input)) throw Fatal(GetLastError(), "Original platform input lease failed retirement.");
        owner.input = INVALID_HANDLE_VALUE; owner.phase = SteamPhase::retired;
        steam_reply(frame, owner, operation, 0, 1, receipt); state->steam.reset(); return;
    }
    if ((owner.phase == SteamPhase::shutdown_returned && operation != SteamOperation::unregister_callback) || owner.phase == SteamPhase::shutdown_entered)
        throw Fatal(ERROR_INVALID_STATE, "Actual platform call followed its source shutdown prefix.");
    state->calls.push_back(frame.header.id); owner.source_request = frame.header.id;
    try {
        if (operation == SteamOperation::initialize) {
            if (owner.phase != SteamPhase::loaded) throw Fatal(ERROR_INVALID_STATE, "Platform Init cannot replay an entered/returned original call.");
            owner.phase = SteamPhase::initialize_entered;
            result = steam_invoke(owner, SteamCallStep::source_export, SteamReturnKind::boolean_byte, steam_export(owner, "SteamAPI_Init"), Abi::cdecl_call, nullptr, 0, 0, receipt) & 0xff;
            owner.phase = SteamPhase::initialize_returned;
        }
        else if (operation == SteamOperation::is_running)
            result = steam_invoke(owner, SteamCallStep::source_export, SteamReturnKind::boolean_byte, steam_export(owner, "SteamAPI_IsSteamRunning"), Abi::cdecl_call, nullptr, 0, 0, receipt) & 0xff;
        else if (operation == SteamOperation::user || operation == SteamOperation::statistics || operation == SteamOperation::utilities) {
            auto* pointer = reinterpret_cast<void*>(steam_invoke(owner, SteamCallStep::source_export, SteamReturnKind::pointer_presence,
                steam_export(owner, operation == SteamOperation::user ? "SteamUser" : operation == SteamOperation::statistics ? "SteamUserStats" : "SteamUtils"), Abi::cdecl_call, nullptr, 0, 0, receipt));
            result = pointer ? 1U : 0U;
            if (pointer) {
                if (owner.next_token == std::numeric_limits<std::uint64_t>::max()) throw Fatal(ERROR_INVALID_DATA, "Actual platform interface token sequence exhausted.");
                reply_token = ++owner.next_token; owner.interfaces.emplace(reply_token, SteamBorrowedInterface{pointer, operation == SteamOperation::user ? 1U : operation == SteamOperation::statistics ? 2U : 3U});
            }
        }
        else if (operation == SteamOperation::logged_on || operation == SteamOperation::set_achievement || operation == SteamOperation::store_statistics || operation == SteamOperation::application_id || operation == SteamOperation::request_statistics) {
            const auto found = owner.interfaces.find(token); const auto kind = operation == SteamOperation::logged_on ? 1U : operation == SteamOperation::application_id ? 3U : 2U;
            if (found == owner.interfaces.end() || found->second.kind != kind) throw Fatal(ERROR_INVALID_HANDLE, "Actual platform method has a stale/foreign interface token.");
            auto* pointer = found->second.pointer;
            auto* method = steam_method(pointer, operation == SteamOperation::logged_on ? owner.user_slot : operation == SteamOperation::set_achievement ? owner.achievement_slot : operation == SteamOperation::application_id ? owner.app_slot : operation == SteamOperation::request_statistics ? 0U : owner.store_slot);
            callable = &steam_callable(owner, method);
            if (operation == SteamOperation::set_achievement && (argument.empty() || argument.size() > 3 ||
                std::any_of(argument.begin(), argument.end(), [](char item) { return static_cast<unsigned char>(item) < 0x20 || static_cast<unsigned char>(item) > 0x7e; })))
                throw Fatal(ERROR_NOT_SUPPORTED, "Original achievement's four-byte invalid-parameter formatting producer is unowned.");
            result = steam_invoke(owner, SteamCallStep::source_method, operation == SteamOperation::application_id ? SteamReturnKind::application_id : SteamReturnKind::boolean_byte, method, Abi::thiscall_call, pointer, operation == SteamOperation::set_achievement ? 1U : 0U,
                operation == SteamOperation::set_achievement ? static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(argument.c_str())) : 0U, receipt, callable);
            if (operation == SteamOperation::application_id) owner.current_app = result;
            else result &= 0xff;
        }
        else if (operation == SteamOperation::register_callback) {
            reply_token = steam_register_source_callback(owner, argument, receipt, result);
        }
        else if (operation == SteamOperation::unregister_callback) {
            steam_unregister_source_callback(owner, token, receipt); result = 0;
        }
        else if (operation == SteamOperation::run_callbacks) {
            (void)steam_invoke(owner, SteamCallStep::source_export, SteamReturnKind::none, steam_export(owner, "SteamAPI_RunCallbacks"), Abi::cdecl_call, nullptr, 0, 0, receipt);
        }
        else if (operation == SteamOperation::shutdown) {
            if (!owner.interfaces.empty()) throw Fatal(ERROR_BUSY, "Actual platform still owns borrowed interface queries.");
            owner.phase = SteamPhase::shutdown_entered;
            (void)steam_invoke(owner, SteamCallStep::source_export, SteamReturnKind::none, steam_export(owner, "SteamAPI_Shutdown"), Abi::cdecl_call, nullptr, 0, 0, receipt);
            owner.phase = SteamPhase::shutdown_returned;
        }
        if (callback_fault.load()) throw Fatal(callback_fault.load(), "Actual platform pump/call retained a native callback failure.");
        state->calls.pop_back(); owner.source_request = 0;
        steam_reply(frame, owner, operation, reply_token, result, receipt, callable);
    }
    catch (...) {
        owner.phase = SteamPhase::failed; owner.source_request = 0;
        if (!state->calls.empty() && state->calls.back() == frame.header.id) state->calls.pop_back();
        throw;
    }
}
void steam_require_retired() {
    if (state->steam) throw Fatal(ERROR_BUSY, "Actual source platform still owns its original DLL/interfaces.");
}
