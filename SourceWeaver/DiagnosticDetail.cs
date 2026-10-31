namespace SourceWeaver;

public sealed record DiagnosticDetail(string Id, string Category, string Title, string Message)
{
    public DiagnosticDetail(string title, string message)
        : this(string.Empty, string.Empty, title, message) { }

    public static DiagnosticDetail Error(string title, string message) => new(title, message);

    public static implicit operator DiagnosticDescriptor(DiagnosticDetail detail) =>
        new(
            detail.Id,
            detail.Title,
            detail.Message,
            detail.Category,
            DiagnosticSeverity.Error,
            true
        );

    public static implicit operator DiagnosticDetail(DiagnosticDescriptor descriptor) =>
        new(
            descriptor.Id,
            descriptor.Category,
            descriptor.Title.ToString(),
            descriptor.MessageFormat.ToString()
        );
}
