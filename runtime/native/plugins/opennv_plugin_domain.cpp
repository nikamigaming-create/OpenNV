#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "opennv_plugin_domain.h"
#include "opennv_plugin_steam.h"
#include "opennv_plugin_guest_arena.h"
#include "opennv_plugin_nvse.h"
#include "opennv_plugin_command_table.h"
#include "opennv_plugin_engine_commands.h"
#include "opennv_plugin_source_call_sites.h"
#include "opennv_plugin_data.h"
#include "opennv_plugin_expression.h"
#include "opennv_plugin_values.h"
#include "opennv_plugin_array_objects.h"
#include "opennv_plugin_value_heap.h"
#include "opennv_plugin_io.h"
#include "opennv_plugin_window_process.h"
#include "opennv_plugin_import_providers.h"
#include "opennv_plugin_source_locals.h"
#include "opennv_plugin_source_objects.h"
#include "opennv_plugin_source_files.h"
#include "opennv_plugin_callable_pages.h"
#include "opennv_plugin_binary_files.h"
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

using namespace opennv_domain;
static_assert(sizeof(void*) == 4, "Build this companion with the x86 toolchain.");

// Two scalar arguments, a scalar return and an optional ECX receiver are the
// only admitted call shape. No game layout, variadic or NVSE interface is implied.
extern "C" __declspec(naked) std::uint32_t __cdecl InvokeX86(
    void*, std::uint32_t, void*, std::uint32_t, std::uint32_t, CallReceipt*) {
    __asm {
        push ebp
        mov ebp, esp
        push ebx
        push esi
        push edi
        sub esp, 8
        mov [ebp - 16], esp
        mov ebx, 0badf00dh
        mov esi, 13579bdfh
        mov edi, 2468ace0h
        mov ecx, [ebp + 16]
        push [ebp + 24]
        push [ebp + 20]
        call [ebp + 8]
        mov [ebp - 20], eax
        cmp dword ptr [ebp + 12], 1
        jne callee_cleanup
        add esp, 8
    callee_cleanup:
        mov edx, esp
        sub edx, [ebp - 16]
        mov ecx, [ebp + 28]
        mov [ecx + 4], edx
        xor edx, edx
        cmp ebx, 0badf00dh
        jne missing_ebx
        or edx, 1
    missing_ebx:
        cmp esi, 13579bdfh
        jne missing_esi
        or edx, 2
    missing_esi:
        cmp edi, 2468ace0h
        jne missing_edi
        or edx, 4
    missing_edi:
        mov [ecx + 8], edx
        mov eax, [ebp - 20]
        mov [ecx], eax
        // Recover only this first-party shim's stack so a mismatched ABI can
        // produce a fatal receipt. The generation is never allowed to continue.
        mov esp, [ebp - 16]
        add esp, 8
        pop edi
        pop esi
        pop ebx
        pop ebp
        ret
    }
}

static std::uint32_t InvokeGuarded(void* function, Abi abi, void* receiver,
    std::uint32_t first, std::uint32_t second, CallReceipt* receipt) {
    __try { return InvokeX86(function, static_cast<std::uint32_t>(abi), receiver, first, second, receipt); }
    __except (CaptureNativeException(GetExceptionInformation(), GetExceptionCode(),
        &receipt->exception_code, &receipt->exception)) { return 0; }
}

#include "opennv_plugin_nvse_call.h"
#include "opennv_plugin_source_call_sites_call.h"
#include "opennv_plugin_expression_call.h"
#include "opennv_plugin_steam_call.h"
#include "opennv_plugin_steam_callbacks_call.h"

