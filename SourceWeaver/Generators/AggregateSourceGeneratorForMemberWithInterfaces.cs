using System.Collections.Immutable;
using JetBrains.Annotations;

namespace SourceWeaver.Generators;

using GeneratorContext = IncrementalGeneratorInitializationContext;

[PublicAPI]
public abstract class AggregateSourceGeneratorForMemberWithInterfaces<TDeclarationSyntax>
    : IIncrementalGenerator
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    protected abstract IReadOnlyList<string> InterfaceMetadataNames { get; }

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

        // Collect all matching targets across the compilation into an ImmutableArray
        IncrementalValuesProvider<InterfaceTargetContext> targetsProvider = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: IsSyntaxTarget,
                transform: GetSyntaxTargets
            )
            .Where(static targets => !targets.IsEmpty)
            .SelectMany(static (items, _) => items);

        // Batch all context items together using .Collect()
        IncrementalValueProvider<(
            (Compilation Compilation, ImmutableArray<InterfaceTargetContext> Targets),
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
        ImmutableArray<InterfaceTargetContext> targets,
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

    protected virtual bool IsSyntaxTarget(SyntaxNode node, CancellationToken _)
    {
        if (node is not TDeclarationSyntax)
            return false;

        return node switch
        {
            BaseTypeDeclarationSyntax baseType => baseType.BaseList != null
                && baseType.BaseList.Types.Count > 0,
            _ => false,
        };
    }

    protected virtual ImmutableArray<InterfaceTargetContext> GetSyntaxTargets(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken
    )
    {
        var node = Node((TDeclarationSyntax)context.Node);

        if (
            context.SemanticModel.GetDeclaredSymbol(node, cancellationToken)
            is not INamedTypeSymbol symbol
        )
            return ImmutableArray<InterfaceTargetContext>.Empty;

        var builder = ImmutableArray.CreateBuilder<InterfaceTargetContext>();

        foreach (var interfaceSymbol in symbol.AllInterfaces)
        {
            var metadataName = interfaceSymbol
                .ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);

            foreach (var targetInterfaceName in InterfaceMetadataNames)
            {
                if (string.Equals(metadataName, targetInterfaceName, StringComparison.Ordinal))
                {
                    builder.Add(new InterfaceTargetContext(node, symbol, interfaceSymbol));
                }
            }
        }

        return builder.ToImmutable();
    }

    protected virtual SyntaxNode Node(TDeclarationSyntax node) => node;

    /// <summary>
    /// Receives all accumulated targets across the compilation unit to produce a single source file.
    /// </summary>
    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        ImmutableArray<InterfaceTargetContext> targets,
        AnalyzerConfigOptions options
    );

    public readonly struct InterfaceTargetContext : IEquatable<InterfaceTargetContext>
    {
        public SyntaxNode Node { get; }
        public ISymbol Symbol { get; }
        public INamedTypeSymbol InterfaceSymbol { get; }

        public InterfaceTargetContext(
            SyntaxNode node,
            ISymbol symbol,
            INamedTypeSymbol interfaceSymbol
        )
        {
            Node = node;
            Symbol = symbol;
            InterfaceSymbol = interfaceSymbol;
        }

        public bool Equals(InterfaceTargetContext other)
        {
            return Equals(Node, other.Node)
                && SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol)
                && SymbolEqualityComparer.Default.Equals(InterfaceSymbol, other.InterfaceSymbol);
        }

        public override bool Equals(object? obj) =>
            obj is InterfaceTargetContext other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + (Node?.GetHashCode() ?? 0);
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(Symbol);
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(InterfaceSymbol);
                return hash;
            }
        }
    }
}
