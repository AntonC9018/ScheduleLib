using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using MainCli.Helper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;
using ScheduleLib.Scraping.Common.Config;

namespace MainCli.BuilderNew.Impl;

public static class Registration
{
    extension(ServiceCollection services)
    {
        public void AddAllServices()
        {
            services.AddScheduleServices();
            services.AddConfigsServices();
            services.AddOnlineRegistry();
            services.AddTaskHandlers();
            services.AddGlobalConfiguration();
            services.AddLogging();
        }

        public void AddConfigsServices()
        {
            services.AddMarkerServices();
            services.AddSingleton<ConfigMappingRegistry>();
            services.AddSingleton<ApplicationConfigBuilder>();

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
            services.AddScoped<IConfigurationSectionResolver, ConfigurationSectionResolver>();
            services.AddScoped(typeof(DynamicOptionsResolver<>), typeof(DynamicOptionsResolver<>));
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
                        CurrentStudyYear = options.StudyYear,
                    });
                });
                services.AddSingleton<ConfigureRemappingsDelegate>(Config.ConfigureRemappings);
                services.AddSingleton<CourseNameParserConfig>(Config.CourseNameParser);
                services.AddSingleton<CourseNameUnifierConfig>(sp =>
                {
                    var parserConfig = sp.GetRequiredService<CourseNameParserConfig>();
                    var unificationConfig = Config.CourseNameUnificationConfig;
                    var ret = CourseNameUnifierConfig.Create(parserConfig, unificationConfig);
                    return ret;
                });
                services.AddSingleton<CourseNameUnifierModule>();

                services.AddSingleton<ProcessSpaces>(Config.WhiteSpaceActionCourseName);
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
                    return Config.SemesterIntervalProvider();
                });
                services.AddSingleton<RegularSeminarDateProvider>();

                services.AddSingleton<IWeeklyScheduledDateProvider, ManualWeeklyScheduledDateProvider>(sp =>
                {
                    _ = sp;
                    var ret = new ManualWeeklyScheduledDateProvider(
                        studyWeeks: Config.StudyWeeks,
                        holidays: Config.HolidayPeriods);
                    return ret;
                });

                services.AddSingleton<IWeeklyScheduledEventsProvider, ScheduledEventsProviderTransformer>();
                services.AddScoped<ScheduledDateTimeProvider>();
            }

            void AddScheduleLifetimeServices()
            {
                services.AddSingleton<IScheduleInitializer, ScheduleBuilderInitializer>();
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
                services.AddSingleton<PdfLessonTextDisplayHandler.Services>();
            }

            void AddOptions()
            {
                services.AddOptions<ManifestDirectoriesOptions>();
                services.AddStudyYear();
                services.AddOptions<RegularSeminarDateConfig>();
                services.AddOptions<GoogleDriveOptions>();
            }
        }

        public void AddTaskHandlers()
        {
            services.AddScoped<GenerateAllTeachersExcelTaskHandler>();

            services.AddScoped<GenerateDeadlinesExcelTaskHandler>();
            services.AddScoped<LabsMappingProvider>();

            services.AddScoped<GenerateFreeRoomsTaskHandler>();
            services.AddScoped<GeneratePdfsForGroupsAndTeachersTaskHandler>();
            services.AddScoped<CopyGradesFromMoodleForTestTaskHandler>();
            services.AddScoped<PrintFreeHoursOfGroupTaskHandler>();
            services.AddScoped<AddLessonsToOnlineRegistryTaskHandler>();
            services.AddScoped<SyncDriveFolderTaskHandler>();

            services.AddScoped<GoogleCalendarLessons>();
            services.AddScoped<GoogleCredentialResolver>();
        }

        public IConfiguration AddGlobalConfiguration()
        {
            IConfiguration config;
            {
                var builder = new ConfigurationBuilder();
                builder.AddUserSecrets<Program>();
                config = builder.Build();
            }
            services.AddSingleton(config);
            return config;
        }
    }
}
