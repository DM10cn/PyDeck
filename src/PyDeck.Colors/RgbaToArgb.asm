; RGBA bytes -> uint32_t 0xAARRGGBB (BGRA bytes on little-endian x64).
; Windows x64 ABI: RCX = source, RDX = destination, R8D = pixel count.
; Internal leaf function: caller validates buffers/count. Only volatile registers
; are used; no stack, calls, alignment requirement, or reads beyond count * 4.
; Caller must check CPU/OS support before entering either SIMD kernel.
OPTION CASEMAP:NONE

.const
ALIGN 16
; VPSHUFB shuffles independently in each 128-bit lane: broadcast this mask.
RgbaShuffle BYTE 2,1,0,3,6,5,4,7,10,9,8,11,14,13,12,15

.code
PUBLIC PyDeckRgbaToArgbAvx2
PyDeckRgbaToArgbAvx2 PROC
    vbroadcasti128 ymm1, XMMWORD PTR [RgbaShuffle]
    cmp r8d, 8
    jb avx_tail_four
avx_loop:
    vmovdqu ymm0, YMMWORD PTR [rcx]
    vpshufb ymm0, ymm0, ymm1
    vmovdqu YMMWORD PTR [rdx], ymm0
    add rcx, 32
    add rdx, 32
    sub r8d, 8
    cmp r8d, 8
    jae avx_loop
avx_tail_four:
    cmp r8d, 4
    jb avx_cleanup
    vmovdqu xmm0, XMMWORD PTR [rcx]
    vpshufb xmm0, xmm0, xmm1
    vmovdqu XMMWORD PTR [rdx], xmm0
    add rcx, 16
    add rdx, 16
    sub r8d, 4
avx_cleanup:
    ; Clear upper YMM lanes before returning to legacy SSE/managed code.
    vzeroupper
    jmp PixelScalarTail
PyDeckRgbaToArgbAvx2 ENDP

PUBLIC PyDeckRgbaToArgbSsse3
PyDeckRgbaToArgbSsse3 PROC
    cmp r8d, 4
    jb PixelScalarTail
    movdqa xmm1, XMMWORD PTR [RgbaShuffle]

ssse3_loop:
    movdqu xmm0, XMMWORD PTR [rcx]
    pshufb xmm0, xmm1
    movdqu XMMWORD PTR [rdx], xmm0
    add rcx, 16
    add rdx, 16
    sub r8d, 4
    cmp r8d, 4
    jae ssse3_loop
    jmp PixelScalarTail
PyDeckRgbaToArgbSsse3 ENDP

; Shared 0-3 pixel tail; general-purpose integer instructions only.
PixelScalarTail PROC
    test r8d, r8d
    jz finished
scalar_loop:
    mov eax, DWORD PTR [rcx]
    mov r9d, eax
    mov r10d, eax
    shl eax, 16
    shr r9d, 16
    or eax, r9d
    and eax, 00FF00FFh
    and r10d, 0FF00FF00h
    or eax, r10d
    mov DWORD PTR [rdx], eax
    add rcx, 4
    add rdx, 4
    dec r8d
    jnz scalar_loop
finished:
    ret
PixelScalarTail ENDP
END
