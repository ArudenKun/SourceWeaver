using System.Collections.Immutable;
using JetBrains.Annotations;
using SourceWeaver.Extensions;

namespace SourceWeaver.Generators;

using GeneratorContext = IncrementalGeneratorInitializationContext;

[PublicAPI]
public abstract class SourceGeneratorForAssemblyWithAttribute<TAttribute>
    : SourceGeneratorForAssemblyWithAttribute
    where TAttribute : Attribute
{
    protected override string AttributeMetadataName { get; } = typeof(TAttribute).FullName!;
}

[PublicAPI]
public abstract class SourceGeneratorForAssemblyWithAttribute : IIncrementalGenerator
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

        // 1. Syntax provider targeting Assembly-level attributes
        var targetsProvider = context
            .SyntaxProvider.ForAttributeWithMetadataName(
                fullyQualifiedMetadataName: AttributeMetadataName,
                predicate: IsSyntaxTarget,
                transform: GetSyntaxTargets
            )
            .SelectMany(static (items, _) => items);

        // 2. Combine with Compilation and Options for incremental pipeline execution
        IncrementalValueProvider<(
            (Compilation Compilation, ImmutableArray<AssemblyGeneratorTargetContext> Targets),
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
        ImmutableArray<AssemblyGeneratorTargetContext> targets,
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
                target.Assembly,
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
                    ?? Location.None;

                spc.ReportDiagnostic(Diagnostic.Create(descriptor, location));
                continue;
            }

            spc.AddSource(GenerateFilename(target.Assembly, target.Attribute), generatedCode!);
        }
    }

    protected virtual bool IsSyntaxTarget(SyntaxNode node, CancellationToken _)
    {
        // Assembly attributes reside inside AttributeListSyntax nodes tagged with 'assembly:'
        return node is AttributeListSyntax attributeList
            && attributeList.Target?.Identifier.ValueText == "assembly";
    }

    protected virtual IEnumerable<AssemblyGeneratorTargetContext> GetSyntaxTargets(
        GeneratorAttributeSyntaxContext context,
        CancellationToken _
    )
    {
        // TargetSymbol for assembly attributes resolves to the IAssemblySymbol
        if (context.TargetSymbol is IAssemblySymbol assemblySymbol)
        {
            foreach (var attribute in context.Attributes)
            {
                yield return new AssemblyGeneratorTargetContext(assemblySymbol, attribute);
            }
        }
    }

    protected abstract (string? GeneratedCode, DiagnosticDetail? Error) GenerateCode(
        Compilation compilation,
        IAssemblySymbol assembly,
        AttributeData attribute,
        AnalyzerConfigOptions options
    );

    private (string? GeneratedCode, DiagnosticDetail? Error) _GenerateCode(
        Compilation compilation,
        IAssemblySymbol assembly,
        AttributeData attribute,
        AnalyzerConfigOptions options
    )
    {
        try
        {
            return GenerateCode(compilation, assembly, attribute, options);
        }
        catch (Exception e)
        {
            return (null, InternalError(e));
        }

        static DiagnosticDetail InternalError(Exception e) =>
            DiagnosticDetail.Error("Internal Error", e.Message);
    }

    protected const string Ext = ".g.cs";
    protected const int MaxFileLength = 255;

    protected virtual string GenerateFilename(IAssemblySymbol assembly, AttributeData attribute)
    {
        var attrName = attribute.AttributeClass?.Name.TrimSuffix("Attribute") ?? AttributeName;
        return $"{Format($"{assembly.Name}_{attrName}")}{Ext}";

        static string Format(string rawName) =>
            string.Join("_", rawName.Split(SourceWeaverConsts.InvalidFileNameChars))
                .Truncate(MaxFileLength - Ext.Length);
    }

    /// <summary>
    /// Immutable value-comparable target context for assembly attributes.
    /// </summary>
    public readonly struct AssemblyGeneratorTargetContext
        : IEquatable<AssemblyGeneratorTargetContext>
    {
        public IAssemblySymbol Assembly { get; }
        public AttributeData Attribute { get; }

        public AssemblyGeneratorTargetContext(IAssemblySymbol assembly, AttributeData attribute)
        {
            Assembly = assembly;
            Attribute = attribute;
        }

        public bool Equals(AssemblyGeneratorTargetContext other)
        {
            return SymbolEqualityComparer.Default.Equals(Assembly, other.Assembly)
                && Equals(
                    Attribute.ApplicationSyntaxReference,
                    other.Attribute.ApplicationSyntaxReference
                );
        }

        public override bool Equals(object? obj) =>
            obj is AssemblyGeneratorTargetContext other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(Assembly);
                hash = hash * 23 + (Attribute.ApplicationSyntaxReference?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
