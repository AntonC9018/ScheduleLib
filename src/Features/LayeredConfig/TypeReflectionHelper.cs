namespace Anton.LayeredData;

public static class TypeReflectionHelper
{
    public static Type[]? GetTypeArgumentsOfImplementationOfGenericInterface(
        this Type implementingType,
        Type genericInterface)
    {
        var concreteInterfaceImplementationType = implementingType
            .GetTypeArgumentsOfImplementationsOfGenericInterface(genericInterface)
            .FirstOrDefault();
        return concreteInterfaceImplementationType;
    }

    public static IEnumerable<Type> GetImplementationsOfGenericInterface(
        this Type implementingType,
        Type genericInterface)
    {
        return implementingType
            .GetInterfaces()
            .Where(t => t.IsGenericType
                && t.GetGenericTypeDefinition() == genericInterface);
    }

    public static IEnumerable<Type> GetImplementationsOfGenericType(
        this Type implementingType,
        Type genericInterface)
    {
        IEnumerable<Type> BaseTypes()
        {
            var baseType = implementingType.BaseType;
            while (baseType is not null)
            {
                yield return baseType;
                baseType = baseType.BaseType;
            }
        }

        return implementingType
            .GetInterfaces()
            .Concat(BaseTypes())
            .Where(t => t.IsGenericType
                && t.GetGenericTypeDefinition() == genericInterface);
    }

    public static IEnumerable<Type[]> GetTypeArgumentsOfImplementationsOfGenericInterface(
        this Type implementingType,
        Type genericInterface)
    {
        return GetImplementationsOfGenericInterface(implementingType, genericInterface)
            .Select(i => i.GenericTypeArguments);
    }
}
