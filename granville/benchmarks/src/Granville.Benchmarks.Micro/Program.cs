using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Granville.Benchmarks.Micro.Benchmarks;

namespace Granville.Benchmarks.Micro
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = DefaultConfig.Instance
                .WithOptions(ConfigOptions.DisableOptimizationsValidator);

            var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
                .Run(args, config);
        }
    }
}