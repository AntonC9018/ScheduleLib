using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ScheduleLib.Builders;
using ScheduleLib.Helper.JsonConverters;
using ScheduleLib.JsonConverters;
using ScheduleLib.Parsing.CourseName;

namespace ScheduleLib;

public static class ScheduleSerializer
{
    public static Task Serialize(
        Schedule schedule,
        Stream outputFile,
        string hash,
        CancellationToken cancellationToken)
    {
        var model = SerializationModels.ConvertToSerializationModel(schedule, hash);
        var json = JsonSerializer.SerializeAsync(outputFile, model, SerializerOptions, cancellationToken);
        return json;
    }

    public static async Task<SerializationModels.ScheduleModel> Deserialize(
        Stream inputFile,
        CancellationToken cancellationToken)
    {
        var model = await JsonSerializer.DeserializeAsync<SerializationModels.ScheduleModel>(
            inputFile,
            SerializerOptions,
            cancellationToken);
        if (model is null)
        {
            throw new InvalidDataException("Could not deserialize schedule");
        }
        return model;
    }

    public static void AddToBuilder(
        ScheduleBuilder builder,
        SerializationModels.ScheduleModel schedule,
        CourseNameUnifierModule? unifier = null)
    {
        SerializationModels.ConvertToScheduleBuilder(builder, schedule);

        if (builder.LookupModule is not null)
        {
            builder.RefreshLookup();
        }

        if (unifier is not null)
        {
            builder.EnableLookupModule();
            unifier.Refresh(builder);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new NamePartsJsonConverter<OptionalNamePart>());
        options.Converters.Add(new NamePartsJsonConverter<string>());
        options.Converters.Add(new DateOnlyJsonConverter());
        options.Converters.Add(new SingleValueWrapperConverterFactory());
        options.Converters.Add(new JsonStringEnumConverter<Language>());
        options.Converters.Add(new OnlySerializeSettersForUserDefinedTypesConverterFactory());
        var textEncoder = new TextEncoderSettings();
        textEncoder.AllowRanges(
            UnicodeRanges.BasicLatin,
            UnicodeRanges.Latin1Supplement,
            UnicodeRanges.LatinExtendedA,
            UnicodeRanges.LatinExtendedB,
            UnicodeRanges.LatinExtendedC,
            UnicodeRanges.LatinExtendedD,
            UnicodeRanges.LatinExtendedE);
        options.Encoder = JavaScriptEncoder.Create(textEncoder);
        options.ReferenceHandler = null;
        options.WriteIndented = true;
        return options;
    }
    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();
}

public static class SerializationModels
{
    public sealed class RegularLessonModel
    {
        public required ImmutableArray<GroupId> Groups { get; set; }
        public required CourseId Course { get; set; }
        public required ImmutableArray<TeacherId> Teachers { get; set; }
        public required RoomId Room { get; set; }
        public required LessonType Type { get; set; }
        public required SubGroup SubGroup { get; set; }
        public required Parity Parity { get; set; }
        public required DayOfWeek DayOfWeek { get; set; }
        public required TimeSlot TimeSlot { get; set; }
        public required PeriodId Period { get; set; }
    }

    public sealed class GroupModel
    {
        public required string Name { get; set; }
        public required Language Language { get; set; }
    }

    public sealed class PersonModel
    {
        public required NameParts<OptionalNamePart> FirstName { get; set; }
        public required NameParts<string?> LastName { get; set; }
    }

    public sealed class CourseModel
    {
        public required ImmutableArray<string> Names { get; set; }
    }

    public sealed class PeriodModel
    {
        public required DateOnly Start { get; set; }
        public required DateOnly? End { get; set; }
    }

    public sealed class ScheduleModel
    {
        public required string Hash { get; set; }
        public required ImmutableArray<RegularLessonModel> RegularLessons { get; set; }
        public required ImmutableArray<GroupModel> Groups { get; set; }
        public required ImmutableArray<PersonModel> Teachers { get; set; }
        public required ImmutableArray<CourseModel> Courses { get; set; }
        public required ImmutableArray<PeriodModel> Periods { get; set; }
    }

    public static ScheduleModel ConvertToSerializationModel(
        Schedule schedule,
        string hash)
    {
        var regularLessons = schedule.RegularLessons.Select(rl => new RegularLessonModel
        {
            Groups = [.. rl.Lesson.Groups],
            Course = rl.Lesson.Course,
            Teachers = rl.Lesson.Teachers,
            Room = rl.Lesson.Room,
            Type = rl.Lesson.Type,
            SubGroup = rl.Lesson.SubGroup,
            Parity = rl.Date.Parity,
            DayOfWeek = rl.Date.DayOfWeek,
            TimeSlot = rl.Date.TimeSlot,
            Period = rl.Date.Period,
        }).ToImmutableArray();

        var groups = schedule.Groups.Select(g => new GroupModel
        {
            Name = g.Name,
            Language = g.Language,
        }).ToImmutableArray();

        var teachers = schedule.Teachers.Select(t => new PersonModel
        {
            FirstName = t.PersonName.FirstName,
            LastName = t.PersonName.LastName,
        }).ToImmutableArray();

        var courses = schedule.Courses.Select(c => new CourseModel
        {
            Names = c.Names,
        }).ToImmutableArray();

        var periods = schedule.Periods.Select(p => new PeriodModel
        {
            Start = p.Start,
            End = p.End,
        }).ToImmutableArray();

        var model = new ScheduleModel
        {
            Hash = hash,
            RegularLessons = regularLessons,
            Groups = groups,
            Teachers = teachers,
            Courses = courses,
            Periods = periods,
        };
        return model;
    }

    // NOTE: the issue is that multiple serialized things can't be merged easily.
    // need to do some id remaps and value merges for this to work.
    public static void ConvertToScheduleBuilder(
        ScheduleBuilder builder,
        ScheduleModel schedule)
    {
        Debug.Assert(builder.Courses.Count == 0);
        foreach (var s in schedule.Courses)
        {
            builder.Courses.List.Add(new()
            {
                Names = s.Names,
            });
        }

        Debug.Assert(builder.Groups.Count == 0);
        foreach (var s in schedule.Groups)
        {
            var g = builder.Group(s.Name);
            g.Ref.Language = s.Language;
        }

        Debug.Assert(builder.Periods.Count == 0);
        foreach (var p in schedule.Periods)
        {
            var per = new PeriodBuilderModel
            {
                Start = p.Start,
            };
            if (p.End is { } e)
            {
                per.EndExclusive = e;
            }

            builder.Periods.List.Add(per);
        }

        Debug.Assert(builder.Teachers.Count == 0);
        foreach (var t in schedule.Teachers)
        {
            var teacher = builder.Teacher(new TeacherBuilderModel.NameModel
            {
                FirstName = t.FirstName,
                LastName = new(t.LastName),
            });
            _ = teacher;
        }

        Debug.Assert(builder.RegularLessons.Count == 0);
        foreach (var rl in schedule.RegularLessons)
        {
            var lesson = builder.RegularLessons.New();
            lesson.Value = new();

            ref var g = ref lesson.Value.General;
            g.Course = rl.Course;
            g.Room = rl.Room;
            g.Teachers = rl.Teachers.ToList();
            g.Type = rl.Type;
            g.Period = rl.Period;

            ref var date = ref lesson.Value.Date;
            date.DayOfWeek = rl.DayOfWeek;
            date.TimeSlot = rl.TimeSlot;
            date.Parity = rl.Parity;

            ref var group = ref lesson.Value.Group;
            group.SubGroup = rl.SubGroup;
            group.Groups = [.. rl.Groups];
        }
    }
}
