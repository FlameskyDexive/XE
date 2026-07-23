## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Linq_ArrayFirstOrDefault()
;     public int Linq_ArrayFirstOrDefault() => _array.FirstOrDefault(static value => value < 0);
;                                              ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,50
       lea       rbp,[rsp+80]
       xor       eax,eax
       mov       [rbp-58],rax
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-50],ymm4
       mov       rbx,[rcx+8]
       mov       rcx,10D9E801338
       mov       rsi,[rcx]
       test      rsi,rsi
       je        near ptr M00_L07
M00_L00:
       test      rbx,rbx
       je        near ptr M00_L08
       mov       edi,1
       mov       rdx,offset MT_System.Int32[]
       cmp       [rbx],rdx
       jne       near ptr M00_L09
       lea       r14,[rbx+10]
       mov       eax,[rbx+8]
M00_L01:
       test      edi,edi
       je        near ptr M00_L15
       test      eax,eax
       jle       short M00_L03
       mov       rbx,[rsi+18]
       mov       rdx,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ArrayFirstOrDefault>b__11_0(Int32)
       cmp       rbx,rdx
       jne       near ptr M00_L12
       xor       edx,edx
M00_L02:
       mov       edi,[r14+rdx]
       test      edi,edi
       jl        near ptr M00_L14
       add       rdx,4
       dec       eax
       jne       short M00_L02
M00_L03:
       xor       edi,edi
M00_L04:
       mov       eax,edi
       add       rsp,50
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M00_L05:
       mov       edi,[r14+r15]
       mov       rdx,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ArrayFirstOrDefault>b__11_0(Int32)
       cmp       rbx,rdx
       jne       near ptr M00_L13
       test      edi,edi
       jl        near ptr M00_L14
M00_L06:
       add       r15,4
       dec       r13d
       jne       short M00_L05
       jmp       short M00_L03
M00_L07:
       mov       rcx,offset MT_System.Func<System.Int32, System.Boolean>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdx,10D9E801330
       mov       rdx,[rdx]
       mov       rcx,rsi
       mov       r8,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ArrayFirstOrDefault>b__11_0(Int32)
       call      qword ptr [7FF9DDBC6BB0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,10D9E801338
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M00_L00
M00_L08:
       mov       ecx,11
       call      qword ptr [7FF9DDBCF930]
       int       3
M00_L09:
       mov       rdx,offset MT_System.Collections.Generic.List<System.Int32>
       cmp       [rbx],rdx
       jne       short M00_L11
       mov       edx,[rbx+10]
       mov       rcx,[rbx+8]
       cmp       [rcx+8],edx
       jae       short M00_L10
       call      qword ptr [7FF9DDBCF480]
       int       3
M00_L10:
       add       rcx,10
       mov       [rbp-58],rcx
       mov       [rbp-50],edx
       lea       rdx,[rbp-58]
       lea       rcx,[rbp-40]
       call      qword ptr [7FF9DDFA5E60]
       mov       r14,[rbp-40]
       mov       eax,[rbp-38]
       jmp       near ptr M00_L01
M00_L11:
       xor       r14d,r14d
       xor       eax,eax
       xor       edi,edi
       jmp       near ptr M00_L01
M00_L12:
       xor       r15d,r15d
       mov       r13d,eax
       jmp       near ptr M00_L05
M00_L13:
       mov       edx,edi
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       test      eax,eax
       je        near ptr M00_L06
M00_L14:
       jmp       near ptr M00_L04
M00_L15:
       mov       rcx,rbx
       mov       r11,7FF9DDB10528
       call      qword ptr [r11]
       mov       [rbp-60],rax
M00_L16:
       mov       rcx,[rbp-60]
       mov       r11,7FF9DDB10530
       call      qword ptr [r11]
       test      eax,eax
       je        short M00_L17
       mov       rcx,[rbp-60]
       mov       r11,7FF9DDB10538
       call      qword ptr [r11]
       mov       edi,eax
       mov       edx,edi
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       test      eax,eax
       je        short M00_L16
       mov       [rbp-44],edi
       jmp       short M00_L18
M00_L17:
       mov       rcx,[rbp-60]
       mov       r11,7FF9DDB10540
       call      qword ptr [r11]
       jmp       near ptr M00_L03
M00_L18:
       call      M00_L19
       nop
       mov       edi,[rbp-44]
       jmp       near ptr M00_L04
M00_L19:
       sub       rsp,28
       cmp       qword ptr [rbp-60],0
       je        short M00_L20
       mov       rcx,[rbp-60]
       mov       r11,7FF9DDB10540
       call      qword ptr [r11]
M00_L20:
       nop
       add       rsp,28
       ret
; Total bytes of code 570
```
```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ArrayFirstOrDefault>b__11_0(Int32)
;     public int Linq_ArrayFirstOrDefault() => _array.FirstOrDefault(static value => value < 0);
;                                                                                    ^^^^^^^^^
       mov       eax,edx
       shr       eax,1F
       ret
; Total bytes of code 6
```
```assembly
; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       test      rsi,rsi
       je        short M02_L00
       mov       rcx,7FF9DE0109C4
       call      CORINFO_HELP_COUNTPROFILE32
       lea       rcx,[rbx+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbx+18],rdi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M02_L00:
       mov       rcx,7FF9DE0109C0
       call      CORINFO_HELP_COUNTPROFILE32
       call      qword ptr [7FF9DDFAD098]
       int       3
; Total bytes of code 82
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Loop_ArrayFirstOrDefault()
;         for (int i = 0; i < _array.Length; i++)
;              ^^^^^^^^^
;             if (_array[i] < 0)
;             ^^^^^^^^^^^^^^^^^^
;                 return _array[i];
;                 ^^^^^^^^^^^^^^^^^
;         return default;
;         ^^^^^^^^^^^^^^^
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+8]
       cmp       dword ptr [rcx+8],0
       jle       short M00_L01
M00_L00:
       mov       rdx,rcx
       cmp       eax,[rdx+8]
       jae       short M00_L03
       cmp       dword ptr [rdx+rax*4+10],0
       jl        short M00_L02
       inc       eax
       cmp       [rcx+8],eax
       jg        short M00_L00
M00_L01:
       xor       eax,eax
       add       rsp,28
       ret
M00_L02:
       cmp       eax,[rcx+8]
       jae       short M00_L03
       mov       eax,eax
       mov       eax,[rcx+rax*4+10]
       add       rsp,28
       ret
M00_L03:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 67
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Linq_ArrayFirstOrDefault()
;     public int Linq_ArrayFirstOrDefault() => _array.FirstOrDefault(static value => value < 0);
;                                              ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
       push      rbp
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,58
       lea       rbp,[rsp+80]
       vxorps    xmm4,xmm4,xmm4
       vmovdqu   ymmword ptr [rbp-50],ymm4
       xor       eax,eax
       mov       [rbp-30],rax
       mov       rbx,[rcx+8]
       mov       rcx,2A2B1C01338
       mov       rsi,[rcx]
       test      rsi,rsi
       je        near ptr M00_L12
M00_L00:
       test      rbx,rbx
       je        near ptr M00_L11
       mov       edi,1
       mov       rdx,offset MT_System.Int32[]
       cmp       [rbx],rdx
       jne       near ptr M00_L13
       lea       r14,[rbx+10]
       mov       eax,[rbx+8]
M00_L01:
       test      edi,edi
       je        short M00_L08
       test      eax,eax
       jle       short M00_L03
       mov       rdx,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ArrayFirstOrDefault>b__11_0(Int32)
       cmp       [rsi+18],rdx
       jne       near ptr M00_L16
       xor       edx,edx
M00_L02:
       mov       ebx,[r14+rdx]
       test      ebx,ebx
       jl        short M00_L07
       add       rdx,4
       dec       eax
       jne       short M00_L02
M00_L03:
       xor       ebx,ebx
M00_L04:
       mov       eax,ebx
       add       rsp,58
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       pop       rbp
       ret
M00_L05:
       mov       ebx,[r14+rdi]
       mov       rdx,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ArrayFirstOrDefault>b__11_0(Int32)
       cmp       [rsi+18],rdx
       jne       near ptr M00_L17
       test      ebx,ebx
       jl        short M00_L07
M00_L06:
       add       rdi,4
       dec       r15d
       jne       short M00_L05
       jmp       short M00_L03
M00_L07:
       jmp       short M00_L04
M00_L08:
       mov       rcx,rbx
       mov       r11,7FF9DDB10568
       call      qword ptr [r11]
       mov       [rbp-58],rax
M00_L09:
       mov       rcx,[rbp-58]
       mov       r11,7FF9DDB10570
       call      qword ptr [r11]
       test      eax,eax
       je        short M00_L10
       mov       rcx,[rbp-58]
       mov       r11,7FF9DDB10578
       call      qword ptr [r11]
       mov       ebx,eax
       mov       edx,ebx
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       test      eax,eax
       je        short M00_L09
       mov       [rbp-3C],ebx
       call      M00_L18
       nop
       mov       ebx,[rbp-3C]
       jmp       near ptr M00_L04
M00_L10:
       mov       rcx,[rbp-58]
       mov       r11,7FF9DDB10580
       call      qword ptr [r11]
       jmp       near ptr M00_L03
M00_L11:
       mov       ecx,11
       call      qword ptr [7FF9DDBCF930]
       int       3
M00_L12:
       mov       rcx,offset MT_System.Func<System.Int32, System.Boolean>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdx,2A2B1C01330
       mov       rdx,[rdx]
       mov       rcx,rsi
       mov       r8,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ArrayFirstOrDefault>b__11_0(Int32)
       call      qword ptr [7FF9DDBC6BB0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,2A2B1C01338
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M00_L00
M00_L13:
       mov       rdx,offset MT_System.Collections.Generic.List<System.Int32>
       cmp       [rbx],rdx
       jne       short M00_L15
       mov       edx,[rbx+10]
       mov       rcx,[rbx+8]
       cmp       [rcx+8],edx
       jae       short M00_L14
       call      qword ptr [7FF9DDBCF480]
       int       3
M00_L14:
       add       rcx,10
       mov       [rbp-50],rcx
       mov       [rbp-48],edx
       lea       rdx,[rbp-50]
       lea       rcx,[rbp-38]
       call      qword ptr [7FF9DDFA5E78]
       mov       r14,[rbp-38]
       mov       eax,[rbp-30]
       jmp       near ptr M00_L01
M00_L15:
       xor       r14d,r14d
       xor       eax,eax
       xor       edi,edi
       jmp       near ptr M00_L01
M00_L16:
       xor       edi,edi
       mov       r15d,eax
       jmp       near ptr M00_L05
M00_L17:
       mov       edx,ebx
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       test      eax,eax
       je        near ptr M00_L06
       jmp       near ptr M00_L07
M00_L18:
       sub       rsp,28
       cmp       qword ptr [rbp-58],0
       je        short M00_L19
       mov       rcx,[rbp-58]
       mov       r11,7FF9DDB10580
       call      qword ptr [r11]
M00_L19:
       nop
       add       rsp,28
       ret
; Total bytes of code 551
```
```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ArrayFirstOrDefault>b__11_0(Int32)
;     public int Linq_ArrayFirstOrDefault() => _array.FirstOrDefault(static value => value < 0);
;                                                                                    ^^^^^^^^^
       mov       eax,edx
       shr       eax,1F
       ret
; Total bytes of code 6
```
```assembly
; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       test      rsi,rsi
       je        short M02_L00
       mov       rcx,7FF9DE01044C
       call      CORINFO_HELP_COUNTPROFILE32
       lea       rcx,[rbx+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbx+18],rdi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M02_L00:
       mov       rcx,7FF9DE010448
       call      CORINFO_HELP_COUNTPROFILE32
       call      qword ptr [7FF9DDFACEB8]
       int       3
; Total bytes of code 82
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Loop_ArrayFirstOrDefault()
;         for (int i = 0; i < _array.Length; i++)
;              ^^^^^^^^^
;             if (_array[i] < 0)
;             ^^^^^^^^^^^^^^^^^^
;                 return _array[i];
;                 ^^^^^^^^^^^^^^^^^
;         return default;
;         ^^^^^^^^^^^^^^^
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+8]
       cmp       dword ptr [rcx+8],0
       jle       short M00_L01
M00_L00:
       mov       rdx,rcx
       cmp       eax,[rdx+8]
       jae       short M00_L03
       cmp       dword ptr [rdx+rax*4+10],0
       jl        short M00_L02
       inc       eax
       cmp       [rcx+8],eax
       jg        short M00_L00
M00_L01:
       xor       eax,eax
       add       rsp,28
       ret
M00_L02:
       cmp       eax,[rcx+8]
       jae       short M00_L03
       mov       eax,eax
       mov       eax,[rcx+rax*4+10]
       add       rsp,28
       ret
M00_L03:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 67
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Linq_ArrayToList()
;     public List<int> Linq_ArrayToList() => _array.ToList();
;                                            ^^^^^^^^^^^^^^^
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       lea       rbp,[rsp+50]
       mov       rbx,[rcx+8]
       test      rbx,rbx
       je        near ptr M00_L08
       mov       rcx,rbx
       mov       rdx,offset MT_System.Int32[]
       cmp       [rcx],rdx
       jne       near ptr M00_L09
       xor       ecx,ecx
