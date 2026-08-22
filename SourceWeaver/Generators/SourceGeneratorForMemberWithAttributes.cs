using System.Collections.Immutable;
using JetBrains.Annotations;
using SourceWeaver.Extensions;

namespace SourceWeaver.Generators;

using GeneratorContext = IncrementalGeneratorInitializationContext;

[PublicAPI]
public abstract class SourceGeneratorForMemberWithAttributes<TDeclarationSyntax>
    : IIncrementalGenerator
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    /// <summary>
    /// Fully qualified metadata names of all attributes to target (e.g. "MyNamespace.MyAttribute").
    /// </summary>
    protected abstract IReadOnlyList<string> AttributeMetadataNames { get; }

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

        // Register a provider for each requested attribute and collect target contexts
        var attributeProviders = AttributeMetadataNames
            .Select(attributeName =>
                context.SyntaxProvider.ForAttributeWithMetadataName(
                    fullyQualifiedMetadataName: attributeName,
                    predicate: IsSyntaxTarget,
                    transform: GetSyntaxTargets
                )
            )
            .ToList();

        // Merge all individual IncrementalValueProviders into a single aggregated stream
        IncrementalValuesProvider<GeneratorTargetContext> combinedTargets = attributeProviders[0];

        for (var i = 1; i < attributeProviders.Count; i++)
        {
            var nextProvider = attributeProviders[i];

            combinedTargets = combinedTargets
                .Collect()
                .Combine(nextProvider.Collect())
                .SelectMany(static (pair, _) => pair.Left.AddRange(pair.Right));
        }

        // Group targets by symbol to ensure all matching attributes for a given symbol are passed together
        IncrementalValuesProvider<GeneratorTargetContext> groupedTargets = combinedTargets
            .Collect()
            .SelectMany(
                static (targets, _) =>
                {
                    var grouped = targets.GroupBy(
                        t => (t.Node, t.Symbol),
                        (key, group) =>
                        {
                            var attributes = group
                                .SelectMany(g => g.Attributes)
                                .Distinct()
                                .ToImmutableArray();

                            return new GeneratorTargetContext(key.Node, key.Symbol, attributes);
                        }
                    );

                    return grouped;
                }
            );

        // Late-stage compilation combination for pipeline caching
        IncrementalValueProvider<(
            (Compilation Compilation, ImmutableArray<GeneratorTargetContext> Targets),
            AnalyzerConfigOptionsProvider Options
        )> pipeline = context
            .CompilationProvider.Combine(groupedTargets.Collect())
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
                target.Attributes,
                options.GlobalOptions
            );

            if (string.IsNullOrWhiteSpace(generatedCode))
            {
                var primaryAttr = target.Attributes.FirstOrDefault();
                var attributeName =
                    primaryAttr?.AttributeClass?.Name.TrimSuffix("Attribute") ?? "Attribute";

                var diagnosticId = !string.IsNullOrWhiteSpace(error?.Id)
                    ? error!.Id
                    : attributeName;
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
                    primaryAttr?.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? target.Node.GetLocation();

                spc.ReportDiagnostic(Diagnostic.Create(descriptor, location));
                continue;
            }

            spc.AddSource(GenerateFilename(target.Symbol, target.Attributes), generatedCode!);
        }
    }

    protected virtual bool IsSyntaxTarget(SyntaxNode node, CancellationToken _) =>
        node is TDeclarationSyntax;

    protected virtual GeneratorTargetContext GetSyntaxTargets(
        GeneratorAttributeSyntaxContext context,
        CancellationToken _
    )
    {
        var node = Node((TDeclarationSyntax)context.TargetNode);
        var symbol = context.TargetSymbol;
        var attributes = context.Attributes.ToImmutableArray();

        return new GeneratorTargetContext(node, symbol, attributes);
    }

    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        ImmutableArray<AttributeData> attributes,
        AnalyzerConfigOptions options
    );

    private (string? GeneratedCode, DiagnosticDetail? Error) _GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        ImmutableArray<AttributeData> attributes,
        AnalyzerConfigOptions options
    )
    {
        try
        {
            return GenerateCode(compilation, node, symbol, attributes, options);
        }
        catch (Exception e)
        {
            return (null, InternalError(e));
        }

        static DiagnosticDetail InternalError(Exception e) =>
            DiagnosticDetail.Error("Internal Error", e.Message);
    }

    protected virtual string GenerateFilename(
        ISymbol symbol,
        ImmutableArray<AttributeData> attributes
    )
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

    public readonly struct GeneratorTargetContext : IEquatable<GeneratorTargetContext>
    {
        public SyntaxNode Node { get; }
        public ISymbol Symbol { get; }
        public ImmutableArray<AttributeData> Attributes { get; }

        public GeneratorTargetContext(
            SyntaxNode node,
            ISymbol symbol,
            ImmutableArray<AttributeData> attributes
        )
        {
            Node = node;
            Symbol = symbol;
            Attributes = attributes;
        }

        public bool Equals(GeneratorTargetContext other)
        {
            if (
                !Equals(Node, other.Node)
                || !SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol)
            )
                return false;

            if (Attributes.Length != other.Attributes.Length)
                return false;

            for (var i = 0; i < Attributes.Length; i++)
            {
                if (
                    !Equals(
                        Attributes[i].ApplicationSyntaxReference,
                        other.Attributes[i].ApplicationSyntaxReference
                    )
                )
                    return false;
            }

            return true;
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

                foreach (var attr in Attributes)
                {
                    hash = hash * 23 + (attr.ApplicationSyntaxReference?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }
    }
}
