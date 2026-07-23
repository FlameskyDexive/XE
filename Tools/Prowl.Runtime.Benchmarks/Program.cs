using BenchmarkDotNet.Running;

namespace Prowl.Runtime.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        if (args is ["--allocation-probe", string outputPath])
        {
            AllocationProbe.Run(outputPath);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, BenchmarkConfig.Instance);
    }
}