M00_L00:
       test      rcx,rcx
       jne       near ptr M00_L10
       mov       rcx,offset MT_System.Collections.Generic.List<System.Int32>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       edi,[rbx+8]
       test      edi,edi
       je        near ptr M00_L11
       mov       edx,edi
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rsi+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,[rsi+8]
       mov       ecx,[rbx+8]
       mov       rcx,[rbx]
       cmp       rcx,[r8]
       jne       near ptr M00_L18
       cmp       dword ptr [rcx+4],18
       jne       near ptr M00_L18
       cmp       edi,[rbx+8]
       ja        near ptr M00_L18
       cmp       edi,[r8+8]
       ja        near ptr M00_L18
       mov       edx,edi
       movzx     eax,word ptr [rcx]
       imul      rax,rdx
       lea       rdx,[rbx+10]
       add       r8,10
       test      dword ptr [rcx],1000000
       jne       near ptr M00_L12
       mov       rcx,r8
       mov       r10,rdx
       mov       r9,rax
       mov       r11,rcx
       sub       r11,r10
       cmp       r11,r9
       jb        near ptr M00_L16
       mov       r11,r10
       sub       r11,rcx
       cmp       r11,r9
       jb        near ptr M00_L16
       lea       r11,[r10+r9]
       lea       rbx,[rcx+r9]
       cmp       r9,10
       jbe       near ptr M00_L13
       cmp       r9,40
       jbe       short M00_L03
       cmp       r9,800
       ja        near ptr M00_L17
       cmp       r9,100
       jb        short M00_L01
       mov       r10,r8
       and       r10,3F
       mov       r9,r10
       neg       r9
       add       r9,40
       vmovdqu   ymm0,ymmword ptr [rdx]
       vmovdqu   ymmword ptr [r8],ymm0
       vmovdqu   ymm0,ymmword ptr [rdx+20]
       vmovdqu   ymmword ptr [r8+20],ymm0
       lea       r10,[rdx+r9]
       lea       rcx,[r8+r9]
       sub       rax,r9
       mov       r9,rax
M00_L01:
       mov       rdx,r9
       shr       rdx,6
M00_L02:
       vmovdqu   ymm0,ymmword ptr [r10]
       vmovdqu   ymmword ptr [rcx],ymm0
       vmovdqu   ymm0,ymmword ptr [r10+20]
       vmovdqu   ymmword ptr [rcx+20],ymm0
       add       rcx,40
       add       r10,40
       dec       rdx
       jne       short M00_L02
       and       r9,3F
       cmp       r9,10
       jbe       short M00_L04
M00_L03:
       vmovups   xmm0,[r10]
       vmovups   [rcx],xmm0
       cmp       r9,20
       jbe       short M00_L04
       vmovups   xmm0,[r10+10]
       vmovups   [rcx+10],xmm0
       cmp       r9,30
       ja        short M00_L07
M00_L04:
       vmovups   xmm0,[r11-10]
       vmovups   [rbx-10],xmm0
M00_L05:
       mov       [rsi+10],edi
M00_L06:
       mov       rax,rsi
       vzeroupper
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
M00_L07:
       vmovups   xmm0,[r10+20]
       vmovups   [rcx+20],xmm0
       jmp       short M00_L04
M00_L08:
       mov       ecx,11
       call      qword ptr [7FF9DDBEF930]
       int       3
M00_L09:
       mov       rdx,rbx
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<System.Int32>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rcx,rax
       jmp       near ptr M00_L00
M00_L10:
       mov       rax,[rcx]
       mov       rax,[rax+48]
       vzeroupper
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       jmp       qword ptr [rax+38]
M00_L11:
       mov       rax,2532D1722E0
       mov       [rsi+8],rax
       jmp       short M00_L06
M00_L12:
       mov       rcx,r8
       mov       r8,rax
       call      qword ptr [7FF9DDBE57A0]
       jmp       short M00_L05
M00_L13:
       test      al,18
       je        short M00_L14
       mov       rdx,[rdx]
       mov       [r8],rdx
       mov       r8,[r11-8]
       mov       [rbx-8],r8
       jmp       near ptr M00_L05
M00_L14:
       test      al,4
       je        short M00_L15
       mov       edx,[rdx]
       mov       [r8],edx
       mov       r8d,[r11-4]
       mov       [rbx-4],r8d
       jmp       near ptr M00_L05
M00_L15:
       test      rax,rax
       je        near ptr M00_L05
       movzx     edx,byte ptr [rdx]
       mov       [r8],dl
       test      al,2
       je        near ptr M00_L05
       movsx     r8,word ptr [r11-2]
       mov       [rbx-2],r8w
       jmp       near ptr M00_L05
M00_L16:
       cmp       r8,rdx
       je        near ptr M00_L05
M00_L17:
       cmp       [r8],r8b
       mov       rcx,r8
       mov       r8,rax
       call      qword ptr [7FF9DDBE66E8]; System.Buffer.MemmoveInternal(Byte ByRef, Byte ByRef, UIntPtr)
       jmp       near ptr M00_L05
M00_L18:
       mov       [rsp+20],edi
       xor       ecx,ecx
       mov       [rsp+28],ecx
       mov       rcx,rbx
       xor       edx,edx
       xor       r9d,r9d
       call      qword ptr [7FF9DDFC5FE0]; System.Array.CopyImpl(System.Array, Int32, System.Array, Int32, Int32, Boolean)
       jmp       near ptr M00_L05
; Total bytes of code 685
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rdx,rdx
       je        short M01_L02
       mov       rax,[rdx]
       cmp       rax,rcx
       je        short M01_L02
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M01_L02
M01_L00:
       test      rax,rax
       je        short M01_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M01_L02
       test      rax,rax
       je        short M01_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M01_L02
       test      rax,rax
       jne       short M01_L03
M01_L01:
       xor       edx,edx
M01_L02:
       mov       rax,rdx
       ret
M01_L03:
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M01_L02
       test      rax,rax
       je        short M01_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M01_L02
       jmp       short M01_L00
; Total bytes of code 86
```
```assembly
; System.Buffer.MemmoveInternal(Byte ByRef, Byte ByRef, UIntPtr)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,0A8
       lea       rbp,[rsp+0E0]
       mov       [rbp-40],rcx
       mov       [rbp-48],rdx
       mov       [rbp-0A8],rcx
       mov       [rbp-0B0],rdx
       mov       [rbp-0B8],r8
       lea       rcx,[rbp-0A0]
       call      qword ptr [7FFA3D629030]; CORINFO_HELP_JIT_PINVOKE_BEGIN
       mov       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       rcx,[rbp-0A8]
       mov       rdx,[rbp-0B0]
       mov       r8,[rbp-0B8]
       call      qword ptr [rax]
       lea       rcx,[rbp-0A0]
       call      qword ptr [7FFA3D629038]; CORINFO_HELP_JIT_PINVOKE_END
       xor       eax,eax
       mov       [rbp-48],rax
       mov       [rbp-40],rax
       add       rsp,0A8
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 142
```
```assembly
; System.Array.CopyImpl(System.Array, Int32, System.Array, Int32, Int32, Boolean)
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       edi,edx
       mov       rsi,r8
       mov       ebp,r9d
       test      rbx,rbx
       je        near ptr M03_L09
       test      rsi,rsi
       je        near ptr M03_L08
       mov       rcx,[rbx]
       cmp       rcx,[rsi]
       je        short M03_L00
       mov       rcx,[rbx]
       mov       ecx,[rcx+4]
       add       ecx,0FFFFFFE8
       shr       ecx,3
       mov       edx,1
       test      ecx,ecx
       cmove     ecx,edx
       mov       rdx,[rsi]
       mov       edx,[rdx+4]
       add       edx,0FFFFFFE8
       shr       edx,3
       mov       eax,1
       test      edx,edx
       cmove     edx,eax
       cmp       ecx,edx
       jne       near ptr M03_L10
M03_L00:
       mov       r14d,[rsp+70]
       test      r14d,r14d
       jl        near ptr M03_L11
       mov       rcx,rbx
       xor       edx,edx
       call      qword ptr [7FFA3D63A390]; Precode of System.Array.GetLowerBound(Int32)
       cmp       edi,eax
       jl        near ptr M03_L07
       sub       edi,eax
       js        near ptr M03_L12
       lea       ecx,[rdi+r14]
       cmp       ecx,[rbx+8]
       ja        near ptr M03_L12
       mov       rcx,rsi
       xor       edx,edx
       call      qword ptr [7FFA3D63A390]; Precode of System.Array.GetLowerBound(Int32)
       cmp       ebp,eax
       jl        near ptr M03_L06
       sub       ebp,eax
       js        near ptr M03_L13
       lea       ecx,[r14+rbp]
       cmp       ecx,[rsi+8]
       ja        near ptr M03_L13
       mov       rcx,[rbx]
       cmp       rcx,[rsi]
       je        short M03_L01
       mov       rcx,rbx
       mov       rdx,rsi
       call      qword ptr [7FFA3D63A2B0]
       test      eax,eax
       je        short M03_L01
       cmp       byte ptr [rsp+78],0
       jne       near ptr M03_L16
       mov       [rsp+70],r14d
       mov       [rsp+78],eax
       mov       rcx,rbx
       mov       edx,edi
       mov       r8,rsi
       mov       r9d,ebp
       lea       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       jmp       qword ptr [rax]
M03_L01:
       mov       rcx,[rbx]
       movzx     edx,word ptr [rcx]
       mov       r8d,r14d
       imul      r8,rdx
       lea       rax,[rbx+8]
       mov       r10,[rbx]
       mov       r10d,[r10+4]
       add       r10,0FFFFFFFFFFFFFFF0
       add       rax,r10
       mov       r10d,edi
       imul      r10,rdx
       add       r10,rax
       lea       rax,[rsi+8]
       mov       r9,[rsi]
       mov       r9d,[r9+4]
       add       r9,0FFFFFFFFFFFFFFF0
       add       rax,r9
       mov       r9d,ebp
       imul      rdx,r9
       add       rdx,rax
       test      dword ptr [rcx],1000000
       jne       short M03_L04
       mov       rcx,rdx
       mov       rdx,r10
       call      qword ptr [7FFA3D63D928]; Precode of System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
M03_L02:
       mov       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       cmp       dword ptr [rax],0
       jne       near ptr M03_L15
M03_L03:
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M03_L04:
       cmp       r8,4000
       jbe       short M03_L05
       mov       rcx,rdx
       mov       rdx,r10
       lea       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       jmp       qword ptr [rax]
M03_L05:
       mov       rcx,rdx
       mov       rdx,r10
       call      qword ptr [7FFA3D63A630]
       mov       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       cmp       dword ptr [rax],0
       je        short M03_L02
       jmp       near ptr M03_L14
M03_L06:
       mov       r8,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       r8,[r8]
       mov       ecx,ebp
       mov       edx,eax
       call      qword ptr [7FFA3D651920]
       int       3
M03_L07:
       mov       r8,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       r8,[r8]
       mov       ecx,edi
       mov       edx,eax
       call      qword ptr [7FFA3D651920]
       int       3
M03_L08:
       mov       rcx,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       rcx,[rcx]
       call      qword ptr [7FFA3D63C210]
       int       3
M03_L09:
       mov       rcx,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       rcx,[rcx]
       call      qword ptr [7FFA3D63C210]
       int       3
M03_L10:
       call      qword ptr [7FFA3D633788]
       mov       rbx,rax
       call      qword ptr [7FFA3D63ED58]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FFA3D63D7B8]
       mov       rcx,rbx
       call      qword ptr [7FFA3D628FC0]; CORINFO_HELP_THROW
       int       3
M03_L11:
       mov       rdx,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       rdx,[rdx]
       mov       ecx,r14d
       call      qword ptr [7FFA3D651908]
       int       3
M03_L12:
       call      qword ptr [7FFA3D633550]
       mov       rdi,rax
       call      qword ptr [7FFA3D63DC10]
       mov       rdx,rax
       mov       r8,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       r8,[r8]
       mov       rcx,rdi
       call      qword ptr [7FFA3D63C1C0]
       mov       rcx,rdi
       call      qword ptr [7FFA3D628FC0]; CORINFO_HELP_THROW
       int       3
M03_L13:
       call      qword ptr [7FFA3D633550]
       mov       rbp,rax
       call      qword ptr [7FFA3D63DC08]
       mov       rdx,rax
       mov       r8,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       r8,[r8]
       mov       rcx,rbp
       call      qword ptr [7FFA3D63C1C0]
       mov       rcx,rbp
       call      qword ptr [7FFA3D628FC0]; CORINFO_HELP_THROW
       int       3
M03_L14:
       call      qword ptr [7FFA3D629040]; CORINFO_HELP_POLL_GC
       jmp       near ptr M03_L02
M03_L15:
       call      qword ptr [7FFA3D629040]; CORINFO_HELP_POLL_GC
       jmp       near ptr M03_L03
M03_L16:
       call      qword ptr [7FFA3D633578]
       mov       rbx,rax
       call      qword ptr [7FFA3D63E6D8]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FFA3D63C240]
       mov       rcx,rbx
       call      qword ptr [7FFA3D628FC0]; CORINFO_HELP_THROW
       int       3
; Total bytes of code 734
```
```assembly
; System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rdx
       call      qword ptr [7FFA3D62EC68]
       mov       rsi,rax
       mov       rcx,rax
       call      qword ptr [7FFA3D629058]; Precode of System.RuntimeTypeHandle.GetRuntimeTypeFromHandle(IntPtr)
       mov       rdx,rax
       mov       rcx,rbx
       mov       r8d,1
       call      qword ptr [7FFA3D63A5E8]
       mov       r8,rax
       test      r8,r8
       je        short M04_L00
       mov       rcx,rsi
       cmp       [r8],rcx
       je        short M04_L00
       mov       rdx,rax
       call      qword ptr [7FFA3D629090]; Precode of System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       r8,rax
M04_L00:
       mov       rax,r8
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 88
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Loop_ArrayToList()
;         var result = new List<int>(_array.Length);
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         for (int i = 0; i < _array.Length; i++)
;              ^^^^^^^^^
;             result.Add(_array[i]);
;             ^^^^^^^^^^^^^^^^^^^^^^
;         return result;
;         ^^^^^^^^^^^^^^
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rcx,offset MT_System.Collections.Generic.List<System.Int32>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdi,[rbx+8]
       mov       edx,[rdi+8]
       test      edx,edx
       je        short M00_L04
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rsi+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
M00_L00:
       xor       ebp,ebp
       cmp       dword ptr [rdi+8],0
       jle       short M00_L03
