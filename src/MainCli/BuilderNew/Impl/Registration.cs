using MainCli.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.ScheduleDefaults;

namespace MainCli.BuilderNew.Impl;

public static class Registration
{
    extension(ServiceCollection services)
    {
        public void AddConfigsServices()
        {
            services.AddMarkerServices();
            services.AddSingleton<IBasicOperations<Name>, ImmutableClassBasicOperations<Name>>();
            services.AddKeyEqualityComparer((LessonNameProviderConfig c) => c.LessonType);
            services.RegisterBasicOperationsAndMergers<LessonTopicsConfig>();
            services.RegisterBasicOperationsAndMergers<RegistryConfig>();
            services.RegisterBasicOperationsAndMergers<MoodleConfig>();
            services.RegisterBasicOperationsAndMergers<GoogleDriveConfig>();
            services.AddKeyEqualityComparer((LessonAttendanceSource s) => s.FilePath);
            services.RegisterBasicOperationsAndMergers<LessonAttendanceConfig>();
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
            services.AddOptions<RegularSeminarDateConfig>().Configure(x =>
            {
                x.Day = DayOfWeek.Wednesday;
                x.Time = new(hour: 15, minute: 00);
            });
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
        }

        public void AddTaskHandlers()
        {
            services.AddScoped<GenerateAllTeachersExcelHandler>();
        }
    }
}
