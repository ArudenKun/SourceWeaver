using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SourceWeaver.Generators;

namespace SourceWeaver.Tests;

public class TestGenerator : SourceGeneratorForMemberWithAttributes<ClassDeclarationSyntax>
{
    protected override IReadOnlyList<string> AttributeMetadataNames =>
        [typeof(TestAttribute).FullName!];

    protected override (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        ImmutableArray<AttributeData> attributes,
        AnalyzerConfigOptions options
    )
    {
        throw new NotImplementedException();
    }
}
