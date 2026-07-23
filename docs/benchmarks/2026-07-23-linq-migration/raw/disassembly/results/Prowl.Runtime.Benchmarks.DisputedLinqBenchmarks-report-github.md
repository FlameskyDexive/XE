```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19044.7417/21H2/November2021Update)
Intel Core i5-14490F 2.80GHz, 1 CPU, 16 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]            : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3
  .NET 10 TieredPGO : .NET 10.0.10 (10.0.10, 10.0.1026.32716), X64 RyuJIT x86-64-v3

Job=.NET 10 TieredPGO  EnvironmentVariables=DOTNET_TieredCompilation=1,DOTNET_TieredPGO=1  Runtime=.NET 10.0  
IterationCount=8  WarmupCount=3  

```
| Method                   | Categories          | Count | Mean        | Error      | StdDev     | Ratio | RatioSD | Rank | Gen0   | Code Size | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------------------- |-------------------- |------ |------------:|-----------:|-----------:|------:|--------:|-----:|-------:|----------:|-------:|-------:|----------:|------------:|
| **Linq_ArrayFirstOrDefault** | **ArrayFirstOrDefault** | **256**   |    **71.56 ns** |   **2.405 ns** |   **1.258 ns** |  **1.00** |    **0.02** |    **1** |      **-** |     **658 B** |      **-** |      **-** |         **-** |          **NA** |
| Loop_ArrayFirstOrDefault | ArrayFirstOrDefault | 256   |    97.54 ns |   4.614 ns |   2.413 ns |  1.36 |    0.04 |    2 |      - |      67 B |      - |      - |         - |          NA |
|                          |                     |       |             |            |            |       |         |      |        |           |        |        |           |             |
| **Linq_ArrayFirstOrDefault** | **ArrayFirstOrDefault** | **4096**  |   **947.12 ns** |  **28.600 ns** |  **14.958 ns** |  **1.00** |    **0.02** |    **1** |      **-** |     **639 B** |      **-** |      **-** |         **-** |          **NA** |
| Loop_ArrayFirstOrDefault | ArrayFirstOrDefault | 4096  | 1,403.60 ns |  77.471 ns |  40.519 ns |  1.48 |    0.05 |    2 |      - |      67 B |      - |      - |         - |          NA |
|                          |                     |       |             |            |            |       |         |      |        |           |        |        |           |             |
| **Linq_ArrayToList**         | **ArrayToList**         | **256**   |    **97.83 ns** |  **13.162 ns** |   **6.884 ns** |  **1.00** |    **0.09** |    **1** | **0.0290** |   **1,735 B** |      **-** |      **-** |    **1080 B** |        **1.00** |
| Loop_ArrayToList         | ArrayToList         | 256   |   285.42 ns |  17.103 ns |   7.594 ns |  2.93 |    0.20 |    2 | 0.0362 |     175 B |      - |      - |    1080 B |        1.00 |
|                          |                     |       |             |            |            |       |         |      |        |           |        |        |           |             |
| **Linq_ArrayToList**         | **ArrayToList**         | **4096**  | **1,353.65 ns** | **112.343 ns** |  **58.758 ns** |  **1.00** |    **0.06** |    **1** | **0.4444** |   **1,798 B** | **0.0038** |      **-** |   **16440 B** |        **1.00** |
| Loop_ArrayToList         | ArrayToList         | 4096  | 4,217.15 ns | 183.213 ns |  95.824 ns |  3.12 |    0.14 |    2 | 0.5112 |     175 B | 0.0076 | 0.0076 |         - |        0.00 |
|                          |                     |       |             |            |            |       |         |      |        |           |        |        |           |             |
| **Linq_ListFirstOrDefault**  | **ListFirstOrDefault**  | **256**   |    **73.77 ns** |   **1.296 ns** |   **0.575 ns** |  **1.00** |    **0.01** |    **1** |      **-** |     **626 B** |      **-** |      **-** |         **-** |          **NA** |
| Loop_ListFirstOrDefault  | ListFirstOrDefault  | 256   |   167.52 ns |  35.068 ns |  18.341 ns |  2.27 |    0.24 |    2 |      - |     135 B |      - |      - |         - |          NA |
|                          |                     |       |             |            |            |       |         |      |        |           |        |        |           |             |
| **Linq_ListFirstOrDefault**  | **ListFirstOrDefault**  | **4096**  |   **987.00 ns** |  **33.671 ns** |  **17.611 ns** |  **1.00** |    **0.02** |    **1** |      **-** |     **625 B** |      **-** |      **-** |         **-** |          **NA** |
| Loop_ListFirstOrDefault  | ListFirstOrDefault  | 4096  | 1,882.31 ns | 112.029 ns |  58.593 ns |  1.91 |    0.06 |    2 |      - |     103 B |      - |      - |         - |          NA |
|                          |                     |       |             |            |            |       |         |      |        |           |        |        |           |             |
| **Linq_ListToList**          | **ListToList**          | **256**   |    **93.87 ns** |   **7.120 ns** |   **3.162 ns** |  **1.00** |    **0.05** |    **1** | **0.0290** |   **3,198 B** |      **-** |      **-** |    **1080 B** |        **1.00** |
| Loop_ListToList          | ListToList          | 256   |   337.64 ns |  26.130 ns |  13.667 ns |  3.60 |    0.18 |    2 | 0.0405 |     219 B | 0.0005 | 0.0005 |         - |        0.00 |
|                          |                     |       |             |            |            |       |         |      |        |           |        |        |           |             |
| **Linq_ListToList**          | **ListToList**          | **4096**  | **1,422.01 ns** | **130.678 ns** |  **68.347 ns** |  **1.00** |    **0.06** |    **1** | **0.4482** |   **3,175 B** | **0.0038** |      **-** |   **16440 B** |        **1.00** |
| Loop_ListToList          | ListToList          | 4096  | 5,035.91 ns | 457.028 ns | 202.923 ns |  3.55 |    0.21 |    2 | 0.5341 |     219 B | 0.0305 | 0.0153 |   15239 B |        0.93 |
