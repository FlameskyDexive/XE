using System.Threading.Tasks;
using Xunit;
using static Prowl.Analyzers.Tests.AnalyzerTestHelper;

namespace Prowl.Analyzers.Tests;

public class HotPathNewAnalyzerTests
{
    private const string Attr = @"
namespace Prowl
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, Inherited = false)]
    public sealed class HotPathAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false)]
    public sealed class PoolAttribute : System.Attribute
    {
        public int InitialCapacity { get; set; } = 16;
    }
}
";

    [Fact]
    public async Task New_Class_In_HotPath_Reports_PR0007()
    {
        var source = Attr + @"
class Box { }
class C
{
    [Prowl.HotPath]
    object M() => {|PR0007:new Box()|};
}
";
        await VerifyAsync<HotPathNewAnalyzer>(source);
    }

    [Fact]
    public async Task New_Struct_In_HotPath_No_Diagnostic()
    {
        var source = Attr + @"
struct Point { public int X; }
class C
{
    [Prowl.HotPath]
    Point M() => new Point();
}
";
        await VerifyAsync<HotPathNewAnalyzer>(source);
    }

    [Fact]
    public async Task New_Pooled_Class_In_HotPath_No_Diagnostic()
    {
        var source = Attr + @"
[Prowl.Pool]
class PooledBox { }
class C
{
    [Prowl.HotPath]
    object M() => new PooledBox();
}
";
        await VerifyAsync<HotPathNewAnalyzer>(source);
    }

    [Fact]
    public async Task New_Class_Outside_HotPath_No_Diagnostic()
    {
        var source = Attr + @"
class Box { }
class C
{
    object M() => new Box();
}
";
        await VerifyAsync<HotPathNewAnalyzer>(source);
    }

    [Fact]
    public async Task New_Array_In_HotPath_Reports_PR0007()
    {
        var source = Attr + @"
class C
{
    [Prowl.HotPath]
    int[] M() => {|PR0007:new int[4]|};
}
";
        await VerifyAsync<HotPathNewAnalyzer>(source);
    }

    [Fact]
    public async Task Collection_Expression_Array_In_HotPath_Reports_PR0007()
    {
        var source = Attr + @"
class C
{
    [Prowl.HotPath]
    int[] M() => {|PR0007:[1, 2, 3]|};
}
";
        await VerifyAsync<HotPathNewAnalyzer>(source);
    }

    [Fact]
    public async Task New_String_In_HotPath_Reports_PR0007()
    {
        var source = Attr + @"
class C
{
    [Prowl.HotPath]
    string M(char[] chars) => {|PR0007:new string(chars)|};
}
";
        await VerifyAsync<HotPathNewAnalyzer>(source);
    }
}
