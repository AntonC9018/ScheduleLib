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
        services.AddPropertySetDisplayFactory(
            new("Online Registry"),
            b =>
            {
                b.Source(x =>
                {
                    var p = PropertySet.From(RegistryConfig.Key)
                        .IncludeAll()
                        .UseUpdateOnDataChange();
                    x.Add(p);

                    x.From(RegistryConfig.Key)
                        .IncludeAll()
                        // applies to all properties from this set.
                        .UseUpdateOnDataChange();

                    x.From(RegistryConfig.Key).Include(i =>
                    {
                        i.Property(c => c.EquationCommandsDerivation).UseUpdateOnDataChange();
                        i.Property("DryRun").UseUpdateOnDataChange();
                    });

                    x.From(RegistryConfig.Key)
                        .IncludeProperty(c => c.EquationCommandsDerivation)
                        .IncludeProperty("DryRun")
                        .UseUpdateOnDataChange()
                        ;
                });
                b.UseUpdateOnDataChange();
            });

        services.ConfigurePropertySet(RegistryConfig.Key, b =>
        {
            // Can easily do a loop over each type.
            b.Property<bool?>("DryRun")
                .Uses(x => x.CommandProcessingConfig)
                // .GetUnwrap(c => c.HasAnyDryRun(LessonEquationCommandTypes.All))
                .Get(c => c?.HasAnyDryRun(LessonEquationCommandTypes.All) ?? null)
                .Set((commandProcessingConfigValue, value) =>
                {
                    CommandProcessingConfigBuilder b1;
                    if (commandProcessingConfigValue is { } c)
                    {
                        b1 = c.Builder();
                    }
                    else
                    {
                        b1 = new();
                        b1.Log().SetAll();
                        b1.Process().SetAll();
                    }

                    b1.DryRun().SetAll(value ?? false);
                });

            b.Property(x => x.ExtraLessonInstanceAction)
                .Rename("ExtraLesson")
                .UseRegistry(r =>
                {
                    r.Add("Delete", ExtraLessonInstanceAction.Delete);
                    r.Add("Leave as is", ExtraLessonInstanceAction.LeaveAlone);
                });

            b.Property(x => x.EquationCommandsDerivation)
                .Rename("Derivation")
                .UseDefaultRegistry();
        });

        services.ConfigurePropertySetDefaults(b =>
        {
            // TODO:
            // ObservableCredentials should work for any property.
            // For that, need to modify the builder to work with a property path.

            // b.PropertyWithType<CredentialsSource>().UseVm<ObservableCredentials>();
            // b.IfImplements<ICredentialsHolder>().Property(x => x.Credentials).UseVm(typeof(ObservableCredentials<>));
            // b.IfImpl<ICredentialsHolder>().UseVm(typeof(ObservableCredentials<>));
        });

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
