using SourceWeaver.Builder.Internals;

namespace SourceWeaver.Builder;

public sealed class SwitchCaseBuilder : IBuilder
{
    internal SwitchBuilder Parent { get; }
    internal IReadOnlyList<string> Cases { get; }
    private Action<ICodeWriter>? _body;
    private string? _returnValue;
    private string? _throwException;
    private bool _useBreak;
    private bool _useBraces;

    public SwitchCaseBuilder(SwitchBuilder parent, params string[] cases)
    {
        Parent = parent;
        Cases = cases.Length > 0 ? cases : ["default"];
    }

    /// <summary>
    /// Encloses statement case blocks inside scoped curly braces `{ ... }`.
    /// </summary>
    public SwitchCaseBuilder WithBraces(bool useBraces = true)
    {
        _useBraces = useBraces;
        return this;
    }

    /// <summary>
    /// Sets code block content delegate for this switch case.
    /// </summary>
    public SwitchBuilder WithBody(Action<ICodeWriter>? bodyDelegate)
    {
        _body = bodyDelegate;
        return Parent;
    }

    /// <summary>
    /// Configures an expression arm returning a value (e.g. <c>case 1 => "Value",</c>).
    /// </summary>
    public SwitchBuilder WithExpression(string returnValue)
    {
        _returnValue = returnValue;
        return Parent;
    }

    /// <summary>
    /// Configures a multi-line block expression arm (e.g. <c>case 1 => { ... },</c>).
    /// </summary>
    public SwitchBuilder WithExpressionBlock(Action<ICodeWriter> blockDelegate)
    {
        _body = blockDelegate;
        _useBraces = true;
        return Parent;
    }

    /// <summary>
    /// Appends a <c>break;</c> statement to end the case arm.
    /// </summary>
    public SwitchBuilder WithBreak()
    {
        _useBreak = true;
        return Parent;
    }

    /// <summary>
    /// Appends a <c>return &lt;value&gt;;</c> statement to end the case arm.
    /// </summary>
    public SwitchBuilder WithReturn(string? returnValue = null)
    {
        _returnValue = returnValue ?? string.Empty;
        return Parent;
    }

    /// <summary>
    /// Appends a <c>throw &lt;exception&gt;;</c> statement or expression arm.
    /// </summary>
    public SwitchBuilder WithThrow(string exceptionExpression)
    {
        _throwException = exceptionExpression;
        return Parent;
    }

    void IBuilder.Write(in CodeWriter writer)
    {
        if (Parent.Expression)
        {
            WriteSwitchExpressionArm(writer);
        }
        else
        {
            WriteClassicSwitchCaseArm(writer);
        }
    }

    private void WriteSwitchExpressionArm(CodeWriter writer)
    {
        string pattern = string.Join(", ", Cases);

        if (_throwException != null)
        {
            writer.AppendLine($"{pattern} => throw {_throwException},");
            return;
        }

        if (_useBraces && _body != null)
        {
            writer.AppendLine($"{pattern} =>");
            writer.AppendLine("{");
            writer.IncreaseIndent();
            _body.Invoke(writer);
            writer.DecreaseIndent();
            writer.AppendLine("},");
            return;
        }

        if (_returnValue != null)
        {
            writer.AppendLine($"{pattern} => {_returnValue},");
            return;
        }

        if (_body != null)
        {
            writer.Append($"{pattern} => ");
            _body.Invoke(writer);
            writer.AppendLine(",");
        }
    }

    private void WriteClassicSwitchCaseArm(CodeWriter writer)
    {
        // Emit shared labels (e.g., case 1:\n case 2:)
        foreach (var @case in Cases)
        {
            var label =
                @case.Equals("default", StringComparison.OrdinalIgnoreCase) || @case == "_"
                    ? "default:"
                    : $"case {@case}:";
            writer.AppendLine(label);
        }

        writer.IncreaseIndent();

        if (_useBraces)
        {
            writer.AppendLine("{");
            writer.IncreaseIndent();
        }

        _body?.Invoke(writer);

        if (_throwException != null)
        {
            writer.AppendLine($"throw {_throwException};");
        }
        else if (_returnValue != null)
        {
            writer.AppendLine(
                string.IsNullOrEmpty(_returnValue) ? "return;" : $"return {_returnValue};"
            );
        }
        else if (_useBreak || (_body == null && _throwException == null))
        {
            writer.AppendLine("break;");
        }

        if (_useBraces)
        {
            writer.DecreaseIndent();
            writer.AppendLine("}");
        }

        writer.DecreaseIndent();
    }
}