namespace {
struct Fatal final : std::runtime_error {
    std::uint32_t code;
    NativeExceptionObservation exception{};
    std::string exception_stage, prior_callback_reason;
    std::uint32_t invocation_entry = 0, prior_callback_code = 0;
    bool prior_callback_truncated = false;
    Fatal(std::uint32_t value, const std::string& message) : std::runtime_error(message), code(value) { }
};
struct Bytes {
    std::vector<std::uint8_t> data;
    template<class T> void put(T value) {
        const auto* begin = reinterpret_cast<const std::uint8_t*>(&value);
        data.insert(data.end(), begin, begin + sizeof(T));
    }
    void text(const std::string& value) {
        if (value.size() > max_payload - sizeof(std::uint32_t)) throw Fatal(ERROR_INVALID_DATA, "Oversized IPC text.");
        put(static_cast<std::uint32_t>(value.size())); data.insert(data.end(), value.begin(), value.end());
    }
};
#include "opennv_plugin_exception.inc"
struct Reader {
    const std::vector<std::uint8_t>& data;
    std::size_t at = 0;
    template<class T> T get() {
        if (at > data.size() || sizeof(T) > data.size() - at) throw Fatal(ERROR_INVALID_DATA, "Truncated IPC payload.");
        T value{}; std::memcpy(&value, data.data() + at, sizeof(T)); at += sizeof(T); return value;
    }
    std::string text() {
        const auto count = get<std::uint32_t>();
        if (at > data.size() || count > data.size() - at) throw Fatal(ERROR_INVALID_DATA, "Truncated IPC text.");
        std::string value(reinterpret_cast<const char*>(data.data() + at), count); at += count;
        if (value.find('\0') != std::string::npos) throw Fatal(ERROR_INVALID_DATA, "Embedded NUL in IPC text.");
        return value;
    }
    const void* bytes(std::uint32_t count) {
        if (at > data.size() || count > data.size() - at) throw Fatal(ERROR_INVALID_DATA, "Truncated IPC byte extent.");
        const auto* result = count ? data.data() + at : nullptr; at += count; return result;
    }
    void finish() const { if (at != data.size()) throw Fatal(ERROR_INVALID_DATA, "Unconsumed IPC payload."); }
};
struct Frame { FrameHeader header{}; std::vector<std::uint8_t> payload; };
HANDLE input = INVALID_HANDLE_VALUE, output = INVALID_HANDLE_VALUE;

bool read_exact(void* destination, std::uint32_t length, bool permit_eof = false) {
    auto* bytes = static_cast<std::uint8_t*>(destination);
    std::uint32_t used = 0;
    while (used < length) {
        DWORD received = 0;
        if (!ReadFile(input, bytes + used, length - used, &received, nullptr) || received == 0) {
            if (permit_eof && used == 0) return false;
            throw Fatal(ERROR_BROKEN_PIPE, "Parent IPC ended within a frame.");
        }
        used += received;
    }
    return true;
}
void write_exact(const void* source, std::uint32_t length) {
    const auto* bytes = static_cast<const std::uint8_t*>(source);
    std::uint32_t used = 0;
    while (used < length) {
        DWORD sent = 0;
        if (!WriteFile(output, bytes + used, length - used, &sent, nullptr) || sent == 0)
            throw Fatal(ERROR_BROKEN_PIPE, "Parent IPC write failed.");
        used += sent;
    }
}
bool receive(Frame& frame, bool permit_eof = false) {
    if (!read_exact(&frame.header, sizeof(FrameHeader), permit_eof)) return false;
    if (frame.header.magic != protocol_magic || frame.header.version != protocol_version ||
        frame.header.reserved != 0 || frame.header.length > max_payload)
        throw Fatal(ERROR_INVALID_DATA, "Invalid IPC header or frame budget.");
    frame.payload.resize(frame.header.length);
    if (!frame.payload.empty()) read_exact(frame.payload.data(), frame.header.length);
    return true;
}
void send(Kind kind, std::uint32_t operation, std::uint64_t generation,
    std::uint64_t id, std::uint64_t parent, const Bytes& bytes) {
    if (bytes.data.size() > max_payload) throw Fatal(ERROR_INVALID_DATA, "IPC reply exceeds its budget.");
    const FrameHeader header{protocol_magic, protocol_version, static_cast<std::uint32_t>(kind), operation,
        generation, id, parent, static_cast<std::uint32_t>(bytes.data.size()), 0};
    write_exact(&header, sizeof(header));
    if (!bytes.data.empty()) write_exact(bytes.data.data(), header.length);
}
std::wstring path_from_utf8(const std::string& path) {
    if (path.empty() || path.size() > 32767) throw Fatal(ERROR_INVALID_NAME, "Invalid module path extent.");
    const auto count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.data(), static_cast<int>(path.size()), nullptr, 0);
    if (count <= 0) throw Fatal(ERROR_NO_UNICODE_TRANSLATION, "Invalid UTF-8 module path.");
    std::wstring result(static_cast<std::size_t>(count), L'\0');
    if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.data(), static_cast<int>(path.size()), result.data(), count))
        throw Fatal(ERROR_NO_UNICODE_TRANSLATION, "Module path conversion failed.");
    const bool drive = result.size() >= 3 && result[1] == L':' && result[2] == L'\\';
    const bool unc = result.size() >= 3 && result[0] == L'\\' && result[1] == L'\\';
    if (!drive && !unc) throw Fatal(ERROR_INVALID_NAME, "Module path must be absolute.");
    return result;
}
template<class T> T function_pointer(FARPROC value) {
    static_assert(sizeof(T) == sizeof(value)); T result{}; std::memcpy(&result, &value, sizeof(result)); return result;
}
std::uint32_t address(const void* pointer) { return static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(pointer)); }

struct Symbol { std::uint64_t id; Abi abi; void* function; };
struct State {
    std::uint64_t generation = 0, last_request = 0, next_callback = 0, next_module = 0, next_symbol = 0;
    DWORD thread = GetCurrentThreadId();
    HMODULE module = nullptr;
    std::uint64_t module_id = 0;
    const AuthoredModuleContract* contract = nullptr;
    ModuleLifetime lifetime{};
    std::vector<Symbol> symbols;
    std::vector<std::uint64_t> calls;
    std::uint32_t transaction_callbacks = 0;
    char callback_reason[512]{};
    bool callback_reason_truncated = false;
    FrameHeader fault_request{};
    bool fault_request_owned = false;
    std::exception_ptr first_guarded_exception;
    bool retired = false;
    GuestArena arena;
    std::unique_ptr<NvseRuntime> nvse;
    std::unique_ptr<NativeCommandTableRuntime> command_table;
    std::unique_ptr<NvseDataRuntime> data;
    std::unique_ptr<NvseExpressionRuntime> expressions;
    std::unique_ptr<NvseValueRuntime> values;
    std::unique_ptr<NvseArrayObjectRuntime> array_objects;
    std::unique_ptr<NvseValueHeap> value_heap;
    std::unique_ptr<PluginIoRuntime> io;
    std::unique_ptr<WindowProcessRuntime> window_process;
    std::unique_ptr<CngSystemService> cng_service;
    std::unique_ptr<ImportProvidersRuntime> import_providers;
    std::unique_ptr<SteamRuntime> steam;
    std::unique_ptr<SourceLocalRuntime> source_locals;
    std::unique_ptr<SourceObjectRuntime> source_objects;
    std::unique_ptr<SourceFileRuntime> source_files;
    std::unique_ptr<BinaryFileRuntime> binary_files;
    bool nvse_image_attempted = false;
};
State* state = nullptr;
std::atomic<std::uint32_t> callback_fault{0};

void lifetime_bytes(Bytes& bytes) {
    const auto& value = state->lifetime;
    bytes.put(value.tls_attach); bytes.put(value.dll_attach); bytes.put(value.tls_order); bytes.put(value.dll_order);
    bytes.put(value.imported_value); bytes.put(value.tls_value); bytes.put(value.tls_detach); bytes.put(value.dll_detach);
    bytes.put(value.calls); bytes.put(value.callbacks);
}
void reply(const Frame& request, DWORD error, const std::string& reason, Bytes data = {}) {
    Bytes bytes; bytes.put(error); bytes.put(state->module ? 1U : 0U); bytes.text(reason);
    bytes.data.insert(bytes.data.end(), data.data.begin(), data.data.end());
    send(Kind::reply, request.header.operation, state->generation, request.header.id, request.header.parent, bytes);
}
void dispatch(const Frame& frame, std::uint64_t expected_parent);

