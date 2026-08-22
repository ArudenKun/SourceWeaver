namespace SourceWeaver.Extensions;

public static class CompilationExtensions
{
    public static INamedTypeSymbol? GetEnumType(this Compilation compilation, string name)
    {
        // Avoid LINQ allocations in compiler-bound loops
        INamedTypeSymbol? found = null;

        foreach (var symbol in compilation.GetSymbolsWithName(name, SymbolFilter.Type))
        {
            if (symbol is INamedTypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Enum)
            {
                if (found is not null)
                {
                    // Ambiguous match (multiple enums share the same name)
                    return null;
                }

                found = typeSymbol;
            }
        }

        return found;
    }
}
