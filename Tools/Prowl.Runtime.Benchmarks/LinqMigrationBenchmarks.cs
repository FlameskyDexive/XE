using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace Prowl.Runtime.Benchmarks;

public enum SourceKind
{
    Array,
    List,
    Iterator,
}

public abstract class SourceBenchmarkBase
{
    [Params(SourceKind.Array, SourceKind.List, SourceKind.Iterator)]
    public SourceKind SourceKind { get; set; }

    [Params(0, 1, 16, 256, 4096)]
    public int Count { get; set; }

    protected int[] ArraySource = [];
    protected List<int> ListSource = [];
    protected IEnumerable<int> Source = [];

    [GlobalSetup]
    public virtual void Setup()
    {
        ArraySource = new int[Count];
        for (int i = 0; i < ArraySource.Length; i++)
            ArraySource[i] = i;

        ListSource = new List<int>(ArraySource);
        Source = SourceKind switch
        {
            SourceKind.Array => ArraySource,
            SourceKind.List => ListSource,
            SourceKind.Iterator => Yield(ArraySource),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    protected int ManualFirstNegative()
    {
        switch (SourceKind)
        {
            case SourceKind.Array:
                for (int i = 0; i < ArraySource.Length; i++)
                    if (ArraySource[i] < 0)
                        return ArraySource[i];
                break;
            case SourceKind.List:
                for (int i = 0; i < ListSource.Count; i++)
                    if (ListSource[i] < 0)
                        return ListSource[i];
                break;
            case SourceKind.Iterator:
                foreach (int value in Source)
                    if (value < 0)
                        return value;
                break;
        }
        return default;
    }

    protected bool ManualAnyNegative()
    {
        switch (SourceKind)
        {
            case SourceKind.Array:
                for (int i = 0; i < ArraySource.Length; i++)
                    if (ArraySource[i] < 0)
                        return true;
                break;
            case SourceKind.List:
                for (int i = 0; i < ListSource.Count; i++)
                    if (ListSource[i] < 0)
                        return true;
                break;
            case SourceKind.Iterator:
                foreach (int value in Source)
                    if (value < 0)
                        return true;
                break;
        }
        return false;
    }

    protected int ManualCountEven()
    {
        int result = 0;
        switch (SourceKind)
        {
            case SourceKind.Array:
                for (int i = 0; i < ArraySource.Length; i++)
                    if ((ArraySource[i] & 1) == 0)
                        result++;
                break;
            case SourceKind.List:
                for (int i = 0; i < ListSource.Count; i++)
                    if ((ListSource[i] & 1) == 0)
                        result++;
                break;
            case SourceKind.Iterator:
                foreach (int value in Source)
                    if ((value & 1) == 0)
                        result++;
                break;
        }
        return result;
    }

    protected bool ManualAllNonNegative()
    {
        switch (SourceKind)
        {
            case SourceKind.Array:
                for (int i = 0; i < ArraySource.Length; i++)
                    if (ArraySource[i] < 0)
                        return false;
                break;
            case SourceKind.List:
                for (int i = 0; i < ListSource.Count; i++)
                    if (ListSource[i] < 0)
                        return false;
                break;
            case SourceKind.Iterator:
                foreach (int value in Source)
                    if (value < 0)
                        return false;
                break;
        }
        return true;
    }

    protected static IEnumerable<int> Yield(int[] values)
    {
        for (int i = 0; i < values.Length; i++)
            yield return values[i];
    }
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SearchAndPredicateBenchmarks : SourceBenchmarkBase
{
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("FirstOrDefault")]
    public int Linq_FirstOrDefault() => Source.FirstOrDefault(static value => value < 0);

    [Benchmark]
    [BenchmarkCategory("FirstOrDefault")]
    public int Loop_FirstOrDefault() => ManualFirstNegative();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Any")]
    public bool Linq_Any() => Source.Any(static value => value < 0);

    [Benchmark]
    [BenchmarkCategory("Any")]
    public bool Loop_Any() => ManualAnyNegative();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Count")]
    public int Linq_Count() => Source.Count(static value => (value & 1) == 0);

    [Benchmark]
    [BenchmarkCategory("Count")]
    public int Loop_Count() => ManualCountEven();

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("All")]
    public bool Linq_All() => Source.All(static value => value >= 0);

    [Benchmark]
    [BenchmarkCategory("All")]
    public bool Loop_All() => ManualAllNonNegative();
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class MaterializationBenchmarks : SourceBenchmarkBase
{
    private IEnumerable<object> _objectSource = [];
    private object[] _objectArray = [];
    private List<object> _objectList = [];

    public override void Setup()
    {
        base.Setup();
        _objectArray = new object[Count];
        for (int i = 0; i < _objectArray.Length; i++)
            _objectArray[i] = ArraySource[i];
        _objectList = new List<object>(_objectArray);
        _objectSource = SourceKind switch
        {
            SourceKind.Array => _objectArray,
            SourceKind.List => _objectList,
            SourceKind.Iterator => YieldObjects(_objectArray),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("WhereToArray")]
    public int[] Linq_WhereToArray() => Source.Where(static value => (value & 1) == 0).ToArray();

    [Benchmark]
    [BenchmarkCategory("WhereToArray")]
    public int[] Loop_WhereToArray()
    {
        var result = new List<int>((Count + 1) / 2);
        switch (SourceKind)
        {
            case SourceKind.Array:
                for (int i = 0; i < ArraySource.Length; i++)
                    if ((ArraySource[i] & 1) == 0)
                        result.Add(ArraySource[i]);
                break;
            case SourceKind.List:
                for (int i = 0; i < ListSource.Count; i++)
                    if ((ListSource[i] & 1) == 0)
                        result.Add(ListSource[i]);
                break;
            case SourceKind.Iterator:
                foreach (int value in Source)
                    if ((value & 1) == 0)
                        result.Add(value);
                break;
        }
        return result.ToArray();
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ToList")]
    public List<int> Linq_ToList() => Source.ToList();

    [Benchmark]
    [BenchmarkCategory("ToList")]
    public List<int> Loop_ToList()
    {
        var result = new List<int>(Count);
        switch (SourceKind)
        {
            case SourceKind.Array:
                for (int i = 0; i < ArraySource.Length; i++)
                    result.Add(ArraySource[i]);
                break;
            case SourceKind.List:
                for (int i = 0; i < ListSource.Count; i++)
                    result.Add(ListSource[i]);
                break;
            case SourceKind.Iterator:
                foreach (int value in Source)
                    result.Add(value);
                break;
        }
        return result;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("CastToList")]
    public List<int> Linq_CastToList() => _objectSource.Cast<int>().ToList();

    [Benchmark]
    [BenchmarkCategory("CastToList")]
    public List<int> Loop_CastToList()
    {
        var result = new List<int>(Count);
        switch (SourceKind)
        {
            case SourceKind.Array:
                for (int i = 0; i < _objectArray.Length; i++)
                    result.Add((int)_objectArray[i]);
                break;
            case SourceKind.List:
                for (int i = 0; i < _objectList.Count; i++)
                    result.Add((int)_objectList[i]);
                break;
            case SourceKind.Iterator:
                foreach (object value in _objectSource)
                    result.Add((int)value);
                break;
        }
        return result;
    }

    private static IEnumerable<object> YieldObjects(object[] values)
    {
        for (int i = 0; i < values.Length; i++)
            yield return values[i];
    }
}

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class CompositionBenchmarks : SourceBenchmarkBase
{
    private IEnumerable<int> _left = [];
    private IEnumerable<int> _right = [];
    private int[][] _segments = [];

    public override void Setup()
    {
        base.Setup();
        int midpoint = Count / 2;
        int[] left = ArraySource[..midpoint];
        int[] right = ArraySource[midpoint..];
        _left = CreateSource(left);
        _right = CreateSource(right);

        _segments = new int[8][];
        for (int segment = 0; segment < _segments.Length; segment++)
        {
            var values = new List<int>();
            for (int i = segment; i < ArraySource.Length; i += _segments.Length)
                values.Add(ArraySource[i]);
            _segments[segment] = values.ToArray();
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Concat")]
    public long Linq_ConcatConsume()
    {
        long sum = 0;
        foreach (int value in _left.Concat(_right))
            sum += value;
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("Concat")]
    public long Loop_ConcatConsume()
    {
        long sum = 0;
        foreach (int value in _left)
            sum += value;
        foreach (int value in _right)
            sum += value;
        return sum;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("DeepConcat")]
    public long Linq_DeepConcatConsume()
    {
        IEnumerable<int> combined = Enumerable.Empty<int>();
        for (int i = 0; i < _segments.Length; i++)
            combined = combined.Concat(_segments[i]);

        long sum = 0;
        foreach (int value in combined)
            sum += value;
        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("DeepConcat")]
    public long Loop_DeepConcatConsume()
    {
        long sum = 0;
        for (int segment = 0; segment < _segments.Length; segment++)
            for (int i = 0; i < _segments[segment].Length; i++)
                sum += _segments[segment][i];
        return sum;
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SelectManyDistinctToList")]
    public List<int> Linq_SelectManyDistinctToList() =>
        _segments.SelectMany(static values => values).Distinct().ToList();

    [Benchmark]
    [BenchmarkCategory("SelectManyDistinctToList")]
    public List<int> Loop_SelectManyDistinctToList()
    {
        var result = new List<int>(Count);
        var seen = new HashSet<int>();
        for (int segment = 0; segment < _segments.Length; segment++)
        {
            int[] values = _segments[segment];
            for (int i = 0; i < values.Length; i++)
                if (seen.Add(values[i]))
                    result.Add(values[i]);
        }
        return result;
    }

    private IEnumerable<int> CreateSource(int[] values) => SourceKind switch
    {
        SourceKind.Array => values,
        SourceKind.List => new List<int>(values),
        SourceKind.Iterator => Yield(values),
        _ => throw new ArgumentOutOfRangeException(),
    };
}
