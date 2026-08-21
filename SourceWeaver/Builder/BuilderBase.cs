using SourceWeaver.Builder.Internals;

#pragma warning disable IDE0008
#pragma warning disable IDE0090
#nullable enable
namespace SourceWeaver.Builder;

public abstract class BuilderBase : IBuilder
{
    protected List<string> PragmaWarnings { get; } = [];

    internal abstract void Write(in CodeWriter writer);

    void IBuilder.Write(in CodeWriter writer)
    {
        var warnings = string.Join(", ", PragmaWarnings.Distinct());
        if (PragmaWarnings.Any())
        {
            writer.AppendUnindentedLine($"#pragma warning disable {warnings}");
        }

        Write(writer);

        if (PragmaWarnings.Any())
        {
            writer.AppendUnindentedLine($"#pragma warning restore {warnings}");
        }
    }
}
