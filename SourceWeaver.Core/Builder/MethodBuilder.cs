using SourceWeaver.Builder.Internals;

namespace SourceWeaver.Builder;

public class MethodBuilder : BuilderBase<MethodBuilder>, IParameterized<MethodBuilder>
{
    private readonly List<string> _attributes = [];
    private readonly GenericCollection _generics = [];
    private DocumentationComment? _xmlDoc;
    private readonly List<ParameterBuilder<MethodBuilder>> _parameters = [];
    private bool _override;
    private bool _virtual;
    private bool _isPartial;
    private bool _isExplicitInterfaceImplementation;

    private Action<ICodeWriter>? _methodBodyWriter;
    private string? _expressionBody;

    internal MethodBuilder(string name, Accessibility? accessModifier, ClassBuilder builder)
    {
        Name = name;
        AccessModifier = accessModifier;
        Class = builder;
    }

    List<ParameterBuilder<MethodBuilder>> IParameterized<MethodBuilder>.Parameters => _parameters;
    MethodBuilder IParameterized<MethodBuilder>.Parent => this;

    public IReadOnlyCollection<ParameterBuilder<MethodBuilder>> Parameters => _parameters;

    public string Name { get; }

    public string? ReturnType { get; private set; }

    public bool IsAsync { get; private set; }

    public bool IsAbstract { get; private set; }

    public bool IsNoBodyBlock { get; private set; }

    public bool IsPartial => _isPartial;

    public bool HasBody
    {
        get
        {
            if (_expressionBody is not null)
                return true;

            if (_methodBodyWriter is null)
                return false;

            var writer = new CodeWriter(IndentStyle.Spaces);
            _methodBodyWriter(writer);
            return !string.IsNullOrEmpty(writer.ToString());
        }
    }

    public ClassBuilder Class { get; }

    public Accessibility? AccessModifier { get; private set; }

    public bool IsStatic { get; private set; }

    public bool IsExtern { get; private set; }

    public MethodBuilder WithSummary(string summary)
    {
        if (_xmlDoc is null || _xmlDoc is not SummaryDocumentationComment summaryDoc)
            _xmlDoc = new ParameterDocumentationComment { Summary = summary };
        else
            summaryDoc.Summary = summary;

        return this;
    }

    public MethodBuilder WithInheritDoc(bool inherit = true)
    {
        _xmlDoc = new InheritDocumentationComment();
        return this;
    }

    public MethodBuilder WithInheritDoc(string from)
    {
        _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
        return this;
    }

    public MethodBuilder WithParameterDoc(string paramName, string documentation)
    {
        if (_xmlDoc is null)
            _xmlDoc = new ParameterDocumentationComment();

        if (_xmlDoc is not ParameterDocumentationComment parameterDoc)
            throw new Exception(
                "DocumentationComment has already been initialized with a non ParameterDocumentationComment"
            );

        parameterDoc.AddParameter(paramName, documentation);

        return this;
    }

    public MethodBuilder AddGeneric(string name) => AddGeneric(name, _ => { });

