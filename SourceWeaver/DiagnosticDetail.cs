namespace SourceWeaver;

public sealed record DiagnosticDetail(string Id, string Category, string Title, string Message)
{
    public DiagnosticDetail(string title, string message)
        : this(string.Empty, string.Empty, title, message) { }

    public static DiagnosticDetail Error(string title, string message) => new(title, message);
}
