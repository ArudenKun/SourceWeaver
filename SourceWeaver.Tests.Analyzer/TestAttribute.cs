// [GenerateSourceText]
// :namespace ScoopSharp.ReadOnlyDictionary
// :ExcludeAttributes GenerateFactoryAttribute,
// [/]

namespace SourceWeaver.Tests.Analyzer;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class TestAttribute : Attribute
{
    public TestAttribute(string path = "")
    {
        Path = path;
    }

    public string Path { get; }
}
