using System.Collections.Immutable;
using JetBrains.Annotations;

namespace SourceWeaver.Generators;

using GeneratorContext = IncrementalGeneratorInitializationContext;

[PublicAPI]
public abstract class AggregateSourceGeneratorForMemberWithAttributes<TDeclarationSyntax>
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

        if (AttributeMetadataNames.Count == 0)
            return;

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

        // Group targets by symbol so all matching attributes on a given symbol are merged
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

        // Batch all grouped targets into a single collection for the final output pipeline
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

        try
        {
            var (generatedCode, error) = GenerateCode(compilation, targets, options.GlobalOptions);

            if (error != null || string.IsNullOrWhiteSpace(generatedCode))
            {
                var diagnosticId = !string.IsNullOrWhiteSpace(error?.Id) ? error!.Id : "AGG001";
                var category = !string.IsNullOrWhiteSpace(error?.Category)
                    ? error!.Category
                    : "Usage";
                var title = error?.Title ?? "Aggregate Generation Error";
                var message =
                    error?.Message ?? "An error occurred during aggregate code generation.";

                var descriptor = new DiagnosticDescriptor(
                    diagnosticId,
                    title,
                    message,
                    category,
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true
                );

                spc.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
                return;
            }

            spc.AddSource(GenerateHintName(), generatedCode!);
        }
        catch (Exception e)
        {
            var descriptor = new DiagnosticDescriptor(
                "AGG002",
                "Internal Error",
                e.Message,
                "Usage",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );
            spc.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None));
        }
    }

    protected abstract string GenerateHintName();

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

    protected virtual SyntaxNode Node(TDeclarationSyntax node) => node;

    /// <summary>
    /// Receives all accumulated attribute-marked targets across the compilation unit to produce a single source file.
    /// </summary>
    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        ImmutableArray<GeneratorTargetContext> targets,
        AnalyzerConfigOptions options
    );

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
