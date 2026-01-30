namespace Anton.LayeredConfig;

public interface IMergerBase
{
}

public interface IMerger<T> : IMergerBase
{
    public T Merge(T from, T? into);
}

public interface IBasicOperationsBase
{
}

public interface IBasicOperations<T> : IBasicOperationsBase
{
    public T? Empty();
    public T Copy(T from);
    public T? Reset(T? item);
}
