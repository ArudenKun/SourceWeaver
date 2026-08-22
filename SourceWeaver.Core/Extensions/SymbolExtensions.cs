using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace SourceWeaver.Extensions;

public static class SymbolExtensions
{
    private static readonly SymbolDisplayFormat FullNameFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(
            SymbolDisplayGlobalNamespaceStyle.Omitted
        );

    // --- Formatting & Naming ---

    public static string ToFullDisplayString(this ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public static string GlobalName(this ISymbol symbol) => symbol.ToFullDisplayString();

    public static string FullName(this ISymbol symbol) => symbol.ToDisplayString(FullNameFormat);

    public static string ClassDef(this INamedTypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    public static string Namespace(this ISymbol symbol) =>
        symbol.ContainingNamespace?.ToDisplayString(FullNameFormat) ?? string.Empty;

    public static bool HasNamespace(this ISymbol symbol) =>
        symbol.ContainingNamespace is { IsGlobalNamespace: false };

    public static string? NamespaceOrNull(this ISymbol symbol) =>
        symbol.HasNamespace() ? symbol.Namespace() : null;

    public static string? GetNamespaceDeclaration(this ISymbol symbol)
    {
        var ns = symbol.NamespaceOrNull();
        return ns is null ? null : $"namespace {ns};\n";
    }

    public static string GetDeclaredAccessibility(this ISymbol symbol) =>
        SyntaxFacts.GetText(symbol.DeclaredAccessibility);

    public static string Scope(this ISymbol symbol) => symbol.GetDeclaredAccessibility();

    public static string? ClassPath(this INamedTypeSymbol symbol) =>
        symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.FilePath;

    // --- Symbol Query Helpers ---

    public static INamedTypeSymbol? OuterType(this ISymbol symbol)
    {
        var current = symbol.ContainingType;
        if (current is null)
            return symbol as INamedTypeSymbol;

        while (current.ContainingType is not null)
        {
            current = current.ContainingType;
        }

        return current;
    }

    public static bool Is(this ISymbol? symbol, ISymbol? other) =>
        SymbolEqualityComparer.Default.Equals(symbol, other);

    public static bool HasGetter(this IPropertySymbol symbol) => symbol.GetMethod is not null;

    public static bool HasSetter(this IPropertySymbol symbol) => symbol.SetMethod is not null;

    public static bool IsEnum(this ITypeSymbol type) => type.TypeKind is TypeKind.Enum;

    public static bool IsClass(this ITypeSymbol type) => type.TypeKind is TypeKind.Class;

    public static bool IsNullable(this ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated
        || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    // Unified hierarchy search to eliminate duplicate inheritance traversal logic
    private static bool CheckHierarchy<TTarget>(
        this ITypeSymbol type,
        TTarget target,
        Func<ITypeSymbol, TTarget, bool> matchFunc
    )
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (matchFunc(current, target))
                return true;
        }

        return type.AllInterfaces.Any(i => matchFunc(i, target));
    }

    public static bool IsOrInherits(this ITypeSymbol type, ITypeSymbol baseType) =>
        type.CheckHierarchy(
            baseType,
            (current, target) => SymbolEqualityComparer.Default.Equals(current, target)
        );

    public static bool IsOrInherits(this ITypeSymbol type, string baseTypeName) =>
        type.CheckHierarchy(baseTypeName, (current, targetName) => current.Name == targetName);

    // --- Immutable / Roslyn Collections ---

    public static T[] Args<T>(this ImmutableArray<TypedConstant> values)
    {
        if (values.IsDefaultOrEmpty)
            return [];

        var result = new List<T>(values.Length);
        foreach (var value in values)
        {
            if (value.Value is T typedVal)
                result.Add(typedVal);
        }

        return [.. result];
    }

    // --- Code Generation ---

    public static string GeneratePartialClass(
        this INamedTypeSymbol symbol,
        IEnumerable<string>? content,
        IEnumerable<string>? usings = null
    )
    {
        var builder = new StringBuilder();

        if (usings is not null)
        {
            foreach (var u in usings)
            {
                builder.AppendLine(u);
            }
            builder.AppendLine();
        }

        var ns = symbol.GetNamespaceDeclaration();
        if (!string.IsNullOrEmpty(ns))
        {
            builder.AppendLine(ns);
        }

        var typeStack = new Stack<INamedTypeSymbol>();
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            typeStack.Push(current);
        }

        var indent = 0;
        while (typeStack.Count > 0)
        {
            var currentSymbol = typeStack.Pop();
            var indentStr = new string(' ', indent * 4);
            var keyword = GetTypeKindKeyword(currentSymbol);

            builder.AppendLine(
                $"{indentStr}{currentSymbol.GetDeclaredAccessibility()} partial {keyword} {currentSymbol.ClassDef()}"
            );
            builder.AppendLine($"{indentStr}{{");

            indent++;
        }

        if (content is not null)
        {
            var bodyIndent = new string(' ', indent * 4);
            foreach (var line in content)
            {
                builder.AppendLine(
                    string.IsNullOrWhiteSpace(line) ? string.Empty : $"{bodyIndent}{line}"
                );
            }
        }

        while (indent > 0)
        {
            indent--;
            var indentStr = new string(' ', indent * 4);
            builder.AppendLine($"{indentStr}}}");
        }

        return builder.ToString().TrimStart();
    }

