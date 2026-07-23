using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Prowl.Analyzers;

/// <summary>PR0001: bans System.Linq usings, invocations, and query expressions.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LinqForbiddenAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor s_rule = new(
        DiagnosticIds.LinqForbidden,
        "System.Linq forbidden",
        "System.Linq is forbidden in Prowl (performance). Use explicit for loops. Affected: {0}.",
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Prowl bans System.Linq entirely. Replace Select/Where/OrderBy/Any/First/etc with explicit for loops.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(s_rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeUsing, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeQuery, SyntaxKind.QueryExpression);
    }

    private static void AnalyzeUsing(SyntaxNodeAnalysisContext ctx)
    {
        var us = (UsingDirectiveSyntax)ctx.Node;
        if (us.Name is null)
            return;

        string name = us.Name.ToString();
        if (name == "System.Linq" || name.StartsWith("System.Linq.", System.StringComparison.Ordinal))
            ctx.ReportDiagnostic(Diagnostic.Create(s_rule, us.Name.GetLocation(), "using " + name));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;
        if (ctx.SemanticModel.GetSymbolInfo(inv).Symbol is not IMethodSymbol m)
            return;

        var ns = m.ContainingNamespace?.ToDisplayString();
        if (ns != null &&
            (ns == "System.Linq" || ns.StartsWith("System.Linq.", System.StringComparison.Ordinal)))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(s_rule, inv.GetLocation(), m.Name));
        }
    }

    private static void AnalyzeQuery(SyntaxNodeAnalysisContext ctx)
    {
        ctx.ReportDiagnostic(Diagnostic.Create(s_rule, ctx.Node.GetLocation(), "query expression"));
    }
}