M00_L01:
       mov       rcx,[rbx+8]
       cmp       ebp,[rcx+8]
       jae       short M00_L06
       mov       edx,[rcx+rbp*4+10]
       inc       dword ptr [rsi+14]
       mov       rcx,[rsi+8]
       mov       eax,[rsi+10]
       mov       r8d,[rcx+8]
       cmp       r8d,eax
       jbe       short M00_L05
       lea       r8d,[rax+1]
       mov       [rsi+10],r8d
       mov       [rcx+rax*4+10],edx
M00_L02:
       inc       ebp
       mov       rax,[rbx+8]
       cmp       [rax+8],ebp
       jg        short M00_L01
M00_L03:
       mov       rax,rsi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L04:
       mov       rcx,23E864E22E0
       mov       [rsi+8],rcx
       jmp       short M00_L00
M00_L05:
       mov       rcx,rsi
       call      qword ptr [7FF9DDF95D10]
       jmp       short M00_L02
M00_L06:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 175
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Linq_ArrayToList()
;     public List<int> Linq_ArrayToList() => _array.ToList();
;                                            ^^^^^^^^^^^^^^^
       push      rbp
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       lea       rbp,[rsp+50]
       mov       rbx,[rcx+8]
       test      rbx,rbx
       je        near ptr M00_L04
       mov       rcx,rbx
       mov       rdx,offset MT_System.Int32[]
       cmp       [rcx],rdx
       jne       near ptr M00_L05
       xor       ecx,ecx
M00_L00:
       test      rcx,rcx
       jne       near ptr M00_L06
       mov       rcx,offset MT_System.Collections.Generic.List<System.Int32>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       edi,[rbx+8]
       test      edi,edi
       je        near ptr M00_L07
       mov       edx,edi
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rsi+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       r8,[rsi+8]
       mov       rcx,[rbx]
       cmp       rcx,[r8]
       jne       near ptr M00_L18
       cmp       dword ptr [rcx+4],18
       jne       near ptr M00_L18
       cmp       edi,[rbx+8]
       ja        near ptr M00_L18
       cmp       edi,[r8+8]
       ja        near ptr M00_L18
       mov       edx,edi
       movzx     eax,word ptr [rcx]
       imul      rax,rdx
       lea       rdx,[rbx+10]
       add       r8,10
       test      dword ptr [rcx],1000000
       jne       near ptr M00_L08
       mov       rcx,r8
       mov       r10,rdx
       mov       r9,rax
       mov       r11,rcx
       sub       r11,r10
       cmp       r11,r9
       jb        near ptr M00_L17
       mov       r11,r10
       sub       r11,rcx
       cmp       r11,r9
       jb        near ptr M00_L17
       lea       r11,[r10+r9]
       lea       rbx,[rcx+r9]
       cmp       r9,10
       jbe       near ptr M00_L10
       cmp       r9,40
       jbe       near ptr M00_L09
       cmp       r9,800
       jbe       near ptr M00_L13
M00_L01:
       cmp       [r8],r8b
       mov       rcx,r8
       mov       r8,rax
       call      qword ptr [7FF9DDBC66E8]; System.Buffer.MemmoveInternal(Byte ByRef, Byte ByRef, UIntPtr)
M00_L02:
       mov       [rsi+10],edi
M00_L03:
       mov       rax,rsi
       vzeroupper
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       ret
M00_L04:
       mov       ecx,11
       call      qword ptr [7FF9DDBCF930]
       int       3
M00_L05:
       mov       rdx,rbx
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<System.Int32>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rcx,rax
       jmp       near ptr M00_L00
M00_L06:
       mov       rax,[rcx]
       mov       rax,[rax+48]
       vzeroupper
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       rbp
       jmp       qword ptr [rax+38]
M00_L07:
       mov       rax,184345222E0
       mov       [rsi+8],rax
       jmp       short M00_L03
M00_L08:
       mov       rcx,r8
       mov       r8,rax
       call      qword ptr [7FF9DDBC57A0]
       jmp       short M00_L02
M00_L09:
       vmovups   xmm0,[r10]
       vmovups   [rcx],xmm0
       cmp       r9,20
       jbe       near ptr M00_L16
       vmovups   xmm0,[r10+10]
       vmovups   [rcx+10],xmm0
       cmp       r9,30
       jbe       near ptr M00_L16
       vmovups   xmm0,[r10+20]
       vmovups   [rcx+20],xmm0
       jmp       near ptr M00_L16
M00_L10:
       test      al,18
       je        short M00_L11
       mov       rdx,[rdx]
       mov       [r8],rdx
       mov       r8,[r11-8]
       mov       [rbx-8],r8
       jmp       near ptr M00_L02
M00_L11:
       test      al,4
       je        short M00_L12
       mov       edx,[rdx]
       mov       [r8],edx
       mov       r8d,[r11-4]
       mov       [rbx-4],r8d
       jmp       near ptr M00_L02
M00_L12:
       test      rax,rax
       je        near ptr M00_L02
       movzx     edx,byte ptr [rdx]
       mov       [r8],dl
       test      al,2
       je        near ptr M00_L02
       movsx     r8,word ptr [r11-2]
       mov       [rbx-2],r8w
       jmp       near ptr M00_L02
M00_L13:
       cmp       r9,100
       jb        short M00_L14
       mov       r10,r8
       and       r10,3F
       mov       r9,r10
       neg       r9
       add       r9,40
       vmovdqu   ymm0,ymmword ptr [rdx]
       vmovdqu   ymmword ptr [r8],ymm0
       vmovdqu   ymm0,ymmword ptr [rdx+20]
       vmovdqu   ymmword ptr [r8+20],ymm0
       lea       r10,[rdx+r9]
       lea       rcx,[r8+r9]
       sub       rax,r9
       mov       r9,rax
M00_L14:
       mov       rdx,r9
       shr       rdx,6
M00_L15:
       vmovdqu   ymm0,ymmword ptr [r10]
       vmovdqu   ymmword ptr [rcx],ymm0
       vmovdqu   ymm0,ymmword ptr [r10+20]
       vmovdqu   ymmword ptr [rcx+20],ymm0
       add       rcx,40
       add       r10,40
       dec       rdx
       jne       short M00_L15
       and       r9,3F
       cmp       r9,10
       ja        near ptr M00_L09
M00_L16:
       vmovups   xmm0,[r11-10]
       vmovups   [rbx-10],xmm0
       jmp       near ptr M00_L02
M00_L17:
       cmp       r8,rdx
       je        near ptr M00_L02
       jmp       near ptr M00_L01
M00_L18:
       mov       [rsp+20],edi
       xor       ecx,ecx
       mov       [rsp+28],ecx
       mov       rcx,rbx
       xor       edx,edx
       xor       r9d,r9d
       call      qword ptr [7FF9DDFA5FC8]; System.Array.CopyImpl(System.Array, Int32, System.Array, Int32, Int32, Boolean)
       jmp       near ptr M00_L02
; Total bytes of code 706
```
```assembly
; System.Buffer.MemmoveInternal(Byte ByRef, Byte ByRef, UIntPtr)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,68
       vzeroupper
       lea       rbp,[rsp+0A0]
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       lea       rcx,[rbp-80]
       call      CORINFO_HELP_INIT_PINVOKE_FRAME
       mov       r14,rax
       mov       rcx,rsp
       mov       [rbp-68],rcx
       mov       rcx,rbp
       mov       [rbp-58],rcx
       mov       [rbp-40],rbx
       mov       [rbp-48],rsi
       mov       rcx,rbx
       mov       rdx,rsi
       mov       r8,rdi
       mov       rax,7FF9DDC03B98
       mov       [rbp-70],rax
       lea       rax,[M01_L00]
       mov       [rbp-60],rax
       lea       rax,[rbp-80]
       mov       [r14+8],rax
       mov       byte ptr [r14+4],0
       mov       rax,7FFA3D869F50
       call      rax
M01_L00:
       mov       byte ptr [r14+4],1
       cmp       dword ptr [7FFA3DB14A90],0
       je        short M01_L01
       call      qword ptr [7FFA3DB02648]; CORINFO_HELP_STOP_FOR_GC
M01_L01:
       mov       rax,[rbp-78]
       mov       [r14+8],rax
       xor       eax,eax
       mov       [rbp-48],rax
       mov       [rbp-40],rax
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 184
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rdx,rdx
       je        short M02_L02
       mov       rax,[rdx]
       cmp       rax,rcx
       je        short M02_L02
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M02_L02
M02_L00:
       test      rax,rax
       je        short M02_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M02_L02
       test      rax,rax
       je        short M02_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M02_L02
       test      rax,rax
       jne       short M02_L03
M02_L01:
       xor       edx,edx
M02_L02:
       mov       rax,rdx
       ret
M02_L03:
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M02_L02
       test      rax,rax
       je        short M02_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M02_L02
       jmp       short M02_L00
; Total bytes of code 86
```
```assembly
; System.Array.CopyImpl(System.Array, Int32, System.Array, Int32, Int32, Boolean)
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       edi,edx
       mov       rsi,r8
       mov       ebp,r9d
       test      rbx,rbx
       je        near ptr M03_L09
       test      rsi,rsi
       je        near ptr M03_L08
       mov       rcx,[rbx]
       cmp       rcx,[rsi]
       je        short M03_L00
       mov       rcx,[rbx]
       mov       ecx,[rcx+4]
       add       ecx,0FFFFFFE8
       shr       ecx,3
       mov       edx,1
       test      ecx,ecx
       cmove     ecx,edx
       mov       rdx,[rsi]
       mov       edx,[rdx+4]
       add       edx,0FFFFFFE8
       shr       edx,3
       mov       eax,1
       test      edx,edx
       cmove     edx,eax
       cmp       ecx,edx
       jne       near ptr M03_L10
M03_L00:
       mov       r14d,[rsp+70]
       test      r14d,r14d
       jl        near ptr M03_L11
       mov       rcx,rbx
       xor       edx,edx
       call      qword ptr [7FFA3D63A390]; Precode of System.Array.GetLowerBound(Int32)
       cmp       edi,eax
       jl        near ptr M03_L07
       sub       edi,eax
       js        near ptr M03_L12
       lea       ecx,[rdi+r14]
       cmp       ecx,[rbx+8]
       ja        near ptr M03_L12
       mov       rcx,rsi
       xor       edx,edx
       call      qword ptr [7FFA3D63A390]; Precode of System.Array.GetLowerBound(Int32)
       cmp       ebp,eax
       jl        near ptr M03_L06
       sub       ebp,eax
       js        near ptr M03_L13
       lea       ecx,[r14+rbp]
       cmp       ecx,[rsi+8]
       ja        near ptr M03_L13
       mov       rcx,[rbx]
       cmp       rcx,[rsi]
       je        short M03_L01
       mov       rcx,rbx
       mov       rdx,rsi
       call      qword ptr [7FFA3D63A2B0]
       test      eax,eax
       je        short M03_L01
       cmp       byte ptr [rsp+78],0
       jne       near ptr M03_L16
       mov       [rsp+70],r14d
       mov       [rsp+78],eax
       mov       rcx,rbx
       mov       edx,edi
       mov       r8,rsi
       mov       r9d,ebp
       lea       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       jmp       qword ptr [rax]
M03_L01:
       mov       rcx,[rbx]
       movzx     edx,word ptr [rcx]
       mov       r8d,r14d
       imul      r8,rdx
       lea       rax,[rbx+8]
       mov       r10,[rbx]
       mov       r10d,[r10+4]
       add       r10,0FFFFFFFFFFFFFFF0
       add       rax,r10
       mov       r10d,edi
       imul      r10,rdx
       add       r10,rax
       lea       rax,[rsi+8]
       mov       r9,[rsi]
       mov       r9d,[r9+4]
       add       r9,0FFFFFFFFFFFFFFF0
       add       rax,r9
       mov       r9d,ebp
       imul      rdx,r9
       add       rdx,rax
       test      dword ptr [rcx],1000000
       jne       short M03_L04
       mov       rcx,rdx
       mov       rdx,r10
       call      qword ptr [7FFA3D63D928]; Precode of System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
M03_L02:
       mov       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       cmp       dword ptr [rax],0
       jne       near ptr M03_L15
M03_L03:
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M03_L04:
       cmp       r8,4000
       jbe       short M03_L05
       mov       rcx,rdx
       mov       rdx,r10
       lea       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       jmp       qword ptr [rax]
M03_L05:
       mov       rcx,rdx
       mov       rdx,r10
       call      qword ptr [7FFA3D63A630]
       mov       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       cmp       dword ptr [rax],0
       je        short M03_L02
       jmp       near ptr M03_L14
M03_L06:
       mov       r8,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       r8,[r8]
       mov       ecx,ebp
       mov       edx,eax
       call      qword ptr [7FFA3D651920]
       int       3
M03_L07:
       mov       r8,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       r8,[r8]
       mov       ecx,edi
       mov       edx,eax
       call      qword ptr [7FFA3D651920]
       int       3
M03_L08:
       mov       rcx,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       rcx,[rcx]
       call      qword ptr [7FFA3D63C210]
       int       3
M03_L09:
       mov       rcx,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       rcx,[rcx]
       call      qword ptr [7FFA3D63C210]
       int       3
M03_L10:
       call      qword ptr [7FFA3D633788]
       mov       rbx,rax
       call      qword ptr [7FFA3D63ED58]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FFA3D63D7B8]
       mov       rcx,rbx
       call      qword ptr [7FFA3D628FC0]; CORINFO_HELP_THROW
       int       3
M03_L11:
       mov       rdx,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       rdx,[rdx]
       mov       ecx,r14d
       call      qword ptr [7FFA3D651908]
       int       3
