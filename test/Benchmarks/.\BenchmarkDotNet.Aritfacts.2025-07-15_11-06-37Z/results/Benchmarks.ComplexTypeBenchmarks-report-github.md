```

BenchmarkDotNet v0.13.12, Ubuntu 24.04.2 LTS (Noble Numbat) WSL
AMD Ryzen 9 3900X, 1 CPU, 24 logical and 12 physical cores
.NET SDK 9.0.107
  [Host] : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2
  Dry    : .NET 8.0.17 (8.0.1725.26602), X64 RyuJIT AVX2

Job=Dry  IterationCount=1  LaunchCount=1  
RunStrategy=ColdStart  UnrollFactor=1  WarmupCount=1  

```
| Method                 | Mean     | Error | Allocated |
|----------------------- |---------:|------:|----------:|
| OrleansStructRoundTrip | 5.072 ms |    NA |   1.33 KB |
