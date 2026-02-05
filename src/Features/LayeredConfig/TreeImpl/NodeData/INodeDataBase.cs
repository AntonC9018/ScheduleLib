namespace Anton.LayeredData;

// TODO: Source generate the key property,
// source generate the BasicOperations class,
// source generate the merger class.
public interface INodeData<T> : INodeDataBase
    where T : class
{
    public static abstract NodeDataKey<T> Key { get; }
}

public interface INodeDataBase
{
}