M03_L12:
       call      qword ptr [7FFA3D633550]
       mov       rdi,rax
       call      qword ptr [7FFA3D63DC10]
       mov       rdx,rax
       mov       r8,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       r8,[r8]
       mov       rcx,rdi
       call      qword ptr [7FFA3D63C1C0]
       mov       rcx,rdi
       call      qword ptr [7FFA3D628FC0]; CORINFO_HELP_THROW
       int       3
M03_L13:
       call      qword ptr [7FFA3D633550]
       mov       rbp,rax
       call      qword ptr [7FFA3D63DC08]
       mov       rdx,rax
       mov       r8,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       r8,[r8]
       mov       rcx,rbp
       call      qword ptr [7FFA3D63C1C0]
       mov       rcx,rbp
       call      qword ptr [7FFA3D628FC0]; CORINFO_HELP_THROW
       int       3
M03_L14:
       call      qword ptr [7FFA3D629040]; CORINFO_HELP_POLL_GC
       jmp       near ptr M03_L02
M03_L15:
       call      qword ptr [7FFA3D629040]; CORINFO_HELP_POLL_GC
       jmp       near ptr M03_L03
M03_L16:
       call      qword ptr [7FFA3D633578]
       mov       rbx,rax
       call      qword ptr [7FFA3D63E6D8]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FFA3D63C240]
       mov       rcx,rbx
       call      qword ptr [7FFA3D628FC0]; CORINFO_HELP_THROW
       int       3
; Total bytes of code 734
```
```assembly
; System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rdx
       call      qword ptr [7FFA3D62EC68]
       mov       rsi,rax
       mov       rcx,rax
       call      qword ptr [7FFA3D629058]; Precode of System.RuntimeTypeHandle.GetRuntimeTypeFromHandle(IntPtr)
       mov       rdx,rax
       mov       rcx,rbx
       mov       r8d,1
       call      qword ptr [7FFA3D63A5E8]
       mov       r8,rax
       test      r8,r8
       je        short M04_L00
       mov       rcx,rsi
       cmp       [r8],rcx
       je        short M04_L00
       mov       rdx,rax
       call      qword ptr [7FFA3D629090]; Precode of System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       r8,rax
M04_L00:
       mov       rax,r8
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 88
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Loop_ArrayToList()
;         var result = new List<int>(_array.Length);
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         for (int i = 0; i < _array.Length; i++)
;              ^^^^^^^^^
;             result.Add(_array[i]);
;             ^^^^^^^^^^^^^^^^^^^^^^
;         return result;
;         ^^^^^^^^^^^^^^
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rcx,offset MT_System.Collections.Generic.List<System.Int32>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdi,[rbx+8]
       mov       edx,[rdi+8]
       test      edx,edx
       je        short M00_L04
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rsi+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
M00_L00:
       xor       ebp,ebp
       cmp       dword ptr [rdi+8],0
       jle       short M00_L03
M00_L01:
       mov       rcx,[rbx+8]
       cmp       ebp,[rcx+8]
       jae       short M00_L06
       mov       edx,[rcx+rbp*4+10]
       inc       dword ptr [rsi+14]
       mov       rcx,[rsi+8]
       mov       eax,[rsi+10]
       mov       r8d,[rcx+8]
       cmp       r8d,eax
       jbe       short M00_L05
       lea       r8d,[rax+1]
       mov       [rsi+10],r8d
       mov       [rcx+rax*4+10],edx
M00_L02:
       inc       ebp
       mov       rax,[rbx+8]
       cmp       [rax+8],ebp
       jg        short M00_L01
M00_L03:
       mov       rax,rsi
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L04:
       mov       rcx,2D7009222E0
       mov       [rsi+8],rcx
       jmp       short M00_L00
M00_L05:
       mov       rcx,rsi
       call      qword ptr [7FF9DDF2FFC0]
       jmp       short M00_L02
M00_L06:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 175
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Linq_ListFirstOrDefault()
;     public int Linq_ListFirstOrDefault() => _list.FirstOrDefault(static value => value < 0);
;                                             ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
       push      rbp
       push      r15
       push      r14
       push      r13
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,30
       lea       rbp,[rsp+60]
       mov       rbx,[rcx+10]
       mov       rcx,2296F801340
       mov       rsi,[rcx]
       test      rsi,rsi
       je        near ptr M00_L08
M00_L00:
       test      rbx,rbx
       je        near ptr M00_L09
       mov       edi,1
       mov       rax,[rbx]
       mov       rcx,offset MT_System.Int32[]
       cmp       rax,rcx
       je        near ptr M00_L10
       mov       rcx,offset MT_System.Collections.Generic.List<System.Int32>
       cmp       rax,rcx
       jne       near ptr M00_L11
       mov       eax,[rbx+10]
       mov       r14,[rbx+8]
       cmp       [r14+8],eax
       jb        short M00_L05
       add       r14,10
M00_L01:
       test      edi,edi
       je        near ptr M00_L15
       test      eax,eax
       jle       short M00_L03
       mov       rbx,[rsi+18]
       mov       rdx,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ListFirstOrDefault>b__13_0(Int32)
       cmp       rbx,rdx
       jne       near ptr M00_L12
       xor       edx,edx
       nop       word ptr [rax+rax]
M00_L02:
       mov       edi,[r14+rdx]
       test      edi,edi
       jl        near ptr M00_L14
       add       rdx,4
       dec       eax
       jne       short M00_L02
M00_L03:
       xor       edi,edi
M00_L04:
       mov       eax,edi
       add       rsp,30
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M00_L05:
       call      qword ptr [7FF9DDBCF480]
       int       3
M00_L06:
       mov       edi,[r14+r15]
       mov       rdx,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ListFirstOrDefault>b__13_0(Int32)
       cmp       rbx,rdx
       jne       near ptr M00_L13
       test      edi,edi
       jl        near ptr M00_L14
M00_L07:
       add       r15,4
       dec       r13d
       jne       short M00_L06
       jmp       short M00_L03
