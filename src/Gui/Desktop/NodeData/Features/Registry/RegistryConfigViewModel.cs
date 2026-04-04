using Desktop.NodeData.Common;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;

namespace Desktop.NodeData.Features.Registry;

public sealed class RegistryConfigViewModel(
    NodeDataAccessor<RegistryConfig> _helper)

    : NodeDataViewModelBase<RegistryConfig>
{
    public static void Register(IServiceCollection services)
    {
        services.AddVmFactory(
            RegistryConfig.Key,
            b => b.VM<RegistryConfigViewModel>().UseUpdateOnDataChange());
    }

    public override void UpdateSelection()
    {
        Credentials.Set(_helper.ConditionallyEditableData);
        OnPropertyChanged(nameof(DryRun));
    }

    public ObservableCredentials<RegistryConfig> Credentials { get; } = new();

    public ExtraLessonInstanceAction[] ExtraLessonInstanceActions => [
        ExtraLessonInstanceAction.Delete,
        ExtraLessonInstanceAction.LeaveAlone];

    public bool? DryRun
    {
        get
        {
            if (_helper.Data?.CommandProcessingConfig is not { } c)
            {
                return null;
            }
            if (c.HasAnyDryRun(LessonEquationCommandTypes.All))
            {
                return true;
            }
            return false;
        }
        set
        {
            var v = _helper.EditableData;
            if (v == null)
            {
                throw new InvalidOperationException("Cannot set DryRun when no node selected");
            }
            CommandProcessingConfigBuilder b;
            if (v.CommandProcessingConfig is { } c)
            {
                b = c.Builder();
            }
            else
            {
                b = new();
                b.Log().SetAll();
                b.Process().SetAll();
            }
            b.DryRun().SetAll(value ?? false);
            v.CommandProcessingConfig = b.Build();
        }
    }

}
