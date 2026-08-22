namespace SourceWeaver.Builder;

public abstract class BuilderBase<T> : BuilderBase
    where T : BuilderBase
{
    public abstract T AddNamespaceImport(string importedNamespace);
    public abstract T AddNamespaceImport(ISymbol symbol);
    public abstract T AddNamespaceImport(INamespaceSymbol symbol);
    public abstract T AddAssemblyAttribute(string attribute);

    protected string? Warning;

    public T SetWarning(string warning)
    {
        Warning = warning;
        if (this is T thisAsT)
            return thisAsT;

        throw new InvalidOperationException($"The Builder must be of type {typeof(T).FullName}");
    }

    public T DisableWarning(string buildCode)
    {
        PragmaWarnings.Add(buildCode);
        if (this is T thisAsT)
            return thisAsT;

        throw new InvalidOperationException($"The Builder must be of type {typeof(T).FullName}");
    }
}