M00_L08:
       mov       rcx,offset MT_System.Func<System.Int32, System.Boolean>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdx,2296F801330
       mov       rdx,[rdx]
       mov       rcx,rsi
       mov       r8,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ListFirstOrDefault>b__13_0(Int32)
       call      qword ptr [7FF9DDBC6BB0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,2296F801340
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M00_L00
M00_L09:
       mov       ecx,11
       call      qword ptr [7FF9DDBCF930]
       int       3
M00_L10:
       lea       r14,[rbx+10]
       mov       eax,[rbx+8]
       jmp       near ptr M00_L01
M00_L11:
       xor       r14d,r14d
       xor       eax,eax
       xor       edi,edi
       jmp       near ptr M00_L01
M00_L12:
       xor       r15d,r15d
       mov       r13d,eax
       jmp       near ptr M00_L06
M00_L13:
       mov       edx,edi
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       test      eax,eax
       je        near ptr M00_L07
M00_L14:
       jmp       near ptr M00_L04
M00_L15:
       mov       rcx,rbx
       mov       r11,7FF9DDB10528
       call      qword ptr [r11]
       mov       [rbp-40],rax
M00_L16:
       mov       rcx,[rbp-40]
       mov       r11,7FF9DDB10530
       call      qword ptr [r11]
       test      eax,eax
       je        short M00_L17
       mov       rcx,[rbp-40]
       mov       r11,7FF9DDB10538
       call      qword ptr [r11]
       mov       edi,eax
       mov       edx,edi
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       test      eax,eax
       je        short M00_L16
       mov       [rbp-34],edi
       jmp       short M00_L18
M00_L17:
       mov       rcx,[rbp-40]
       mov       r11,7FF9DDB10540
       call      qword ptr [r11]
       jmp       near ptr M00_L03
M00_L18:
       call      M00_L19
       nop
       mov       edi,[rbp-34]
       jmp       near ptr M00_L04
M00_L19:
       sub       rsp,28
       cmp       qword ptr [rbp-40],0
       je        short M00_L20
       mov       rcx,[rbp-40]
       mov       r11,7FF9DDB10540
       call      qword ptr [r11]
M00_L20:
       nop
       add       rsp,28
       ret
; Total bytes of code 538
```
```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ListFirstOrDefault>b__13_0(Int32)
;     public int Linq_ListFirstOrDefault() => _list.FirstOrDefault(static value => value < 0);
;                                                                                  ^^^^^^^^^
       mov       eax,edx
       shr       eax,1F
       ret
; Total bytes of code 6
```
```assembly
; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       test      rsi,rsi
       je        short M02_L00
       mov       rcx,7FF9DE0109C4
       call      CORINFO_HELP_COUNTPROFILE32
       lea       rcx,[rbx+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbx+18],rdi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M02_L00:
       mov       rcx,7FF9DE0109C0
       call      CORINFO_HELP_COUNTPROFILE32
       call      qword ptr [7FF9DDFAD080]
       int       3
; Total bytes of code 82
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Loop_ListFirstOrDefault()
;         for (int i = 0; i < _list.Count; i++)
;              ^^^^^^^^^
;             if (_list[i] < 0)
;             ^^^^^^^^^^^^^^^^^
;                 return _list[i];
;                 ^^^^^^^^^^^^^^^^
;         return default;
;         ^^^^^^^^^^^^^^^
       sub       rsp,28
       xor       edx,edx
       mov       rcx,[rcx+10]
       mov       eax,[rcx+10]
       test      eax,eax
       jle       short M00_L01
       nop       dword ptr [rax]
       nop       dword ptr [rax+rax]
M00_L00:
       mov       r8,rcx
       cmp       edx,eax
       jae       short M00_L02
       mov       r8,[r8+8]
       cmp       edx,[r8+8]
       jae       short M00_L04
       cmp       dword ptr [r8+rdx*4+10],0
       jl        short M00_L03
       inc       edx
       cmp       edx,eax
       jl        short M00_L00
M00_L01:
       xor       eax,eax
       add       rsp,28
       ret
M00_L02:
       call      qword ptr [7FF9DDFA46A8]
       int       3
M00_L03:
       add       rsp,28
       jmp       qword ptr [7FF9DDDC3C48]; System.Collections.Generic.List`1[[System.Int32, System.Private.CoreLib]].get_Item(Int32)
M00_L04:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 93
```
```assembly
; System.Collections.Generic.List`1[[System.Int32, System.Private.CoreLib]].get_Item(Int32)
       sub       rsp,28
       cmp       edx,[rcx+10]
       jae       short M01_L00
       mov       rax,[rcx+8]
       cmp       edx,[rax+8]
       jae       short M01_L01
       mov       ecx,edx
       mov       eax,[rax+rcx*4+10]
       add       rsp,28
       ret
M01_L00:
       call      qword ptr [7FF9DDFA46A8]
       int       3
M01_L01:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 42
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Linq_ListFirstOrDefault()
;     public int Linq_ListFirstOrDefault() => _list.FirstOrDefault(static value => value < 0);
;                                             ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
       push      rbp
       push      r15
       push      r14
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       lea       rbp,[rsp+60]
       mov       rbx,[rcx+10]
       mov       rcx,1605A401340
       mov       rsi,[rcx]
       test      rsi,rsi
       je        near ptr M00_L12
M00_L00:
       test      rbx,rbx
       je        near ptr M00_L11
       mov       edi,1
       mov       rax,[rbx]
       mov       rcx,offset MT_System.Int32[]
       cmp       rax,rcx
       je        near ptr M00_L13
       mov       rcx,offset MT_System.Collections.Generic.List<System.Int32>
       cmp       rax,rcx
       jne       near ptr M00_L14
       mov       eax,[rbx+10]
       mov       r14,[rbx+8]
       cmp       [r14+8],eax
       jb        short M00_L05
       add       r14,10
M00_L01:
       test      edi,edi
       je        short M00_L09
       test      eax,eax
       jle       short M00_L03
       mov       rdx,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ListFirstOrDefault>b__13_0(Int32)
       cmp       [rsi+18],rdx
       jne       near ptr M00_L15
       xor       edx,edx
       nop       dword ptr [rax]
       nop       dword ptr [rax+rax]
M00_L02:
       mov       ebx,[r14+rdx]
       test      ebx,ebx
       jl        short M00_L08
       add       rdx,4
       dec       eax
       jne       short M00_L02
M00_L03:
       xor       ebx,ebx
M00_L04:
       mov       eax,ebx
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r14
       pop       r15
       pop       rbp
       ret
M00_L05:
       call      qword ptr [7FF9DDBCF480]
       int       3
M00_L06:
       mov       ebx,[r14+rdi]
       mov       rdx,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ListFirstOrDefault>b__13_0(Int32)
       cmp       [rsi+18],rdx
       jne       near ptr M00_L16
       test      ebx,ebx
       jl        short M00_L08
M00_L07:
       add       rdi,4
       dec       r15d
       jne       short M00_L06
       jmp       short M00_L03
M00_L08:
       jmp       short M00_L04
M00_L09:
       mov       rcx,rbx
       mov       r11,7FF9DDB10568
       call      qword ptr [r11]
       mov       [rbp-38],rax
M00_L10:
       mov       rcx,[rbp-38]
       mov       r11,7FF9DDB10570
       call      qword ptr [r11]
       test      eax,eax
       je        near ptr M00_L17
       mov       rcx,[rbp-38]
       mov       r11,7FF9DDB10578
       call      qword ptr [r11]
       mov       ebx,eax
       mov       edx,ebx
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       test      eax,eax
       je        short M00_L10
       mov       [rbp-2C],ebx
       jmp       near ptr M00_L18
M00_L11:
       mov       ecx,11
       call      qword ptr [7FF9DDBCF930]
       int       3
M00_L12:
       mov       rcx,offset MT_System.Func<System.Int32, System.Boolean>
       call      CORINFO_HELP_NEWSFAST
       mov       rsi,rax
       mov       rdx,1605A401330
       mov       rdx,[rdx]
       mov       rcx,rsi
       mov       r8,offset Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ListFirstOrDefault>b__13_0(Int32)
       call      qword ptr [7FF9DDBC6BB0]; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       mov       rcx,1605A401340
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       jmp       near ptr M00_L00
M00_L13:
       lea       r14,[rbx+10]
       mov       eax,[rbx+8]
       jmp       near ptr M00_L01
M00_L14:
       xor       r14d,r14d
       xor       eax,eax
       xor       edi,edi
       jmp       near ptr M00_L01
M00_L15:
       xor       edi,edi
       mov       r15d,eax
       jmp       near ptr M00_L06
M00_L16:
       mov       edx,ebx
       mov       rcx,[rsi+8]
       call      qword ptr [rsi+18]
       test      eax,eax
       je        near ptr M00_L07
       jmp       near ptr M00_L08
M00_L17:
       mov       rcx,[rbp-38]
       mov       r11,7FF9DDB10580
       call      qword ptr [r11]
       jmp       near ptr M00_L03
M00_L18:
       call      M00_L19
       nop
       mov       ebx,[rbp-2C]
       jmp       near ptr M00_L04
M00_L19:
       sub       rsp,28
       cmp       qword ptr [rbp-38],0
       je        short M00_L20
       mov       rcx,[rbp-38]
       mov       r11,7FF9DDB10580
       call      qword ptr [r11]
M00_L20:
       nop
       add       rsp,28
       ret
; Total bytes of code 537
```
```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks+<>c.<Linq_ListFirstOrDefault>b__13_0(Int32)
;     public int Linq_ListFirstOrDefault() => _list.FirstOrDefault(static value => value < 0);
;                                                                                  ^^^^^^^^^
       mov       eax,edx
       shr       eax,1F
       ret
; Total bytes of code 6
```
```assembly
; System.MulticastDelegate.CtorClosed(System.Object, IntPtr)
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,20
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       test      rsi,rsi
       je        short M02_L00
       mov       rcx,7FF9DE010614
       call      CORINFO_HELP_COUNTPROFILE32
       lea       rcx,[rbx+8]
       mov       rdx,rsi
       call      CORINFO_HELP_ASSIGN_REF
       mov       [rbx+18],rdi
       add       rsp,20
       pop       rbx
       pop       rsi
       pop       rdi
       ret
M02_L00:
       mov       rcx,7FF9DE010610
       call      CORINFO_HELP_COUNTPROFILE32
       call      qword ptr [7FF9DDFACEA0]
       int       3
; Total bytes of code 82
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Loop_ListFirstOrDefault()
;         for (int i = 0; i < _list.Count; i++)
;              ^^^^^^^^^
;             if (_list[i] < 0)
;             ^^^^^^^^^^^^^^^^^
;                 return _list[i];
;                 ^^^^^^^^^^^^^^^^
;         return default;
;         ^^^^^^^^^^^^^^^
       sub       rsp,28
       xor       eax,eax
       mov       rcx,[rcx+10]
       mov       edx,[rcx+10]
       test      edx,edx
       jle       short M00_L01
       nop       dword ptr [rax]
       nop       dword ptr [rax+rax]
M00_L00:
       mov       r8,rcx
       cmp       eax,edx
       jae       short M00_L03
       mov       r8,[r8+8]
       cmp       eax,[r8+8]
       jae       short M00_L04
       cmp       dword ptr [r8+rax*4+10],0
       jl        short M00_L02
       inc       eax
       cmp       eax,edx
       jl        short M00_L00
M00_L01:
       xor       eax,eax
       add       rsp,28
       ret
M00_L02:
       mov       rcx,[rcx+8]
       cmp       eax,[rcx+8]
       jae       short M00_L04
       mov       eax,eax
       mov       eax,[rcx+rax*4+10]
       add       rsp,28
       ret
M00_L03:
       call      qword ptr [7FF9DDF2FF90]
       int       3
M00_L04:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 103
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Linq_ListToList()
;     public List<int> Linq_ListToList() => _list.ToList();
;                                           ^^^^^^^^^^^^^^
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       lea       rbp,[rsp+70]
       mov       rbx,[rcx+10]
       test      rbx,rbx
       je        near ptr M00_L17
       mov       rcx,rbx
       mov       rdx,offset MT_System.Collections.Generic.List<System.Int32>
       cmp       [rcx],rdx
       jne       near ptr M00_L18
       xor       ecx,ecx
M00_L00:
       test      rcx,rcx
       jne       near ptr M00_L19
       mov       rsi,offset MT_System.Collections.Generic.List<System.Int32>
       mov       rcx,rsi
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       cmp       [rbx],rsi
       jne       near ptr M00_L20
       mov       r14d,[rbx+10]
M00_L01:
       test      r14d,r14d
       je        near ptr M00_L21
       movsxd    rdx,r14d
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rdi+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       r15,[rdi+8]
       cmp       [rbx],rsi
       jne       near ptr M00_L31
       mov       rsi,[rbx+8]
       mov       ebx,[rbx+10]
       test      rsi,rsi
       jne       short M00_L02
       mov       ecx,12D
       mov       rdx,7FF9DDAF4000
       call      qword ptr [7FF9DDBBF210]
       mov       rcx,rax
       call      qword ptr [7FF9DDF96028]
       int       3
M00_L02:
       mov       r13,[rsi]
       mov       rax,r13
       mov       rcx,[r15]
       cmp       rax,rcx
       jne       short M00_L03
       cmp       dword ptr [rax+4],18
       jne       short M00_L03
       test      ebx,ebx
       jl        short M00_L03
       cmp       ebx,[rsi+8]
       ja        short M00_L03
       cmp       ebx,[r15+8]
       jbe       near ptr M00_L16
M00_L03:
       cmp       r13,rcx
       je        short M00_L04
       mov       rdx,r13
       mov       edx,[rdx+4]
       add       edx,0FFFFFFE8
       shr       edx,3
       mov       eax,1
       test      edx,edx
       cmove     edx,eax
       mov       rax,rcx
       mov       eax,[rax+4]
       add       eax,0FFFFFFE8
       shr       eax,3
       mov       r8d,1
       test      eax,eax
       cmove     eax,r8d
       cmp       edx,eax
       jne       near ptr M00_L24
M00_L04:
       test      ebx,ebx
       jl        near ptr M00_L25
       mov       rdx,r13
       mov       edx,[rdx+4]
       add       edx,0FFFFFFE8
       shr       edx,3
       jne       near ptr M00_L11
       xor       r12d,r12d
M00_L05:
       test      r12d,r12d
       jg        near ptr M00_L26
       neg       r12d
       js        near ptr M00_L27
       lea       edx,[r12+rbx]
       cmp       edx,[rsi+8]
       ja        near ptr M00_L27
       mov       rdx,rcx
       mov       edx,[rdx+4]
       add       edx,0FFFFFFE8
       shr       edx,3
       jne       near ptr M00_L12
       xor       eax,eax
M00_L06:
       test      eax,eax
       jg        near ptr M00_L28
       neg       eax
       mov       [rbp-3C],eax
       test      eax,eax
       jl        near ptr M00_L29
       lea       edx,[rax+rbx]
       cmp       edx,[r15+8]
       ja        near ptr M00_L29
       cmp       r13,rcx
       je        short M00_L07
       mov       rcx,rsi
       mov       rdx,r15
       call      qword ptr [7FF9DDF960A0]; System.Array.CanAssignArrayType(System.Array, System.Array)
       test      eax,eax
       jne       short M00_L08
M00_L07:
       movzx     ecx,word ptr [r13]
       mov       r8d,ebx
       imul      r8,rcx
       mov       edx,r12d
       imul      rdx,rcx
       lea       rdx,[rsi+rdx+10]
       mov       eax,[rbp-3C]
       imul      rcx,rax
       lea       rcx,[r15+rcx+10]
       test      dword ptr [r13],1000000
       je        short M00_L13
       cmp       r8,4000
       ja        near ptr M00_L15
       call      00007FFA3D7DA2B0
       cmp       dword ptr [7FFA3DB14A90],0
       je        short M00_L09
       jmp       near ptr M00_L30
M00_L08:
       mov       [rsp+20],ebx
       mov       [rsp+28],eax
       mov       rcx,rsi
       mov       edx,r12d
       mov       r8,r15
       mov       r9d,[rbp-3C]
       call      qword ptr [7FF9DDF960B8]
M00_L09:
       mov       [rdi+10],r14d
M00_L10:
       mov       rax,rdi
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M00_L11:
       movsxd    rdx,edx
       mov       r12d,[rsi+rdx*4+10]
       jmp       near ptr M00_L05
M00_L12:
       movsxd    rax,edx
       mov       eax,[r15+rax*4+10]
       jmp       near ptr M00_L06
M00_L13:
       cmp       r8,40
       je        short M00_L14
       call      qword ptr [7FF9DDBB5818]; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       jmp       short M00_L09
M00_L14:
       vmovdqu   ymm0,ymmword ptr [rdx]
       vmovdqu   ymm1,ymmword ptr [rdx+20]
       vmovdqu   ymmword ptr [rcx],ymm0
       vmovdqu   ymmword ptr [rcx+20],ymm1
       jmp       short M00_L09
M00_L15:
       call      qword ptr [7FF9DDF95F38]
       jmp       short M00_L09
M00_L16:
       mov       r8d,ebx
       movzx     ecx,word ptr [rax]
       imul      r8,rcx
       lea       rdx,[rsi+10]
       lea       rcx,[r15+10]
       test      dword ptr [rax],1000000
       je        short M00_L23
       jmp       short M00_L22
M00_L17:
       mov       ecx,11
       call      qword ptr [7FF9DDBBF930]
       int       3
M00_L18:
       mov       rdx,rbx
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<System.Int32>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rcx,rax
       jmp       near ptr M00_L00
M00_L19:
       mov       rax,[rcx]
       mov       rax,[rax+48]
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       jmp       qword ptr [rax+38]
M00_L20:
       mov       rcx,rbx
       mov       r11,7FF9DDB00518
       call      qword ptr [r11]
       mov       r14d,eax
       jmp       near ptr M00_L01
M00_L21:
       mov       rax,251F91A22E0
       mov       [rdi+8],rax
       jmp       near ptr M00_L10
M00_L22:
       call      qword ptr [7FF9DDBB57A0]
       jmp       near ptr M00_L09
M00_L23:
       call      qword ptr [7FF9DDBB5818]; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       jmp       near ptr M00_L09
M00_L24:
       mov       rcx,offset MT_System.RankException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF9DDF96040]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF9DDF96058]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M00_L25:
       mov       ecx,0B3
       mov       rdx,7FF9DDAF4000
       call      qword ptr [7FF9DDBBF210]
       mov       rdx,rax
       mov       ecx,ebx
       call      qword ptr [7FF9DDF95F98]
       int       3
M00_L26:
       mov       ecx,167
       mov       rdx,7FF9DDAF4000
       call      qword ptr [7FF9DDBBF210]
       mov       r8,rax
       mov       edx,r12d
       xor       ecx,ecx
       call      qword ptr [7FF9DDF95FE0]
       int       3
M00_L27:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       call      qword ptr [7FF9DDF96070]
       mov       rdi,rax
       mov       ecx,12D
       mov       rdx,7FF9DDAF4000
       call      qword ptr [7FF9DDBBF210]
       mov       r8,rax
       mov       rdx,rdi
       mov       rcx,r15
       call      qword ptr [7FF9DDE87C60]
       mov       rcx,r15
       call      CORINFO_HELP_THROW
       int       3
M00_L28:
       mov       [rbp-40],eax
       mov       ecx,17F
       mov       rdx,7FF9DDAF4000
       call      qword ptr [7FF9DDBBF210]
       mov       r8,rax
       mov       edx,[rbp-40]
       xor       ecx,ecx
       call      qword ptr [7FF9DDF95FE0]
       int       3
M00_L29:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF9DDF96088]
       mov       rsi,rax
       mov       ecx,145
       mov       rdx,7FF9DDAF4000
       call      qword ptr [7FF9DDBBF210]
       mov       r8,rax
       mov       rdx,rsi
       mov       rcx,rbx
       call      qword ptr [7FF9DDE87C60]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M00_L30:
       call      CORINFO_HELP_POLL_GC
       jmp       near ptr M00_L09
M00_L31:
       mov       rcx,rbx
       mov       rdx,r15
       mov       r11,7FF9DDB00520
       xor       r8d,r8d
       call      qword ptr [r11]
       jmp       near ptr M00_L09
; Total bytes of code 1118
```
```assembly
; System.Array.CanAssignArrayType(System.Array, System.Array)
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,20
       mov       rax,[rcx]
       mov       rcx,[rax+30]
       mov       rbx,rcx
       mov       rax,[rdx]
       mov       rsi,[rax+30]
       mov       rdi,rsi
       cmp       rbx,rdi
       je        near ptr M01_L32
       mov       eax,ecx
       and       eax,2
       mov       edx,esi
       and       edx,2
       or        eax,edx
       jne       near ptr M01_L28
       mov       rbp,rbx
       mov       r14,rdi
       mov       eax,[rcx]
       and       eax,0C0000
       cmp       eax,40000
       je        near ptr M01_L04
       mov       eax,[rsi]
       and       eax,0C0000
       cmp       eax,40000
       jne       near ptr M01_L05
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       mov       r8,rbx
       mov       rax,rdi
       add       rdx,10
       rol       r8,20
       xor       r8,rax
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L00:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rbx
       jne       near ptr M01_L15
       mov       r9,rdi
       xor       r9,[rax+10]
       cmp       r9,1
       ja        near ptr M01_L15
       cmp       r10d,[rax]
       jne       near ptr M01_L39
M01_L01:
       test      r9d,r9d
       je        near ptr M01_L16
       cmp       r9d,1
       je        short M01_L02
       mov       rcx,rbx
       mov       rdx,rdi
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       je        near ptr M01_L16
M01_L02:
       mov       eax,4
       jmp       near ptr M01_L14
