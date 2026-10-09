option casemap:none
PUBLIC opennv_fistp_fixture_save
PUBLIC opennv_fistp_fixture_restore
PUBLIC opennv_fistp_fixture_environment
PUBLIC opennv_fistp_fixture_pending

.code
; These writers exist only in this separately built authored audit process.
; The product bridge contains no control-word, MXCSR, reset or restore writer.
opennv_fistp_fixture_save PROC
    fxsave [rcx]
    ret
opennv_fistp_fixture_save ENDP
opennv_fistp_fixture_restore PROC FRAME
    sub rsp, 228h
    .allocstack 228h
    .endprolog
    ; Restore original x87/MXCSR while retaining the caller's CURRENT XMM
    ; lanes, including Win64 nonvolatile XMM6..XMM15. FXRSTOR of an old full
    ; snapshot would otherwise violate the C++ caller's actual register lease.
    fxsave [rsp+20h]
    lea r10, [rsp+20h]
    xor eax, eax
restore_prefix:
    mov r11, qword ptr [rcx+rax]
    mov qword ptr [r10+rax], r11
    add eax, 8
    cmp eax, 160
    jb restore_prefix
    fnclex
    fxrstor [rsp+20h]
    add rsp, 228h
    ret
opennv_fistp_fixture_restore ENDP
opennv_fistp_fixture_environment PROC
    mov word ptr [rsp+8], cx
    mov dword ptr [rsp+12], r8d
    fninit
    fldcw word ptr [rsp+8]
    ldmxcsr dword ptr [rsp+12]
    mov eax, edx
fill:
    test eax, eax
    jz filled
    fld1
    dec eax
    jmp fill
filled:
    ret
opennv_fistp_fixture_environment ENDP
opennv_fistp_fixture_pending PROC
    fninit
    fnstcw word ptr [rsp+8]
    and word ptr [rsp+8], 0fffbh
    fldcw word ptr [rsp+8]
    fld1
    fldz
    fdivp st(1), st(0)
    ; No waiting instruction follows the deliberate pending x87 exception.
    ret
opennv_fistp_fixture_pending ENDP
END
