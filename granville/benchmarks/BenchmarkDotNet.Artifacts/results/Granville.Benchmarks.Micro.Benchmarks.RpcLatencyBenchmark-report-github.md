```

BenchmarkDotNet v0.13.12, Ubuntu 24.04.2 LTS (Noble Numbat) WSL
AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.107
  [Host]   : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2
  .NET 8.0 : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2
  Dry      : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2

Runtime=.NET 8.0  

```
| Method                          | Job      | IterationCount | LaunchCount | RunStrategy | UnrollFactor | WarmupCount | Mean          | Error     | StdDev     | Gen0   | Completed Work Items | Lock Contentions | Allocated |
|-------------------------------- |--------- |--------------- |------------ |------------ |------------- |------------ |--------------:|----------:|-----------:|-------:|---------------------:|-----------------:|----------:|
| SmallPayload_SerializationOnly  | .NET 8.0 | Default        | Default     | Default     | 16           | Default     |      31.85 ns |  1.111 ns |   3.258 ns | 0.0153 |                    - |                - |     128 B |
| MediumPayload_SerializationOnly | .NET 8.0 | Default        | Default     | Default     | 16           | Default     |     126.41 ns |  8.573 ns |  24.872 ns | 0.1252 |                    - |                - |    1048 B |
| LargePayload_SerializationOnly  | .NET 8.0 | Default        | Default     | Default     | 16           | Default     |   1,016.06 ns | 90.482 ns | 265.367 ns | 1.2245 |                    - |                - |   10264 B |
| SmallPayload_SerializationOnly  | Dry      | 1              | 1           | ColdStart   | 1            | 1           | 367,839.00 ns |        NA |   0.000 ns |      - |                    - |                - |     864 B |
| MediumPayload_SerializationOnly | Dry      | 1              | 1           | ColdStart   | 1            | 1           | 536,955.00 ns |        NA |   0.000 ns |      - |                    - |                - |    1784 B |
| LargePayload_SerializationOnly  | Dry      | 1              | 1           | ColdStart   | 1            | 1           | 434,010.00 ns |        NA |   0.000 ns |      - |                    - |                - |   11000 B |
