namespace MainCli.BuilderNew;

public interface IMerger<T>
{
    public T Merge(T from, T? into);
}

public interface IBasicOperations<T>
{
    public T? Empty();
    public T Copy(T from);
    public T? Reset(T? item);
}

