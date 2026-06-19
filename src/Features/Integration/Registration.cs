using System.Reflection;
using System.Text;
using Anton.LayeredData;
using Anton.LayeredData.Options;
using Anton.LayeredData.Retrieval;
using FmiWebsiteInterop.Api;
using FmiWebsiteInterop.Theses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Generation;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.Scraping.Common.Config;
using ScheduleLib.Theses.Parsing;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ScheduleLib.Application.Config;

public static class Registration
{
    extension(IServiceCollection services)
    {
        public void AddAllServices()
        {
            services.AddScheduleServices();
            services.AddConfigsServices();
            services.AddOnlineRegistry();
            services.AddTaskHandlers();
            services.AddGlobalConfiguration();

            Console.OutputEncoding = new UTF8Encoding();
            services.AddLogging(logging =>
            {
                logging.ClearProviders();

                var builder = new LoggerConfiguration();
                builder = builder.Enrich.FromLogContext();
                builder.WriteTo.Logger(w =>
                {
                    w = w.Enrich.With(new RemovePropertyEnricher("EventId"));
                    w = w.Enrich.With(new RemovePropertyEnricher("SourceContext"));
                    w.WriteTo.Console(outputTemplate:
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
                });

                logging.AddSerilog(builder.CreateLogger());
            });

            services.AddHelperServices();
            services.AddItUsmIntegration();
        }

        public void AddConfigsServices()
        {
            CredentialsSource.Register(services);

            services.AddMarkerServices();
            services.AddSingleton<DataMappingRegistry>();
            services.AddSingleton<TreeBuilder>();

            LessonTopicsConfig.Register(services);
            MoodleConfig.Register(services);

            GoogleCredentialsConfig.Register(services);
            GoogleDriveConfig.Register(services);
            GoogleCalendarConfig.Register(services);

            LessonAttendanceConfig.Register(services);
            DeadlinesExcelConfig.Register(services);
            LabTasksDatabaseConfig.Register(services);
            RegistryLessonFilterConfig.Register(services);

            // Credentials
            services.AddScoped<ICredentialsResolver, CredentialsResolver>();
            // Binding config from IConfiguration
            services.AddDynamicConfigurationBinders();
            services.AddCredentialsResolver<MoodleConfig>(serviceKey: "Moodle", x => x.Credentials!);
            services.AddCredentialsResolver<BuiltRegistryConfig>(serviceKey: "Registry", x => x.Credentials);
        }

        public void AddDynamicConfigurationBinders()
        {
            services.AddScoped<IMarkedConfigurationSectionResolver, MarkedConfigurationSectionResolver>();
            services.AddScoped(typeof(MarkedDynamicOptionsResolver<>), typeof(MarkedDynamicOptionsResolver<>));
            services.AddScoped(typeof(DynamicOptionsBinder<>), typeof(DynamicOptionsBinder<>));
        }

        public void AddScheduleServices()
        {
            AddScheduleGeneralServices();
            AddScheduleLifetimeServices();
            AddParserServices();
            AddTimeServices();
            AddOutputServices();
            AddOptions();
            return;

            void AddScheduleGeneralServices()
            {
                services.AddSingleton<LookupFacade>();
                services.AddSingleton<LookupModule>(sp =>
                {
                    return sp.GetRequiredService<ScheduleBuilder>().LookupModule!;
                });
                services.AddSingleton<ScheduleBuilder>(sp =>
                {
                    var builder = new ScheduleBuilder();
                    builder.GroupParseContext = sp.GetRequiredService<GroupParseContext>();
                    builder.EnableLookupModule();
                    return builder;
                });
                services.AddSingleton<SubGroupNameRemapper>();
            }

            void AddParserServices()
            {
                services.AddSingleton<GroupParseContext>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<StudyYearOptions>>().Value;
                    return GroupParseContext.Create(new()
                    {
                        GroupLabelsThatAreMaster = ScheduleDefaults.Config.GroupLabelsThatAreMaster,
                        CurrentStudyYear = options.StudyYear,
                    });
                });
                services.AddSingleton<ConfigureRemappingsDelegate>(ScheduleDefaults.Config.ConfigureRemappings);
                services.AddSingleton<CourseNameParserConfig>(ScheduleDefaults.Config.CourseNameParser);
                services.AddSingleton<CourseNameUnifierConfig>(sp =>
                {
                    var parserConfig = sp.GetRequiredService<CourseNameParserConfig>();
                    var unificationConfig = ScheduleDefaults.Config.CourseNameUnificationConfig;
                    var ret = CourseNameUnifierConfig.Create(parserConfig, unificationConfig);
                    return ret;
                });
                services.AddSingleton<CourseNameUnifierModule>();

                services.AddSingleton<ProcessSpaces>(ScheduleDefaults.Config.WhiteSpaceActionCourseName);
                services.AddSingleton(ParityParser.Instance);
                services.AddSingleton(LessonTypeParser.Instance);
                services.AddSingleton(RoomParser.Instance);
                services.AddSingleton<LessonParserFactory>(sp =>
                {
                    return new LessonParserFactory(new()
                    {
                        LessonTypeParser = sp.GetRequiredService<LessonTypeParser>(),
                        ParityParser = sp.GetRequiredService<ParityParser>(),
                        ProcessSpacesCourseName = sp.GetRequiredService<ProcessSpaces>(),
                        RoomParser = sp.GetRequiredService<RoomParser>(),
                    });
                });

                services.AddSingleton<DayNameProvider>();
                services.AddSingleton<DayNameParser>();
            }