    public MethodBuilder AddGeneric(string name, Action<GenericBuilder>? configureBuilder)
    {
        if (configureBuilder is null)
            throw new ArgumentNullException(nameof(configureBuilder));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));
        if (_generics.Any(x => x.Name == name))
            throw new ArgumentException($"The argument {name} already exists");

        var builder = new GenericBuilder(name);
        configureBuilder(builder);
        _generics.Add(builder);
        return this;
    }

    public override MethodBuilder AddNamespaceImport(string importedNamespace)
    {
        Class.AddNamespaceImport(importedNamespace);
        return this;
    }

    public override MethodBuilder AddNamespaceImport(ISymbol symbol)
    {
        return AddNamespaceImport(symbol.ContainingNamespace);
    }

    public override MethodBuilder AddNamespaceImport(INamespaceSymbol symbol)
    {
        return AddNamespaceImport(symbol.ToString());
    }

    public override MethodBuilder AddAssemblyAttribute(string attribute)
    {
        Class.AddAssemblyAttribute(attribute);
        return this;
    }

    public PropertyBuilder AddProperty(string name, Accessibility? accessModifier = null)
    {
        return Class.AddProperty(name, accessModifier);
    }

    public MethodBuilder MakeAsync()
    {
        IsAsync = true;
        return this;
    }

    public MethodBuilder Abstract(bool isAbstract = true)
    {
        IsAbstract = isAbstract;
        return this;
    }

    public MethodBuilder MakePublicMethod() => WithAccessModifier(Accessibility.Public);

    public MethodBuilder MakePrivateMethod() => WithAccessModifier(Accessibility.Private);

    public MethodBuilder MakeProtectedMethod() => WithAccessModifier(Accessibility.Protected);

    public MethodBuilder MakeInternalMethod() => WithAccessModifier(Accessibility.Internal);

    public MethodBuilder WithAccessModifier(Accessibility accessModifier)
    {
        AccessModifier = accessModifier;
        return this;
    }

    public MethodBuilder WithExplicitInterface()
    {
        _isExplicitInterfaceImplementation = true;
        AccessModifier = null; // Explicit implementations do not take access modifiers
        return this;
    }

    public MethodBuilder Override(bool @override = true)
    {
        _override = @override;
        return this;
    }

    public MethodBuilder MakeStaticMethod()
    {
        IsStatic = true;
        return this;
    }

    public MethodBuilder MakeExternMethod()
    {
        IsExtern = true;
        return this;
    }

    public MethodBuilder MakeVirtualMethod()
    {
        _virtual = true;
        return this;
    }

    /// <summary>
    /// Marks the method as <c>partial</c>.
    /// Combine with <see cref="WithBody"/>/<see cref="WithExpressionBody"/> for an
    /// implementing declaration, or with <see cref="WithNoBodyBlock"/> (or use
    /// <see cref="MakePartialDeclaration"/>) for a defining declaration.
    /// </summary>
    public MethodBuilder MakePartial()
    {
        _isPartial = true;
        return this;
    }

    /// <summary>
    /// Convenience for the defining half of a partial method pair:
    /// emits <c>partial &lt;ret&gt; Name(...);</c> with no body.
    /// </summary>
    public MethodBuilder MakePartialDeclaration()
    {
        _isPartial = true;
        IsNoBodyBlock = true;
        return this;
    }

    public MethodBuilder AddAttribute(string attribute)
    {
        var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
        if (!_attributes.Contains(sanitized))
            _attributes.Add(sanitized);

        return this;
    }

    public MethodBuilder WithBody(Action<ICodeWriter> writerDelegate)
    {
        _methodBodyWriter = writerDelegate;
        return this;
    }

    public MethodBuilder WithExpressionBody(string expression)
    {
        _expressionBody = expression;
        return this;
    }

    public MethodBuilder WithReturnType(string returnType)
    {
        ReturnType = returnType;
        return this;
    }

    public MethodBuilder WithNoBodyBlock()
    {
        IsNoBodyBlock = true;
        return this;
    }

    internal override void Write(in CodeWriter writer)
    {
        if (Warning is not null)
        {
            writer.AppendLine("#warning " + Warning);
        }

        var output =
            ReturnType is null || string.IsNullOrEmpty(ReturnType)
                ? _isExplicitInterfaceImplementation
                    ? string.Empty
                    : "void"
                : ReturnType.Trim();

        // Prepend in reverse order of the desired output so the final
        // sequence is: accessibility [extern] [static|abstract|virtual|override]
        //                [async] [partial] <returnType>
        //
        // partial must be prepended *before* async so it ends up to the right
        // of async, matching SA1206 and typical .NET source conventions
        // (e.g. `public static async partial Task FooAsync()`).
        // Guard against explicit-interface implementations which cannot be partial.
        if (_isPartial && !_isExplicitInterfaceImplementation)
            output = $"partial {output}";

        if (IsAsync)
            output = $"async {output}";

        if (!_isExplicitInterfaceImplementation)
        {
            if (_override)
                output = $"override {output}";
            else if (_virtual)
                output = $"virtual {output}";
            else if (IsAbstract)
                output = $"abstract {output}";
            else if (IsStatic)
                output = $"static {output}";
        }

        if (IsExtern)
            output = $"extern {output}";

        var parameters = string.Join(", ", _parameters.Select(x => x.ToString()));

        var accessibilityCode = _isExplicitInterfaceImplementation
            ? string.Empty
            : AccessibilityHelpers.Code(AccessModifier);

        output = string.IsNullOrEmpty(accessibilityCode)
            ? $"{output} {Name}{_generics}({parameters})"
            : $"{accessibilityCode} {output} {Name}{_generics}({parameters})";

        if (_xmlDoc is ParameterDocumentationComment parameterDocumentation)
            parameterDocumentation.RemoveUnusedParameters(_parameters);

        _xmlDoc?.Write(writer);

        foreach (var attribute in _attributes)
            writer.AppendLine($"[{attribute}]");

        if (IsAbstract || IsNoBodyBlock)
        {
            writer.AppendLine($"{output.Trim()};");
            return;
        }

        var rawConstraints = _generics.Contraints();
        var formattedConstraints =
            rawConstraints.Length == 0 ? string.Empty : string.Join(" ", rawConstraints);

        if (!string.IsNullOrEmpty(_expressionBody))
        {
            var constraints = string.IsNullOrEmpty(formattedConstraints)
                ? string.Empty
                : $" {formattedConstraints}";
            writer.AppendLine($"{output.Trim()}{constraints} => {_expressionBody};");
            return;
        }

        using (writer.Block(output.Trim(), formattedConstraints))
        {
            _methodBodyWriter?.Invoke(writer);
        }
    }
}
