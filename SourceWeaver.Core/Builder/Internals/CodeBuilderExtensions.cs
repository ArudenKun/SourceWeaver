namespace SourceWeaver.Builder.Internals;

internal static class CodeBuilderExtensions
{
    private static readonly Dictionary<string, string> Mappings = new()
    {
        { "Boolean", "bool" },
        { "Byte", "byte" },
        { "SByte", "sbyte" },
        { "Char", "char" },
        { "Decimal", "decimal" },
        { "Double", "double" },
        { "Single", "float" },
        { "Int32", "int" },
        { "UInt32", "uint" },
        { "Int64", "long" },
        { "UInt64", "ulong" },
        { "Int16", "short" },
        { "UInt16", "ushort" },
        { "Object", "object" },
        { "String", "string" },
    };

    public static string GetTypeName(this ITypeSymbol symbol)
    {
        if (
            symbol.ContainingNamespace.Name == "System"
            && Mappings.TryGetValue(symbol.Name, out var typeName)
        )
            return typeName;

        return SymbolHelpers.GetFullMetadataName(symbol);
    }

    public static string GetTypeName(this Type type)
    {
        if (type.Namespace == "System" && Mappings.TryGetValue(type.Name, out var name))
            return name;

        return type.FullName ?? string.Empty;
    }
}
