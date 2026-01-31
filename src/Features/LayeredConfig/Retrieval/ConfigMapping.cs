using System.Reflection;

namespace Anton.LayeredConfig.Retrieval;

public interface IConfigMapperBase
{
}

public static class ConfigMapCallHelper
{
    private delegate object MapDelegate(IConfigMapperBase mapper, object x);
    private static readonly MethodInfo GenericMethod = typeof(ConfigMapCallHelper)
        .GetMethod(nameof(MapGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly CallHelper<MapDelegate> _helper = new(typeof(IConfigMapper<,>), GenericMethod);

    private static object MapGeneric<T1, T2>(IConfigMapperBase mapper, object from)
    {
        var m1 = (IConfigMapper<T1, T2>) mapper;
        var from1 = (T1) from;
        var ret = m1.Map(from1);
        return ret!;
    }

    public static object Map(this IConfigMapperBase mapper, object from)
    {
        var deleg = _helper.Get(mapper.GetType());
        var ret = deleg(mapper, from);
        return ret;
    }
}

public interface IConfigMapper<TFrom, TTo> : IConfigMapperBase
{
    TTo Map(TFrom input);
}

public readonly record struct ConfigMapping(IConfigMapperBase Mapper, Type From)
{
    public static ConfigMapping Null => default;
    public bool IsNull => Mapper is null;
}

public sealed class ConfigMappingRegistry
{
    private readonly Dictionary<Type, ConfigMapping> _mappers;

    public ConfigMappingRegistry(IEnumerable<IConfigMapperBase> mappers)
    {
        _mappers = new();
        foreach (var m in mappers)
        {
            var type = m.GetType();
            var itypes = type.GetImplementationsOfGenericInterface(typeof(IConfigMapper<,>));
            using var itypesE = itypes.GetEnumerator();
            if (!itypesE.MoveNext())
            {
                throw new InvalidOperationException($"Type `{type.Name}` inherits `IConfigMapperBase` directly. Don't do it!");
            }

            while (true)
            {
                var itype = itypesE.Current;
                var args = itype.GetGenericArguments();
                var from = args[0];
                var to = args[1];
                _ = from;
                _ = to;
                // May add something like "can handle type" and run a check through each?
                if (from == to)
                {
                    throw new InvalidOperationException("Can't have a mapper between the same types.");
                }
                bool added = _mappers.TryAdd(to, new(m, from));
                if (!added)
                {
                    throw new InvalidOperationException($"Mapper for config type `{to.Name}` already exists.");
                }

                if (!itypesE.MoveNext())
                {
                    break;
                }
            }
        }
    }

    public ConfigMapping Get(Type outputType)
    {
        var ret = _mappers.GetValueOrDefault(outputType, ConfigMapping.Null);
        return ret;
    }
}
