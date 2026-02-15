using System.Reflection;

namespace Anton.LayeredData.Retrieval;

public interface IDataMapperBase
{
}

public static class DataMapCallHelper
{
    private delegate object MapDelegate(IDataMapperBase mapper, object x);
    private static readonly MethodInfo GenericMethod = typeof(DataMapCallHelper)
        .GetMethod(nameof(MapGeneric), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly CallHelper<MapDelegate> _helper = new(typeof(IDataMapper<,>), GenericMethod);

    private static object MapGeneric<T1, T2>(IDataMapperBase mapper, object from)
    {
        var m1 = (IDataMapper<T1, T2>) mapper;
        var from1 = (T1) from;
        var ret = m1.Map(from1);
        return ret!;
    }

    public static object Map(this IDataMapperBase mapper, object from)
    {
        var deleg = _helper.Get(mapper.GetType());
        var ret = deleg(mapper, from);
        return ret;
    }
}

public interface IDataMapper<TFrom, TTo> : IDataMapperBase
{
    TTo Map(TFrom input);
}

public readonly record struct DataMapping(IDataMapperBase Mapper, Type From)
{
    public static DataMapping Null => default;
    public bool IsNull => Mapper is null;
}

public sealed class DataMappingRegistry
{
    private readonly Dictionary<Type, DataMapping> _mappers;

    public DataMappingRegistry(IEnumerable<IDataMapperBase> mappers)
    {
        _mappers = new();
        foreach (var m in mappers)
        {
            var type = m.GetType();
            var itypes = type.GetImplementationsOfGenericInterface(typeof(IDataMapper<,>));
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

    public DataMapping Get(Type outputType)
    {
        var ret = _mappers.GetValueOrDefault(outputType, DataMapping.Null);
        return ret;
    }
}
