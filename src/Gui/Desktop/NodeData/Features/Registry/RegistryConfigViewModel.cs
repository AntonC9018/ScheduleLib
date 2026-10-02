using Desktop.NodeData.Common;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.OnlineRegistry.Impl;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.NodeData.Features.Registry;

public sealed class RegistryConfigViewModel(
    NodeDataAccessor<RegistryConfig> _helper,
    Registry<IEquationCommandsDerivation> _derivations,
    Registry<ExtraLessonInstanceAction> _extraLessons)

    : NodeDataViewModelBase<RegistryConfig>
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IPropertySetViewModelFactory, RegistryEditorFactory>();

        services.AddRegistry<IEquationCommandsDerivation>(opts =>
        {
            opts.UseTypeComparer();
            opts.Add("Compare lessons from any day", new AnyDayDerivation());
            opts.Add("Only compare lessons in the same day", new SameDayDerivation());
        });

        services.AddRegistry<ExtraLessonInstanceAction>(opts =>
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

    public SelectionHelper<RegistryConfig, ExtraLessonInstanceAction> ExtraLesson { get; } =
        _helper.SelectionHelper(_extraLessons, c => c.ExtraLessonInstanceAction, allowNull: true);

    public SelectionHelper<RegistryConfig, IEquationCommandsDerivation> Derivation { get; } =
        _helper.SelectionHelper(_derivations, c => c.EquationCommandsDerivation, allowNull: true);

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

            if (value is null)
            {
                v.CommandProcessingConfig = null;
                OnPropertyChanged();
                return;
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
            OnPropertyChanged();
        }
    }
}

public sealed class RegistryEditorFactory : IPropertySetViewModelFactory
{
    public static PropertySetId EditorKey { get; } = new("Online Registry");
    public PropertySetId Key => EditorKey;

    public NodeDataViewModelResult Create(NodeDataVMCreateParams p)
    {
        var factory = new VmFactoryBuilder<RegistryConfig>()
            .VM<RegistryConfigViewModel>()
            .UseUpdateOnDataChange();
        return p.BuildVm(RegistryConfig.Key, factory.CreateFactory());
    }
}
