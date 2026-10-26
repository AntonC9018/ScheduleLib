namespace ScheduleLib.DependencyInjection;

public sealed class InvalidMarkerServiceInheritanceException(Type markerServiceType, Type unboundServiceType)
    : InvalidOperationException($"{markerServiceType.Name} should only be inherited via {unboundServiceType.Name}")
{
    public Type MarkerServiceType { get; } = markerServiceType;
    public Type UnboundServiceType { get; } = unboundServiceType;
}
