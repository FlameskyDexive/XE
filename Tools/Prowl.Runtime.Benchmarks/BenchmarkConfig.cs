using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace Prowl.Runtime.Benchmarks;

internal sealed class BenchmarkConfig : ManualConfig
{
    public static readonly IConfig Instance = new BenchmarkConfig();

    private BenchmarkConfig()
    {
        AddJob(Job.Default
            .WithRuntime(CoreRuntime.Core10_0)
            .WithWarmupCount(3)
            .WithIterationCount(8)
            .WithEnvironmentVariable("DOTNET_TieredCompilation", "1")
            .WithEnvironmentVariable("DOTNET_TieredPGO", "1")
            .WithId(".NET 10 TieredPGO"));

        AddDiagnoser(MemoryDiagnoser.Default);
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(JsonExporter.Full);
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddColumn(RankColumn.Arabic);
        AddLogger(ConsoleLogger.Default);
        WithOption(ConfigOptions.JoinSummary, true);
    }
}
