using Anton.LayeredData;

namespace Desktop.ViewModelData;

public sealed class UnsupportedEnumUnderlyingTypeSizeException(int size)
    : NotSupportedException($"Enum underlying type size {size} is not supported.")
{
    public int Size { get; } = size;
}

public sealed class ExpectedUiTeacherLayerException(Layer expectedLayer)
    : InvalidOperationException($"Expected a layer with name {expectedLayer}")
{
    public Layer ExpectedLayer { get; } = expectedLayer;
}