std::uint32_t retain_callback_fault(std::uint32_t code, const char* reason) noexcept {
    if (state) {
        const auto length = std::strlen(reason);
        const auto retained = length < sizeof(state->callback_reason) ? length : sizeof(state->callback_reason) - 1;
        std::memcpy(state->callback_reason, reason, retained); state->callback_reason[retained] = '\0';
        state->callback_reason_truncated = retained != length;
    }
    callback_fault.store(code); return 0;
}
void source_local_sync(bool);
bool source_local_callback_needs_sync(Kind, std::uint32_t);
bool cng_detach_active();
bool mutex_detach_callback_allowed();
std::uint32_t array_objects_pointer(std::uint32_t);
void array_objects_refresh_all();
void array_objects_retire(const Frame&);
void array_objects_dispatch(const Frame&);
Bytes perform_callback_bytes(Kind kind, std::uint32_t event, const Bytes& payload) noexcept {
    if (!state || GetCurrentThreadId() != state->thread || state->calls.empty()) {
        callback_fault.store(ERROR_INVALID_THREAD_ID); return {};
    }
    try {
        if (cng_detach_active() && (kind != Kind::io_callback || event != 21U && !(event == 22U && mutex_detach_callback_allowed())))
            throw Fatal(ERROR_NOT_SUPPORTED, "Actual original unload only admits existing CNG cleanup and mutex before/after-loader receipts.");
        const auto synchronize_locals = source_local_callback_needs_sync(kind, event);
        if (synchronize_locals) source_local_sync(false);
        if (kind == Kind::io_callback) {
            if (!state->io) throw Fatal(ERROR_INVALID_STATE, "Private I/O callback has no generation owner.");
            auto& owner = *state->io; const auto transaction = state->calls.front();
            if (owner.transaction != transaction) { owner.transaction = transaction; owner.transaction_callbacks = 0; }
            if (++owner.transaction_callbacks > 1048576) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native private I/O callback budget exceeded.");
        }
        else if (kind == Kind::nvse_callback && event == 0x601) {
            if (!state->steam || state->steam->source_hashes.empty() || state->steam->source_hashes.size() > 65 ||
                state->steam->source_request != state->calls.back() ||
                state->steam->source_hashes.back()->request != state->calls.back() ||
                state->steam->source_hashes.back()->phase != SteamSourceHashPhase::callback_entered)
                throw Fatal(ERROR_INVALID_STATE, "Original-file hash callback has no genuine entered platform source/file prefix.");
        }
        else if (kind == Kind::nvse_callback && event == 0x600) {
            if (!state->steam || state->steam->callback_prefix.empty() || state->steam->callback_prefix.size() > 65536)
                throw Fatal(ERROR_INVALID_STATE, "Steam callback has no bounded entered source member prefix.");
        }
        else if (kind == Kind::nvse_callback) {
            if (!state->nvse || ++state->nvse->interface_calls > state->nvse->interface_budget)
                throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "NVSE interface callback budget exceeded.");
        }
        else if (++state->transaction_callbacks > 64) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native callback budget exceeded.");
        if (++state->next_callback >= (1ULL << 63)) throw Fatal(ERROR_INVALID_DATA, "Callback identity exhausted.");
        const auto id = (1ULL << 63) | state->next_callback;
        const auto parent = state->calls.back();
        Bytes bytes; bytes.put(state->thread); bytes.data.insert(bytes.data.end(), payload.data.begin(), payload.data.end());
        send(kind, event, state->generation, id, parent, bytes);
        for (;;) {
            Frame incoming; receive(incoming);
            if (incoming.header.generation != state->generation) throw Fatal(ERROR_INVALID_DATA, "Callback generation drift.");
            const auto expected_reply = kind == Kind::io_callback ? Kind::io_reply :
                kind == Kind::nvse_callback ? Kind::nvse_reply :
                kind == Kind::state_query ? Kind::state_reply : Kind::callback_reply;
            if (incoming.header.kind == static_cast<std::uint32_t>(expected_reply)) {
                if (incoming.header.id != id || incoming.header.parent != parent || incoming.header.operation != event)
                    throw Fatal(ERROR_INVALID_DATA, "Callback reply identity drift.");
                Bytes result; result.data = std::move(incoming.payload);
                if (synchronize_locals) source_local_sync(false);
                if (kind == Kind::nvse_callback && (event < 0x240 || event > 0x243) &&
                    state->array_objects && !state->array_objects->refreshing &&
                    (!state->source_locals || !state->source_locals->synchronizing) && !cng_detach_active())
                    array_objects_refresh_all();
                return result;
            }
            if (cng_detach_active()) throw Fatal(ERROR_BUSY, "Original CNG unload callback cannot enter another original command or allocation.");
            const auto operation = static_cast<Operation>(incoming.header.operation);
            if (operation != Operation::call && operation != Operation::guest_read &&
                operation != Operation::guest_write && operation != Operation::guest_stats &&
                operation != Operation::nvse_command && operation != Operation::nvse_expression_statistics && operation != Operation::nvse_values_statistics && operation != Operation::nvse_local_statistics && operation != Operation::window_process)
                throw Fatal(ERROR_BUSY, "Only a nested call or existing guest access is admitted while a callback owns the stack.");
            dispatch(incoming, id);
        }
    }
    catch (const Fatal& failure) {
        // This catch runs only on the owning native call thread. Retain the
        // actual refusal; a quota/retirement fault is not a parent-ID fault.
        remember_native_exception(state->first_guarded_exception, failure);
        retain_callback_fault(failure.code, failure.what()); return {};
    }
    catch (...) { callback_fault.store(ERROR_INVALID_DATA); return {}; }
}
std::uint32_t perform_callback(Kind kind, std::uint32_t event, const Bytes& payload) noexcept {
    try {
        auto bytes = perform_callback_bytes(kind, event, payload);
        if (callback_fault.load()) return 0;
        Reader reader{bytes.data}; const auto result = reader.get<std::uint32_t>(); reader.finish(); return result;
    }
    catch (const Fatal& failure) { return retain_callback_fault(failure.code, failure.what()); }
    catch (...) { return retain_callback_fault(ERROR_INVALID_DATA, "Native scalar callback reply lacks its exact extent."); }
}
std::uint32_t callback(std::uint32_t event, std::uint32_t first, std::uint32_t second) noexcept {
    try { Bytes bytes; bytes.put(first); bytes.put(second); return perform_callback(Kind::callback, event, bytes); }
    catch (...) { return retain_callback_fault(ERROR_INVALID_DATA, "Native callback payload construction failed."); }
}
std::uint32_t __cdecl guest_query(const GuestStateView* object, std::uint32_t operation, std::uint32_t argument) noexcept {
    if (!state || GetCurrentThreadId() != state->thread || state->calls.empty())
        return retain_callback_fault(ERROR_INVALID_THREAD_ID, "A native state query has no owning call/thread.");
    try {
        const auto region = state->arena.object(object);
        state->arena.note_query();
        Bytes bytes; bytes.put(region.id); bytes.put(address(region.base)); bytes.put(argument);
        return perform_callback(Kind::state_query, operation, bytes);
    }
    catch (const GuestArenaFailure& failure) { return retain_callback_fault(failure.code, failure.what()); }
    catch (...) { return retain_callback_fault(ERROR_INVALID_DATA, "Native state query preparation failed."); }
}
std::uint32_t __stdcall guest_stdcall(const GuestStateView* object, std::uint32_t operation, std::uint32_t argument) noexcept {
    return guest_query(object, operation, argument);
}
// Authored ECX adapter. It calls the same typed state bridge, with two DWORD
// stack arguments and ret 8. No retail instruction or vtable is executed.
__declspec(naked) std::uint32_t guest_thiscall() {
    __asm {
        push ebp
        mov ebp, esp
        push [ebp + 12]
        push [ebp + 8]
        push ecx
        call guest_query
        add esp, 12
        pop ebp
        ret 8
    }
}
std::uint32_t owned_thunk(void* function) {
    MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(function, &memory, sizeof(memory)) || memory.State != MEM_COMMIT || memory.Type != MEM_IMAGE ||
        memory.AllocationBase != GetModuleHandleW(nullptr) ||
        (memory.Protect != PAGE_EXECUTE && memory.Protect != PAGE_EXECUTE_READ &&
            memory.Protect != PAGE_EXECUTE_READWRITE && memory.Protect != PAGE_EXECUTE_WRITECOPY))
        throw Fatal(ERROR_INVALID_DATA, "First-party guest thunk is not executable companion image code.");
    return address(function);
}
void arena_stats(Bytes& bytes) {
    bytes.put(state->arena.live()); bytes.put(state->arena.retired());
    bytes.put(state->arena.committed()); bytes.put(state->arena.reserved()); bytes.put(state->arena.queries());
}
void arena_region(Bytes& bytes, const GuestRegion& region) {
    bytes.put(region.id); bytes.put(address(region.base)); bytes.put(region.length);
    bytes.put(region.committed); bytes.put(region.reserved);
    bytes.put(static_cast<std::uint32_t>(region.access)); arena_stats(bytes);
}
std::uint32_t __cdecl cdecl_callback(void*, std::uint32_t event, std::uint32_t first, std::uint32_t second) {
    return callback(event, first, second);
}
std::uint32_t __stdcall stdcall_callback(void*, std::uint32_t event, std::uint32_t first, std::uint32_t second) {
    return callback(event, first, second);
}
HostCallbacks host_callbacks{sizeof(HostCallbacks), 1, nullptr, cdecl_callback, stdcall_callback};

