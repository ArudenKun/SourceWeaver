using System.Collections.Immutable;
using JetBrains.Annotations;

namespace SourceWeaver.Generators;

[PublicAPI]
public abstract class SourceGeneratorForMemberWithAttribute<TAttribute, TDeclarationSyntax>
    : SourceGeneratorForMemberWithAttribute<TDeclarationSyntax>
    where TAttribute : Attribute
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    protected override string AttributeMetadataName { get; } = typeof(TAttribute).FullName!;
}

[PublicAPI]
public abstract class SourceGeneratorForMemberWithAttribute<TDeclarationSyntax>
    : SourceGeneratorForMemberWithAttributes<TDeclarationSyntax>
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    protected abstract string AttributeMetadataName { get; }

    protected sealed override IReadOnlyList<string> AttributeMetadataNames =>
        [AttributeMetadataName];

    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    protected sealed override (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        ImmutableArray<AttributeData> attributes,
        AnalyzerConfigOptions options
    )
    {
        // For single-attribute triggers, pass the first found matching attribute
        return GenerateCode(compilation, node, symbol, attributes[0], options);
    }
}
