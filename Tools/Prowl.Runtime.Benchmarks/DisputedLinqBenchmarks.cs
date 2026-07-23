using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace Prowl.Runtime.Benchmarks;

[MemoryDiagnoser]
[DisassemblyDiagnoser(
    maxDepth: 3,
    printSource: true,
    exportCombinedDisassemblyReport: true)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class DisputedLinqBenchmarks
{
    private int[] _array = [];
    private List<int> _list = [];

    [Params(256, 4096)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _array = new int[Count];
        for (int i = 0; i < _array.Length; i++)
            _array[i] = i;
        _list = new List<int>(_array);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ArrayToList")]
    public List<int> Linq_ArrayToList() => _array.ToList();

    [Benchmark]
    [BenchmarkCategory("ArrayToList")]
    public List<int> Loop_ArrayToList()
    {
        var result = new List<int>(_array.Length);
        for (int i = 0; i < _array.Length; i++)
            result.Add(_array[i]);
        return result;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ListToList")]
    public List<int> Linq_ListToList() => _list.ToList();

    [Benchmark]
    [BenchmarkCategory("ListToList")]
    public List<int> Loop_ListToList()
    {
        var result = new List<int>(_list.Count);
        for (int i = 0; i < _list.Count; i++)
            result.Add(_list[i]);
        return result;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ArrayFirstOrDefault")]
    public int Linq_ArrayFirstOrDefault() => _array.FirstOrDefault(static value => value < 0);

    [Benchmark]
    [BenchmarkCategory("ArrayFirstOrDefault")]
    public int Loop_ArrayFirstOrDefault()
    {
        for (int i = 0; i < _array.Length; i++)
            if (_array[i] < 0)
                return _array[i];
        return default;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ListFirstOrDefault")]
    public int Linq_ListFirstOrDefault() => _list.FirstOrDefault(static value => value < 0);

    [Benchmark]
    [BenchmarkCategory("ListFirstOrDefault")]
    public int Loop_ListFirstOrDefault()
    {
        for (int i = 0; i < _list.Count; i++)
            if (_list[i] < 0)
                return _list[i];
        return default;
    }
}
