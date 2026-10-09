#pragma once

// Real eight-pointer cdecl CommandInfo.Execute invocation. No padded two-arg
// shim, guessed ECX object or scalar substitution for COMMAND_ARGS is used.
extern "C" __declspec(naked) std::uint32_t __cdecl InvokeNvseCommand(
    void*, const std::uint32_t*, opennv_domain::CallReceipt*) {
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
        mov edx, [ebp + 12]
        push [edx + 28]
        push [edx + 24]
        push [edx + 20]
        push [edx + 16]
        push [edx + 12]
        push [edx + 8]
        push [edx + 4]
        push [edx]
        call [ebp + 8]
        mov [ebp - 20], eax
        add esp, 32
        mov edx, esp
        sub edx, [ebp - 16]
        mov ecx, [ebp + 16]
        mov [ecx + 4], edx
        xor edx, edx
        cmp ebx, 0badf00dh
        jne expr_missing_ebx
        or edx, 1
    expr_missing_ebx:
        cmp esi, 13579bdfh
        jne expr_missing_esi
        or edx, 2
    expr_missing_esi:
        cmp edi, 2468ace0h
        jne expr_missing_edi
        or edx, 4
    expr_missing_edi:
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
static void InvokeNvseCommandGuarded(void* function, const std::uint32_t* arguments, opennv_domain::CallReceipt* receipt) {
    __try { InvokeNvseCommand(function, arguments, receipt); }
    __except (opennv_domain::CaptureNativeException(GetExceptionInformation(), GetExceptionCode(),
        &receipt->exception_code, &receipt->exception)) { }
}
