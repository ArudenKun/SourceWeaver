using System.Text.RegularExpressions;
using SourceWeaver.Builder.Internals;

namespace SourceWeaver.Builder;

public class ClassBuilder : BuilderBase<ClassBuilder>
{
    private static readonly Regex InterfaceNamespaceRegex = new(
        @"(?:global::)?(?<namespace>(?:[a-zA-Z_]\w*\.)+)[a-zA-Z_]\w*",
        RegexOptions.Compiled
    );

    private readonly List<string> _attributes = [];
    private readonly List<string> _interfaces = [];
    private readonly List<ConstructorBuilder> _constructors = [];
    private readonly List<EventBuilder> _events = [];
    private readonly List<FieldBuilder> _fields = [];
    private readonly List<PropertyBuilder> _properties = [];
    private readonly List<MethodBuilder> _methods = [];
    private readonly Queue<ClassBuilder> _nestedClasses = [];
    private readonly Queue<DelegateBuilder> _nestedDelegates = [];
    private readonly GenericCollection _generics = [];
    private readonly List<string> _directives = [];

    private readonly bool _isPartial;
    private DocumentationComment? _xmlDoc;
    private Func<PropertyBuilder, string>? _propertiesOrderBy;
    private Func<FieldBuilder, string>? _fieldsOrderBy;

    internal ClassBuilder(string className, CodeBuilder codeBuilder, bool partial = true)
    {
        Name = className;
        Builder = codeBuilder;
        _isPartial = partial;
    }

    public string Name { get; }
    public string FullyQualifiedName => $"{Builder.Namespace}.{Name}";

    public IReadOnlyList<ConstructorBuilder> Constructors => _constructors;
    public IReadOnlyList<FieldBuilder> Fields => _fields;
    public IReadOnlyList<PropertyBuilder> Properties => _properties;
    public IReadOnlyList<MethodBuilder> Methods => _methods;
    public IReadOnlyCollection<ClassBuilder> NestedClasses => _nestedClasses;
    public IReadOnlyList<string> Directives => _directives;

    public CodeBuilder Builder { get; internal set; }
    public string? BaseClass { get; private set; }
    public Accessibility? AccessModifier { get; private set; }
    public TypeKind Kind { get; private set; } = TypeKind.Class;

    public bool IsFile { get; private set; }
    public bool IsStatic { get; private set; }
    public bool IsAbstract { get; private set; }
    public bool IsSealed { get; private set; }

    // Pre-Processor Directives Methods
    public ClassBuilder AddDirective(string directive)
    {
        if (string.IsNullOrWhiteSpace(directive))
            return this;

        var formatted = directive.Trim();
        if (!formatted.StartsWith("#", StringComparison.Ordinal))
        {
            formatted = $"#{formatted}";
        }

        _directives.Add(formatted);
        return this;
    }

    public ClassBuilder AddNullableDirective(bool enable = true) =>
        AddDirective("#nullable enable");

    public ClassBuilder AddIfDirective(string condition) => AddDirective($"#if {condition}");

    public ClassBuilder AddElifDirective(string condition) => AddDirective($"#elif {condition}");

    public ClassBuilder AddElseDirective() => AddDirective("#else");

    public ClassBuilder AddEndIfDirective() => AddDirective("#endif");

    public ClassBuilder AddRegion(string name) => AddDirective($"#region {name}");

    public ClassBuilder AddEndRegion() => AddDirective("#endregion");

    public ClassBuilder AddPragmaWarningDisable(string warningCode) =>
        AddDirective($"#pragma warning disable {warningCode}");

    public ClassBuilder AddPragmaWarningRestore(string warningCode) =>
        AddDirective($"#pragma warning restore {warningCode}");

    public ClassBuilder WithSummary(string summary)
    {
        _xmlDoc = new SummaryDocumentationComment { Summary = summary };
        return this;
    }

    public ClassBuilder WithInheritDoc()
    {
        _xmlDoc = new InheritDocumentationComment();
        return this;
    }

    public ClassBuilder WithInheritDoc(string from)
    {
        _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
        return this;
    }

