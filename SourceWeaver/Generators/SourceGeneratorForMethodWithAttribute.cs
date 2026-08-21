using JetBrains.Annotations;

namespace SourceWeaver.Generators;

[PublicAPI]
public abstract class SourceGeneratorForMethodWithAttribute
    : SourceGeneratorForMemberWithAttribute<MethodDeclarationSyntax>
{
    protected abstract (string GeneratedCode, DiagnosticDetail Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        IMethodSymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    protected sealed override (string GeneratedCode, DiagnosticDetail Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    ) => GenerateCode(compilation, node, (IMethodSymbol)symbol, attribute, options);
}

[PublicAPI]
public abstract class SourceGeneratorForMethodWithAttribute<TAttribute>
    : SourceGeneratorForMemberWithAttribute<TAttribute, MethodDeclarationSyntax>
    where TAttribute : Attribute
{
    protected abstract (string GeneratedCode, DiagnosticDetail Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        IMethodSymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    protected sealed override (string GeneratedCode, DiagnosticDetail Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    ) => GenerateCode(compilation, node, (IMethodSymbol)symbol, attribute, options);
}
