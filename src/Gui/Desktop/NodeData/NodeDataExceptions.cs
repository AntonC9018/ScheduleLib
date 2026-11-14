namespace Desktop.NodeData.Common;

public sealed class RegistryConfigNodeNotSelectedException()
    : InvalidOperationException("Cannot set DryRun when no node is selected.");

public sealed class UnregisteredPropertyValueException(
    Type propertyType,
    Type? valueType,
    string? valueText = null)
    : NotImplementedException(FormatMessage(
        propertyType: propertyType,
        valueType: valueType,
        valueText: valueText))
{
    public Type PropertyType { get; } = propertyType;
    public Type? ValueType { get; } = valueType;
    public string? ValueText { get; } = valueText;

    private static string FormatMessage(
        Type propertyType,
        Type? valueType,
        string? valueText)
    {
        if (valueText is not null)
        {
            return
                $"Registry item was not found for {propertyType} value '{valueText}'. Implement the proper registry system!";
        }
        if (valueType is not null)
        {
            return
                $"Registry item was not found for {propertyType} value of runtime type {valueType}. Implement the proper registry system!";
        }
        return $"Registry item was not found for null {propertyType} value. Implement the proper registry system!";
    }
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