    public ClassBuilder Sealed()
    {
        IsSealed = true;
        return this;
    }

    public ClassBuilder IsStruct()
    {
        Kind = TypeKind.Struct;
        return this;
    }

    public ClassBuilder OfType(TypeKind kind)
    {
        Kind = kind;
        return this;
    }

    public ClassBuilder SetBaseClass(string baseClass)
    {
        BaseClass = baseClass;

        var lastDotIndex = baseClass.LastIndexOf('.');
        if (lastDotIndex > 0)
        {
            AddNamespaceImport(baseClass[..lastDotIndex]);
        }

        return this;
    }

    public ClassBuilder SetBaseClass(INamedTypeSymbol symbol)
    {
        var symbolNamespace = symbol.ContainingNamespace?.ToDisplayString();

        if (
            symbol.Name == Name
            && string.Equals(symbolNamespace, Builder.Namespace, StringComparison.Ordinal)
        )
        {
            BaseClass = $"global::{SymbolHelpers.GetFullMetadataName(symbol)}";
            return this;
        }

        BaseClass = symbol.Name;
        return symbol.ContainingNamespace is not null
            ? AddNamespaceImport(symbol.ContainingNamespace)
            : this;
    }

    public ClassBuilder AddGeneric(string name) => AddGeneric(name, _ => { });

    public ClassBuilder AddGeneric(string name, Action<GenericBuilder> configureBuilder)
    {
        if (configureBuilder is null)
            throw new ArgumentNullException(nameof(configureBuilder));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Value cannot be null or empty.", nameof(name));

        if (_generics.Any(x => x.Name == name))
            throw new ArgumentException($"The argument {name} already exists.", nameof(name));

        var builder = new GenericBuilder(name);
        configureBuilder(builder);
        _generics.Add(builder);
        return this;
    }

    public override ClassBuilder AddAssemblyAttribute(string attribute)
    {
        Builder.AddAssemblyAttribute(attribute);
        return this;
    }

    public ClassBuilder AddAttribute(string attribute)
    {
        var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
        if (!_attributes.Contains(sanitized))
            _attributes.Add(sanitized);

        return this;
    }

    public override ClassBuilder AddNamespaceImport(string importedNamespace)
    {
        Builder.AddNamespaceImport(importedNamespace);
        return this;
    }

    public override ClassBuilder AddNamespaceImport(ISymbol symbol)
    {
        Builder.AddNamespaceImport(symbol);
        return this;
    }

    public override ClassBuilder AddNamespaceImport(INamespaceSymbol symbol)
    {
        Builder.AddNamespaceImport(symbol);
        return this;
    }

    public ClassBuilder AddInterface(string interfaceName)
    {
        _interfaces.Add(interfaceName);

        var matches = InterfaceNamespaceRegex.Matches(interfaceName);
        foreach (Match match in matches)
        {
            if (match.Success)
            {
                var ns = match.Groups["namespace"].Value.TrimEnd('.');

                if (ns.StartsWith("global::"))
                {
                    ns = ns.Substring(8);
                }

                if (!string.IsNullOrWhiteSpace(ns))
                {
                    AddNamespaceImport(ns);
                }
            }
        }

        return this;
    }

    public ClassBuilder AddInterface(ITypeSymbol symbol)
    {
        _interfaces.Add(symbol.Name);
        return AddNamespaceImport(symbol);
    }

    public ClassBuilder AddInterfaces(IEnumerable<string>? interfaces)
    {
        if (interfaces is null)
            return this;

        foreach (var interfaceName in interfaces)
            AddInterface(interfaceName);

        return this;
    }

    public ClassBuilder AddInterfaces(IEnumerable<INamedTypeSymbol>? interfaces)
    {
        if (interfaces is null)
            return this;

        foreach (var symbol in interfaces)
            AddInterface(symbol);

        return this;
    }

    public ConstructorBuilder AddConstructor(Accessibility? accessModifier = null)
    {
        var builder = new ConstructorBuilder(accessModifier, this);
        _constructors.Add(builder);
        return builder;
    }

