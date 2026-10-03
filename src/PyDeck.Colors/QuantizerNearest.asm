; WSMeans nearest-center search. RCX points to QuantizerSearch (Quantization.h).
; Four independent double distances per vector; separate MUL/ADD (no FMA),
; left-associative (dl*dl + da*da) + db*db, and scalar ascending-index reduction
; preserve the pinned MCU algorithm's rounding and strict first-wins ties.
; Only volatile registers; 40-byte stack has unwind metadata and 32-byte scratch.
OPTION CASEMAP:NONE

.const
ALIGN 8
Four REAL8 4.0

TRY_CANDIDATE MACRO maskbit, byteoffset, indexoffset
    LOCAL next_candidate
    test eax, maskbit
    jz next_candidate
    vcomisd xmm3, QWORD PTR [rsp + byteoffset]
    jbe next_candidate
    vmovsd xmm3, QWORD PTR [rsp + byteoffset]
    mov r11d, r10d
    shr r11d, 3
    add r11d, indexoffset
next_candidate:
ENDM

.code
PUBLIC PyDeckQuantizerNearestAvx2
PyDeckQuantizerNearestAvx2 PROC FRAME
    sub rsp, 40
    .allocstack 40
    .endprolog
    mov rdx, QWORD PTR [rcx + 32]
    mov r8, QWORD PTR [rcx + 40]
    mov r9d, DWORD PTR [rcx + 48]
    xor r10d, r10d
    mov r11d, -1
    vbroadcastsd ymm3, QWORD PTR [rcx + 24]
    vbroadcastsd ymm5, QWORD PTR [Four]
    vmulpd ymm5, ymm5, ymm3
    cmp r9d, 4
    jb tail_loop
vector_loop:
    ; Separation row has a 16-byte stride. Strip index/padding lanes.
    vmovupd ymm1, YMMWORD PTR [r8 + r10 * 2]
    vmovupd ymm2, YMMWORD PTR [r8 + r10 * 2 + 32]
    vunpcklpd ymm1, ymm1, ymm2
    vpermpd ymm1, ymm1, 0D8h
    vcmppd ymm4, ymm1, ymm5, 1
    vmovmskpd eax, ymm4
    test eax, eax
    jz next_vector

    vbroadcastsd ymm0, QWORD PTR [rcx]
    vsubpd ymm0, ymm0, YMMWORD PTR [rdx + r10]
    vbroadcastsd ymm1, QWORD PTR [rcx + 8]
    vsubpd ymm1, ymm1, YMMWORD PTR [rdx + r10 + 2048]
    vbroadcastsd ymm2, QWORD PTR [rcx + 16]
    vsubpd ymm2, ymm2, YMMWORD PTR [rdx + r10 + 4096]
    vmulpd ymm0, ymm0, ymm0
    vmulpd ymm1, ymm1, ymm1
    vmulpd ymm2, ymm2, ymm2
    vaddpd ymm0, ymm0, ymm1
    vaddpd ymm0, ymm0, ymm2
    vcmppd ymm1, ymm0, ymm3, 1
    vandpd ymm1, ymm1, ymm4
    vmovmskpd eax, ymm1
    test eax, eax
    jz next_vector
    vmovupd YMMWORD PTR [rsp], ymm0
    TRY_CANDIDATE 1, 0, 0
    TRY_CANDIDATE 2, 8, 1
    TRY_CANDIDATE 4, 16, 2
    TRY_CANDIDATE 8, 24, 3
    vbroadcastsd ymm3, xmm3
next_vector:
    add r10, 32
    sub r9d, 4
    cmp r9d, 4
    jae vector_loop
tail_loop:
    test r9d, r9d
    jz finished
    vmovsd xmm0, QWORD PTR [r8 + r10 * 2]
    vcomisd xmm0, xmm5
    jae next_tail
    vmovsd xmm0, QWORD PTR [rcx]
    vsubsd xmm0, xmm0, QWORD PTR [rdx + r10]
    vmovsd xmm1, QWORD PTR [rcx + 8]
    vsubsd xmm1, xmm1, QWORD PTR [rdx + r10 + 2048]
    vmovsd xmm2, QWORD PTR [rcx + 16]
    vsubsd xmm2, xmm2, QWORD PTR [rdx + r10 + 4096]
    vmulsd xmm0, xmm0, xmm0
    vmulsd xmm1, xmm1, xmm1
    vmulsd xmm2, xmm2, xmm2
    vaddsd xmm0, xmm0, xmm1
    vaddsd xmm0, xmm0, xmm2
    vcomisd xmm3, xmm0
    jbe next_tail
    vmovsd xmm3, xmm3, xmm0
    mov r11d, r10d
    shr r11d, 3
next_tail:
    add r10, 8
    dec r9d
    jmp tail_loop
finished:
    vmovsd QWORD PTR [rcx + 24], xmm3
    mov eax, r11d
    vzeroupper
    add rsp, 40
    ret
PyDeckQuantizerNearestAvx2 ENDP
END
