using SourceWeaver.Builder.Internals;

namespace SourceWeaver.Builder;

internal interface IBuilder
{
    void Write(in CodeWriter writer);
}
