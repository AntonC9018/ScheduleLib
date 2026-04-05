using Desktop.NodeData.Common;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.OnlineRegistry.Impl;

namespace Desktop.NodeData.Features.Registry;

public sealed class RegistryConfigViewModel(
    NodeDataAccessor<RegistryConfig> _helper,
    Registry<IEquationCommandsDerivation> _derivations,
    Registry<ExtraLessonInstanceAction> _extraLessons)

    : NodeDataViewModelBase<RegistryConfig>
{
    public static void Register(IServiceCollection services)
    {
        services.AddVmFactory(
            RegistryConfig.Key,
            b => b.VM<RegistryConfigViewModel>().UseUpdateOnDataChange());

        services.AddRegistry<IEquationCommandsDerivation?>(opts =>
        {
            opts.UseTypeComparer();
            opts.Add("Compare lessons from any day", new AnyDayDerivation());
            opts.Add("Only compare lessons in the same day", new SameDayDerivation());
        });

        services.AddRegistry<ExtraLessonInstanceAction?>(opts =>
        {
            opts.Add("Delete", ExtraLessonInstanceAction.Delete);
            opts.Add("Leave as is", ExtraLessonInstanceAction.LeaveAlone);
        });
    }

    public override void UpdateSelection()
    {
        Credentials.Set(_helper.ConditionallyEditableData);
        ExtraLesson.UpdateSelection();
        Derivation.UpdateSelection();
        OnPropertyChanged(nameof(DryRun));
    }

    public ObservableCredentials<RegistryConfig> Credentials { get; } = new();

    public SelectionHelper<ExtraLessonInstanceAction> ExtraLesson { get; } =
        _helper.SelectionHelper(_extraLessons, c => c.ExtraLessonInstanceAction);

    public SelectionHelper<IEquationCommandsDerivation> Derivation { get; } =
        _helper.SelectionHelper(_derivations, c => c.EquationCommandsDerivation);

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
