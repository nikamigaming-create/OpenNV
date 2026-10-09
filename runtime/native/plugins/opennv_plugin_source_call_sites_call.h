#pragma once

// One receiver in ECX, no stack arguments, UInt32/pointer EAX return. The
// authored CALL/RET producer owns this ABI independently of a retail caller.
extern "C" __declspec(naked) std::uint32_t __cdecl InvokeSourceCallSite(void*, void*, opennv_domain::CallReceipt*) {
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
        mov ecx, [ebp + 12]
        call [ebp + 8]
        mov [ebp - 20], eax
        mov edx, esp
        sub edx, [ebp - 16]
        mov ecx, [ebp + 16]
        mov [ecx + 4], edx
        xor edx, edx
        cmp ebx, 0badf00dh
        jne source_site_missing_ebx
        or edx, 1
    source_site_missing_ebx:
        cmp esi, 13579bdfh
        jne source_site_missing_esi
        or edx, 2
    source_site_missing_esi:
        cmp edi, 2468ace0h
        jne source_site_missing_edi
        or edx, 4
    source_site_missing_edi:
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
static void InvokeSourceCallSiteGuarded(void* function, void* receiver, opennv_domain::CallReceipt* receipt) {
    __try { InvokeSourceCallSite(function, receiver, receipt); }
    __except (opennv_domain::CaptureNativeException(GetExceptionInformation(), GetExceptionCode(),
        &receipt->exception_code, &receipt->exception)) { }
}
