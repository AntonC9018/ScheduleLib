using Desktop.NodeData.Common;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.OnlineRegistry.Impl;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.NodeData.Features.Registry;

public static class RegistryEditorRegistration
{
    public static PropertySetId EditorKey { get; } = new("Online Registry");

    public static void Register(IServiceCollection services)
    {
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

        services.ConfigurePropertySetDefaults(b =>
        {
            b.PropertiesWithType<CredentialsSource>(p => p.UseVm(ObservableCredentials.Id));
        });
        services.ConfigurePropertySet(RegistryConfig.Key, b =>
        {
            b.Property<bool?>("DryRun")
                .Uses(c => c.CommandProcessingConfig)
                .Get(c => c?.HasAnyDryRun(LessonEquationCommandTypes.All))
                .Set((source, value) =>
                {
                    if (value is null) return null;
                    CommandProcessingConfigBuilder builder;
                    if (source is { } config) builder = config.Builder();
                    else
                    {
                        builder = new();
                        builder.Log().SetAll();
                        builder.Process().SetAll();
                    }
                    builder.DryRun().SetAll(value.Value);
                    return builder.Build();
                })
                .Rename("Dry run")
                .Describe("Preview changes without updating the registry. The indeterminate state inherits command processing.");
            b.Property(c => c.CommandProcessingConfig).Hide();
            b.Property(c => c.Credentials).Rename("Credentials");
            b.Property(c => c.ExtraLessonInstanceAction)
                .Rename("Extra lessons in the registry").UseDefaultRegistry();
            b.Property(c => c.EquationCommandsDerivation)
                .Rename("Compare lessons").UseDefaultRegistry();
        });

        services.AddPropertySetDisplayFactory(EditorKey, b => b.SourceFrom(RegistryConfig.Key, c =>
        {
            c.Include(g => g.Title("Processing").IncludeProperty("DryRun"));
            c.Include(g => g.Title("Registry credentials").IncludeProperty(x => x.Credentials));
            c.Include(g =>
            {
                g.Title("Lesson synchronization");
                g.IncludeProperty(x => x.ExtraLessonInstanceAction);
                g.IncludeProperty(x => x.EquationCommandsDerivation)
                    .Describe("Choose Default to inherit the setting from earlier layers.");
            });
        }));
    }
}
