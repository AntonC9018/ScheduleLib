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

public sealed class NodeNotChildException()
    : InvalidOperationException("This node is not a child");

public sealed class NullBasicOperationsValueException(Type targetType)
    : InvalidOperationException(
        $"Root BasicOperations for `{targetType.Name}` is supposed to create actual objects, not null.")
{
    public Type TargetType { get; } = targetType;
}

public sealed class NodeDataValueMissingException(Type valueType)
    : InvalidOperationException($"Node data of type `{valueType.Name}` does not exist!")
{
    public Type ValueType { get; } = valueType;
}

public sealed class OptionsBindingNullException(string sectionPath, Type optionsType)
    : InvalidOperationException($"Failed to bind options `{optionsType.Name}` from section `{sectionPath}`.")
{
    public string SectionPath { get; } = sectionPath;
    public Type OptionsType { get; } = optionsType;
}

public sealed class NodeNotInPathException()
    : InvalidOperationException("Node not found in the path");

public sealed class NoMatchingConfigurationPathException(Type configType)
    : InvalidOperationException($"No matching path found for the current configuration (`{configType.Name}`).")
{
    public Type ConfigType { get; } = configType;
}

public sealed class SelfReferentialDataMappingException(Type configType)
    : InvalidOperationException($"Can't have a mapper between the same type `{configType.Name}`.")
{
    public Type ConfigType { get; } = configType;
}

public sealed class MarkerConfigTypeMismatchException(Type expectedType, Type? actualType)
    : InvalidOperationException(FormatMessage(expectedType: expectedType, actualType: actualType))
{
    public Type ExpectedType { get; } = expectedType;
    public Type? ActualType { get; } = actualType;

    private static string FormatMessage(Type expectedType, Type? actualType)
    {
        if (actualType is null)
        {
            return $"Marker config type mismatch: expected `{expectedType.Name}`, got null.";
        }

        return $"Marker config type mismatch: expected `{expectedType.Name}`, got `{actualType.Name}`.";
    }
}

public sealed class SingleUseEnumerableReusedException()
    : InvalidOperationException("This enumerable may only be enumerated once");

public sealed class GenericDelegateArgumentCountMismatchException(int expectedCount, int actualCount)
    : InvalidOperationException($"Argument count mismatch: expected {expectedCount}, got {actualCount}.")
{
    public int ExpectedCount { get; } = expectedCount;
    public int ActualCount { get; } = actualCount;
}
