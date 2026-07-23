using System.Globalization;
using System.Text;

namespace Prowl.Runtime.Benchmarks;

internal static class AllocationProbe
{
    private static object? s_sink;

    public static void Run(string outputPath)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Operation,Implementation,SourceKind,Count,AllocatedBytesPerOperation");

        foreach (SourceKind sourceKind in Enum.GetValues<SourceKind>())
        {
            foreach (int count in new[] { 0, 1, 16, 256, 4096 })
            {
                var materialization = new MaterializationBenchmarks
                {
                    SourceKind = sourceKind,
                    Count = count,
                };
                materialization.Setup();

                Record(csv, "WhereToArray", "Linq", sourceKind, count, () => materialization.Linq_WhereToArray());
                Record(csv, "WhereToArray", "Loop", sourceKind, count, () => materialization.Loop_WhereToArray());
                Record(csv, "ToList", "Linq", sourceKind, count, () => materialization.Linq_ToList());
                Record(csv, "ToList", "Loop", sourceKind, count, () => materialization.Loop_ToList());
                Record(csv, "CastToList", "Linq", sourceKind, count, () => materialization.Linq_CastToList());
                Record(csv, "CastToList", "Loop", sourceKind, count, () => materialization.Loop_CastToList());

                var composition = new CompositionBenchmarks
                {
                    SourceKind = sourceKind,
                    Count = count,
                };
                composition.Setup();
                Record(csv, "SelectManyDistinctToList", "Linq", sourceKind, count,
                    () => composition.Linq_SelectManyDistinctToList());
                Record(csv, "SelectManyDistinctToList", "Loop", sourceKind, count,
                    () => composition.Loop_SelectManyDistinctToList());
            }
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(outputPath, csv.ToString());
    }

    private static void Record(
        StringBuilder csv,
        string operation,
        string implementation,
        SourceKind sourceKind,
        int count,
        Func<object> action)
    {
        for (int i = 0; i < 100; i++)
            s_sink = action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int iterations = count switch
        {
            <= 16 => 10_000,
            <= 256 => 2_000,
            _ => 200,
        };

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            s_sink = action();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        double perOperation = allocated / (double)iterations;

        csv.Append(operation).Append(',')
            .Append(implementation).Append(',')
            .Append(sourceKind).Append(',')
            .Append(count.ToString(CultureInfo.InvariantCulture)).Append(',')
            .AppendLine(perOperation.ToString("F2", CultureInfo.InvariantCulture));
    }
}
