using Anton.LayeredData;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core.Helper;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Dates;

public static class AppConfiguration
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddAllServices();

        services.Configure<ManifestDirectoriesOptions>(x =>
        {
            x.Directories.Add("data/topics");
        });
        services.Configure<StudyYearOptions>(x =>
        {
            x.StudyYear = new(2026);
            x.Semester = Semester.Sem1;
        });
        services.Configure<RegularSeminarDateConfig>(x =>
        {
            x.Day = DayOfWeek.Wednesday;
            x.Time = new(hour: 15, minute: 00);
        });
        services.Configure<ScheduleBuilderInitializerOptions>(x =>
        {
            x.BypassCache = false;
            x.EnrichWithFullNames = true;
            x.LoadConsultations = false;
        });
        services.ConfigureNodeDataJsonSerialization(x =>
        {
            x.WriteIndented = true;
        });
    }

    public static ServiceProvider BuildServiceProvider(IServiceCollection services)
    {
        var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        return serviceProvider;
    }

    public static void ConfigureLayeredConfig(IServiceProvider sp)
    {
        var b = sp.GetRequiredService<TreeBuilder>();
        b.AddDefaultConfig();
    }

    public static ServiceProvider CreateDefaultApp()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = BuildServiceProvider(services);
        ConfigureLayeredConfig(serviceProvider);
        return serviceProvider;
    }
}


