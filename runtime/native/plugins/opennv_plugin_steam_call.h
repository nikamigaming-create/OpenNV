#pragma once

// First-party exact zero/one-argument call boundary. Never pass phantom
// arguments to a source export or use a 64-bit host pointer as the receiver.
extern "C" __declspec(naked) std::uint32_t __cdecl OpenNVInvokeSteamX86(
    void*, std::uint32_t, void*, std::uint32_t, std::uint32_t, opennv_domain::CallReceipt*) {
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
        cmp dword ptr [ebp + 20], 0
        je no_argument
        push [ebp + 24]
    no_argument:
        call [ebp + 8]
        mov [ebp - 20], eax
        cmp dword ptr [ebp + 12], 1
        jne called_cleanup
        cmp dword ptr [ebp + 20], 0
        je called_cleanup
        add esp, 4
    called_cleanup:
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
        mov esp, [ebp - 16]
        add esp, 8
        pop edi
        pop esi
        pop ebx
        pop ebp
        ret
    }
}
static std::uint32_t OpenNVInvokeSteamGuarded(void* function, opennv_domain::Abi abi, void* receiver,
    std::uint32_t arguments, std::uint32_t argument, opennv_domain::CallReceipt* receipt) {
    __try { return OpenNVInvokeSteamX86(function, static_cast<std::uint32_t>(abi), receiver, arguments, argument, receipt); }
    __except (EXCEPTION_EXECUTE_HANDLER) { receipt->exception_code = GetExceptionCode(); return 0; }
}
