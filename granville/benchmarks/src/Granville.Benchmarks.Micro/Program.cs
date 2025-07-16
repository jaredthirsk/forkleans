using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Columns;
using Granville.Benchmarks.Micro.Benchmarks;

namespace Granville.Benchmarks.Micro
{
    class Program
    {
        static void Main(string[] args)
        {
            // Create a custom config that doesn't duplicate exporters when specified via command line
            var config = new ManualConfig()
                .AddLogger(ConsoleLogger.Default)
                .AddColumnProvider(DefaultColumnProviders.Instance)
                .WithOptions(ConfigOptions.DisableOptimizationsValidator | ConfigOptions.DontOverwriteResults);

            var summary = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
                .Run(args, config);
        }
    }
}