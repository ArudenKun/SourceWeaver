using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SourceWeaver.Builder;
using SourceWeaver.Generators;

namespace SourceWeaver.Tests.Analyzer;

[Generator]
public class TestGenerator
    : SourceGeneratorForMemberWithAttribute<TestAttribute, ClassDeclarationSyntax>
{
    protected override ICollection<(string Name, string Text)> StaticSources { get; } =
    [
        (
            "TestAttribute.g.cs",
            """
             
            namespace SourceWeaver.Tests.Analyzer;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
            public class TestAttribute : Attribute { }
            """
        ),
    ];

    protected override (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    )
    {
        var builder = CodeBuilder.Create((INamedTypeSymbol)symbol);

        builder.AddProperty("TestProperty").SetType<string>().WithValue("Money");

        return (builder.Build(), null);
    }
}
