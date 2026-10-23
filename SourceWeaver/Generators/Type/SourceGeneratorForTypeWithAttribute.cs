using JetBrains.Annotations;

namespace SourceWeaver.Generators.Type;

[PublicAPI]
public abstract class SourceGeneratorForTypeWithAttribute
    : SourceGeneratorForMemberWithAttribute<TypeDeclarationSyntax>
{
    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        INamedTypeSymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    protected sealed override (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    ) => GenerateCode(compilation, node, (INamedTypeSymbol)symbol, attribute, options);
}

[PublicAPI]
public abstract class SourceGeneratorForTypeWithAttribute<TAttribute>
    : SourceGeneratorForMemberWithAttribute<TAttribute, TypeDeclarationSyntax>
    where TAttribute : Attribute
{
    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        INamedTypeSymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    protected sealed override (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    ) => GenerateCode(compilation, node, (INamedTypeSymbol)symbol, attribute, options);
}
