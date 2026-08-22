using SourceWeaver.Builder.Internals;

#pragma warning disable IDE0008
#pragma warning disable IDE0090
#pragma warning disable IDE1006
namespace SourceWeaver.Builder;

public class FieldBuilder : IBuilder
{
    internal enum FieldType
    {
        Const,
        ReadOnly,
        Default,
    }

    internal FieldType FieldTypeValue = FieldType.Default;
    internal ValueType FieldValueType = ValueType.UserSpecified;
    private Action<ICodeWriter>? _valueWriter;
    private string? _value;
    private string? _warning;
    private readonly List<string> _attributes = [];
    private DocumentationComment? _xmlDoc;

    internal FieldBuilder(string name, Accessibility? accessModifier, ClassBuilder builder)
    {
        Name = name;
        AccessModifier = accessModifier;
        Class = builder;
    }

    public string Name { get; }

    public string? Type { get; private set; }

    public ClassBuilder Class { get; }

    public Accessibility? AccessModifier { get; private set; }

    public bool IsStatic { get; private set; }

    public bool IsConstant => FieldTypeValue == FieldType.Const;

    public FieldBuilder WithSummary(string summary)
    {
        _xmlDoc = new SummaryDocumentationComment { Summary = summary };
        return this;
    }

    public FieldBuilder WithInheritDoc(bool inherit = true)
    {
        _xmlDoc = new InheritDocumentationComment();
        return this;
    }

    public FieldBuilder WithInheritDoc(string from)
    {
        _xmlDoc = new InheritDocumentationComment { InheritFrom = from };
        return this;
    }

    public FieldBuilder AddNamespaceImport(string importedNamespace)
    {
        Class.AddNamespaceImport(importedNamespace);
        return this;
    }

    public FieldBuilder AddNamespaceImport(ISymbol symbol)
    {
        return AddNamespaceImport(symbol.ContainingNamespace.ToString());
    }

    public FieldBuilder AddNamespaceImport(INamespaceSymbol symbol)
    {
        return AddNamespaceImport(symbol.ToString());
    }

    public FieldBuilder SetType(string type)
    {
        Type = type;
        return this;
    }

    public FieldBuilder SetType(INamedTypeSymbol symbol)
    {
        return AddNamespaceImport(symbol.ContainingNamespace).SetType(symbol.GetTypeName());
    }

    public FieldBuilder SetType(Type type)
    {
        return AddNamespaceImport(type.Namespace!).SetType(type.GetTypeName());
    }

    public FieldBuilder SetType<T>() => SetType(typeof(T));

    public FieldBuilder SetWarning(string warning)
    {
        _warning = warning;
        return this;
    }

    public FieldBuilder MakePublic() => WithAccessModifier(Accessibility.Public);

    public FieldBuilder MakePrivate() => WithAccessModifier(Accessibility.Private);

    public FieldBuilder MakeProtected() => WithAccessModifier(Accessibility.Protected);

    public FieldBuilder MakeInternal() => WithAccessModifier(Accessibility.Internal);

    public FieldBuilder WithAccessModifier(Accessibility accessModifier)
    {
        AccessModifier = accessModifier;
        return this;
    }

    public FieldBuilder MakeStatic()
    {
        IsStatic = true;
        return this;
    }

    public FieldBuilder AddAttribute(string attribute)
    {
        var sanitized = attribute.Replace("[", string.Empty).Replace("]", string.Empty);
        if (!_attributes.Contains(sanitized))
            _attributes.Add(sanitized);

        return this;
    }

    public ClassBuilder WithConstValue(string value)
    {
        FieldTypeValue = FieldType.Const;
        _value = value;
        return Class;
    }

    public ClassBuilder WithReadonlyValue(ValueType valueType = ValueType.UserSpecified) =>
        WithReadonlyValue(null, valueType);

    public ClassBuilder WithReadonlyValue(
        string? value,
        ValueType valueType = ValueType.UserSpecified
    )
    {
        FieldTypeValue = FieldType.ReadOnly;
        _value = value;
        FieldValueType = valueType;
        return Class;
    }

    public ClassBuilder WithReadonlyValue(Action<ICodeWriter> valueWriter)
    {
        FieldTypeValue = FieldType.ReadOnly;
        _valueWriter = valueWriter;
        return Class;
    }

    public FieldBuilder WithValue(Action<ICodeWriter> valueWriter)
    {
        _valueWriter = valueWriter;
        return this;
    }

    public ClassBuilder WithValue(string? value, ValueType valueType = ValueType.UserSpecified)
    {
        _value = value;
        FieldValueType = valueType;
        return Class;
    }

    void IBuilder.Write(in CodeWriter writer)
    {
        _xmlDoc?.Write(writer);

        if (_warning is not null)
        {
            writer.AppendLine("#warning " + _warning);
        }

        foreach (var attribute in _attributes)
            writer.AppendLine($"[{attribute}]");

        var value = FieldValueType switch
        {
            ValueType.Null => "null",
            ValueType.Default => "default",
            _ => _value,
        };

        if (string.IsNullOrEmpty(Type))
            throw new ArgumentNullException($"There is no 'Type' Specified for {Name}");

        var type = Type!.Trim();
        var name = Name.Trim();
        var staticModifier = IsStatic ? " static" : string.Empty;
        var newModifier = name == nameof(Equals) ? " new" : string.Empty;

        var declaration = (
            FieldTypeValue switch
            {
                FieldType.Const =>
                    $"{AccessibilityHelpers.Code(AccessModifier)}{newModifier} const {type} {name}",
                FieldType.ReadOnly =>
                    $"{AccessibilityHelpers.Code(AccessModifier)}{newModifier}{staticModifier} readonly {type} {name}",
                _ =>
                    $"{AccessibilityHelpers.Code(AccessModifier)}{newModifier}{staticModifier} {type} {name}",
            }
        ).Trim();

        // Handle value generation via Action callback if provided
        if (_valueWriter != null)
        {
            writer.Append($"{declaration} = ");
            _valueWriter.Invoke(writer);
            writer.AppendLine(";");
            return;
        }

        // Handle standard value assignment or uninitialized field declaration
        if (string.IsNullOrEmpty(value) && FieldTypeValue != FieldType.Const)
        {
            writer.AppendLine($"{declaration};");
        }
        else
        {
            var maxCharacters = value?.StartsWith("\"") ?? false ? 9 : 5;

            if (value?.Length > maxCharacters)
            {
                writer.AppendLine($"{declaration} =");
                writer.IncreaseIndent();
                writer.AppendLine($"{value};");
                writer.DecreaseIndent();
            }
            else
            {
                writer.AppendLine($"{declaration} = {value};");
            }
        }
    }
}