    public ConstructorBuilder AddConstructor(
        IMethodSymbol baseConstructor,
        Accessibility? accessModifier = null
    )
    {
        return AddConstructor(accessModifier).AddParameters(baseConstructor.Parameters);
    }

    public MethodBuilder AddMethod(string name, Accessibility? accessModifier = null)
    {
        var builder = new MethodBuilder(name, accessModifier, this);
        _methods.Add(builder);
        return builder;
    }

    public FieldBuilder AddField(string name, Accessibility? accessModifier = null)
    {
        var builder = new FieldBuilder(name, accessModifier, this);
        _fields.Add(builder);
        return builder;
    }

    public PropertyBuilder AddProperty(string name, Accessibility? accessModifier = null)
    {
        var builder = new PropertyBuilder(name, accessModifier, this);
        _properties.Add(builder);
        return builder;
    }

    public EventBuilder AddEvent(string eventName)
    {
        var builder = new EventBuilder(this, eventName);
        _events.Add(builder);
        return builder;
    }

    public ClassBuilder MakePublicClass() => WithAccessModifier(Accessibility.Public);

    public ClassBuilder MakeInternalClass() => WithAccessModifier(Accessibility.Internal);

    public ClassBuilder WithAccessModifier(Accessibility accessModifier)
    {
        AccessModifier = accessModifier;
        return this;
    }

    public ClassBuilder MakeFileClass()
    {
        IsFile = true;
        return this;
    }

    public ClassBuilder MakeStaticClass()
    {
        IsStatic = true;
        return this;
    }

    public ClassBuilder Abstract(bool isAbstract = true)
    {
        IsAbstract = isAbstract;
        return this;
    }

    public ClassBuilder AddNestedClass(string name, Accessibility? accessModifier = null) =>
        AddNestedClass(name, false, accessModifier);

    public ClassBuilder AddNestedClass(
        string name,
        bool partial,
        Accessibility? accessModifier = null
    )
    {
        var builder = new ClassBuilder(name, Builder, partial);
        if (accessModifier.HasValue)
            builder.WithAccessModifier(accessModifier.Value);

        _nestedClasses.Enqueue(builder);
        return builder;
    }

    public DelegateBuilder AddNestedDelegate(string name, Accessibility? accessModifier = null)
    {
        var builder = new DelegateBuilder(name, Builder);
        if (accessModifier.HasValue)
            builder.WithAccessModifier(accessModifier.Value);

        _nestedDelegates.Enqueue(builder);
        return builder;
    }

    public string Build() => Builder.Build();

    public ClassBuilder DontSortPropertiesByName()
    {
        _propertiesOrderBy = _ => string.Empty;
        return this;
    }

    public ClassBuilder DontSortFieldsByName()
    {
        _fieldsOrderBy = _ => string.Empty;
        return this;
    }

    internal override void Write(in CodeWriter writer)
    {
        foreach (var directive in _directives)
        {
            writer.AppendLine(directive);
        }

        _xmlDoc?.Write(writer);

        if (Warning is not null)
        {
            writer.AppendLine("#warning " + Warning);
        }

        var heritageList = new List<string>();
        if (!string.IsNullOrEmpty(BaseClass))
        {
            heritageList.Add(BaseClass!);
        }

        heritageList.AddRange(
            _interfaces.Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x)
        );

        var heritageDeclaration =
            heritageList.Count > 0 ? $": {string.Join(", ", heritageList)}" : string.Empty;

        foreach (var attr in _attributes)
        {
            writer.AppendLine($"[{attr}]");
        }

        if (_methods.Any(x => x.IsAbstract))
            IsAbstract = true;

        var classDeclaration = new[]
        {
            AccessibilityHelpers.Code(AccessModifier),
            IsStatic ? "static" : null,
            IsFile ? "file" : null,
            IsSealed ? "sealed" : null,
            IsAbstract ? "abstract" : null,
            _isPartial ? "partial" : null,
            Kind.ToString().ToLowerInvariant(),
            $"{Name}{_generics}",
            heritageDeclaration,
        };

