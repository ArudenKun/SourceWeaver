using JetBrains.Annotations;

namespace SourceWeaver.Generators;

[PublicAPI]
public abstract class SourceGeneratorForPropertyWithAttribute
    : SourceGeneratorForMemberWithAttribute<PropertyDeclarationSyntax>
{
    protected abstract (string GeneratedCode, DiagnosticDetail Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        IPropertySymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    protected sealed override (string GeneratedCode, DiagnosticDetail Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    ) => GenerateCode(compilation, node, (IPropertySymbol)symbol, attribute, options);
}

[PublicAPI]
public abstract class SourceGeneratorForPropertyWithAttribute<TAttribute>
    : SourceGeneratorForMemberWithAttribute<TAttribute, PropertyDeclarationSyntax>
    where TAttribute : Attribute
{
    protected abstract (string GeneratedCode, DiagnosticDetail Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        IPropertySymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    protected sealed override (string GeneratedCode, DiagnosticDetail Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    ) => GenerateCode(compilation, node, (IPropertySymbol)symbol, attribute, options);
}