    private static string GetTypeKindKeyword(INamedTypeSymbol symbol) =>
        symbol switch
        {
            { IsRecord: true, IsValueType: true } => "record struct",
            { IsRecord: true } => "record",
            { TypeKind: TypeKind.Interface } => "interface",
            { TypeKind: TypeKind.Struct } => "struct",
            _ => "class",
        };

    public static string GetEnumValue(this ITypeSymbol type, object value)
    {
        string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (type is INamedTypeSymbol namedType)
        {
            foreach (var member in namedType.GetMembers())
            {
                if (
                    member is IFieldSymbol field
                    && field.HasConstantValue
                    && ConstantEquals(field.ConstantValue, value)
                )
                {
                    return $"{typeName}.{field.Name}";
                }
            }
        }

        // Fallback for [Flags] combinations or underlying values without a named constant
        return $"({typeName}){value}";
    }

    private static bool ConstantEquals(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;

        // Boxing numeric values from Roslyn symbols often leads to mismatched primitive types
        // (e.g. byte vs int), so convert to systemic uint64 for unified comparison.
        try
        {
            return Convert.ToUInt64(a) == Convert.ToUInt64(b);
        }
        catch
        {
            return Equals(a, b);
        }
    }

    public static string ToParameterString(this IParameterSymbol p) =>
        $"{p.Modifier(p.IsParams)}{p.Type()} {p.Name}{p.Default()}";

    public static string ToArgumentString(this IParameterSymbol p, bool castEnum = false) =>
        $"{p.Modifier()}{p.Cast(castEnum)}{p.Name}";

    private static string Modifier(this IParameterSymbol p, bool isParams = false)
    {
        return p.RefKind switch
        {
            RefKind.In => "in ",
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.RefReadOnlyParameter => "ref readonly ",
            _ => isParams ? "params " : "",
        };
    }

    private static string Cast(this IParameterSymbol p, bool castEnum = false) =>
        castEnum && p.Type.IsEnum() ? "(int)" : "";

    private static string Type(this IParameterSymbol p)
    {
        string typeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Handle value types explicitly annotated as nullable (e.g. int?)
        // vs reference types with NRT annotations (e.g. string?)
        if (!p.Type.IsValueType && p.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return typeName.EndsWith("?") ? typeName : $"{typeName}?";
        }

        return typeName;
    }

    private static string Default(this IParameterSymbol p) =>
        p.HasExplicitDefaultValue ? $" = {p.Format(p.ExplicitDefaultValue)}" : "";

    private static string Format(this IParameterSymbol p, object? value)
    {
        if (value is null)
        {
            return p.NullableAnnotation == NullableAnnotation.NotAnnotated && !p.Type.IsValueType
                ? "default!"
                : "default";
        }

        return p.Type.Format(value);
    }

    private static string Format(this ITypeSymbol type, object value)
    {
        // Unwrap Nullable<T> first so underlying primitives format properly
        if (type.IsNullable() && type is INamedTypeSymbol namedType)
        {
            return namedType.TypeArguments.Single().Format(value);
        }

        return type.SpecialType switch
        {
            SpecialType.System_Char => SymbolDisplay.FormatLiteral((char)value, quote: true),
            SpecialType.System_String => SymbolDisplay.FormatLiteral((string)value, quote: true),
            SpecialType.System_Boolean => (bool)value ? "true" : "false",
            SpecialType.System_Byte => $"(byte){value}",
            SpecialType.System_SByte => $"(sbyte){value}",
            SpecialType.System_Int16 => $"(short){value}",
            SpecialType.System_UInt16 => $"(ushort){value}",
            SpecialType.System_Single => ((float)value).ToString("G9", CultureInfo.InvariantCulture)
                + "f",
            SpecialType.System_Double => ((double)value).ToString(
                "G17",
                CultureInfo.InvariantCulture
            ) + "d",
            SpecialType.System_Decimal => ((decimal)value).ToString(CultureInfo.InvariantCulture)
                + "m",
            SpecialType.System_UInt32 => $"{value}u",
            SpecialType.System_Int64 => $"{value}L",
            SpecialType.System_UInt64 => $"{value}UL",
            _ when type.IsEnum() => FormatEnum(type, value),
            _ => value.ToString() ?? "default",
        };
    }

    private static string FormatEnum(ITypeSymbol type, object value)
    {
        // Cast raw integral values to fully qualified enum members where possible
        string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return $"({typeName}){value}";
    }
}
