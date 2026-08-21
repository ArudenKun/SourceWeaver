// [GenerateSourceText]
// :ExcludeAttributes GenerateFactoryAttribute
// :Accessibility Internal
// [/]

using RhoMicro.CodeAnalysis;

namespace SourceWeaver;

public enum TestType
{
    Holy,
    Mommy,
}

[GenerateFactory]
[Obsolete]
public partial class ExtrasSource
{
    public TestType TestType { get; set; }
}
