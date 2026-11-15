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

public sealed class EventQueueNotActiveException()
    : InvalidOperationException("Cannot end queueing while not queueing.");

public sealed class EventQueueWrongThreadException(string operation)
    : InvalidOperationException($"Can only {operation} on the UI thread.")
{
    public string Operation { get; } = operation;
}

public sealed class EventQueueClosedBeforePostCompletedException()
    : InvalidOperationException("Queueing ended before the final message was processed.");
