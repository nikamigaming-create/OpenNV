#pragma once

// Actual source RegisterCallback is a two-argument cdecl export. This boundary
// measures the same x86 nonvolatile registers/stack as the other real calls.
extern "C" __declspec(naked) std::uint32_t __cdecl OpenNVInvokeSteamRegisterX86(
    void*, void*, std::uint32_t, opennv_domain::CallReceipt*) {
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
        push [ebp + 16]
        push [ebp + 12]
        call [ebp + 8]
        add esp, 8
        mov [ebp - 20], eax
        mov edx, esp
        sub edx, [ebp - 16]
        mov ecx, [ebp + 20]
        mov [ecx + 4], edx
        xor edx, edx
        cmp ebx, 0badf00dh
        jne register_missing_ebx
        or edx, 1
    register_missing_ebx:
        cmp esi, 13579bdfh
        jne register_missing_esi
        or edx, 2
    register_missing_esi:
        cmp edi, 2468ace0h
        jne register_missing_edi
        or edx, 4
    register_missing_edi:
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
static std::uint32_t OpenNVInvokeSteamRegisterGuarded(void* function, void* object,
    std::uint32_t id, opennv_domain::CallReceipt* receipt) {
    __try { return OpenNVInvokeSteamRegisterX86(function, object, id, receipt); }
    __except (EXCEPTION_EXECUTE_HANDLER) { receipt->exception_code = GetExceptionCode(); return 0; }
}
