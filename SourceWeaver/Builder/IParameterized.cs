namespace SourceWeaver.Builder;

public interface IParameterized<T>
    where T : BuilderBase<T>
{
    T Parent { get; }

    List<ParameterBuilder<T>> Parameters { get; }
}