void* command_table_interface_address();
void command_table_validate_all();
void engine_command_handler(void*, std::uint32_t, const std::string&);
void engine_command_executable(void*);
bool engine_command_zero_parameter_caller(std::uint32_t, std::uint32_t, std::uint32_t);
void engine_commands_validate_all();
void engine_commands_retire(const Frame&);
void* source_call_site_api(const std::string&);
void source_call_sites_retire(const Frame&);
void command_table_retire();
void expression_initialize(void*);
void expression_retire();
void* values_interface(std::uint32_t);
void values_retire();
void values_heap_flush_detach(const Frame&);
void values_heap_retire();
std::uint32_t values_array_reference(std::uint32_t);
void values_expression_element(void*, void*);
void values_expression_assign(void*, void*);
bool __cdecl values_assign_array(NvseOpaqueArray*, double*);
bool values_heap_contains(const void*);
void* values_heap_allocate(std::uint32_t, std::uint32_t, std::uint32_t);
void values_heap_release(void*, std::uint32_t, std::uint32_t);
void io_bind_imports(HMODULE);
void* mutex_entry(const std::string&);
bool mutex_close_handle(HANDLE, BOOL&);
void mutex_flush_detach();
void mutex_retire();
void mutex_before_free(const Frame&, HMODULE);
BOOL mutex_original_free_library(HMODULE);
void mutex_after_free(const Frame&, HMODULE, BOOL, DWORD);
void io_flush_detach(const Frame&);
void io_retire();
void crt_prepare(PluginIoRuntime&, Reader&);
void crypto_prepare(PluginIoRuntime&, Reader&);
void* crypto_import_entry(const std::string&, const std::string&, void*);
void* crypto_dynamic_entry(HMODULE, const std::string&);
void crypto_flush_detach();
void crypto_retire();
void* mapping_entry(const std::string&);
bool mapping_close_handle(HANDLE, BOOL&);
void mapping_flush_detach();
void mapping_retire();
void* crt_import_entry(const std::string&, const std::string&, void*);
bool crt_io_import_name(const std::string&, const std::string&);
void* crt_dynamic_entry(HMODULE, const std::string&);
void crt_flush_detach();
void crt_retire();
void find_flush_detach();
void find_retire();
void* io_find_entry(const std::string&);
void* io_profile_extended_entry(const std::string&);
void crt_owned_callback(std::uint32_t, const Bytes&);
void crt_defer_heap_receipt(const NvseHeapEvent&);
void* crt_extended_entry(const std::string&);
void* crt_standard_entry(const std::string&);
void cpp_prepare(PluginIoRuntime&, Reader&);
void* cpp_import_entry(const std::string&, const std::string&, void*);
void* cpp_dynamic_entry(HMODULE, const std::string&);
void cpp_retire();
void cpp_require_retired();
void crt_standard_retire();
void crt_prepare_preentry_standard();
void crt_standard_before_close(void*);
void crt_standard_after_close(void*, int, DWORD, const CrtStatus&);
void crt_descriptor_views_closed(void*);
bool crt_standard_borrowed_handle(HANDLE);
void crt_support_require_releasable(std::uint64_t);
void crt_support_flush_detach();
void crt_support_require_retired();
DWORD WINAPI io_get_environment_a(LPCSTR, LPSTR, DWORD);
BOOL WINAPI io_attributes_ex_w(LPCWSTR, GET_FILEEX_INFO_LEVELS, LPVOID);
BOOL WINAPI io_attributes_ex_a(LPCSTR, GET_FILEEX_INFO_LEVELS, LPVOID);
BOOL WINAPI io_information_by_handle_ex(HANDLE, FILE_INFO_BY_HANDLE_CLASS, LPVOID, DWORD);
void source_locals_retire_all();
SourceObject& source_object(std::uint64_t);
void source_objects_retire_all();
void source_files_retire();
void binary_files_retire_all();
void* source_script_interface_address();
void* expression_source_form(NvseExpressionEvaluator&, NvseExpressionToken&);
const char* __cdecl values_get_string(std::uint32_t);
bool expression_local_type(std::uint32_t);
SourceLocalCell expression_local_read(NvseExpressionEvaluator&, const NvseExpressionToken&);
void* expression_local_pointer(NvseExpressionEvaluator&, const NvseExpressionToken&);
double expression_local_number(NvseExpressionEvaluator&, const NvseExpressionToken&);
bool expression_local_boolean(NvseExpressionEvaluator&, const NvseExpressionToken&);
std::uint32_t expression_local_form_id(NvseExpressionEvaluator&, const NvseExpressionToken&);
std::uint32_t expression_local_handle(NvseExpressionEvaluator&, const NvseExpressionToken&);
const char* expression_local_string(NvseExpressionEvaluator&, const NvseExpressionToken&);
void data_sync_if_bound();
void* data_interface_address(std::uint32_t);
void import_providers_begin_original(const Frame&, const std::wstring&);
void import_providers_end_original(const Frame&, bool);
void import_providers_validate_original(HMODULE);
BOOL cng_original_free_library(const Frame&, HMODULE);
void cng_shared_require_releasable(std::uint32_t, std::uint64_t);
void cng_shared_forget(std::uint32_t, std::uint64_t);
void cng_shared_retain_mapping_view(PluginMappingView&, HANDLE);
void cng_shared_close_mapping_view(PluginMappingView&);
void cng_shared_flush_detach();
void cng_shared_hash_destroyed(std::uint64_t);
void cng_shared_handle_destination(const void*, std::uint32_t);
void cng_detach_require_request(CngServiceStep, const Bytes&);
Bytes cng_detach_host(const Bytes&);
struct CngCallerBuffer;
struct CngCallerResult;
bool cng_shared_needed(std::uint64_t, const std::array<CngCallerBuffer, 4>&, ULONG*, PUCHAR, ULONG);
CngCallerResult cng_shared_invoke(std::uint32_t, std::uint64_t, ULONG, DWORD, ULONG, bool, ULONG*, const std::array<CngCallerBuffer, 4>&, void*, PUCHAR);
PUCHAR cng_shared_service_pointer(CngSystemService&, const CngSharedPointer&);
void cng_shared_service_before(CngSystemService&, CngInvocation&);
void cng_shared_service_after(CngSystemService&, const CngInvocation&);
void cng_shared_service_release_invocation(CngSystemService&, const CngInvocation&);
void cng_shared_service_hash_abandoned(CngSystemService&, std::uint64_t);
void cng_shared_service_abandon(CngSystemService&);
void cng_shared_service_require_retired(CngSystemService&);
void cng_shared_service_step(const Frame&, Reader&, CngServiceStep);
#include "opennv_plugin_window_process_forward.inc"
#include "opennv_plugin_nvse.inc"
#include "opennv_plugin_data.inc"
#include "opennv_plugin_command_table.inc"
#include "opennv_plugin_source_locals.inc"
#include "opennv_plugin_expression.inc"
#include "opennv_plugin_expression_locals.inc"
#include "opennv_plugin_values.inc"
#include "opennv_plugin_array_objects.inc"
#include "opennv_plugin_value_heap.inc"
#include "opennv_plugin_io.inc"
#include "opennv_plugin_mutexes.inc"
#include "opennv_plugin_mutex_namespace.inc"
#include "opennv_plugin_profiles.inc"
#include "opennv_plugin_find.inc"
#include "opennv_plugin_crt.inc"
#include "opennv_plugin_crt_open.inc"
#include "opennv_plugin_crt_support.inc"
#include "opennv_plugin_crt_members.inc"
#include "opennv_plugin_environment.inc"
#include "opennv_plugin_crt_support_members.inc"
#include "opennv_plugin_cpp_runtime.inc"
#include "opennv_plugin_crt_standard.inc"
#include "opennv_plugin_crt_descriptors.inc"
#include "opennv_plugin_file_metadata.inc"
#include "opennv_plugin_window_process.inc"
#include "opennv_plugin_window_process_sdk.inc"
#include "opennv_plugin_window_process_imports.inc"
#include "opennv_plugin_crypto.inc"
#include "opennv_plugin_cng_client.inc"
#include "opennv_plugin_import_providers.inc"
#include "opennv_plugin_shared_placement.inc"
#include "opennv_plugin_mappings.inc"
#include "opennv_plugin_cng_shared_client.inc"
#include "opennv_plugin_source_objects.inc"
#include "opennv_plugin_source_refresh.inc"
#include "opennv_plugin_source_local_attach.inc"
#include "opennv_plugin_callable_pages.inc"
#include "opennv_plugin_engine_commands.inc"
#include "opennv_plugin_source_call_sites.inc"
#include "opennv_plugin_source_publication.inc"
#include "opennv_plugin_source_files.inc"
#include "opennv_plugin_binary_files.inc"
#include "opennv_plugin_steam.inc"
#include "opennv_plugin_cng_service.inc"
#include "opennv_plugin_cng_shared_service.inc"

