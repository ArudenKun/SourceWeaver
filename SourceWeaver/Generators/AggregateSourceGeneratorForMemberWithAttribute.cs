namespace SourceWeaver.Generators;

public abstract class AggregateSourceGeneratorForMemberWithAttribute<TAttribute, TDeclarationSyntax>
    : AggregateSourceGeneratorForMemberWithAttribute<TDeclarationSyntax>
    where TAttribute : Attribute
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    protected override string AttributeMetadataName { get; } = typeof(TAttribute).FullName!;
}

public abstract class AggregateSourceGeneratorForMemberWithAttribute<TDeclarationSyntax>
    : AggregateSourceGeneratorForMemberWithAttributes<TDeclarationSyntax>
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    protected abstract string AttributeMetadataName { get; }

    protected sealed override IReadOnlyList<string> AttributeMetadataNames =>
        [AttributeMetadataName];
}
