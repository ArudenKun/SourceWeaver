namespace SourceWeaver.Extensions;

public static class TypeExtensions
{
    /// <summary>
    /// Gets the value of a public static field from the target Type cast to <typeparamref name="T"/>.
    /// </summary>
    public static T? GetStaticFieldValue<T>(this Type type, string fieldName)
    {
        var fieldInfo = type.GetField(
            fieldName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );

        if (fieldInfo is null)
        {
            throw new InvalidOperationException(
                $"Field '{fieldName}' not found on type '{type.FullName}'."
            );
        }

        return (T?)fieldInfo.GetValue(null);
    }

    /// <summary>
    /// Tries to get the value of a public static field from the target Type.
    /// </summary>
    public static bool TryGetStaticFieldValue<T>(this Type type, string fieldName, out T? value)
    {
        var fieldInfo = type.GetField(
            fieldName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );

        if (fieldInfo is not null)
        {
            value = (T?)fieldInfo.GetValue(null);
            return true;
        }

        value = default;
        return false;
    }
}
