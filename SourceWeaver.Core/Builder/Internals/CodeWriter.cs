using System.Text;

namespace SourceWeaver.Builder.Internals;

internal sealed class CodeWriter : IDisposable, ICodeWriter
{
    private readonly IndentStyle _indentStyle;
    private readonly string _indent;

    private int _indentLevel;
    private int _extraIndent;
    private readonly List<(StringBuilder? StringBuilder, CodeWriter? Writer)> _blocks = new();

    public CodeWriter(IndentStyle indentStyle, int startingLevel = 0)
    {
        _indentStyle = indentStyle;
        _indent = indentStyle switch
        {
            IndentStyle.Tabs => "\t",
            _ => "    ",
        };
    }

    public void IncreaseIndent() => _extraIndent++;

    public void DecreaseIndent() => _extraIndent--;

    public IDisposable Block(string value, params string[] constraints)
    {
        AppendLine(value.TrimEnd());
        _indentLevel++;
        foreach (var constraint in constraints)
        {
            if (string.IsNullOrEmpty(constraint))
                continue;

            AppendLine(constraint);
        }

        _indentLevel--;
        AppendLine("{");
        _indentLevel++;
        return this;
    }

    public ICodeWriter BlockWriter(string? originalLine)
    {
        var writer = new CodeWriter(_indentStyle, _indentLevel);
        if (originalLine is { })
        {
            writer.AppendLine(originalLine);
        }

        writer.AppendLine("{");
        writer._indentLevel++;
        _blocks.Add((null, writer));
        return writer;
    }

    private StringBuilder EnsureStringBuilder()
    {
        if (_blocks.LastOrDefault().StringBuilder is not { } sb)
        {
            sb = new StringBuilder();
            _blocks.Add((sb, null));
        }

        return sb;
    }

    public void Append(string value)
    {
        EnsureStringBuilder().Append(GetIndentedValue(value.TrimEnd()));
    }

    public void AppendUnindented(string value)
    {
        EnsureStringBuilder().Append(value.TrimEnd());
    }

    public void NewLine()
    {
        EnsureStringBuilder().AppendLine();
    }

    public void AppendLine(string value)
    {
        EnsureStringBuilder().AppendLine(GetIndentedValue(value.TrimEnd()));
    }

    public void AppendUnindentedLine(string value)
    {
        EnsureStringBuilder().AppendLine(value.TrimEnd());
    }

    private string GetIndentedValue(string value)
    {
        var indent = string.Empty;
        for (var i = 0; i < _indentLevel + _extraIndent; i++)
            indent += _indent;

        return indent + value;
    }

    public void Dispose()
    {
        if (_indentLevel > 0)
        {
            _indentLevel--;
            EnsureStringBuilder().AppendLine(GetIndentedValue("}"));
        }
    }

    public string Render()
    {
        var result = new StringBuilder();

        foreach (var block in _blocks)
        {
            if (block.StringBuilder is { })
            {
                result.Append(block.StringBuilder);
            }

            if (block.Writer is { })
            {
                block.Writer.Dispose();
                result.Append(block.Writer.Render());
            }
        }

        Dispose();

        return result.ToString();
    }
}
