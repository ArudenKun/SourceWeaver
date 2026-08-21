using System.Collections.Immutable;
using JetBrains.Annotations;
using SourceWeaver.Extensions;

namespace SourceWeaver.Generators;

using GeneratorContext = IncrementalGeneratorInitializationContext;

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
    : IIncrementalGenerator
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    protected abstract string AttributeMetadataName { get; }

    protected string AttributeType => AttributeMetadataName.Split('.').Last();

    protected string AttributeName => AttributeType.TrimSuffix("Attribute");

    protected virtual ICollection<(string Name, string Text)> StaticSources => [];

    public void Initialize(GeneratorContext context)
    {
        if (StaticSources.Count > 0)
        {
            context.RegisterPostInitializationOutput(postContext =>
            {
                foreach (var (name, source) in StaticSources)
                {
                    postContext.AddSource($"{name}.g.cs", source);
                }
            });
        }

        // 2. Syntax provider using ForAttributeWithMetadataName
        var targetsProvider = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: AttributeMetadataName,
                predicate: IsSyntaxTarget,
                transform: GetSyntaxTargets
            )
            .SelectMany(static (items, _) => items); // Flattens targets when attribute is applied multiple times

        // 3. Late-stage compilation combination to maximize pipeline caching
        IncrementalValueProvider<(
            (Compilation Compilation, ImmutableArray<GeneratorTargetContext> Targets),
            AnalyzerConfigOptionsProvider Options
        )> pipeline = context
            .CompilationProvider.Combine(targetsProvider.Collect())
            .Combine(context.AnalyzerConfigOptionsProvider);

        context.RegisterSourceOutput(
            pipeline,
            (spc, source) =>
                OnExecute(spc, source.Item1.Compilation, source.Item1.Targets, source.Options)
        );
    }

    private void OnExecute(
        SourceProductionContext spc,
        Compilation compilation,
        ImmutableArray<GeneratorTargetContext> targets,
        AnalyzerConfigOptionsProvider options
    )
    {
        if (targets.IsEmpty)
            return;

        foreach (var target in targets)
        {
            if (spc.CancellationToken.IsCancellationRequested)
                return;

            var (generatedCode, error) = _GenerateCode(
                compilation,
                target.Node,
                target.Symbol,
                target.Attribute,
                options.GlobalOptions
            );

            if (string.IsNullOrWhiteSpace(generatedCode))
            {
                var diagnosticId = !string.IsNullOrWhiteSpace(error?.Id)
                    ? error!.Id
                    : AttributeName;
                var category = !string.IsNullOrWhiteSpace(error?.Category)
                    ? error!.Category
                    : "Usage";
                var title = error?.Title ?? "Generation Error";
                var message = error?.Message ?? "An error occurred during code generation.";

                var descriptor = new DiagnosticDescriptor(
                    diagnosticId,
                    title,
                    message,
                    category,
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true
                );

                var location =
                    target.Attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? target.Node.GetLocation();

                spc.ReportDiagnostic(Diagnostic.Create(descriptor, location));
                continue;
            }

            spc.AddSource(GenerateFilename(target.Symbol), generatedCode!);
        }
    }

    protected virtual bool IsSyntaxTarget(SyntaxNode node, CancellationToken _) =>
        node is TDeclarationSyntax;

    /// <summary>
    /// Transforms the context into target items. Yields all matching attributes present on the target node.
    /// </summary>
    protected virtual IEnumerable<GeneratorTargetContext> GetSyntaxTargets(
        GeneratorAttributeSyntaxContext context,
        CancellationToken _
    )
    {
        var node = Node((TDeclarationSyntax)context.TargetNode);
        var symbol = context.TargetSymbol;

        foreach (var attribute in context.Attributes)
            yield return new GeneratorTargetContext(node, symbol, attribute);
    }

    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    private (string? GeneratedCode, DiagnosticDetail? Error) _GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        AttributeData attribute,
        AnalyzerConfigOptions options
    )
    {
        try
        {
            return GenerateCode(compilation, node, symbol, attribute, options);
        }
        catch (Exception e)
        {
            return (null, InternalError(e));
        }

        static DiagnosticDetail InternalError(Exception e) =>
            DiagnosticDetail.Error("Internal Error", e.Message);
    }

    protected virtual string GenerateFilename(ISymbol symbol)
    {
        return $"{Format(symbol)}{SourceWeaverConsts.Ext}";
        static string Format(ISymbol? symbol) =>
            string.Join(
                    "_",
                    (symbol?.ToDisplayString() ?? "Generated").Split(
                        SourceWeaverConsts.InvalidFileNameChars
                    )
                )
                .Truncate(SourceWeaverConsts.MaxFileLength - SourceWeaverConsts.Ext.Length);
    }

    protected virtual SyntaxNode Node(TDeclarationSyntax node) => node;

    /// <summary>
    /// Immutable value-comparable target context. Uses <see cref="SymbolEqualityComparer.Default"/>
    /// to avoid breaking Roslyn incremental cache keys across build passes.
    /// </summary>
    public readonly struct GeneratorTargetContext : IEquatable<GeneratorTargetContext>
    {
        public SyntaxNode Node { get; }
        public ISymbol Symbol { get; }
        public AttributeData Attribute { get; }

        public GeneratorTargetContext(SyntaxNode node, ISymbol symbol, AttributeData attribute)
        {
            Node = node;
            Symbol = symbol;
            Attribute = attribute;
        }

        public bool Equals(GeneratorTargetContext other)
        {
            return Equals(Node, other.Node)
                && SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol)
                && Equals(
                    Attribute.ApplicationSyntaxReference,
                    other.Attribute.ApplicationSyntaxReference
                );
        }

        public override bool Equals(object? obj) =>
            obj is GeneratorTargetContext other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + (Node?.GetHashCode() ?? 0);
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(Symbol);
                hash = hash * 23 + (Attribute.ApplicationSyntaxReference?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
