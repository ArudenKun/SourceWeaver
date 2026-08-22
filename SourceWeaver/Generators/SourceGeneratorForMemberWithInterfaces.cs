using System.Collections.Immutable;
using JetBrains.Annotations;
using SourceWeaver.Extensions;

namespace SourceWeaver.Generators;

using GeneratorContext = IncrementalGeneratorInitializationContext;

[PublicAPI]
public abstract class SourceGeneratorForMemberWithInterfaces<TDeclarationSyntax>
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

        var targetsProvider = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: IsSyntaxTarget,
                transform: GetSyntaxTargets
            )
            .Where(static targets => !targets.IsEmpty)
            .SelectMany(static (items, _) => items);

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

        foreach (var target in targets)
        {
            if (spc.CancellationToken.IsCancellationRequested)
                return;

            var (generatedCode, error) = _GenerateCode(
                compilation,
                target.Node,
                target.Symbol,
                target.InterfaceSymbol,
                options.GlobalOptions
            );

            if (string.IsNullOrWhiteSpace(generatedCode))
            {
                var diagnosticId = !string.IsNullOrWhiteSpace(error?.Id)
                    ? error!.Id
                    : target.InterfaceSymbol.Name;
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

                var location = target.Node.GetLocation();
                spc.ReportDiagnostic(Diagnostic.Create(descriptor, location));
                continue;
            }

            spc.AddSource(GenerateFilename(target.Symbol, target.InterfaceSymbol), generatedCode!);
        }
    }

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
        var symbol =
            context.SemanticModel.GetDeclaredSymbol(node, cancellationToken) as INamedTypeSymbol;

        if (symbol == null)
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

    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        INamedTypeSymbol interfaceSymbol,
        AnalyzerConfigOptions options
    );

    private (string? GeneratedCode, DiagnosticDetail? Error) _GenerateCode(
        Compilation compilation,
        SyntaxNode node,
        ISymbol symbol,
        INamedTypeSymbol interfaceSymbol,
        AnalyzerConfigOptions options
    )
    {
        try
        {
            return GenerateCode(compilation, node, symbol, interfaceSymbol, options);
        }
        catch (Exception e)
        {
            return (null, InternalError(e));
        }

        static DiagnosticDetail InternalError(Exception e) =>
            DiagnosticDetail.Error("Internal Error", e.Message);
    }

    protected virtual string GenerateFilename(ISymbol symbol, INamedTypeSymbol interfaceSymbol)
    {
        return $"{Format(symbol)}_{interfaceSymbol.Name}{SourceWeaverConsts.Ext}";

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
