using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.WebEncoders.Testing;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Dates;

namespace ScheduleFromDoc.Tests;

public enum TestOption
{
    Default,
    New,
}

public sealed class IntegrationTestHelper : IAsyncDisposable
{
    public static IEnumerable<object[]> TestOptionMemberData
    {
        get
        {
            return
            [
                [TestOption.Default],
                [TestOption.New],
            ];
        }
    }

    public static string VerifyScheduleSnapshotName(TestOption option)
    {
        string? name = Enum.GetName(option);
        string text = $"{name}_verify_schedule_model";
        return text;
    }

    public static string ScheduleJsonSnapshotName(TestOption option)
    {
        string? name = Enum.GetName(option);
        string text = $"{name}_verify_schedule_json";
        return text;
    }

    public static string ScheduleSnapshotJsonPath(TestOption option)
    {
        string? name = Enum.GetName(option);
        string text = $"schedule_{name}.json";
        return text;
    }

    public readonly int Year;
    public readonly Semester Semester;
    private readonly ServiceProvider _rootServiceProvider;
    public CancellationToken CancellationToken;
    private readonly CancellationTokenSource _cts;
    private ScheduleScope? _scope;

    public IServiceProvider ServiceProvider
    {
        get
        {
            if (_scope is null)
            {
                throw new InvalidOperationException("Initialize the schedule first.");
            }
            return _scope.ServiceProvider;
        }
    }

    public async Task InitializeSchedule()
    {
        var source = new ScheduleSource(DataDirectory: "data", UseCache: false);
        _scope = await _rootServiceProvider.OpenScheduleScope(source, CancellationToken);
    }

    public IntegrationTestHelper(
        int year,
        Semester sem,
        SubGroupValidationMode subGroupValidation = SubGroupValidationMode.Strict)
    {
        _cts = CreateCts();

        Year = year;
        Semester = sem;

        var services = new ServiceCollection();
        services.AddScheduleServices();
        services.AddLogging();
        services.Configure<StudyYearOptions>(opts =>
        {
            opts.StudyYear = new(year);
            opts.Semester = sem;
        });
        services.Configure<ScheduleSourceLoaderOptions>(opts =>
        {
            opts.EnrichWithFullNames = false;
            opts.LoadConsultations = false;
            opts.SubGroupValidation = subGroupValidation;
        });

        _rootServiceProvider = services.BuildServiceProvider();
    }

    public static Task<IntegrationTestHelper> Create(TestOption option)
    {
        return option switch
        {
#pragma warning disable CA2000
            TestOption.Default => CreateDefault(),
            TestOption.New => CreateNew(),
#pragma warning restore CA2000
            _ => throw UnreachableHelper.Unreachable(),
        };
    }

    public static async Task<IntegrationTestHelper> CreateDefault()
    {
        var ret = new IntegrationTestHelper(2024, Semester.Sem2, SubGroupValidationMode.None);
        try
        {
            await ret.InitializeSchedule();
        }
        catch
        {
            await ret.DisposeAsync();
            throw;
        }
        return ret;
    }

    public static async Task<IntegrationTestHelper> CreateNew()
    {
        var ret = new IntegrationTestHelper(2025, Semester.Sem1, SubGroupValidationMode.None);
        try
        {
            await ret.InitializeSchedule();
        }
        catch
        {
            await ret.DisposeAsync();
            throw;
        }
        return ret;
    }

    public static CancellationTokenSource CreateCts()
    {
        return TestAnyLessonSchedulingDateHelper.CreateCts();
    }

    public Schedule GetScheduleFromSourceOfTruth()
    {
        return ServiceProvider.GetRequiredService<Schedule>();
    }

    public static async Task AddScheduleToBuilder(
        ScheduleBuilder builder,
        string jsonPath,
        CancellationToken cancellationToken)
    {
        await using var reader = File.OpenRead(jsonPath);
        var scheduleModel = await ScheduleSerializer.Deserialize(reader, cancellationToken);
        ScheduleSerializer.AddToBuilder(builder, scheduleModel);
    }

    public static async Task<Schedule> GetScheduleFromJson(string jsonPath, CancellationToken cancellationToken)
    {
        var scheduleBuilder = new ScheduleBuilder();
        scheduleBuilder.ValidationSettings.SubGroup = SubGroupValidationMode.None;
        await AddScheduleToBuilder(
            scheduleBuilder,
            jsonPath,
            cancellationToken);
        var jsonSchedule = scheduleBuilder.Build();
        return jsonSchedule;
    }

