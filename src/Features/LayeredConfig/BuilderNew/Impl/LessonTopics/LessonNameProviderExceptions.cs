namespace ScheduleLib.Application.Config;

public sealed class DirectLessonNameProviderBaseInheritanceException(Type contractType)
    : NotImplementedException($"Don't inherit {contractType.Name} directly!")
{
    public Type ContractType { get; } = contractType;
}
