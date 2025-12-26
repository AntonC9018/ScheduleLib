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
using ScheduleLib.ScheduleDefaults;
using ScheduleLib.Scraping.Common.Config;

namespace MainCli.BuilderNew.Impl;

public static class Registration
{
    extension(ServiceCollection services)
    {
        public void AddConfigsServices()
        {
            services.AddMarkerServices();

            services.AddKeyEqualityComparer((LessonNameProviderConfig c) => c.LessonType);
            services.AddBasicOperations<LessonTopicsSourceDefinitionBasicOperations>();

            services.RegisterBasicOperationsAndMergers<LessonTopicsConfig>();

            services.AddOnlineRegistryConfig();

            services.RegisterBasicOperationsAndMergers<MoodleConfig>();

            services.RegisterBasicOperationsAndMergers<GoogleDriveConfig>();

            services.AddKeyEqualityComparer((LessonAttendanceSource s) => s.FilePath);
            services.RegisterBasicOperationsAndMergers<LessonAttendanceConfig>();

            services.AddMapper<DeadlinesConfigMapper>();
            services.AddMerger<DeadlinesExcelConfigMerger>();
            services.RegisterBasicOperationsAndMergers<DeadlinesExcelConfig>();
        }

        public void AddScheduleServices()
        {
            services.AddSingleton<LookupFacade>(sp =>
                sp.GetRequiredService<ScheduleBuilder>().Lookup());
            services.AddSingleton<GroupParseContext>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<StudyYearOptions>>().Value;
                return GroupParseContext.Create(new()
                {
                    CurrentStudyYear = options.StudyYear,
                });
            });
            services.AddSingleton<ScheduleBuilder>(sp =>
            {
                var builder = new ScheduleBuilder();
                builder.GroupParseContext = sp.GetRequiredService<GroupParseContext>();
                return builder;
            });
            services.AddSingleton<IScheduleInitializer, ScheduleBuilderInitializer>();

            services.AddSingleton(Config.CourseNameParser);
            services.AddSingleton<CourseNameUnifierConfig>(sp =>
            {
                var parserConfig = sp.GetRequiredService<CourseNameParserConfig>();
                var unificationConfig = Config.CourseNameUnificationConfig;
                var ret = CourseNameUnifierConfig.Create(parserConfig, unificationConfig);
                return ret;
            });
            services.AddSingleton<CourseNameUnifierModule>();
            services.AddSingleton<SemesterIntervalProvider>(sp =>
            {
                _ = sp;
                return Config.SemesterIntervalProvider();
            });

            // These don't seem necessary?
            // I'm not sure how to set up the schedule in DI.
            services.AddSingleton<ScheduleProvider>();
            services.AddScoped<Schedule>(x =>
            {
                var provider = x.GetRequiredService<ScheduleProvider>();
                return provider.Get();
            });
            services.AddScoped<ScopeFilteredScheduleProvider>();
            services.AddSingleton<LatestPeriodFilteredScheduleProvider>();

            services.AddSingleton<LessonTimeConfig>(
                LessonTimeConfig.CreateDefault());

            services.AddSingleton<ProcessSpaces>(Config.WhiteSpaceActionCourseName);

            services.AddSingleton<RegularSeminarDateProvider>();
            services.AddSingleton<DayNameProvider>();
            services.AddSingleton<LessonTypeDisplayHandler>();
            services.AddSingleton<ParityDisplayHandler>();
            services.AddSingleton<TimeSlotDisplayHandler>();

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

            services.AddScoped<ICredentialsResolver, CredentialsResolver>();

            services.AddConfigProvider(MoodleConfig.Key);
            services.AddCredentialsResolver<MoodleConfig>(serviceKey: "Moodle", x => x.Credentials!);

            services.AddConfigProvider(BuiltRegistryConfig.Key);
            services.AddCredentialsResolver<BuiltRegistryConfig>(serviceKey: "Registry", x => x.Credentials);

            services.AddSingleton<IAllScheduledDateProvider, ManualAllScheduledDateProvider>(sp =>
            {
                _ = sp;
                var ret = new ManualAllScheduledDateProvider(
                    studyWeeks: Config.StudyWeeks,
                    holidays: Config.HolidayPeriods);
                return ret;
            });

            // TODO: This should function as a provider then? doing work in DI is not good.
            services.AddScoped<IRegistryErrorHandler>(sp =>
            {
                var config = sp.GetRequiredService<ConfigProvider<RegistryConfig>>().Get();
                var ret = ActivatorUtilities.CreateInstance<RegistryErrorLogger>(sp);
                if (config.ExtraLessonInstanceAction is { } action)
                {
                    ret.ExtraLessonAction = action;
                }
                return ret;
            });

            services.AddOptions<ManifestDirectoriesOptions>();
            services.AddStudyYear();
            services.AddOptions<RegularSeminarDateConfig>();
        }

        public void AddTaskHandlers()
        {
            services.AddScoped<GenerateAllTeachersExcelTaskHandler>();
            services.AddScoped<GenerateDeadlinesExcelTaskHandler>();
            services.AddScoped<GenerateFreeRoomsTaskHandler>();
            services.AddScoped<GeneratePdfsForGroupsAndTeachersTaskHandler>();
            services.AddScoped<PrintFreeHoursOfGroupTaskHandler>();
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
