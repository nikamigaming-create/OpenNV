#pragma once

// The injected declaration owns ECX plus zero through four raw stack words.
// There is no task queue, form lookup or guessed game receiver in this ABI.
extern "C" __declspec(naked) std::uint32_t __cdecl InvokeNvseModuleFunction(
    void*, std::uint32_t, void*, const std::uint32_t*, std::uint32_t, opennv_domain::CallReceipt*) {
    __asm {
        push ebp
        mov ebp, esp
        push ebx
        push esi
        push edi
        sub esp, 8
        mov [ebp-16], esp
        mov ebx, 0badf00dh
        mov esi, 13579bdfh
        mov edi, 2468ace0h
        mov eax, [ebp+20]
        mov edx, [ebp+24]
    nvse_module_push_word:
        test edx, edx
        jz nvse_module_enter_function
        dec edx
        push dword ptr [eax+edx*4]
        jmp nvse_module_push_word
    nvse_module_enter_function:
        mov ecx, [ebp+16]
        xor edx, edx
        call [ebp+8]
        mov [ebp-20], eax
        cmp dword ptr [ebp+12], 1
        jne nvse_module_stack_observation
        mov eax, [ebp+24]
        lea esp, [esp+eax*4]
    nvse_module_stack_observation:
        mov edx, esp
        sub edx, [ebp-16]
        mov ecx, [ebp+28]
        mov [ecx+4], edx
        xor edx, edx
        cmp ebx, 0badf00dh
        jne nvse_module_missing_bx
        or edx, 1
    nvse_module_missing_bx:
        cmp esi, 13579bdfh
        jne nvse_module_missing_si
        or edx, 2
    nvse_module_missing_si:
        cmp edi, 2468ace0h
        jne nvse_module_missing_di
        or edx, 4
    nvse_module_missing_di:
        mov [ecx+8], edx
        mov eax, [ebp-20]
        mov [ecx], eax
        mov esp, [ebp-16]
        add esp, 8
        pop edi
        pop esi
        pop ebx
        pop ebp
        ret
    }
}
static void InvokeNvseModuleFunctionGuarded(void* function, opennv_domain::Abi abi, void* receiver,
    const std::uint32_t* words, std::uint32_t count, opennv_domain::CallReceipt* receipt) {
    __try { InvokeNvseModuleFunction(function, static_cast<std::uint32_t>(abi), receiver, words, count, receipt); }
    __except (opennv_domain::CaptureNativeException(GetExceptionInformation(), GetExceptionCode(),
        &receipt->exception_code, &receipt->exception)) { }
}
