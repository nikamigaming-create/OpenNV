#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include "opennv_plugin_domain.h"
#include <atomic>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <limits>
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
    __except (EXCEPTION_EXECUTE_HANDLER) { receipt->exception_code = GetExceptionCode(); return 0; }
}

namespace {
struct Fatal final : std::runtime_error {
    std::uint32_t code;
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
    bool retired = false;
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

std::uint32_t callback(std::uint32_t event, std::uint32_t first, std::uint32_t second) noexcept {
    if (!state || GetCurrentThreadId() != state->thread || state->calls.empty()) {
        callback_fault.store(ERROR_INVALID_THREAD_ID); return 0;
    }
    try {
        if (++state->transaction_callbacks > 64) throw Fatal(ERROR_NOT_ENOUGH_QUOTA, "Native callback budget exceeded.");
        if (++state->next_callback >= (1ULL << 63)) throw Fatal(ERROR_INVALID_DATA, "Callback identity exhausted.");
        const auto id = (1ULL << 63) | state->next_callback;
        const auto parent = state->calls.back();
        Bytes bytes; bytes.put(state->thread); bytes.put(first); bytes.put(second);
        send(Kind::callback, event, state->generation, id, parent, bytes);
        for (;;) {
            Frame incoming; receive(incoming);
            if (incoming.header.generation != state->generation) throw Fatal(ERROR_INVALID_DATA, "Callback generation drift.");
            if (incoming.header.kind == static_cast<std::uint32_t>(Kind::callback_reply)) {
                if (incoming.header.id != id || incoming.header.parent != parent || incoming.header.operation != event)
                    throw Fatal(ERROR_INVALID_DATA, "Callback reply identity drift.");
                Reader reader{incoming.payload}; const auto result = reader.get<std::uint32_t>(); reader.finish(); return result;
            }
            if (incoming.header.operation != static_cast<std::uint32_t>(Operation::call))
                throw Fatal(ERROR_BUSY, "Only a nested call is admitted while a callback owns the stack.");
            dispatch(incoming, id);
        }
    }
    catch (const Fatal& failure) {
        // This catch runs only on the owning native call thread. Retain the
        // actual refusal; a quota/retirement fault is not a parent-ID fault.
        const auto length = std::strlen(failure.what());
        const auto retained = length < sizeof(state->callback_reason) ? length : sizeof(state->callback_reason) - 1;
        std::memcpy(state->callback_reason, failure.what(), retained); state->callback_reason[retained] = '\0';
        state->callback_reason_truncated = retained != length;
        callback_fault.store(failure.code); return 0;
    }
    catch (...) { callback_fault.store(ERROR_INVALID_DATA); return 0; }
}
std::uint32_t __cdecl cdecl_callback(void*, std::uint32_t event, std::uint32_t first, std::uint32_t second) {
    return callback(event, first, second);
}
std::uint32_t __stdcall stdcall_callback(void*, std::uint32_t event, std::uint32_t first, std::uint32_t second) {
    return callback(event, first, second);
}
HostCallbacks host_callbacks{sizeof(HostCallbacks), 1, nullptr, cdecl_callback, stdcall_callback};

void unload(const Frame& frame) {
    if (!state->module || !state->calls.empty()) { reply(frame, ERROR_BUSY, "Module is absent or still owns an active call."); return; }
    Reader reader{frame.payload}; const auto module = reader.get<std::uint64_t>(); reader.finish();
    if (module != state->module_id) { reply(frame, ERROR_INVALID_HANDLE, "Stale module identity."); return; }
    const auto previous = state->module;
    if (!FreeLibrary(previous)) throw Fatal(GetLastError(), "FreeLibrary failed; retirement is incomplete.");
    state->module = nullptr; state->contract = nullptr; state->module_id = 0; state->symbols.clear();
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
    const auto loaded = LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!loaded) { reply(frame, GetLastError(), "Windows DLL/import/entry admission failed."); return; }
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
    if (!state->module || module != state->module_id) { reply(frame, ERROR_INVALID_HANDLE, "Stale module identity."); return; }
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
    if (receipt.exception_code != 0) throw Fatal(receipt.exception_code, "Native module raised a structured exception.");
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
    switch (static_cast<Operation>(frame.header.operation)) {
    case Operation::hello: {
        Reader reader{frame.payload}; reader.finish(); Bytes data;
        data.put(GetCurrentProcessId()); data.put(state->thread); data.put(32U); data.put(max_payload); data.put(max_call_depth);
        reply(frame, ERROR_SUCCESS, "", data); break;
    }
    case Operation::load_authored: load(frame); break;
    case Operation::resolve: resolve(frame); break;
    case Operation::call: call(frame); break;
    case Operation::unload: unload(frame); break;
    case Operation::retire: {
        Reader reader{frame.payload}; reader.finish();
        if (state->module || !state->calls.empty()) { reply(frame, ERROR_BUSY, "An active module or call prevents retirement."); break; }
        state->retired = true; reply(frame, ERROR_SUCCESS, ""); break;
    }
    default: throw Fatal(ERROR_INVALID_FUNCTION, "Unknown native-domain operation.");
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
    if (argc != 3 || std::wstring(argv[1]) != L"--generation") { std::fputs("Use --generation <nonzero-uint64>.\n", stderr); return 2; }
    wchar_t* end = nullptr; const auto generation = _wcstoui64(argv[2], &end, 10);
    if (!generation || !end || *end != L'\0') { std::fputs("Invalid native-domain generation.\n", stderr); return 2; }
    input = GetStdHandle(STD_INPUT_HANDLE); output = GetStdHandle(STD_OUTPUT_HANDLE);
    if (!input || input == INVALID_HANDLE_VALUE || !output || output == INVALID_HANDLE_VALUE) return 2;
    State owned; owned.generation = generation; state = &owned;
    try {
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
            send(Kind::fault, request.operation, owned.generation, request.id, request.parent, bytes); } catch (...) { }
        std::fprintf(stderr, "OPENNV_NATIVE_DOMAIN_FAULT code=%lu owner=%s\n", static_cast<unsigned long>(failure.code), failure.what());
    }
    catch (const std::exception& failure) { std::fprintf(stderr, "OPENNV_NATIVE_DOMAIN_FAULT owner=%s\n", failure.what()); }
    // Fatal generations cannot safely run arbitrary module detach callbacks.
    // Terminate only this companion; abnormal retirement has no detach claim.
    std::fflush(stderr); state = nullptr;
    TerminateProcess(GetCurrentProcess(), 3); return 3;
}