M01_L03:
       test      r10d,r10d
       je        near ptr M01_L40
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L17
       jmp       near ptr M01_L40
M01_L04:
       mov       eax,[rsi]
       and       eax,0C0000
       cmp       eax,40000
       jne       near ptr M01_L23
M01_L05:
       mov       eax,[rcx]
       and       eax,0E0000
       cmp       eax,60000
       jne       short M01_L06
       mov       eax,[rsi]
       and       eax,0E0000
       cmp       eax,60000
       je        near ptr M01_L20
M01_L06:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       add       rdx,10
       mov       r8,rbp
       rol       r8,20
       xor       r8,r14
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L07:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rbp
       jne       near ptr M01_L34
       mov       r9,r14
       xor       r9,[rax+10]
       cmp       r9,1
       ja        near ptr M01_L34
       cmp       r10d,[rax]
       jne       near ptr M01_L41
M01_L08:
       test      r9d,r9d
       je        short M01_L09
       cmp       r9d,1
       je        near ptr M01_L32
       mov       rcx,rbp
       mov       rdx,r14
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       jne       near ptr M01_L32
M01_L09:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       mov       r8,r14
       mov       rax,rbp
       add       rdx,10
       rol       r8,20
       xor       r8,rax
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L10:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,r14
       jne       near ptr M01_L35
       mov       r9,rbp
       xor       r9,[rax+10]
       cmp       r9,1
       ja        near ptr M01_L35
       cmp       r10d,[rax]
       jne       near ptr M01_L42
M01_L11:
       test      r9d,r9d
       je        short M01_L12
       cmp       r9d,1
       je        short M01_L13
       mov       rcx,r14
       mov       rdx,rbp
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       jne       short M01_L13
M01_L12:
       mov       ecx,[r14]
       and       ecx,0F0000
       cmp       ecx,0C0000
       je        short M01_L13
       mov       ecx,[rbp]
       and       ecx,0F0000
       cmp       ecx,0C0000
       jne       near ptr M01_L19
M01_L13:
       mov       eax,2
M01_L14:
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M01_L15:
       test      r10d,r10d
       je        near ptr M01_L39
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L00
       jmp       near ptr M01_L39
M01_L16:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       mov       r8,rdi
       mov       rax,rbx
       add       rdx,10
       rol       r8,20
       xor       r8,rax
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L17:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rdi
       jne       near ptr M01_L03
       mov       r9,rbx
       xor       r9,[rax+10]
       cmp       r9,1
       ja        near ptr M01_L03
       cmp       r10d,[rax]
       jne       near ptr M01_L40
M01_L18:
       test      r9d,r9d
       je        short M01_L19
       cmp       r9d,1
       je        near ptr M01_L02
       mov       rcx,rdi
       mov       rdx,rbx
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       jne       near ptr M01_L02
M01_L19:
       mov       eax,1
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M01_L20:
       call      qword ptr [7FFA3D644E78]
       mov       ebx,eax
       mov       rcx,rsi
       call      qword ptr [7FFA3D644E78]
       mov       esi,eax
       mov       ecx,ebx
       call      qword ptr [7FFA3D63A2A8]; Precode of System.Array.GetNormalizedIntegralArrayElementType(System.Reflection.CorElementType)
       mov       edi,eax
       mov       ecx,esi
       call      qword ptr [7FFA3D63A2A8]; Precode of System.Array.GetNormalizedIntegralArrayElementType(System.Reflection.CorElementType)
       cmp       edi,eax
       je        near ptr M01_L32
       cmp       ebx,0E
       jge       short M01_L21
       cmp       ebx,0E
       jae       near ptr M01_L43
       mov       eax,ebx
       lea       rcx,[7FFA3C9B85B8]
       movsx     rax,word ptr [rcx+rax*2]
       bt        eax,esi
       jae       short M01_L19
       jmp       short M01_L22
M01_L21:
       cmp       ebx,esi
       jne       short M01_L19
M01_L22:
       mov       eax,5
       jmp       near ptr M01_L14
M01_L23:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       mov       r8,rsi
       add       rdx,10
       mov       rax,rbx
       rol       rax,20
       xor       r8,rax
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L24:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rbx
       jne       short M01_L27
       mov       r9,rsi
       xor       r9,[rax+10]
       cmp       r9,1
       ja        short M01_L27
       cmp       r10d,[rax]
       jne       near ptr M01_L38
M01_L25:
       test      r9d,r9d
       je        near ptr M01_L19
       cmp       r9d,1
       je        short M01_L26
       mov       rcx,rbx
       mov       rdx,rsi
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       je        near ptr M01_L19
M01_L26:
       mov       eax,3
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M01_L27:
       test      r10d,r10d
       je        near ptr M01_L38
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L24
       jmp       near ptr M01_L38
M01_L28:
       mov       rdi,rsi
       test      cl,2
       jne       short M01_L29
       test      sil,2
       jne       near ptr M01_L36
M01_L29:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       add       rdx,10
       mov       r8,rbx
       rol       r8,20
       xor       r8,rdi
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L30:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rbx
       jne       short M01_L33
       mov       rsi,rdi
       xor       rsi,[rax+10]
       cmp       rsi,1
       ja        short M01_L33
       cmp       r10d,[rax]
       jne       near ptr M01_L37
M01_L31:
       test      esi,esi
       je        near ptr M01_L19
       cmp       esi,1
       je        short M01_L32
       mov       rcx,rbx
       mov       rdx,rdi
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       je        near ptr M01_L19
M01_L32:
       xor       eax,eax
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M01_L33:
       test      r10d,r10d
       je        short M01_L37
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        short M01_L30
       jmp       short M01_L37
M01_L34:
       test      r10d,r10d
       je        short M01_L41
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L07
       jmp       short M01_L41
M01_L35:
       test      r10d,r10d
       je        short M01_L42
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L10
       jmp       short M01_L42
M01_L36:
       xor       esi,esi
       jmp       short M01_L31
M01_L37:
       mov       esi,2
       jmp       near ptr M01_L31
M01_L38:
       mov       r9d,2
       jmp       near ptr M01_L25
M01_L39:
       mov       r9d,2
       jmp       near ptr M01_L01
M01_L40:
       mov       r9d,2
       jmp       near ptr M01_L18
M01_L41:
       mov       r9d,2
       jmp       near ptr M01_L08
M01_L42:
       mov       r9d,2
       jmp       near ptr M01_L11
M01_L43:
       call      qword ptr [7FFA3D628FD8]
       int       3
; Total bytes of code 1451
```
```assembly
; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       mov       rax,rcx
       sub       rax,rdx
       cmp       rax,r8
       jb        near ptr M02_L09
       mov       rax,rdx
       sub       rax,rcx
       cmp       rax,r8
       jb        near ptr M02_L09
       lea       rax,[rdx+r8]
       lea       r10,[rcx+r8]
       cmp       r8,10
       jbe       near ptr M02_L06
       cmp       r8,40
       jbe       short M02_L02
       cmp       r8,800
       ja        near ptr M02_L10
       cmp       r8,100
       jb        short M02_L00
       mov       r9,rcx
       and       r9,3F
       neg       r9
       add       r9,40
       vmovdqu   ymm0,ymmword ptr [rdx]
       vmovdqu   ymmword ptr [rcx],ymm0
       vmovdqu   ymm0,ymmword ptr [rdx+20]
       vmovdqu   ymmword ptr [rcx+20],ymm0
       add       rdx,r9
       add       rcx,r9
       sub       r8,r9
M02_L00:
       mov       r9,r8
       shr       r9,6
M02_L01:
       vmovdqu   ymm0,ymmword ptr [rdx]
       vmovdqu   ymmword ptr [rcx],ymm0
       vmovdqu   ymm0,ymmword ptr [rdx+20]
       vmovdqu   ymmword ptr [rcx+20],ymm0
       add       rcx,40
       add       rdx,40
       dec       r9
       jne       short M02_L01
       and       r8,3F
       cmp       r8,10
       jbe       short M02_L03
M02_L02:
       vmovups   xmm0,[rdx]
       vmovups   [rcx],xmm0
       cmp       r8,20
       jbe       short M02_L03
       vmovups   xmm0,[rdx+10]
       vmovups   [rcx+10],xmm0
       cmp       r8,30
       ja        short M02_L05
M02_L03:
       vmovups   xmm0,[rax-10]
       vmovups   [r10-10],xmm0
M02_L04:
       vzeroupper
       ret
M02_L05:
       vmovups   xmm0,[rdx+20]
       vmovups   [rcx+20],xmm0
       jmp       short M02_L03
M02_L06:
       test      r8b,18
       je        short M02_L07
       mov       rdx,[rdx]
       mov       [rcx],rdx
       mov       rcx,[rax-8]
       mov       [r10-8],rcx
       jmp       short M02_L04
M02_L07:
       test      r8b,4
       je        short M02_L08
       mov       edx,[rdx]
       mov       [rcx],edx
       mov       ecx,[rax-4]
       mov       [r10-4],ecx
       jmp       short M02_L04
M02_L08:
       test      r8,r8
       je        short M02_L04
       movzx     edx,byte ptr [rdx]
       mov       [rcx],dl
       test      r8b,2
       je        short M02_L04
       movsx     rcx,word ptr [rax-2]
       mov       [r10-2],cx
       jmp       short M02_L04
M02_L09:
       cmp       rcx,rdx
       jne       short M02_L10
       cmp       [rdx],dl
       jmp       short M02_L04
M02_L10:
       cmp       [rcx],cl
       cmp       [rdx],dl
       vzeroupper
       jmp       qword ptr [7FF9DDBB66E8]; System.Buffer.MemmoveInternal(Byte ByRef, Byte ByRef, UIntPtr)
; Total bytes of code 313
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rdx,rdx
       je        short M03_L02
       mov       rax,[rdx]
       cmp       rax,rcx
       je        short M03_L02
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
M03_L00:
       test      rax,rax
       je        short M03_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
       test      rax,rax
       jne       short M03_L03
M03_L01:
       xor       edx,edx
M03_L02:
       mov       rax,rdx
       ret
M03_L03:
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
       test      rax,rax
       je        short M03_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
       test      rax,rax
       je        short M03_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
       jmp       short M03_L00
; Total bytes of code 86
```
```assembly
; System.Buffer.MemmoveInternal(Byte ByRef, Byte ByRef, UIntPtr)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,0A8
       lea       rbp,[rsp+0E0]
       mov       [rbp-40],rcx
       mov       [rbp-48],rdx
       mov       [rbp-0A8],rcx
       mov       [rbp-0B0],rdx
       mov       [rbp-0B8],r8
       lea       rcx,[rbp-0A0]
       call      qword ptr [7FFA3D629030]; CORINFO_HELP_JIT_PINVOKE_BEGIN
       mov       rax,[System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)]
       mov       rcx,[rbp-0A8]
       mov       rdx,[rbp-0B0]
       mov       r8,[rbp-0B8]
       call      qword ptr [rax]
       lea       rcx,[rbp-0A0]
       call      qword ptr [7FFA3D629038]; CORINFO_HELP_JIT_PINVOKE_END
       xor       eax,eax
       mov       [rbp-48],rax
       mov       [rbp-40],rax
       add       rsp,0A8
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 142
```
```assembly
; System.Reflection.CustomAttributeExtensions.GetCustomAttribute[[System.__Canon, System.Private.CoreLib]](System.Reflection.Assembly)
       push      rsi
       push      rbx
       sub       rsp,28
       mov       [rsp+20],rcx
       mov       rbx,rdx
       call      qword ptr [7FFA3D62EC68]
       mov       rsi,rax
       mov       rcx,rax
       call      qword ptr [7FFA3D629058]; Precode of System.RuntimeTypeHandle.GetRuntimeTypeFromHandle(IntPtr)
       mov       rdx,rax
       mov       rcx,rbx
       mov       r8d,1
       call      qword ptr [7FFA3D63A5E8]
       mov       r8,rax
       test      r8,r8
       je        short M05_L00
       mov       rcx,rsi
       cmp       [r8],rcx
       je        short M05_L00
       mov       rdx,rax
       call      qword ptr [7FFA3D629090]; Precode of System.Runtime.CompilerServices.CastHelpers.ChkCastAny(Void*, System.Object)
       mov       r8,rax
M05_L00:
       mov       rax,r8
       add       rsp,28
       pop       rbx
       pop       rsi
       ret
; Total bytes of code 88
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Loop_ListToList()
;         var result = new List<int>(_list.Count);
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         for (int i = 0; i < _list.Count; i++)
;              ^^^^^^^^^
;             result.Add(_list[i]);
;             ^^^^^^^^^^^^^^^^^^^^^
;         return result;
;         ^^^^^^^^^^^^^^
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rsi,[rbx+10]
       mov       rdi,rsi
       mov       rcx,offset MT_System.Collections.Generic.List<System.Int32>
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       mov       edx,[rdi+10]
       test      edx,edx
       jl        short M00_L04
       test      edx,edx
       je        near ptr M00_L05
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rbp+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
M00_L00:
       xor       edi,edi
       cmp       dword ptr [rsi+10],0
       jle       short M00_L03
M00_L01:
       mov       rcx,[rbx+10]
       cmp       edi,[rcx+10]
       jae       short M00_L07
       mov       rcx,[rcx+8]
       cmp       edi,[rcx+8]
       jae       short M00_L08
       mov       edx,[rcx+rdi*4+10]
       inc       dword ptr [rbp+14]
       mov       rcx,[rbp+8]
       mov       eax,[rbp+10]
       mov       r8d,[rcx+8]
       cmp       r8d,eax
       jbe       short M00_L06
       lea       r8d,[rax+1]
       mov       [rbp+10],r8d
       mov       [rcx+rax*4+10],edx
