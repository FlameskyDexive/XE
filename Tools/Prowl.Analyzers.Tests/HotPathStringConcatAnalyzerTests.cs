using System.Threading.Tasks;
using Xunit;
using static Prowl.Analyzers.Tests.AnalyzerTestHelper;

namespace Prowl.Analyzers.Tests;

public class HotPathStringConcatAnalyzerTests
{
    private const string Attr = @"
namespace Prowl
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, Inherited = false)]
    public sealed class HotPathAttribute : System.Attribute { }
}
";

    [Fact]
    public async Task String_Plus_In_HotPath_Reports_PR0004()
    {
        var source = Attr + @"
class C
{
    [Prowl.HotPath]
    string M(string a, string b) => {|PR0004:a + b|};
}
";
        await VerifyAsync<HotPathStringConcatAnalyzer>(source);
    }

    [Fact]
    public async Task Interpolation_In_HotPath_Reports_PR0004()
    {
        var source = Attr + @"
class C
{
    [Prowl.HotPath]
    string M(int x) => {|PR0004:$""x={x}""|};
}
";
        await VerifyAsync<HotPathStringConcatAnalyzer>(source);
    }

    [Fact]
    public async Task String_Plus_Outside_HotPath_No_Diagnostic()
    {
        var source = Attr + @"
class C
{
    string M(string a, string b) => a + b;
}
";
        await VerifyAsync<HotPathStringConcatAnalyzer>(source);
    }

    [Fact]
    public async Task Numeric_Plus_In_HotPath_No_Diagnostic()
    {
        var source = Attr + @"
class C
{
    [Prowl.HotPath]
    int M(int a, int b) => a + b;
}
";
        await VerifyAsync<HotPathStringConcatAnalyzer>(source);
    }
}
