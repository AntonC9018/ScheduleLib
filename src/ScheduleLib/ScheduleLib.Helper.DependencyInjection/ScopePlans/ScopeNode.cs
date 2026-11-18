namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>A node of the scope plan graph: a service type and its DI key (null when unkeyed).</summary>
public readonly record struct ScopeNode(Type Type, object? Key)
{
    public static ScopeNode Unkeyed(Type type)
    {
        return new(type, Key: null);
    }

    public override string ToString()
    {
        if (Key is null)
        {
            return Type.Name;
        }
        return $"{Type.Name}[\"{Key}\"]";
    }
}