M00_L02:
       inc       edi
       mov       rax,[rbx+10]
       cmp       edi,[rax+10]
       jl        short M00_L01
M00_L03:
       mov       rax,rbp
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L04:
       mov       ecx,16
       mov       edx,0D
       call      qword ptr [7FF9DDF15A40]
       int       3
M00_L05:
       mov       rcx,1AADE4622E0
       mov       [rbp+8],rcx
       jmp       short M00_L00
M00_L06:
       mov       rcx,rbp
       call      qword ptr [7FF9DDF95D70]
       jmp       short M00_L02
M00_L07:
       call      qword ptr [7FF9DDF94708]
       int       3
M00_L08:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 219
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Linq_ListToList()
;     public List<int> Linq_ListToList() => _list.ToList();
;                                           ^^^^^^^^^^^^^^
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,38
       lea       rbp,[rsp+70]
       mov       rbx,[rcx+10]
       test      rbx,rbx
       je        near ptr M00_L17
       mov       rcx,rbx
       mov       rdx,offset MT_System.Collections.Generic.List<System.Int32>
       cmp       [rcx],rdx
       jne       near ptr M00_L18
       xor       ecx,ecx
M00_L00:
       test      rcx,rcx
       jne       near ptr M00_L19
       mov       rsi,offset MT_System.Collections.Generic.List<System.Int32>
       mov       rcx,rsi
       call      CORINFO_HELP_NEWSFAST
       mov       rdi,rax
       cmp       [rbx],rsi
       jne       near ptr M00_L20
       mov       r14d,[rbx+10]
M00_L01:
       test      r14d,r14d
       je        near ptr M00_L21
       movsxd    rdx,r14d
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rdi+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
       mov       r15,[rdi+8]
       cmp       [rbx],rsi
       jne       near ptr M00_L31
       mov       rsi,[rbx+8]
       mov       ebx,[rbx+10]
       test      rsi,rsi
       jne       short M00_L02
       mov       ecx,12D
       mov       rdx,7FF9DDB04000
       call      qword ptr [7FF9DDBCF210]
       mov       rcx,rax
       call      qword ptr [7FF9DDFA6010]
       int       3
M00_L02:
       mov       r13,[rsi]
       mov       rax,r13
       mov       rcx,[r15]
       cmp       rax,rcx
       jne       short M00_L03
       cmp       dword ptr [rax+4],18
       jne       short M00_L03
       test      ebx,ebx
       jl        short M00_L03
       cmp       ebx,[rsi+8]
       ja        short M00_L03
       cmp       ebx,[r15+8]
       jbe       near ptr M00_L16
M00_L03:
       cmp       r13,rcx
       je        short M00_L04
       mov       rdx,r13
       mov       edx,[rdx+4]
       add       edx,0FFFFFFE8
       shr       edx,3
       mov       eax,1
       test      edx,edx
       cmove     edx,eax
       mov       rax,rcx
       mov       eax,[rax+4]
       add       eax,0FFFFFFE8
       shr       eax,3
       mov       r8d,1
       test      eax,eax
       cmove     eax,r8d
       cmp       edx,eax
       jne       near ptr M00_L24
M00_L04:
       test      ebx,ebx
       jl        near ptr M00_L25
       mov       rdx,r13
       mov       edx,[rdx+4]
       add       edx,0FFFFFFE8
       shr       edx,3
       jne       near ptr M00_L11
       xor       r12d,r12d
M00_L05:
       test      r12d,r12d
       jg        near ptr M00_L26
       neg       r12d
       js        near ptr M00_L27
       lea       edx,[r12+rbx]
       cmp       edx,[rsi+8]
       ja        near ptr M00_L27
       mov       rdx,rcx
       mov       edx,[rdx+4]
       add       edx,0FFFFFFE8
       shr       edx,3
       jne       near ptr M00_L12
       xor       eax,eax
M00_L06:
       test      eax,eax
       jg        near ptr M00_L28
       neg       eax
       mov       [rbp-3C],eax
       test      eax,eax
       jl        near ptr M00_L29
       lea       edx,[rax+rbx]
       cmp       edx,[r15+8]
       ja        near ptr M00_L29
       cmp       r13,rcx
       je        short M00_L07
       mov       rcx,rsi
       mov       rdx,r15
       call      qword ptr [7FF9DDFA6088]; System.Array.CanAssignArrayType(System.Array, System.Array)
       test      eax,eax
       jne       short M00_L08
M00_L07:
       movzx     ecx,word ptr [r13]
       mov       r8d,ebx
       imul      r8,rcx
       mov       edx,r12d
       imul      rdx,rcx
       lea       rdx,[rsi+rdx+10]
       mov       eax,[rbp-3C]
       imul      rcx,rax
       lea       rcx,[r15+rcx+10]
       test      dword ptr [r13],1000000
       je        short M00_L13
       cmp       r8,4000
       ja        near ptr M00_L15
       call      00007FFA3D7DA2B0
       cmp       dword ptr [7FFA3DB14A90],0
       je        short M00_L09
       jmp       near ptr M00_L30
M00_L08:
       mov       [rsp+20],ebx
       mov       [rsp+28],eax
       mov       rcx,rsi
       mov       edx,r12d
       mov       r8,r15
       mov       r9d,[rbp-3C]
       call      qword ptr [7FF9DDFA60A0]
M00_L09:
       mov       [rdi+10],r14d
M00_L10:
       mov       rax,rdi
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
M00_L11:
       movsxd    rdx,edx
       mov       r12d,[rsi+rdx*4+10]
       jmp       near ptr M00_L05
M00_L12:
       movsxd    rax,edx
       mov       eax,[r15+rax*4+10]
       jmp       near ptr M00_L06
M00_L13:
       cmp       r8,40
       je        short M00_L14
       call      qword ptr [7FF9DDBC5818]; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       jmp       short M00_L09
M00_L14:
       vmovdqu   ymm0,ymmword ptr [rdx]
       vmovdqu   ymm1,ymmword ptr [rdx+20]
       vmovdqu   ymmword ptr [rcx],ymm0
       vmovdqu   ymmword ptr [rcx+20],ymm1
       jmp       short M00_L09
M00_L15:
       call      qword ptr [7FF9DDFA5F20]
       jmp       short M00_L09
M00_L16:
       mov       r8d,ebx
       movzx     ecx,word ptr [rax]
       imul      r8,rcx
       lea       rdx,[rsi+10]
       lea       rcx,[r15+10]
       test      dword ptr [rax],1000000
       je        short M00_L23
       jmp       short M00_L22
M00_L17:
       mov       ecx,11
       call      qword ptr [7FF9DDBCF930]
       int       3
M00_L18:
       mov       rdx,rbx
       mov       rcx,offset MT_System.Linq.Enumerable+Iterator<System.Int32>
       call      System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       mov       rcx,rax
       jmp       near ptr M00_L00
M00_L19:
       mov       rax,[rcx]
       mov       rax,[rax+48]
       add       rsp,38
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       jmp       qword ptr [rax+38]
M00_L20:
       mov       rcx,rbx
       mov       r11,7FF9DDB10548
       call      qword ptr [r11]
       mov       r14d,eax
       jmp       near ptr M00_L01
M00_L21:
       mov       rax,22B505C22E0
       mov       [rdi+8],rax
       jmp       near ptr M00_L10
M00_L22:
       call      qword ptr [7FF9DDBC57A0]
       jmp       near ptr M00_L09
M00_L23:
       call      qword ptr [7FF9DDBC5818]; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       jmp       near ptr M00_L09
M00_L24:
       mov       rcx,offset MT_System.RankException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF9DDFA6028]
       mov       rdx,rax
       mov       rcx,rbx
       call      qword ptr [7FF9DDFA6040]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M00_L25:
       mov       ecx,0B3
       mov       rdx,7FF9DDB04000
       call      qword ptr [7FF9DDBCF210]
       mov       rdx,rax
       mov       ecx,ebx
       call      qword ptr [7FF9DDFA5F80]
       int       3
M00_L26:
       mov       ecx,167
       mov       rdx,7FF9DDB04000
       call      qword ptr [7FF9DDBCF210]
       mov       r8,rax
       mov       edx,r12d
       xor       ecx,ecx
       call      qword ptr [7FF9DDFA5FC8]
       int       3
M00_L27:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       r15,rax
       call      qword ptr [7FF9DDFA6058]
       mov       rdi,rax
       mov       ecx,12D
       mov       rdx,7FF9DDB04000
       call      qword ptr [7FF9DDBCF210]
       mov       r8,rax
       mov       rdx,rdi
       mov       rcx,r15
       call      qword ptr [7FF9DDE97C60]
       mov       rcx,r15
       call      CORINFO_HELP_THROW
       int       3
M00_L28:
       mov       [rbp-40],eax
       mov       ecx,17F
       mov       rdx,7FF9DDB04000
       call      qword ptr [7FF9DDBCF210]
       mov       r8,rax
       mov       edx,[rbp-40]
       xor       ecx,ecx
       call      qword ptr [7FF9DDFA5FC8]
       int       3
M00_L29:
       mov       rcx,offset MT_System.ArgumentException
       call      CORINFO_HELP_NEWSFAST
       mov       rbx,rax
       call      qword ptr [7FF9DDFA6070]
       mov       rsi,rax
       mov       ecx,145
       mov       rdx,7FF9DDB04000
       call      qword ptr [7FF9DDBCF210]
       mov       r8,rax
       mov       rdx,rsi
       mov       rcx,rbx
       call      qword ptr [7FF9DDE97C60]
       mov       rcx,rbx
       call      CORINFO_HELP_THROW
       int       3
M00_L30:
       call      CORINFO_HELP_POLL_GC
       jmp       near ptr M00_L09
M00_L31:
       mov       rcx,rbx
       mov       rdx,r15
       mov       r11,7FF9DDB10550
       xor       r8d,r8d
       call      qword ptr [r11]
       jmp       near ptr M00_L09
; Total bytes of code 1118
```
```assembly
; System.Array.CanAssignArrayType(System.Array, System.Array)
       push      r14
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,20
       mov       rax,[rcx]
       mov       rcx,[rax+30]
       mov       rbx,rcx
       mov       rax,[rdx]
       mov       rsi,[rax+30]
       mov       rdi,rsi
       cmp       rbx,rdi
       je        near ptr M01_L32
       mov       eax,ecx
       and       eax,2
       mov       edx,esi
       and       edx,2
       or        eax,edx
       jne       near ptr M01_L28
       mov       rbp,rbx
       mov       r14,rdi
       mov       eax,[rcx]
       and       eax,0C0000
       cmp       eax,40000
       je        near ptr M01_L04
       mov       eax,[rsi]
       and       eax,0C0000
       cmp       eax,40000
       jne       near ptr M01_L05
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       mov       r8,rbx
       mov       rax,rdi
       add       rdx,10
       rol       r8,20
       xor       r8,rax
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L00:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rbx
       jne       near ptr M01_L15
       mov       r9,rdi
       xor       r9,[rax+10]
       cmp       r9,1
       ja        near ptr M01_L15
       cmp       r10d,[rax]
       jne       near ptr M01_L39
M01_L01:
       test      r9d,r9d
       je        near ptr M01_L16
       cmp       r9d,1
       je        short M01_L02
       mov       rcx,rbx
       mov       rdx,rdi
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       je        near ptr M01_L16
M01_L02:
       mov       eax,4
       jmp       near ptr M01_L14
M01_L03:
       test      r10d,r10d
       je        near ptr M01_L40
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L17
       jmp       near ptr M01_L40
M01_L04:
       mov       eax,[rsi]
       and       eax,0C0000
       cmp       eax,40000
       jne       near ptr M01_L23
M01_L05:
       mov       eax,[rcx]
       and       eax,0E0000
       cmp       eax,60000
       jne       short M01_L06
       mov       eax,[rsi]
       and       eax,0E0000
       cmp       eax,60000
       je        near ptr M01_L20
M01_L06:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       add       rdx,10
       mov       r8,rbp
       rol       r8,20
       xor       r8,r14
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L07:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rbp
       jne       near ptr M01_L34
       mov       r9,r14
       xor       r9,[rax+10]
       cmp       r9,1
       ja        near ptr M01_L34
       cmp       r10d,[rax]
       jne       near ptr M01_L41
M01_L08:
       test      r9d,r9d
       je        short M01_L09
       cmp       r9d,1
       je        near ptr M01_L32
       mov       rcx,rbp
       mov       rdx,r14
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       jne       near ptr M01_L32
M01_L09:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       mov       r8,r14
       mov       rax,rbp
       add       rdx,10
       rol       r8,20
       xor       r8,rax
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L10:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,r14
       jne       near ptr M01_L35
       mov       r9,rbp
       xor       r9,[rax+10]
       cmp       r9,1
       ja        near ptr M01_L35
       cmp       r10d,[rax]
       jne       near ptr M01_L42
M01_L11:
       test      r9d,r9d
       je        short M01_L12
       cmp       r9d,1
       je        short M01_L13
       mov       rcx,r14
       mov       rdx,rbp
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       jne       short M01_L13
M01_L12:
       mov       ecx,[r14]
       and       ecx,0F0000
       cmp       ecx,0C0000
       je        short M01_L13
       mov       ecx,[rbp]
       and       ecx,0F0000
       cmp       ecx,0C0000
       jne       near ptr M01_L19
M01_L13:
       mov       eax,2
M01_L14:
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M01_L15:
       test      r10d,r10d
       je        near ptr M01_L39
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L00
       jmp       near ptr M01_L39
M01_L16:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       mov       r8,rdi
       mov       rax,rbx
       add       rdx,10
       rol       r8,20
       xor       r8,rax
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L17:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rdi
       jne       near ptr M01_L03
       mov       r9,rbx
       xor       r9,[rax+10]
       cmp       r9,1
       ja        near ptr M01_L03
       cmp       r10d,[rax]
       jne       near ptr M01_L40
