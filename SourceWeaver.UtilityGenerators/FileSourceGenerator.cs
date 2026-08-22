using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceWeaver.UtilityGenerators;

[Generator]
internal sealed class FileSourceGenerator : IIncrementalGenerator
{
    private const string StartHeaderMarker = "// [GenerateSourceText]";
    private const string EndHeaderMarker = "// [/]";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(SyntaxTree Tree, SemanticModel SemanticModel)> targets = context
            .SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is CompilationUnitSyntax,
                transform: static (ctx, _) => GetTarget(ctx)
            )
            .Where(static item => item.HasValue)
            .Select(static (item, _) => item!.Value);

        IncrementalValueProvider<
            ImmutableArray<(SyntaxTree Tree, SemanticModel SemanticModel)>
        > collectedTargets = targets.Collect();

        context.RegisterSourceOutput(
            collectedTargets,
            static (spc, source) => Execute(source, spc)
        );
    }

    private static (SyntaxTree Tree, SemanticModel SemanticModel)? GetTarget(
        GeneratorSyntaxContext context
    )
    {
        var text = context.Node.SyntaxTree.GetText();
        if (
            text.Lines.Count > 0
            && text.Lines[0].ToString().Trim().Equals(StartHeaderMarker, StringComparison.Ordinal)
        )
        {
            return (context.Node.SyntaxTree, context.SemanticModel);
        }

        return null;
    }

    private static void Execute(
        ImmutableArray<(SyntaxTree Tree, SemanticModel SemanticModel)> targets,
        SourceProductionContext context
    )
    {
        if (targets.IsDefaultOrEmpty)
            return;

        foreach (var (tree, semanticModel) in targets.Distinct())
        {
            if (context.CancellationToken.IsCancellationRequested)
                return;

            var root = (CompilationUnitSyntax)tree.GetRoot();

            string declaredNamespace = GetDeclaredNamespace(root);
            var fileConfig = DirectivesConfig.Parse(tree.GetText());

            string effectiveNamespace = !string.IsNullOrWhiteSpace(fileConfig.Namespace)
                ? fileConfig.Namespace!
                : declaredNamespace;

            // 1. Get ORIGINAL declared type nodes attached to the tree
            var originalTypeNodes = root.DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .ToList();

            if (originalTypeNodes.Count == 0)
                continue;

            // 2. Rewrite tree with FullyQualifiedRewriter
            var rewriter = new FullyQualifiedRewriter(semanticModel, fileConfig.ExcludedAttributes);
            var processedRoot = (CompilationUnitSyntax)rewriter.Visit(root);

            var processedTypeNodes = processedRoot
                .DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .ToList();

            bool isFileScopedNamespace = root
                .Members.OfType<FileScopedNamespaceDeclarationSyntax>()
                .Any();

            var typeMetadataList =
                new List<(
                    string Name,
                    string FullName,
                    string Namespace,
                    string Source,
                    string SourceWithNamespace
                )>();

            // 3. Pair original node with processed node
            for (int i = 0; i < originalTypeNodes.Count; i++)
            {
                var originalNode = originalTypeNodes[i];
                var processedNode = processedTypeNodes[i];

                if (semanticModel.GetDeclaredSymbol(originalNode) is { } typeSymbol)
                {
                    string name = typeSymbol.Name;

                    // Honor directive namespace override if provided
                    string ns = !string.IsNullOrWhiteSpace(fileConfig.Namespace)
                        ? fileConfig.Namespace!
                        : (
                            typeSymbol.ContainingNamespace.IsGlobalNamespace
                                ? string.Empty
                                : typeSymbol.ContainingNamespace.ToDisplayString()
                        );

                    string fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                    string typeSource = FormatMember(processedNode, fileConfig);
                    string typeSourceWithNamespace = WrapWithBlockScopedNamespace(typeSource, ns);

                    typeMetadataList.Add((name, fullName, ns, typeSource, typeSourceWithNamespace));
                }
            }

            string rawFileName = Path.GetFileNameWithoutExtension(tree.FilePath);
            string sourceClassName = string.IsNullOrWhiteSpace(rawFileName)
                ? "GeneratedSource"
                : $"{rawFileName}Source";

            string cleanedSourceText = BuildCleanedFileSource(
                processedRoot,
                effectiveNamespace,
                fileConfig
            );

            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(declaredNamespace))
            {
                if (isFileScopedNamespace)
                {
                    sb.AppendLine($"namespace {declaredNamespace};");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine($"namespace {declaredNamespace}");
                    sb.AppendLine("{");
                }
            }

            string indent =
                !string.IsNullOrEmpty(declaredNamespace) && !isFileScopedNamespace
                    ? "    "
                    : string.Empty;

            sb.AppendLine($"{indent}internal static class {sourceClassName}");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine(
                $"{indent}    public const string FileName = \"{Path.GetFileNameWithoutExtension(tree.FilePath)}\";"
            );
            sb.AppendLine($"{indent}    public const string Namespace = \"{effectiveNamespace}\";");
            sb.AppendLine(
                $"{indent}    public const int DeclaredTypesCount = {originalTypeNodes.Count};"
            );
            sb.AppendLine(
                $"{indent}    public const bool HasFileScopedNamespace = {(isFileScopedNamespace ? "true" : "false")};"
            );
            sb.AppendLine(
                $"{indent}    public const string Content = @\"{cleanedSourceText.Replace("\"", "\"\"")}\";"
            );

            foreach (var typeMeta in typeMetadataList)
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}    public static class {typeMeta.Name}");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        public const string Name = \"{typeMeta.Name}\";");
                sb.AppendLine(
                    $"{indent}        public const string FullName = \"{typeMeta.FullName}\";"
                );
                sb.AppendLine(
                    $"{indent}        public const string Namespace = \"{typeMeta.Namespace}\";"
                );
                sb.AppendLine(
                    $"{indent}        public const string Source = @\"{typeMeta.Source.Replace("\"", "\"\"")}\";"
                );
                sb.AppendLine(
                    $"{indent}        public const string SourceWithNamespace = @\"{typeMeta.SourceWithNamespace.Replace("\"", "\"\"")}\";"
                );
                sb.AppendLine($"{indent}    }}");
            }

            sb.AppendLine($"{indent}}}");

            if (!string.IsNullOrEmpty(declaredNamespace) && !isFileScopedNamespace)
            {
                sb.AppendLine("}");
            }

            context.AddSource(
                $"{sourceClassName}.g.cs",
                SourceText.From(sb.ToString(), Encoding.UTF8)
            );
        }
    }

    private static string WrapWithBlockScopedNamespace(string typeSource, string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
            return typeSource;

        var sb = new StringBuilder();
        sb.AppendLine($"namespace {namespaceName}");
        sb.AppendLine("{");
        sb.AppendLine(typeSource);
        sb.Append("}");

        return sb.ToString();
    }

    private static string GetDeclaredNamespace(CompilationUnitSyntax root)
    {
        var fileScopedNs = root
            .Members.OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (fileScopedNs != null)
            return fileScopedNs.Name.ToString();
        var blockNs = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
        return blockNs != null ? blockNs.Name.ToString() : string.Empty;
    }

    private static string BuildCleanedFileSource(
        CompilationUnitSyntax processedRoot,
        string namespaceName,
        DirectivesConfig config
    )
    {
        var sb = new StringBuilder();
        bool isFileScoped = processedRoot
            .Members.OfType<FileScopedNamespaceDeclarationSyntax>()
            .Any();

        if (!string.IsNullOrEmpty(namespaceName))
        {
            if (isFileScoped)
            {
                sb.AppendLine($"namespace {namespaceName};");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }
        }

        foreach (var member in processedRoot.Members)
        {
            if (member is BaseNamespaceDeclarationSyntax nsDecl)
            {
                foreach (var innerMember in nsDecl.Members)
                {
                    sb.AppendLine(FormatMember(innerMember, config));
                }
            }
            else if (member is BaseTypeDeclarationSyntax)
            {
                sb.AppendLine(FormatMember(member, config));
            }
        }

        if (!string.IsNullOrEmpty(namespaceName) && !isFileScoped)
        {
            sb.AppendLine("}");
        }

        return sb.ToString().Trim();
    }

    private static string FormatMember(MemberDeclarationSyntax member, DirectivesConfig config)
    {
        if (member is TypeDeclarationSyntax typeDecl)
        {
            var modifierList = typeDecl.Modifiers.Select(m => m.Text).ToList();

            if (config.Accessibility != null)
            {
                modifierList.RemoveAll(m =>
                    m is "public" or "internal" or "private" or "protected"
                );
                modifierList.Insert(0, config.Accessibility.ToLowerInvariant());
            }

            if (config.KeepPartial)
            {
                if (!modifierList.Contains("partial"))
                    modifierList.Add("partial");
            }
            else
            {
                modifierList.RemoveAll(m => m == "partial");
            }

            string modifierStr = string.Join(" ", modifierList);
            string kindKeyword = typeDecl.Kind() switch
            {
                SyntaxKind.StructDeclaration => "struct",
                SyntaxKind.InterfaceDeclaration => "interface",
                SyntaxKind.RecordDeclaration => "record",
                SyntaxKind.RecordStructDeclaration => "record struct",
                _ => "class",
            };

            var sb = new StringBuilder();

            foreach (var attrGroup in typeDecl.AttributeLists)
            {
                if (attrGroup.Attributes.Count > 0)
                    sb.AppendLine($"    [{attrGroup.Attributes}]");
            }

            sb.AppendLine(
                $"    {modifierStr} {kindKeyword} {typeDecl.Identifier}{typeDecl.TypeParameterList}{typeDecl.BaseList}"
            );
            sb.AppendLine(typeDecl.Members.FullOptionToFullString());
            return sb.ToString().Trim();
        }

        return $"    {member.ToFullString().Trim()}";
    }

    private sealed class DirectivesConfig
    {
        public string? Namespace { get; set; }
        public HashSet<string> ExcludedAttributes { get; } = new(StringComparer.Ordinal);
        public bool KeepPartial { get; set; }
        public string? Accessibility { get; set; }

        public static DirectivesConfig Parse(SourceText text)
        {
            var config = new DirectivesConfig();
            bool inHeader = false;

            foreach (var line in text.Lines)
            {
                string lineText = line.ToString().Trim();

                if (lineText.Equals(StartHeaderMarker, StringComparison.Ordinal))
                {
                    inHeader = true;
                    continue;
                }

                if (lineText.Equals(EndHeaderMarker, StringComparison.Ordinal))
                {
                    break;
                }

                if (!inHeader || !lineText.StartsWith("// :"))
                    continue;

                string directiveText = lineText.Substring("// :".Length).Trim();
                int spaceIndex = directiveText.IndexOf(' ');

                string key =
                    spaceIndex > 0 ? directiveText.Substring(0, spaceIndex) : directiveText;
                string value =
                    spaceIndex > 0 ? directiveText.Substring(spaceIndex + 1).Trim() : string.Empty;

                switch (key.ToLowerInvariant())
                {
                    case "namespace":
                        if (!string.IsNullOrWhiteSpace(value))
                            config.Namespace = value;
                        break;

                    case "excludeattributes":
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            var attrs = value.Split(
                                [',', '|'],
                                StringSplitOptions.RemoveEmptyEntries
                            );
                            foreach (var attr in attrs)
                            {
                                string trimmed = attr.Trim();
                                config.ExcludedAttributes.Add(trimmed);
                                if (trimmed.EndsWith("Attribute", StringComparison.Ordinal))
                                {
                                    config.ExcludedAttributes.Add(
                                        trimmed.Substring(0, trimmed.Length - "Attribute".Length)
                                    );
                                }
                            }
                        }

                        break;

                    case "partial":
                        if (bool.TryParse(value, out bool partialResult))
                            config.KeepPartial = partialResult;
                        break;

                    case "accesibility":
                    case "accessibility":
                        if (
                            value.Equals("Public", StringComparison.OrdinalIgnoreCase)
                            || value.Equals("Internal", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            config.Accessibility = value;
                        }

                        break;
                }
            }

            return config;
        }
    }

    private sealed class FullyQualifiedRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly HashSet<string> _excludedAttributes;

        public FullyQualifiedRewriter(
            SemanticModel semanticModel,
            HashSet<string> excludedAttributes
        )
        {
            _semanticModel = semanticModel;
            _excludedAttributes = excludedAttributes;
        }

        public override SyntaxNode? VisitAttributeList(AttributeListSyntax node)
        {
            var remainingAttributes = new List<AttributeSyntax>();

            foreach (var attribute in node.Attributes)
            {
                string rawName = attribute.Name.ToString().Trim();
                if (_excludedAttributes.Contains(rawName))
                    continue;

                var symbolInfo = _semanticModel.GetSymbolInfo(attribute);
                var attributeSymbol = symbolInfo.Symbol as IMethodSymbol;
                var attributeType = attributeSymbol?.ContainingType;

                if (attributeType != null)
                {
                    string globalMetadataName = attributeType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    var fullyQualifiedNameSyntax = SyntaxFactory
                        .ParseName(globalMetadataName)
                        .WithLeadingTrivia(attribute.Name.GetLeadingTrivia())
                        .WithTrailingTrivia(attribute.Name.GetTrailingTrivia());

                    var rewrittenAttribute = (AttributeSyntax)VisitAttribute(attribute)!;
                    remainingAttributes.Add(rewrittenAttribute.WithName(fullyQualifiedNameSyntax));
                }
                else
                {
                    var rewrittenAttribute = (AttributeSyntax)VisitAttribute(attribute)!;
                    remainingAttributes.Add(rewrittenAttribute);
                }
            }

            if (remainingAttributes.Count == 0)
                return null;

            return node.WithAttributes(SyntaxFactory.SeparatedList(remainingAttributes));
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Do not rewrite member, parameter, or property declaration names
            if (node.Parent is PropertyDeclarationSyntax prop && prop.Identifier == node.Identifier)
                return base.VisitIdentifierName(node);

            if (
                node.Parent is MethodDeclarationSyntax method
                && method.Identifier == node.Identifier
            )
                return base.VisitIdentifierName(node);

            if (
                node.Parent is VariableDeclaratorSyntax declarator
                && declarator.Identifier == node.Identifier
            )
                return base.VisitIdentifierName(node);

            if (
                node.Parent is EnumMemberDeclarationSyntax enumMember
                && enumMember.Identifier == node.Identifier
            )
                return base.VisitIdentifierName(node);

            if (node.Parent is ParameterSyntax parameter && parameter.Identifier == node.Identifier)
                return base.VisitIdentifierName(node);

            if (
                node.Parent is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name == node
            )
                return base.VisitIdentifierName(node);

            // Guard against rewriting attribute targets/names, argument keys, named parameters, and typeof expressions
            if (node.Parent is AttributeSyntax)
                return base.VisitIdentifierName(node);

            if (node.Parent is NameEqualsSyntax)
                return base.VisitIdentifierName(node);

            if (node.Parent is AttributeArgumentSyntax)
                return base.VisitIdentifierName(node);

            if (node.Parent is TypeOfExpressionSyntax)
                return base.VisitIdentifierName(node);

            var typeInfo = _semanticModel.GetTypeInfo(node);
            if (typeInfo.Type is { } typeSymbol && typeSymbol.TypeKind != TypeKind.Error)
            {
                string fullyQualified = typeSymbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                return SyntaxFactory
                    .ParseTypeName(fullyQualified)
                    .WithLeadingTrivia(node.GetLeadingTrivia())
                    .WithTrailingTrivia(node.GetTrailingTrivia());
            }

            return base.VisitIdentifierName(node);
        }
    }
}

internal static class SyntaxExtensions
{
    public static string FullOptionToFullString(this SyntaxList<MemberDeclarationSyntax> members)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    {");
        foreach (var member in members)
        {
            sb.AppendLine($"        {member.ToFullString().Trim()}");
        }

        sb.AppendLine("    }");
        return sb.ToString();
    }
}