        using (
            writer.Block(
                string.Join(" ", classDeclaration.Where(x => !string.IsNullOrEmpty(x))),
                _generics.Contraints()
            )
        )
        {
            var hadOutput = false;
            hadOutput = InvokeBuilderWrite(_nestedDelegates, ref hadOutput, in writer);
            hadOutput = InvokeBuilderWrite(_events, ref hadOutput, in writer);

            var fieldOrderBy = _fieldsOrderBy ?? (x => x.Name);
            var propOrderBy = _propertiesOrderBy ?? (x => x.Name);

            // 1. Constants
            hadOutput = InvokeBuilderWrite(
                _fields
                    .Where(x => x.FieldTypeValue == FieldBuilder.FieldType.Const && !x.IsStatic)
                    .OrderBy(fieldOrderBy),
                ref hadOutput,
                writer,
                true
            );
            hadOutput = InvokeBuilderWrite(
                _fields
                    .Where(x => x.FieldTypeValue == FieldBuilder.FieldType.Const && x.IsStatic)
                    .OrderBy(fieldOrderBy),
                ref hadOutput,
                writer,
                true
            );
            hadOutput = InvokeBuilderWrite(
                _properties
                    .Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Const && !x.IsStatic)
                    .OrderBy(propOrderBy),
                ref hadOutput,
                writer,
                true
            );
            hadOutput = InvokeBuilderWrite(
                _properties
                    .Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Const && x.IsStatic)
                    .OrderBy(propOrderBy),
                ref hadOutput,
                writer,
                true
            );

            // 2. ReadOnly Items
            hadOutput = InvokeBuilderWrite(
                _fields
                    .Where(x => x.FieldTypeValue == FieldBuilder.FieldType.ReadOnly)
                    .OrderBy(fieldOrderBy),
                ref hadOutput,
                writer,
                true
            );
            hadOutput = InvokeBuilderWrite(
                _properties
                    .Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.ReadOnly)
                    .OrderBy(propOrderBy),
                ref hadOutput,
                writer,
                true
            );

            // 3. Default Fields & Properties
            hadOutput = InvokeBuilderWrite(
                _fields
                    .Where(x => x.FieldTypeValue == FieldBuilder.FieldType.Default)
                    .OrderBy(fieldOrderBy),
                ref hadOutput,
                writer,
                true
            );
            hadOutput = InvokeBuilderWrite(
                _properties
                    .Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Default)
                    .OrderBy(propOrderBy),
                ref hadOutput,
                writer,
                true
            );

            // 4. Constructors
            hadOutput = InvokeBuilderWrite(
                _constructors.OrderBy(x => x.Parameters.Count),
                ref hadOutput,
                writer
            );

            // 5. Standard Properties
            hadOutput = InvokeBuilderWrite(
                _properties
                    .Where(x => x.FieldTypeValue == PropertyBuilder.FieldType.Property)
                    .OrderBy(propOrderBy),
                ref hadOutput,
                writer
            );

            // 6. Methods
            hadOutput = InvokeBuilderWrite(
                _methods.OrderBy(x => x.Name).ThenBy(x => x.Parameters.Count),
                ref hadOutput,
                writer
            );

            // 7. Nested Classes
            InvokeBuilderWrite(_nestedClasses, ref hadOutput, writer);
        }
    }

    private static bool InvokeBuilderWrite<T>(
        IEnumerable<T>? builders,
        ref bool hadOutput,
        in CodeWriter writer,
        bool group = false
    )
        where T : IBuilder
    {
        if (builders is null)
            return hadOutput;

        using var enumerator = builders.GetEnumerator();
        if (!enumerator.MoveNext())
            return hadOutput;

        if (hadOutput)
            writer.NewLine();

        do
        {
            var builder = enumerator.Current;
            builder?.Write(writer);

            var hasMore = enumerator.MoveNext();
            if (!group && hasMore)
                writer.NewLine();

            if (!hasMore)
                break;
        } while (true);

        return true;
    }
}