            void AddTimeServices()
            {
                services.AddSingleton<LessonTimeConfig>(
                    LessonTimeConfig.CreateDefault());
                services.AddSingleton<CurrentYearSemesterIntervalProvider>(sp =>
                {
                    _ = sp;
                    return ScheduleDefaults.Config.SemesterIntervalProvider();
                });
                services.AddSingleton<RegularSeminarDateProvider>();

                services.AddSingleton<IWeeklyScheduledDateProvider, ManualWeeklyScheduledDateProvider>(sp =>
                {
                    _ = sp;
                    var ret = new ManualWeeklyScheduledDateProvider(
                        studyWeeks: ScheduleDefaults.Config.StudyWeeks,
                        holidays: ScheduleDefaults.Config.HolidayPeriods);
                    return ret;
                });

                services.AddSingleton<IWeeklyScheduledEventsProvider, ScheduledEventsProviderTransformer>();
                services.AddScoped<ScheduledDateTimeProvider>();
                services.AddScoped<ScheduledTimeEventsProvider>();
                services.AddScoped<ScheduleDateProviderHelper>();
            }

            void AddScheduleLifetimeServices()
            {
                services.AddSingleton<IScheduleInitializer, ScheduleBuilderInitializer>();
                services.AddTransient<EnrichWithTeacherFullNamesFromWebsite>();
                services.AddTransient<ConsultationsLoaderComponent>();

                services.AddOptions<ScheduleBuilderInitializerOptions>();

                // These don't seem necessary?
                // I'm not sure how to set up the schedule in DI.
                services.AddSingleton<ScheduleProvider>();
                services.AddScoped<Schedule>(x =>
                {
                    var provider = x.GetRequiredService<ScheduleProvider>();
                    // Caching this should probably be on by default / make this singleton.
                    return provider.Get();
                });
                services.AddScoped<CurrentTeacherIdProvider>();
                services.AddScoped<ScopeFilteredScheduleProvider>();
                services.AddScoped<LatestPeriodFilteredScheduleProvider>();
                services.AddScoped<CurrentUserNameProvider>();
            }

            void AddOutputServices()
            {
                services.AddSingleton<LessonTypeDisplayHandler>();
                services.AddSingleton<ParityDisplayHandler>();
                services.AddSingleton<TimeSlotDisplayHandler>();
                services.AddSingleton<SubGroupNumberDisplayHandler>();
                services.AddSingleton<LessonTextDisplayHandler.Services>();
            }

            void AddOptions()
            {
                services.AddOptions<ManifestDirectoriesOptions>();
                services.AddStudyYear();
                services.AddOptions<RegularSeminarDateConfig>();
                services.AddOptions<GoogleDriveOptions>();
            }
        }

        public void AddItUsmIntegration()
        {
            ItUsmWebsiteApi.Register(services);
        }

        public void AddHelperServices()
        {
            GoogleApiHelper.Register(services);
            DriveFileLoader.Register(services);
            TeacherNameMapper.Register(services);
            services.AddKeyedSingleton<INameRemapper, DoNothingNameRemapper>(NameMappingKeys.Student);
        }

        public void AddTaskHandlers()
        {
            {
                services.AddScoped<GenerateDeadlinesExcelTaskHandler>();
                services.AddScoped<LabsMappingProvider>();
            }
            services.AddScoped<GenerateAllTeachersExcelTaskHandler>();
            services.AddScoped<GenerateFreeRoomsTaskHandler>();
            services.AddScoped<GeneratePdfsForGroupsAndTeachersTaskHandler>();
            services.AddScoped<CopyGradesFromMoodleForTestTaskHandler>();
            services.AddScoped<PrintFreeHoursOfGroupTaskHandler>();
            {
                services.AddScoped<AddLessonsToOnlineRegistryTaskHandler>();

                services.AddScoped<ContextProvider>();
                services.AddScoped<CurrentTeacherLessonFilterProvider>();
                services.AddScoped<LessonTopicsOfCurrentTeacherLoader>();
                services.AddScoped<AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler>();
            }

            {
                services.AddScoped<SyncDriveFolderTaskHandler>();
                services.AddScoped<UpdateLessonsInGoogleCalendarTaskHandler>();
            }
            {
                services.AddScoped<ThesesConversionTaskHandler>();
                services.AddScoped<ListsForPredzashitaTaskHandler>();
                ThesesListProvider.Register(services);
                LocalThesesFileProvider.Register(services);
                // OnlineThesesFileProvider.Register(services);
            }
            {
                StudentAttendanceLoader.Register(services);
            }
        }

        public IConfiguration AddGlobalConfiguration()
        {
            IConfiguration config;
            {
                var builder = new ConfigurationBuilder();
                builder.AddUserSecrets(Assembly.GetEntryAssembly()!);
                config = builder.Build();
            }
            services.AddSingleton(config);
            return config;
        }
    }
}

file sealed class RemovePropertyEnricher : ILogEventEnricher
{
    private readonly string _propertyName;
    public RemovePropertyEnricher(string propertyName) => _propertyName = propertyName;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
        => logEvent.RemovePropertyIfPresent(_propertyName);
}
