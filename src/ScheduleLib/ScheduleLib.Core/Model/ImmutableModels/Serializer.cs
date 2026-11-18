using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ScheduleLib.Builders;
using ScheduleLib.Helper.JsonConverters;
using ScheduleLib.JsonConverters;

namespace ScheduleLib;

public static class ScheduleSerializer
{
    /// <summary>Creates a reusable serialization operation. Configure Input and Output
    /// before execution; Hash is optional and defaults to empty.</summary>
    public static ScheduleSerializationBuilder Serialize() => new();

    internal static Task SerializeModelAsync(
        SerializationModels.ScheduleModel model,
        Stream output,
        CancellationToken cancellationToken)
    {
        return JsonSerializer.SerializeAsync(
            output,
            model,
            SerializerOptions,
            cancellationToken);
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
        SerializationModels.ScheduleModel schedule)
    {
        SerializationModels.ConvertToScheduleBuilder(builder, schedule);

        if (builder.LookupModule is not null)
        {
            builder.RefreshLookup();
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
        options.Converters.Add(new JsonStringEnumConverter<LessonType>());
        options.Converters.Add(new JsonStringEnumConverter<DayOfWeek>());
        options.Converters.Add(new JsonStringEnumConverter<Parity>());
        options.Converters.Add(new JsonStringEnumConverter<QualificationType>());
        options.Converters.Add(new JsonStringEnumConverter<AttendanceMode>());
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
    public abstract class LessonBaseModel
    {
        [JsonRequired] public ImmutableArray<GroupId> Groups { get; set; }
        [JsonRequired] public CourseId Course { get; set; }
        [JsonRequired] public ImmutableArray<TeacherId> Teachers { get; set; }
        [JsonRequired] public RoomId Room { get; set; }
        [JsonRequired] public LessonType Type { get; set; }
        [JsonRequired] public SubGroup SubGroup { get; set; }

        [JsonRequired] public Specialization Specialization { get; set; }

        // Not required: caches written before alternatives existed deserialize with All.
        public Alternative Alternative { get; set; }

        public void SetCommon(in LessonData common)
        {
            Groups = [.. common.Groups];
            Course = common.Course;
            Teachers = common.Teachers;
            Room = common.Room;
            Type = common.Type;
            SubGroup = common.SubGroup;
            Specialization = common.Specialization;
            Alternative = common.Alternative;
        }
    }

    public sealed class OneTimeLessonModel : LessonBaseModel
    {
        public required DateOnly Date { get; set; }
        public required TimeSlot TimeSlot { get; set; }
    }

    public sealed class WeeklyLessonModel : LessonBaseModel
    {
        public required DayOfWeek DayOfWeek { get; set; }
        public required Parity Parity { get; set; }
        public required TimeSlot TimeSlot { get; set; }
        public required PeriodId Period { get; set; }
    }

    /// <summary>Every field of a built group, so restoring it never parses or remaps the name.</summary>
    public sealed class GroupModel
    {
        public required string Name { get; set; }
        public required Language Language { get; set; }
        public required Grade Grade { get; set; }
        public required int GroupNumber { get; set; }
        public required QualificationType QualificationType { get; set; }
        public required Faculty Faculty { get; set; }
        public required AttendanceMode AttendanceMode { get; set; }
        public required Specialty Specialty { get; set; }

        public static GroupModel From(Group group)
        {
            return new()
            {
                Name = group.Name,
                Language = group.Language,
                Grade = group.Grade,
                GroupNumber = group.GroupNumber,
                QualificationType = group.QualificationType,
                Faculty = group.Faculty,
                AttendanceMode = group.AttendanceMode,
                Specialty = group.Specialty,
            };
        }

        public GroupBuilderModel ToBuilderModel()
        {
            return new()
            {
                Name = Name,
                Language = Language,
                Grade = Grade,
                GroupNumber = GroupNumber,
                QualificationType = QualificationType,
                Faculty = Faculty,
                AttendanceMode = AttendanceMode,
                Specialty = Specialty,
            };
        }
    }

    /// <summary>A teacher as built, so restoring it never remaps or merges the name.</summary>
    public sealed class PersonModel
    {
        public required NameParts<OptionalNamePart> FirstName { get; set; }
        public required NameParts<string?> LastName { get; set; }
        public required ContactsModel Contacts { get; set; }

        public static PersonModel From(Teacher teacher)
        {
            return new()
            {
                FirstName = teacher.PersonName.FirstName,
                LastName = teacher.PersonName.LastName.Parts,
                Contacts = ContactsModel.From(teacher.Contacts),
            };
        }
    }

    public sealed class ContactsModel
    {
        public required string? PersonalEmail { get; set; }
        public required string? CorporateEmail { get; set; }
        public required string? PhoneNumber { get; set; }

        public static ContactsModel From(PersonContacts contacts)
        {
            return new()
            {
                PersonalEmail = contacts.PersonalEmail,
                CorporateEmail = contacts.CorporateEmail,
                PhoneNumber = contacts.PhoneNumber,
            };
        }

        public PersonContacts ToPersonContacts()
        {
            return new(
                personalEmail: PersonalEmail,
                corporateEmail: CorporateEmail,
                phoneNumber: PhoneNumber);
        }

        public TeacherBuilderModel.ContactsModel ToBuilderModel()
        {
            return new()
            {
                PersonalEmail = PersonalEmail,
                CorporateEmail = CorporateEmail,
                PhoneNumber = PhoneNumber,
            };
        }
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
        public string? Hash { get; set; }
        public required ImmutableArray<WeeklyLessonModel> RegularLessons { get; set; }
        public required ImmutableArray<OneTimeLessonModel> OneTimeLessons { get; set; }
        public required ImmutableArray<GroupModel> Groups { get; set; }
        public required ImmutableArray<PersonModel> Teachers { get; set; }
        public required ImmutableArray<CourseModel> Courses { get; set; }
        public required ImmutableArray<PeriodModel> Periods { get; set; }
    }

    public static ScheduleModel ConvertToSerializationModel(
        Schedule schedule,
        string hash)
    {
        var regularLessons = schedule.EnumerateWeeklyLessons()
            .Select(wl =>
            {
                var ret = new WeeklyLessonModel
                {
                    Parity = wl.Date.Parity,
                    DayOfWeek = wl.Date.DayOfWeek,
                    TimeSlot = wl.Date.TimeSlot,
                    Period = wl.Date.Period,
                };
                ret.SetCommon(wl.Lesson);
                return ret;
            })
            .ToImmutableArray();
        var oneTimeLessons = schedule.EnumerateOneTimeLessons()
            .Select(ol =>
            {
                var ret = new OneTimeLessonModel
                {
                    Date = ol.Date.Date,
                    TimeSlot = ol.Date.TimeSlot,
                };
                ret.SetCommon(ol.Lesson);
                return ret;
            })
            .ToImmutableArray();

        var groups = schedule.Groups.Select(GroupModel.From)
            .ToImmutableArray();
        var teachers = schedule.Teachers.Select(PersonModel.From)
            .ToImmutableArray();

        var courses = schedule.Courses.Select(c => new CourseModel
        {
            Names = c.Names,
        })
            .ToImmutableArray();

        var periods = schedule.Periods.Select(p => new PeriodModel
        {
            Start = p.Start,
            End = p.End,
        })
            .ToImmutableArray();

        var model = new ScheduleModel
        {
            Hash = hash,
            OneTimeLessons = oneTimeLessons,
            RegularLessons = regularLessons,
            Groups = groups,
            Teachers = teachers,
            Courses = courses,
            Periods = periods,
        };
        return model;
    }

    /// <summary>
    /// Restores a built schedule without building it again. Every value is restored as saved:
    /// names are not parsed, remapped or merged again.
    /// </summary>
    public static Schedule ConvertToSchedule(ScheduleModel schedule)
    {
        ImmutableArray<WeeklyLesson> weeklyLessons = [.. schedule.RegularLessons.Select(ToWeeklyLesson)];
        ImmutableArray<OneTimeLesson> oneTimeLessons = [.. schedule.OneTimeLessons.Select(ToOneTimeLesson)];
        ImmutableArray<Group> groups = [.. schedule.Groups.Select(ToGroup)];
        ImmutableArray<Teacher> teachers = [.. schedule.Teachers.Select(ToTeacher)];
        ImmutableArray<Course> courses = [.. schedule.Courses.Select(ToCourse)];
        ImmutableArray<Period> periods = [.. schedule.Periods.Select(ToPeriod)];
        return new Schedule(
            weeklyLessons: weeklyLessons,
            oneTimeLessons: oneTimeLessons,
            groups: groups,
            teachers: teachers,
            courses: courses,
            periods: periods);

        static Group ToGroup(GroupModel model)
        {
            return model.ToBuilderModel()
                .Build();
        }

        static WeeklyLesson ToWeeklyLesson(WeeklyLessonModel model)
        {
            var lessonBase = ToLessonBase(model);
            var date = new WeeklyLessonDate(
                dayOfWeek: model.DayOfWeek,
                timeSlot: model.TimeSlot,
                period: model.Period,
                parity: model.Parity);
            return new(lessonBase, date);
        }

        static OneTimeLesson ToOneTimeLesson(OneTimeLessonModel model)
        {
            var lessonBase = ToLessonBase(model);
            var date = new OneTimeLessonDate(model.Date, model.TimeSlot);
            return new(lessonBase, date);
        }

        static LessonBase ToLessonBase(LessonBaseModel model)
        {
            LessonGroups groups = [.. model.Groups];
            var data = new LessonData(
                groups: groups,
                course: model.Course,
                teachers: model.Teachers,
                room: model.Room,
                type: model.Type,
                subGroup: model.SubGroup,
                specialization: model.Specialization,
                alternative: model.Alternative);
            return new(data);
        }

        static Teacher ToTeacher(PersonModel model)
        {
            LastName lastName = new(model.LastName);
            var personName = new PersonName(model.FirstName, lastName);
            var contacts = model.Contacts.ToPersonContacts();
            return new(personName, contacts);
        }

        static Course ToCourse(CourseModel model)
        {
            return new(model.Names);
        }

        static Period ToPeriod(PeriodModel model)
        {
            return new(start: model.Start, end: model.End ?? default);
        }
    }

    // NOTE: the issue is that multiple serialized things can't be merged easily.
    // need to do some id remaps and value merges for this to work.
    /// <summary>
    /// Fills an empty builder with a saved schedule. Like the courses, groups and teachers are
    /// added as saved, without parsing, remapping or merging their names, so ids stay positions.
    /// </summary>
    public static void ConvertToScheduleBuilder(
        ScheduleBuilder builder,
        ScheduleModel schedule)
    {
        Debug.Assert(builder.Courses.Count == 0);
        foreach (var s in schedule.Courses)
        {
            builder.Courses.List.Add(new(s.Names));
        }

        Debug.Assert(builder.Groups.Count == 0);
        foreach (var s in schedule.Groups)
        {
            builder.Groups.List.Add(s.ToBuilderModel());
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
            builder.Teachers.List.Add(new()
            {
                Name = new()
                {
                    FirstName = t.FirstName,
                    LastName = new(t.LastName),
                },
                Contacts = t.Contacts.ToBuilderModel(),
            });
        }

        Debug.Assert(builder.WeeklyLessons.Count == 0);
        foreach (var rl in schedule.RegularLessons)
        {
            var lesson = builder.WeeklyLessons.New();
            lesson.Value = new();

            ref var date = ref lesson.Value.Date;
            date.DayOfWeek = rl.DayOfWeek;
            date.TimeSlot = rl.TimeSlot;
            date.Parity = rl.Parity;
            lesson.Value.General.Period = rl.Period;
            CopyBase(ref lesson.Value.Base, rl);
        }

        Debug.Assert(builder.OneTimeLessons.Count == 0);
        foreach (var ol in schedule.OneTimeLessons)
        {
            var lesson = builder.OneTimeLessons.New();
            lesson.Value = new();

            ref var date = ref lesson.Value.Date;
            date.Date = ol.Date;
            date.TimeSlot = ol.TimeSlot;
            CopyBase(ref lesson.Value.Base, ol);
        }

        void CopyBase(ref LessonBuilderModelDataBase b, LessonBaseModel model)
        {
            ref var g = ref b.General;
            g.Course = model.Course;
            g.Room = model.Room;
            g.Teachers = model.Teachers.ToList();
            g.Type = model.Type;

            ref var group = ref b.Group;
            group.SubGroup = model.SubGroup;
            group.Specialization = model.Specialization;
            group.Alternative = model.Alternative;
            group.Groups = [.. model.Groups];
        }
    }
}
