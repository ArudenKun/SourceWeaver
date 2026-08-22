using SourceWeaver.Builder.Internals;

namespace SourceWeaver.Builder;

public sealed class SwitchBuilder
{
    internal ICodeWriter Writer { get; }
    internal string SwitchOn { get; }
    internal bool Expression { get; }
    private readonly List<SwitchCaseBuilder> _switchCases = [];

    internal SwitchBuilder(ICodeWriter writer, string switchOn, bool expression)
    {
        Writer = writer;
        SwitchOn = switchOn;
        Expression = expression;
    }

    /// <summary>
    /// Adds a new case arm to the switch block.
    /// Pass multiple pattern arguments to share a single implementation block across multiple cases.
    /// </summary>
    public SwitchCaseBuilder AddCase(params string[] cases)
    {
        var builder = new SwitchCaseBuilder(this, cases);
        _switchCases.Add(builder);
        return builder;
    }

    /// <summary>
    /// Adds a default fallback case arm (<c>"default"</c> for classic switches, or <c>"_"</c> for switch expressions).
    /// </summary>
    public SwitchCaseBuilder AddDefault() => AddCase(Expression ? "_" : "default");

    public ICodeWriter Close()
    {
        if (Writer is CodeWriter codeWriter)
        {
            if (Expression)
            {
                WriteExpressionSwitchCase(codeWriter);
            }
            else
            {
                WriteClassicSwitchCase(codeWriter);
            }
        }

        return Writer;
    }

    private void WriteClassicSwitchCase(CodeWriter writer)
    {
        using (writer.Block($"switch ({SwitchOn})"))
        {
            foreach (IBuilder @case in _switchCases)
            {
                @case.Write(writer);
            }
        }
    }

    private void WriteExpressionSwitchCase(CodeWriter writer)
    {
        writer.AppendLine($"{SwitchOn} switch");
        writer.AppendLine("{");
        writer.IncreaseIndent();

        foreach (IBuilder @case in _switchCases)
        {
            @case.Write(writer);
        }

        writer.DecreaseIndent();
        writer.AppendLine("};");
    }
}
