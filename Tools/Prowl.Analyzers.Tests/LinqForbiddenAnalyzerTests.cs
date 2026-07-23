using System.Threading.Tasks;
using Xunit;
using static Prowl.Analyzers.Tests.AnalyzerTestHelper;

namespace Prowl.Analyzers.Tests;

public class LinqForbiddenAnalyzerTests
{
    [Fact]
    public async Task Using_System_Linq_Reports_PR0001()
    {
        const string source = @"
using {|PR0001:System.Linq|};
class C { void M() { } }
";
        await VerifyAsync<LinqForbiddenAnalyzer>(source);
    }

    [Fact]
    public async Task No_Linq_No_Diagnostic()
    {
        const string source = @"
class C
{
    void M()
    {
        var a = new[] { 1, 2, 3 };
        for (int i = 0; i < a.Length; i++) { _ = a[i]; }
    }
}
";
        await VerifyAsync<LinqForbiddenAnalyzer>(source);
    }

    [Fact]
    public async Task Linq_Invocation_Reports_PR0001()
    {
        const string source = @"
using {|PR0001:System.Linq|};
class C
{
    void M(int[] xs)
    {
        var f = {|PR0001:xs.First()|};
    }
}
";
        await VerifyAsync<LinqForbiddenAnalyzer>(source);
    }

    [Fact]
    public async Task Query_Expression_Reports_PR0001()
    {
        // Include System.Linq so the compiler accepts the query pattern; PR0001 still fires on using + query.
        const string source = @"
using {|PR0001:System.Linq|};
class C
{
    void M(int[] xs)
    {
        var q = {|PR0001:from x in xs where x > 0 select x|};
    }
}
";
        await VerifyAsync<LinqForbiddenAnalyzer>(source);
    }
}
