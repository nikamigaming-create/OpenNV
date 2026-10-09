#pragma once

// The public Query signature uses the existing two-argument shim. Load and
// message/serialization callbacks have one cdecl argument; never pad their ABI
// with a second argument or execute a callback through a mismatched prototype.
extern "C" __declspec(naked) std::uint32_t __cdecl InvokeNvseOne(void*, std::uint32_t, opennv_domain::CallReceipt*) {
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
        push [ebp + 12]
        call [ebp + 8]
        mov [ebp - 20], eax
        add esp, 4
        mov edx, esp
        sub edx, [ebp - 16]
        mov ecx, [ebp + 16]
        mov [ecx + 4], edx
        xor edx, edx
        cmp ebx, 0badf00dh
        jne nvse_missing_ebx
        or edx, 1
    nvse_missing_ebx:
        cmp esi, 13579bdfh
        jne nvse_missing_esi
        or edx, 2
    nvse_missing_esi:
        cmp edi, 2468ace0h
        jne nvse_missing_edi
        or edx, 4
    nvse_missing_edi:
        mov [ecx + 8], edx
        mov eax, [ebp - 20]
        mov [ecx], eax
        mov esp, [ebp - 16]
        add esp, 8
        pop edi
        pop esi
        pop ebx
        pop ebp
        ret
    }
}
static void InvokeNvseOneGuarded(void* function, std::uint32_t argument, opennv_domain::CallReceipt* receipt) {
    __try { InvokeNvseOne(function, argument, receipt); }
    __except (opennv_domain::CaptureNativeException(GetExceptionInformation(), GetExceptionCode(),
        &receipt->exception_code, &receipt->exception)) { }
}

inline opennv_domain::NvseRuntime::~NvseRuntime() {
    // Rejected admission never publishes these pages. Published retirement uses
    // the checked release below; this is construction/fatal-path ownership only.
    if (tables) VirtualFree(tables, 0, MEM_RELEASE);
}
