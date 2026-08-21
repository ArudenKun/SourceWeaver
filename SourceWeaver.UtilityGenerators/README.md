# SourceWeaver Utility Source Generator

`SourceGenerator` is a Roslyn Incremental Source Generator that converts C# source files into strongly-typed static C#
constants (`SourceText`, type metadata, fully qualified global type names) at compile time.

It is designed for meta-programming, source generators, and macro systems that need to embed cleanly transformed source
code without manual string escaping or missing runtime dependencies.

---

## 🚀 Getting Started

To enable generation for a file, place the directive header block at the very top of your C# source file:

```csharp
// [GenerateSourceText]
// :Namespace Custom.Target.Namespace
// :ExcludeAttributes GenerateFactoryAttribute, JetBrains.Annotations.PublicAPI
// :Partial true
// :Accessibility Public
// [/]

namespace MyLibrary.Source;

public enum TestType
{
    Holy,
    Mommy
}

[GenerateFactory]
public partial class ExtrasSource
{
    public TestType TestType { get; set; }
}