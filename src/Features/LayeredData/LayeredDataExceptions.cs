using System.Text.Json;

namespace Anton.LayeredData;

public sealed class TypeDoesNotImplementGenericInterfaceException(Type implementationType, Type interfaceType)
    : ArgumentException(
        message: $"Type {implementationType} does not implement {interfaceType.FullName}<T>",
        paramName: "implType")
{
    public Type ImplementationType { get; } = implementationType;
    public Type InterfaceType { get; } = interfaceType;
}

public sealed class TypeHasPublicInstanceFieldsException(Type targetType)
    : InvalidOperationException($"Type `{targetType.Name}` should only have properties")
{
    public Type TargetType { get; } = targetType;
}

public sealed class TypeMissingParameterlessConstructorException(Type targetType)
    : InvalidOperationException(
        $"Type {targetType.Name} does not have a parameterless constructor and is not a value type.")
{
    public Type TargetType { get; } = targetType;
}

public sealed class NameAlreadyRegisteredException(Type registeredType, string name)
    : InvalidOperationException($"{registeredType.Name} with name '{name}' has already been registered.")
{
    public Type RegisteredType { get; } = registeredType;
    public string Name { get; } = name;
}

public sealed class InvalidDataMapperImplementationException(Type mapperType)
    : InvalidOperationException(
        $"Type `{mapperType.Name}` inherits `IConfigMapperBase` directly. Don't do it!")
{
    public Type MapperType { get; } = mapperType;
}

public sealed class ConfigMapperAlreadyRegisteredException(Type configType)
    : InvalidOperationException($"Mapper for config type `{configType.Name}` already exists.")
{
    public Type ConfigType { get; } = configType;
}

public sealed class ConfigMapperNotRegisteredException(Type configType)
    : InvalidOperationException($"Config mapper not registered for type `{configType.Name}`")
{
    public Type ConfigType { get; } = configType;
}

public sealed class InvalidConfigKeyException(string key)
    : JsonException($"Invalid config key: {key}")
{
    public string Key { get; } = key;
}

public sealed class NodeDataKeyNotFoundException(string key)
    : KeyNotFoundException($"{key} not found")
{
    public string Key { get; } = key;
}