M01_L18:
       test      r9d,r9d
       je        short M01_L19
       cmp       r9d,1
       je        near ptr M01_L02
       mov       rcx,rdi
       mov       rdx,rbx
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       jne       near ptr M01_L02
M01_L19:
       mov       eax,1
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M01_L20:
       call      qword ptr [7FFA3D644E78]
       mov       ebx,eax
       mov       rcx,rsi
       call      qword ptr [7FFA3D644E78]
       mov       esi,eax
       mov       ecx,ebx
       call      qword ptr [7FFA3D63A2A8]; Precode of System.Array.GetNormalizedIntegralArrayElementType(System.Reflection.CorElementType)
       mov       edi,eax
       mov       ecx,esi
       call      qword ptr [7FFA3D63A2A8]; Precode of System.Array.GetNormalizedIntegralArrayElementType(System.Reflection.CorElementType)
       cmp       edi,eax
       je        near ptr M01_L32
       cmp       ebx,0E
       jge       short M01_L21
       cmp       ebx,0E
       jae       near ptr M01_L43
       mov       eax,ebx
       lea       rcx,[7FFA3C9B85B8]
       movsx     rax,word ptr [rcx+rax*2]
       bt        eax,esi
       jae       short M01_L19
       jmp       short M01_L22
M01_L21:
       cmp       ebx,esi
       jne       short M01_L19
M01_L22:
       mov       eax,5
       jmp       near ptr M01_L14
M01_L23:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       mov       r8,rsi
       add       rdx,10
       mov       rax,rbx
       rol       rax,20
       xor       r8,rax
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L24:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rbx
       jne       short M01_L27
       mov       r9,rsi
       xor       r9,[rax+10]
       cmp       r9,1
       ja        short M01_L27
       cmp       r10d,[rax]
       jne       near ptr M01_L38
M01_L25:
       test      r9d,r9d
       je        near ptr M01_L19
       cmp       r9d,1
       je        short M01_L26
       mov       rcx,rbx
       mov       rdx,rsi
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       je        near ptr M01_L19
M01_L26:
       mov       eax,3
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M01_L27:
       test      r10d,r10d
       je        near ptr M01_L38
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L24
       jmp       near ptr M01_L38
M01_L28:
       mov       rdi,rsi
       test      cl,2
       jne       short M01_L29
       test      sil,2
       jne       near ptr M01_L36
M01_L29:
       call      qword ptr [7FFA3D629C18]
       mov       rdx,[rax]
       add       rdx,10
       mov       r8,rbx
       rol       r8,20
       xor       r8,rdi
       mov       rax,9E3779B97F4A7C15
       imul      r8,rax
       mov       ecx,[rdx]
       shr       r8,cl
       xor       ecx,ecx
M01_L30:
       lea       eax,[r8+1]
       cdqe
       lea       rax,[rax+rax*2]
       lea       rax,[rdx+rax*8]
       mov       r10d,[rax]
       mov       r9,[rax+8]
       and       r10d,0FFFFFFFE
       cmp       r9,rbx
       jne       short M01_L33
       mov       rsi,rdi
       xor       rsi,[rax+10]
       cmp       rsi,1
       ja        short M01_L33
       cmp       r10d,[rax]
       jne       near ptr M01_L37
M01_L31:
       test      esi,esi
       je        near ptr M01_L19
       cmp       esi,1
       je        short M01_L32
       mov       rcx,rbx
       mov       rdx,rdi
       xor       r8d,r8d
       call      qword ptr [7FFA3D644E98]; Precode of System.Runtime.CompilerServices.TypeHandle.CanCastToWorker(System.Runtime.CompilerServices.TypeHandle, System.Runtime.CompilerServices.TypeHandle, Boolean)
       test      eax,eax
       je        near ptr M01_L19
M01_L32:
       xor       eax,eax
       add       rsp,20
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       pop       r14
       ret
M01_L33:
       test      r10d,r10d
       je        short M01_L37
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        short M01_L30
       jmp       short M01_L37
M01_L34:
       test      r10d,r10d
       je        short M01_L41
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L07
       jmp       short M01_L41
M01_L35:
       test      r10d,r10d
       je        short M01_L42
       inc       ecx
       add       r8d,ecx
       and       r8d,[rdx+4]
       cmp       ecx,8
       jl        near ptr M01_L10
       jmp       short M01_L42
M01_L36:
       xor       esi,esi
       jmp       short M01_L31
M01_L37:
       mov       esi,2
       jmp       near ptr M01_L31
M01_L38:
       mov       r9d,2
       jmp       near ptr M01_L25
M01_L39:
       mov       r9d,2
       jmp       near ptr M01_L01
M01_L40:
       mov       r9d,2
       jmp       near ptr M01_L18
M01_L41:
       mov       r9d,2
       jmp       near ptr M01_L08
M01_L42:
       mov       r9d,2
       jmp       near ptr M01_L11
M01_L43:
       call      qword ptr [7FFA3D628FD8]
       int       3
; Total bytes of code 1451
```
```assembly
; System.SpanHelpers.Memmove(Byte ByRef, Byte ByRef, UIntPtr)
       mov       rax,rcx
       sub       rax,rdx
       cmp       rax,r8
       jb        near ptr M02_L11
       mov       rax,rdx
       sub       rax,rcx
       cmp       rax,r8
       jb        near ptr M02_L11
       lea       rax,[rdx+r8]
       lea       r10,[rcx+r8]
       cmp       r8,10
       jbe       short M02_L02
       cmp       r8,40
       jbe       short M02_L01
       cmp       r8,800
       jbe       near ptr M02_L07
M02_L00:
       cmp       [rcx],cl
       cmp       [rdx],dl
       vzeroupper
       jmp       qword ptr [7FF9DDBC66E8]; System.Buffer.MemmoveInternal(Byte ByRef, Byte ByRef, UIntPtr)
M02_L01:
       vmovups   xmm0,[rdx]
       vmovups   [rcx],xmm0
       cmp       r8,20
       jbe       near ptr M02_L10
       vmovups   xmm0,[rdx+10]
       vmovups   [rcx+10],xmm0
       cmp       r8,30
       jbe       near ptr M02_L10
       vmovups   xmm0,[rdx+20]
       vmovups   [rcx+20],xmm0
       jmp       near ptr M02_L10
M02_L02:
       test      r8b,18
       je        short M02_L03
       mov       rdx,[rdx]
       mov       [rcx],rdx
       mov       rcx,[rax-8]
       mov       [r10-8],rcx
       jmp       short M02_L05
M02_L03:
       test      r8b,4
       je        short M02_L04
       mov       edx,[rdx]
       mov       [rcx],edx
       mov       ecx,[rax-4]
       mov       [r10-4],ecx
       jmp       short M02_L05
M02_L04:
       test      r8,r8
       jne       short M02_L06
M02_L05:
       vzeroupper
       ret
M02_L06:
       movzx     edx,byte ptr [rdx]
       mov       [rcx],dl
       test      r8b,2
       je        short M02_L05
       movsx     rcx,word ptr [rax-2]
       mov       [r10-2],cx
       jmp       short M02_L05
M02_L07:
       cmp       r8,100
       jb        short M02_L08
       mov       r9,rcx
       and       r9,3F
       neg       r9
       add       r9,40
       vmovdqu   ymm0,ymmword ptr [rdx]
       vmovdqu   ymmword ptr [rcx],ymm0
       vmovdqu   ymm0,ymmword ptr [rdx+20]
       vmovdqu   ymmword ptr [rcx+20],ymm0
       add       rdx,r9
       add       rcx,r9
       sub       r8,r9
M02_L08:
       mov       r9,r8
       shr       r9,6
M02_L09:
       vmovdqu   ymm0,ymmword ptr [rdx]
       vmovdqu   ymmword ptr [rcx],ymm0
       vmovdqu   ymm0,ymmword ptr [rdx+20]
       vmovdqu   ymmword ptr [rcx+20],ymm0
       add       rcx,40
       add       rdx,40
       dec       r9
       jne       short M02_L09
       and       r8,3F
       cmp       r8,10
       ja        near ptr M02_L01
M02_L10:
       vmovups   xmm0,[rax-10]
       vmovups   [r10-10],xmm0
       jmp       near ptr M02_L05
M02_L11:
       cmp       rcx,rdx
       jne       near ptr M02_L00
       cmp       [rdx],dl
       jmp       near ptr M02_L05
; Total bytes of code 336
```
```assembly
; System.Runtime.CompilerServices.CastHelpers.IsInstanceOfClass(Void*, System.Object)
       test      rdx,rdx
       je        short M03_L02
       mov       rax,[rdx]
       cmp       rax,rcx
       je        short M03_L02
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
M03_L00:
       test      rax,rax
       je        short M03_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
       test      rax,rax
       jne       short M03_L03
M03_L01:
       xor       edx,edx
M03_L02:
       mov       rax,rdx
       ret
M03_L03:
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
       test      rax,rax
       je        short M03_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
       test      rax,rax
       je        short M03_L01
       mov       rax,[rax+10]
       cmp       rax,rcx
       je        short M03_L02
       jmp       short M03_L00
; Total bytes of code 86
```
```assembly
; System.Buffer.MemmoveInternal(Byte ByRef, Byte ByRef, UIntPtr)
       push      rbp
       push      r15
       push      r14
       push      r13
       push      r12
       push      rdi
       push      rsi
       push      rbx
       sub       rsp,68
       vzeroupper
       lea       rbp,[rsp+0A0]
       mov       rbx,rcx
       mov       rsi,rdx
       mov       rdi,r8
       lea       rcx,[rbp-80]
       call      CORINFO_HELP_INIT_PINVOKE_FRAME
       mov       r14,rax
       mov       rcx,rsp
       mov       [rbp-68],rcx
       mov       rcx,rbp
       mov       [rbp-58],rcx
       mov       [rbp-40],rbx
       mov       [rbp-48],rsi
       mov       rcx,rbx
       mov       rdx,rsi
       mov       r8,rdi
       mov       rax,7FF9DDC03B98
       mov       [rbp-70],rax
       lea       rax,[M04_L00]
       mov       [rbp-60],rax
       lea       rax,[rbp-80]
       mov       [r14+8],rax
       mov       byte ptr [r14+4],0
       mov       rax,7FFA3D869F50
       call      rax
M04_L00:
       mov       byte ptr [r14+4],1
       cmp       dword ptr [7FFA3DB14A90],0
       je        short M04_L01
       call      qword ptr [7FFA3DB02648]; CORINFO_HELP_STOP_FOR_GC
M04_L01:
       mov       rax,[rbp-78]
       mov       [r14+8],rax
       xor       eax,eax
       mov       [rbp-48],rax
       mov       [rbp-40],rax
       add       rsp,68
       pop       rbx
       pop       rsi
       pop       rdi
       pop       r12
       pop       r13
       pop       r14
       pop       r15
       pop       rbp
       ret
; Total bytes of code 184
```

## .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3 (Job: .NET 10 TieredPGO(EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1, Runtime=.NET 10.0, IterationCount=8, WarmupCount=3))

```assembly
; Prowl.Runtime.Benchmarks.DisputedLinqBenchmarks.Loop_ListToList()
;         var result = new List<int>(_list.Count);
;         ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
;         for (int i = 0; i < _list.Count; i++)
;              ^^^^^^^^^
;             result.Add(_list[i]);
;             ^^^^^^^^^^^^^^^^^^^^^
;         return result;
;         ^^^^^^^^^^^^^^
       push      rdi
       push      rsi
       push      rbp
       push      rbx
       sub       rsp,28
       mov       rbx,rcx
       mov       rsi,[rbx+10]
       mov       rdi,rsi
       mov       rcx,offset MT_System.Collections.Generic.List<System.Int32>
       call      CORINFO_HELP_NEWSFAST
       mov       rbp,rax
       mov       edx,[rdi+10]
       test      edx,edx
       jl        short M00_L04
       test      edx,edx
       je        near ptr M00_L05
       mov       rcx,offset MT_System.Int32[]
       call      CORINFO_HELP_NEWARR_1_VC
       lea       rcx,[rbp+8]
       mov       rdx,rax
       call      CORINFO_HELP_ASSIGN_REF
M00_L00:
       xor       edi,edi
       cmp       dword ptr [rsi+10],0
       jle       short M00_L03
M00_L01:
       mov       rcx,[rbx+10]
       cmp       edi,[rcx+10]
       jae       short M00_L07
       mov       rcx,[rcx+8]
       cmp       edi,[rcx+8]
       jae       short M00_L08
       mov       edx,[rcx+rdi*4+10]
       inc       dword ptr [rbp+14]
       mov       rcx,[rbp+8]
       mov       eax,[rbp+10]
       mov       r8d,[rcx+8]
       cmp       r8d,eax
       jbe       short M00_L06
       lea       r8d,[rax+1]
       mov       [rbp+10],r8d
       mov       [rcx+rax*4+10],edx
M00_L02:
       inc       edi
       mov       rax,[rbx+10]
       cmp       edi,[rax+10]
       jl        short M00_L01
M00_L03:
       mov       rax,rbp
       add       rsp,28
       pop       rbx
       pop       rbp
       pop       rsi
       pop       rdi
       ret
M00_L04:
       mov       ecx,16
       mov       edx,0D
       call      qword ptr [7FF9DDF25A40]
       int       3
M00_L05:
       mov       rcx,242BF7A22E0
       mov       [rbp+8],rcx
       jmp       short M00_L00
M00_L06:
       mov       rcx,rbp
       call      qword ptr [7FF9DDFA4000]
       jmp       short M00_L02
M00_L07:
       call      qword ptr [7FF9DDFA4018]
       int       3
M00_L08:
       call      CORINFO_HELP_RNGCHKFAIL
       int       3
; Total bytes of code 219
```

