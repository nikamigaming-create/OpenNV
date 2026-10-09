option casemap:none
PUBLIC opennv_host_fistp32_asm

.code
opennv_host_fistp32_asm PROC FRAME
    sub rsp, 228h
    .allocstack 228h
    .endprolog
    mov r10, rcx
    ; FXSAVE is non-destructive and does not deliver a pending x87 exception.
    ; At Win64 entry RSP is 8 mod 16; this frame and area are 16-byte aligned.
    fxsave [rsp+20h]
    mov ax, word ptr [rsp+20h]
    mov word ptr [r10+24], ax
    mov word ptr [r10+28], ax
    mov ax, word ptr [rsp+22h]
    mov word ptr [r10+26], ax
    mov word ptr [r10+30], ax
    mov word ptr [r10+44], ax
    mov al, byte ptr [rsp+24h]
    mov byte ptr [r10+32], al
    mov byte ptr [r10+33], al
    cmp byte ptr [r10+35], 0
    jne convert
    mov byte ptr [r10+34], 1
    jmp returned
convert:
    test word ptr [r10+26], 80h
    jnz pending
    movzx eax, word ptr [r10+24]
    mov ecx, eax
    and ecx, 3fh
    cmp ecx, 3fh
    jne unmasked
    and eax, 300h
    cmp eax, 100h
    je reserved
    ; The abridged tag bits are physical R0..R7, not logical ST0..ST7.
    ; Preserve all existing registers; only push into an actual empty slot.
    movzx ecx, word ptr [r10+26]
    shr ecx, 11
    dec ecx
    and ecx, 7
    mov eax, 1
    shl eax, cl
    test byte ptr [r10+32], al
    jnz occupied
    cmp byte ptr [r10+35], 2
    jne integer_value
    ; The selected source caller first stores its argument as Float32. Retain
    ; its actual buffered bits and flags independently from the helper result.
    fld dword ptr [r10+16]
    fstp dword ptr [r10+40]
    fxsave [rsp+20h]
    mov ax, word ptr [rsp+22h]
    mov word ptr [r10+44], ax
integer_value:
    fld dword ptr [r10+40]
    fistp dword ptr [r10+20]
    fxsave [rsp+20h]
    mov ax, word ptr [rsp+20h]
    mov word ptr [r10+28], ax
    mov ax, word ptr [rsp+22h]
    mov word ptr [r10+30], ax
    mov al, byte ptr [rsp+24h]
    mov byte ptr [r10+33], al
    mov byte ptr [r10+34], 2
    jmp returned
pending:
    mov byte ptr [r10+34], 3
    jmp returned
unmasked:
    mov byte ptr [r10+34], 4
    jmp returned
reserved:
    mov byte ptr [r10+34], 5
    jmp returned
occupied:
    mov byte ptr [r10+34], 6
returned:
    add rsp, 228h
    ret
opennv_host_fistp32_asm ENDP
END