    public SettingsTask ScheduleVerify(Schedule schedule, bool canonicalOrder = false)
    {
        var timeConfig = ServiceProvider.GetRequiredService<LessonTimeConfig>();
        var readableSchedule = new ReadableScheduleModel
        {
            Lessons = schedule.EnumerateAllLessons()
                .Select(x =>
                {
                    ReadablePeriodModel? periodModel = null;
                    if (x.Weekly?.Date.Period is { IsSpecified: true } periodId)
                    {
                        var period = schedule.Get(periodId);
                        periodModel = new()
                        {
                            Id = periodId.Value,
                            End = period.End,
                            Start = period.Start,
                        };
                    }

                    ReadableRepeatableDate? repeatableDateModel = null;
                    if (x.Weekly?.Date is { } weeklyDate)
                    {
                        repeatableDateModel = new()
                        {
                            DayOfWeek = weeklyDate.DayOfWeek,
                            Parity = weeklyDate.Parity,
                        };
                    }

                    ref readonly var lesson = ref x.Lesson;

                    return new ReadableLessonModel
                    {
                        Course = schedule.Get(lesson.Course).FullName,
                        Date = x.OneTime?.Date.Date,
                        Groups = lesson.Groups.Select(id => schedule.Get(id).Name)
                            .ToArray(),
                        Id = x.Id.Id,
                        Period = periodModel,
                        RepeatableDate = repeatableDateModel,
                        Room = lesson.Room.IsValid ? schedule.Get(lesson.Room) : null,
                        Teachers = lesson.Teachers.Select(tid => schedule.Get(tid).PersonName
                                .ToString())
                            .ToArray(),
                        Time = timeConfig.GetTimeSlotInterval(x.GetTimeSlot()).Start,
                        LessonType = lesson.Type,
                        SubGroup = lesson.SubGroup.Value,
                        Specialization = lesson.Specialization.Value,
                    };
                })
                .ToArray(),
            Groups = schedule.EnumerateGroups()
                .Select(g =>
                {
                    return new ReadableGroupModel
                    {
                        Id = g.Id.Value,
                        Name = g.Item.Name,
                    };
                })
                .ToArray(),
            Courses = schedule.EnumerateCourses()
                .Select(c =>
                {
                    return new ReadableCourseModel
                    {
                        Id = c.Id.Id,
                        Names = c.Item.Names.ToArray(),
                    };
                })
                .ToArray(),
            Periods = schedule.EnumeratePeriods()
                .Select(p =>
                {
                    return new ReadablePeriodModel
                    {
                        Id = p.Id.Value,
                        Start = p.Item.Start,
                        End = p.Item.End,
                    };
                })
                .ToArray(),
        };
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter<LessonType>());
        options.Converters.Add(new JsonStringEnumConverter<DayOfWeek>());
        options.Converters.Add(new JsonStringEnumConverter<Parity>());

        var textEncoder = new TextEncoderSettings();
        textEncoder.AllowRanges(UnicodeRanges.All);
        options.Encoder = JavaScriptEncoder.Create(textEncoder);

        if (canonicalOrder)
        {
            // Source discovery can assign different model IDs and enumeration orders.
            // The readable projection uses names for references, so assign snapshot-local IDs.
            foreach (var lesson in readableSchedule.Lessons)
            {
                lesson.Id = 0;
                Array.Sort(lesson.Groups, StringComparer.Ordinal);
                Array.Sort(lesson.Teachers, StringComparer.Ordinal);
            }
            readableSchedule.Lessons = readableSchedule.Lessons
                .OrderBy(lesson => JsonSerializer.Serialize(lesson, options), StringComparer.Ordinal)
                .ToArray();
            for (int index = 0; index < readableSchedule.Lessons.Length; index++)
            {
                readableSchedule.Lessons[index].Id = index;
            }
            foreach (var course in readableSchedule.Courses)
            {
                Array.Sort(course.Names, StringComparer.Ordinal);
            }
            readableSchedule.Courses = readableSchedule.Courses
                .OrderBy(course => JsonSerializer.Serialize(course.Names, options), StringComparer.Ordinal)
                .ToArray();
            for (int index = 0; index < readableSchedule.Courses.Length; index++)
            {
                readableSchedule.Courses[index].Id = index;
            }
            readableSchedule.Groups = readableSchedule.Groups
                .OrderBy(group => group.Name, StringComparer.Ordinal)
                .ToArray();
            for (int index = 0; index < readableSchedule.Groups.Length; index++)
            {
                readableSchedule.Groups[index].Id = index;
            }
        }

        using var stream = new MemoryStream();
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
        JsonSerializer.Serialize(
            stream,
            readableSchedule,
            options);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
        var str = reader.ReadToEnd();
        return Verify(new Target("json", str));
    }

    public async ValueTask DisposeAsync()
    {
        if (_scope is not null)
        {
            await _scope.DisposeAsync();
        }
        await _rootServiceProvider.DisposeAsync();
        _cts.Dispose();
    }
}

public sealed class ReadableScheduleModel
{
    public required ReadableLessonModel[] Lessons { get; set; }
    public required ReadableCourseModel[] Courses { get; set; }
    public required ReadablePeriodModel[] Periods { get; set; }
    public required ReadableGroupModel[] Groups { get; set; }
}

public sealed class ReadableLessonModel
{
    public required int Id { get; set; }
    public required string Course { get; set; }
    public required string[] Teachers { get; set; }
    public required string[] Groups { get; set; }
    public required ReadablePeriodModel? Period { get; set; }
    public required DateOnly? Date { get; set; }
    public required ReadableRepeatableDate? RepeatableDate { get; set; }
    public required string? Room { get; set; }
    public required TimeOnly Time { get; set; }
    public required LessonType LessonType { get; set; }
    public required string? SubGroup { get; set; }
    public required string? Specialization { get; set; }
}

public sealed class ReadableCourseModel
{
    public required int Id { get; set; }
    public required string[] Names { get; set; }
}

public sealed class ReadablePeriodModel
{
    public required int Id { get; set; }
    public required DateOnly Start { get; set; }
    public required DateOnly? End { get; set; }
}

public sealed class ReadableRepeatableDate
{
    public required DayOfWeek DayOfWeek { get; set; }
    public required Parity Parity { get; set; }
}

public sealed class ReadableGroupModel
{
    public required int Id { get; set; }
    public required string Name { get; set; }
}
