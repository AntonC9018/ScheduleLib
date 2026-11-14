namespace Desktop.NodeData.Common;

public sealed class RegistryConfigNodeNotSelectedException()
    : InvalidOperationException("Cannot set DryRun when no node is selected.");

public sealed class UnregisteredPropertyValueException(object? value)
    : NotImplementedException($"Registry item was not found for value '{value}'. Implement the proper registry system!")
{
    public object? Value { get; } = value;
}

public sealed class CurrentUserScopeNotInitializedException()
    : InvalidOperationException("The scope must be initialized before use.");

public sealed class MissingTeacherMarkerNodeDataException()
    : InvalidOperationException("Cannot reset scope when selected path does not contain marker node data.");

public sealed class MissingTeacherMarkerValueException()
    : InvalidOperationException("Expected the marker node to have the value directly.");

public sealed class SelectionDataNotEditableException()
    : InvalidOperationException("Data is not editable.");

public sealed class NullableStructPropertyAccessRequiredException(Type propertyType)
    : InvalidOperationException($"Must call the nullable struct overload for {propertyType}!")
{
    public Type PropertyType { get; } = propertyType;
}

public sealed class PropertySetDisplayNotImplementedException(string operation)
    : NotImplementedException($"Property set display {operation} is not implemented.")
{
    public string Operation { get; } = operation;
}

public sealed class NodeDataNotEditableException()
    : InvalidOperationException("Node is not editable.");
