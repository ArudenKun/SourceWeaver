using JetBrains.Annotations;

namespace SourceWeaver.Generators;

[PublicAPI]
public abstract class SourceGeneratorForMemberWithInterface<TInterface, TDeclarationSyntax>
    : SourceGeneratorForMemberWithInterface<TDeclarationSyntax>
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    protected override string InterfaceMetadataName { get; } = typeof(TInterface).FullName!;
}

[PublicAPI]
public abstract class SourceGeneratorForMemberWithInterface<TDeclarationSyntax>
    : SourceGeneratorForMemberWithInterfaces<TDeclarationSyntax>
    where TDeclarationSyntax : MemberDeclarationSyntax
{
    protected abstract string InterfaceMetadataName { get; }

    protected sealed override IReadOnlyList<string> InterfaceMetadataNames =>
        [InterfaceMetadataName];
}