void unload(const Frame& frame) {
    if (!state->module || state->nvse || !state->calls.empty()) { reply(frame, ERROR_BUSY, "Authored module is absent or still owns an active call."); return; }
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (module != state->module_id) { reply(frame, ERROR_INVALID_HANDLE, "Stale module identity."); return; }
    const auto previous = state->module;
    if (!FreeLibrary(previous)) throw Fatal(GetLastError(), "FreeLibrary failed; retirement is incomplete.");
    state->module = nullptr; state->contract = nullptr; state->module_id = 0; state->symbols.clear();
    if (callback_fault.load() != 0) throw Fatal(callback_fault.load(),
        std::string(state->callback_reason[0] ? state->callback_reason : "DLL retirement invoked an unowned callback.") +
        (state->callback_reason_truncated ? " [Native callback diagnostics truncated.]" : ""));
    MEMORY_BASIC_INFORMATION memory{};
    if (!VirtualQuery(previous, &memory, sizeof(memory))) throw Fatal(GetLastError(), "Retired module mapping cannot be observed.");
    const auto mapping_present = memory.State != MEM_FREE;
    Bytes data; lifetime_bytes(data); data.put(mapping_present ? 1U : 0U);
    if (state->lifetime.tls_detach != 1 || state->lifetime.dll_detach != 1 || mapping_present)
        throw Fatal(ERROR_INVALID_DATA, "Module retirement lacks one TLS/DllMain detach or retains its image mapping.");
    reply(frame, ERROR_SUCCESS, "", data);
}
void load(const Frame& frame) {
    Reader reader{frame.payload}; const auto path = path_from_utf8(reader.text()); reader.finish();
    if (state->module) { reply(frame, ERROR_BUSY, "One module is already admitted."); return; }
    state->lifetime = {}; state->callback_reason[0] = '\0'; state->callback_reason_truncated = false; callback_fault.store(0);
    LoaderInvocation loader;
    const auto loaded = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    const auto loader_error = loaded ? ERROR_SUCCESS : GetLastError();
    loader.finish();
    if (!loaded) { reply(frame, loader_error, loader.refusal("Windows DLL/import/entry admission failed.")); return; }
    state->module = loaded;
    const auto describe = function_pointer<DescribeAuthoredModule>(GetProcAddress(loaded, "OpenNvDomainDescribe"));
    const auto bind = function_pointer<BindAuthoredModule>(GetProcAddress(loaded, "OpenNvDomainBind"));
    const auto* contract = describe ? describe() : nullptr;
    if (!contract || !bind || contract->size != sizeof(AuthoredModuleContract) || contract->version != 1 ||
        contract->magic != module_magic || contract->export_count == 0 || contract->export_count > 64 || !contract->exports) {
        if (!FreeLibrary(loaded)) throw Fatal(GetLastError(), "Rejected authored module did not retire.");
        state->module = nullptr; reply(frame, ERROR_BAD_EXE_FORMAT, "The first-party diagnostic module ABI is absent or invalid."); return;
    }
    if (!bind(&host_callbacks, &state->lifetime) || callback_fault.load() != 0)
        throw Fatal(ERROR_INVALID_DATA, "Authored module binding refused or invoked an unowned callback.");
    if (state->lifetime.tls_attach != 1 || state->lifetime.dll_attach != 1 || state->lifetime.tls_order == 0 ||
        state->lifetime.tls_order >= state->lifetime.dll_order)
        throw Fatal(ERROR_INVALID_DATA, "The module did not observe real ordered TLS and DllMain entry.");
    state->contract = contract; state->module_id = ++state->next_module;
    Bytes data; data.put(state->module_id); data.put(address(loaded)); data.put(address(contract->receiver));
    lifetime_bytes(data); data.put(contract->export_count); reply(frame, ERROR_SUCCESS, "", data);
}
void resolve(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>();
    const auto abi = static_cast<Abi>(reader.get<std::uint32_t>()); const auto name = reader.text(); reader.finish();
    if (!state->module || state->nvse || module != state->module_id) { reply(frame, ERROR_INVALID_HANDLE, "No current authored module identity."); return; }
    if (name.empty() || name.size() > 255) { reply(frame, ERROR_INVALID_NAME, "Invalid export name."); return; }
    for (std::uint32_t index = 0; index < state->contract->export_count; ++index) {
        const auto& entry = state->contract->exports[index];
        if (!entry.name || name != entry.name) continue;
        if (entry.abi != abi || (abi != Abi::cdecl_call && abi != Abi::stdcall_call && abi != Abi::thiscall_call)) {
            reply(frame, ERROR_INVALID_PARAMETER, "Declared export calling convention mismatch."); return;
        }
        const auto exported = function_pointer<void*>(GetProcAddress(state->module, name.c_str()));
        if (!exported || exported != entry.function || (abi == Abi::thiscall_call && !state->contract->receiver)) {
            reply(frame, ERROR_PROC_NOT_FOUND, "Declared exported function or its receiver is absent."); return;
        }
        const auto id = ++state->next_symbol; state->symbols.push_back({id, abi, exported});
        Bytes data; data.put(id); data.put(address(exported)); reply(frame, ERROR_SUCCESS, "", data); return;
    }
    reply(frame, ERROR_PROC_NOT_FOUND, "Export has no admitted authored call contract.");
}
void call(const Frame& frame) {
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); const auto function = reader.get<std::uint64_t>();
    const auto first = reader.get<std::uint32_t>(); const auto second = reader.get<std::uint32_t>(); reader.finish();
    if (!state->module || module != state->module_id) { reply(frame, ERROR_INVALID_HANDLE, "Stale module identity."); return; }
    const Symbol* symbol = nullptr;
    for (const auto& candidate : state->symbols) if (candidate.id == function) { symbol = &candidate; break; }
    if (!symbol) { reply(frame, ERROR_INVALID_HANDLE, "Stale function identity."); return; }
    if (state->calls.size() >= max_call_depth) { reply(frame, ERROR_STACK_OVERFLOW, "Native reentrant call depth exceeded."); return; }
    // A nested resolve cannot invalidate this pointer: callback pump admits only calls.
    const auto owned_symbol = *symbol;
    if (state->calls.empty()) state->transaction_callbacks = 0;
    state->calls.push_back(frame.header.id);
    CallReceipt receipt{};
    InvokeGuarded(owned_symbol.function, owned_symbol.abi, state->contract->receiver, first, second, &receipt);
    state->calls.pop_back();
    if (state->first_guarded_exception) std::rethrow_exception(state->first_guarded_exception);
    native_exception_raise(receipt, "authored scalar call", address(owned_symbol.function),
        callback_fault.load(), state->callback_reason, state->callback_reason_truncated);
    if (callback_fault.load() != 0) throw Fatal(callback_fault.load(),
        std::string(state->callback_reason[0] ? state->callback_reason : "Native callback lacks a valid thread, frame or parent owner.") +
        (state->callback_reason_truncated ? " [Native callback diagnostics truncated.]" : ""));
    if (receipt.stack_delta != 0 || receipt.preserved_registers != 7)
        throw Fatal(ERROR_INVALID_DATA, "Native calling convention violated ownership: stackDelta=" +
            std::to_string(receipt.stack_delta) + " registerMask=" + std::to_string(receipt.preserved_registers));
    Bytes data; data.put(receipt.result); data.put(receipt.stack_delta); data.put(receipt.preserved_registers);
    data.put(receipt.exception_code); data.put(state->lifetime.calls); data.put(state->lifetime.callbacks);
    reply(frame, ERROR_SUCCESS, "", data);
}
void dispatch_request(const Frame& frame, std::uint64_t expected_parent) {
    if (frame.header.generation != state->generation || frame.header.kind != static_cast<std::uint32_t>(Kind::request) ||
        frame.header.parent != expected_parent || frame.header.id == 0 || frame.header.id >= (1ULL << 63) ||
        frame.header.id <= state->last_request || state->retired)
        throw Fatal(ERROR_INVALID_DATA, "Request generation, sequence or stack owner drift.");
    state->last_request = frame.header.id;
#ifdef OPENNV_SYSTEM_CNG_SERVICE
    if (frame.header.operation != static_cast<std::uint32_t>(Operation::hello) &&
        frame.header.operation != static_cast<std::uint32_t>(Operation::cng_system_service) &&
        frame.header.operation != static_cast<std::uint32_t>(Operation::retire))
        throw Fatal(ERROR_NOT_SUPPORTED, "First-party CNG service admits no module, original DLL, object, file or call operation.");
#endif
    try { switch (static_cast<Operation>(frame.header.operation)) {
    case Operation::hello: {
        Reader reader{frame.payload}; reader.finish(); Bytes data;
        data.put(GetCurrentProcessId()); data.put(state->thread); data.put(32U); data.put(max_payload); data.put(max_call_depth);
        reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::load_authored: load(frame); break;
    case Operation::load_nvse: load_nvse(frame); break;
    case Operation::source_address_space: source_image_address_space(frame); break;
    case Operation::source_call_sites: source_call_sites_operation(frame); break;
    case Operation::nvse_query: nvse_stage(frame, false); break;
    case Operation::nvse_load: nvse_stage(frame, true); break;
    case Operation::nvse_message: nvse_message(frame); break;
    case Operation::nvse_serialization: nvse_serialization_event(frame); break;
    case Operation::unload_nvse: unload_nvse(frame); break;
    case Operation::nvse_expression_abi: expression_configure(frame); break;
    case Operation::nvse_command: expression_command(frame); break;
    case Operation::nvse_expression_statistics: expression_statistics(frame); break;
    case Operation::nvse_values_attach: values_configure(frame); break;
    case Operation::nvse_values_statistics: values_statistics(frame); break;
    case Operation::nvse_value_heap: values_heap_configure(frame); break;
    case Operation::private_io_prepare: io_prepare(frame); break;
    case Operation::steam_provider: steam_provider(frame); break;
    case Operation::cng_system_service: cng_system_service(frame); break;
    case Operation::import_providers: import_providers_operation(frame); break;
    case Operation::nvse_array_objects: array_objects_dispatch(frame); break;
    case Operation::crt_runtime: crt_standard_runtime(frame); break;
    case Operation::window_process: window_process_dispatch(frame); break;
    case Operation::nvse_local_create: source_local_create(frame); break;
    case Operation::nvse_local_fill: source_local_fill(frame); break;
    case Operation::nvse_local_seal: source_local_seal(frame); break;
    case Operation::nvse_local_retire: source_local_retire(frame); break;
    case Operation::nvse_local_statistics: source_local_statistics(frame); break;
    case Operation::nvse_object_bind: source_object_bind(frame); break;
    case Operation::guest_seal: guest_seal(frame); break;
    case Operation::nvse_script_interface: source_script_interface(frame); break;
    case Operation::nvse_object_retire: source_object_retire(frame); break;
    case Operation::nvse_object_refresh: source_object_refresh(frame); break;
    case Operation::nvse_local_attach_script: source_local_attach_script(frame); break;
    case Operation::nvse_file_methods: source_files_configure(frame); break;
    case Operation::nvse_binary_methods: binary_methods_configure(frame); break;
    case Operation::nvse_binary_bind: binary_file_bind(frame); break;
    case Operation::nvse_binary_retire: binary_file_retire(frame); break;
    case Operation::resolve: resolve(frame); break;
    case Operation::call: call(frame); break;
    case Operation::unload: unload(frame); break;
    case Operation::guest_caps: {
        Reader reader{frame.payload}; reader.finish(); Bytes data;
        data.put(guest_state_version); data.put(max_guest_committed); data.put(max_guest_reserved);
        data.put(max_guest_live); data.put(max_guest_reservations); data.put(max_guest_transfer);
        data.put(state->arena.page_size()); data.put(state->arena.allocation_granularity());
        reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::guest_allocate: {
        if (!state->calls.empty()) { reply(frame, ERROR_BUSY, "An active native call owns its guest allocations."); break; }
        Reader reader{frame.payload}; const auto length = reader.get<std::uint32_t>();
        const auto access = static_cast<GuestAccess>(reader.get<std::uint32_t>()); const auto count = reader.get<std::uint32_t>();
        const auto* initial = reader.bytes(count); reader.finish();
        if (count > max_guest_transfer) throw GuestArenaFailure(ERROR_INVALID_PARAMETER, "Guest initial bytes exceed their transfer budget.");
        const auto region = state->arena.allocate(length, access, initial, count);
        Bytes data; arena_region(data, region); reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::guest_bind_state: {
        if (!state->calls.empty()) { reply(frame, ERROR_BUSY, "An active native call owns its state bindings."); break; }
        Reader reader{frame.payload}; reader.finish();
        const auto cdecl_query = owned_thunk(reinterpret_cast<void*>(guest_query));
        const auto stdcall_query = owned_thunk(reinterpret_cast<void*>(guest_stdcall));
        const auto thiscall_query = owned_thunk(reinterpret_cast<void*>(guest_thiscall));
        const auto region = state->arena.bind(cdecl_query, stdcall_query, thiscall_query);
        Bytes data; arena_region(data, region); data.put(cdecl_query); data.put(stdcall_query); data.put(thiscall_query);
        reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::guest_read: {
        Reader reader{frame.payload}; const auto id = reader.get<std::uint64_t>();
        const auto offset = reader.get<std::uint32_t>(); const auto count = reader.get<std::uint32_t>(); reader.finish();
        const auto* begin = static_cast<const std::uint8_t*>(state->arena.read(id, offset, count));
        Bytes data; data.put(count); data.data.insert(data.data.end(), begin, begin + count); arena_stats(data);
        reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::guest_write: {
        Reader reader{frame.payload}; const auto id = reader.get<std::uint64_t>(); const auto offset = reader.get<std::uint32_t>();
        const auto count = reader.get<std::uint32_t>(); const auto* bytes = reader.bytes(count); reader.finish();
        state->arena.write(id, offset, bytes, count); Bytes data; arena_stats(data); reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::guest_release: {
        if (!state->calls.empty()) { reply(frame, ERROR_BUSY, "An active native call prevents guest lifetime retirement."); break; }
        Reader reader{frame.payload}; const auto id = reader.get<std::uint64_t>(); reader.finish();
        state->arena.release(id); Bytes data; data.put(1U); arena_stats(data); reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::guest_stats: {
        Reader reader{frame.payload}; reader.finish(); Bytes data; arena_stats(data); reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::retire: {
        Reader reader{frame.payload}; reader.finish();
        if (state->module || !state->calls.empty()) { reply(frame, ERROR_BUSY, "An active module or call prevents retirement."); break; }
        steam_require_retired();
        cng_system_service_require_retired();
        import_providers_require_retired();
        io_retire(); state->io.reset();
        source_callables_close();
        state->arena.retire(); state->retired = true; Bytes data; arena_stats(data);
        reply(frame, ERROR_SUCCESS, "", data); break;
    }
    default: throw Fatal(ERROR_INVALID_FUNCTION, "Unknown native-domain operation.");
    } }
    catch (const GuestArenaFailure& failure) {
        if (failure.terminal) throw Fatal(failure.code, failure.what());
        reply(frame, failure.code, failure.what());
    }
}
void dispatch(const Frame& frame, std::uint64_t expected_parent) {
    try { dispatch_request(frame, expected_parent); }
    catch (const Fatal&) {
        // Preserve the first failed nested request through the native callback
        // stack. The parent is waiting on that request, not the outer call.
        if (!state->fault_request_owned) {
            state->fault_request = frame.header;
            state->fault_request_owned = true;
        }
        throw;
    }
}
}

int wmain(int argc, wchar_t** argv) {
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    if ((argc != 3 && argc != 6) || std::wstring(argv[1]) != L"--generation" ||
        (argc == 6 && std::wstring(argv[3]) != L"--source-image")) {
        std::fputs("Use --generation <nonzero-uint64> [--source-image <base> <bytes>].\n", stderr); return 2;
    }
    wchar_t* end = nullptr; const auto generation = _wcstoui64(argv[2], &end, 10);
    if (!generation || !end || *end != L'\0') { std::fputs("Invalid native-domain generation.\n", stderr); return 2; }
    std::uint32_t source_image_base = 0, source_image_bytes = 0;
    if (argc == 6) {
        const auto base = _wcstoui64(argv[4], &end, 10);
        if (!base || !end || *end != L'\0' || base > std::numeric_limits<std::uint32_t>::max()) return 2;
        const auto bytes = _wcstoui64(argv[5], &end, 10);
        if (!bytes || !end || *end != L'\0' || bytes > std::numeric_limits<std::uint32_t>::max()) return 2;
        source_image_base = static_cast<std::uint32_t>(base); source_image_bytes = static_cast<std::uint32_t>(bytes);
    }
    input = GetStdHandle(STD_INPUT_HANDLE); output = GetStdHandle(STD_OUTPUT_HANDLE);
    if (!input || input == INVALID_HANDLE_VALUE || !output || output == INVALID_HANDLE_VALUE) return 2;
    State owned; owned.generation = generation; state = &owned;
    try {
        if (source_image_base) {
            original_callable_pages = std::make_unique<OriginalCallablePages>();
            original_callable_pages->adopt_source_image(source_image_base, source_image_bytes);
        }
        while (!owned.retired) {
            Frame frame;
            if (!receive(frame, true)) throw Fatal(ERROR_BROKEN_PIPE, "Parent retired without a native retirement receipt.");
            dispatch(frame, 0);
        }
        state = nullptr; return 0;
    }
    catch (const Fatal& failure) {
        try { Bytes bytes; bytes.put(failure.code); bytes.text(failure.what());
            const auto& request = owned.fault_request;
            if (request.operation == static_cast<std::uint32_t>(Operation::steam_provider)) steam_fault_bytes(bytes, owned.steam.get());
            native_exception_bytes(bytes, failure);
            send(Kind::fault, request.operation, owned.generation, request.id, request.parent, bytes); } catch (...) { }
        std::fprintf(stderr, "OPENNV_NATIVE_DOMAIN_FAULT code=%lu owner=%s\n", static_cast<unsigned long>(failure.code), failure.what());
    }
    catch (const std::exception& failure) { std::fprintf(stderr, "OPENNV_NATIVE_DOMAIN_FAULT owner=%s\n", failure.what()); }
    // Fatal generations cannot safely run arbitrary module detach callbacks.
    // Terminate only this companion; abnormal retirement has no detach claim.
    std::fflush(stderr); state = nullptr;
    TerminateProcess(GetCurrentProcess(), 3); return 3;
}
