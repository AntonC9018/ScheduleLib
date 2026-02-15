namespace Anton.LayeredData;

public interface ICreateFromString<T>
{
    static abstract T Create(string val);
}

public sealed class NameRegistry<T>
    where T : ICreateFromString<T>
{
    private readonly HashSet<string> _registered = new();

    public T Register(string name)
    {
        lock (_registered)
        {
            if (!_registered.Add(name))
            {
                throw new InvalidOperationException($"{typeof(T).Name} with name '{name}' has already been registered.");
            }
            return T.Create(name);
        }
    }
}

