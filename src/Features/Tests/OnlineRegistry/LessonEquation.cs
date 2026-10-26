using System.Diagnostics;
using System.Text;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry.Impl;
using ScheduleLib.Parsing.GroupParser;
using DateOnly = System.DateOnly;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class LessonEquationTests
{
    private LessonConfig DefaultLesson
    {
        get
        {
            return new()
            {
                Day = DayOfWeek.Monday,
                Type = LessonType.Lab,
                TimeSlot = new(0),
            };
        }
    }

    [Fact]
    public void SingleExactMatch_ProducesNoCommands()
    {
        var ctx = new TestContext();
        ctx.AddScheduled(DefaultLesson);
        ctx.AddExisting(DefaultLesson);
        var result = ctx.Resolve();
        Assert.Empty(result);
    }

    [Fact]
    public void LessonTypeDifferent_ProducesUpdate()
    {
        var ctx = new TestContext();
        ctx.AddScheduled(DefaultLesson with
        {
            Type = LessonType.Curs,
        });
        ctx.AddExisting(DefaultLesson);
        var result = ctx.Resolve();
        Assert.Collection(result,
            c =>
            {
                Assert.Equal(expected: LessonEquationCommandType.Update, actual: c.Type);
            });
    }

    [Fact]
    public void TimeDifferent_ProducesUpdate()
    {
        var ctx = new TestContext();
        ctx.AddScheduled(DefaultLesson with
        {
            TimeSlot = new(1),
        });
        ctx.AddExisting(DefaultLesson);
        var result = ctx.Resolve();
        Assert.Collection(result,
            c =>
            {
                Assert.Equal(expected: LessonEquationCommandType.Update, actual: c.Type);
            });
    }

    [Fact]
    public void ExtraExisting_ProducesDelete()
    {
        var ctx = new TestContext();
        ctx.AddScheduled(DefaultLesson);
        ctx.AddExisting(DefaultLesson);
        ctx.AddExisting(DefaultLesson with
        {
            TimeSlot = new(1),
        });
        var result = ctx.Resolve();
        Assert.Collection(result,
            c =>
            {
                Assert.Equal(expected: LessonEquationCommandType.Delete, actual: c.Type);
                TimeSlot expected2 = new(1);
                var actual2 = c.Existing.TimeSlot;
                Assert.Equal(expected: expected2, actual: actual2);
            });
    }

    [Fact(Skip =
        "It is not ignored anymore, it counts as a separate type but does not produce changes at a later point")]
    public void LessonTypeNotSet_IgnoredInChecks()
    {
        var ctx = new TestContext();
        ctx.AddScheduled(new()
        {
            Day = DayOfWeek.Monday,
            Type = LessonType.Unspecified,
            TimeSlot = new(0),
        });
        ctx.AddScheduled(new()
        {
            Day = DayOfWeek.Monday,
            Type = LessonType.Lab,
            TimeSlot = new(1),
        });
        ctx.AddExisting(new()
        {
            Day = DayOfWeek.Monday,
            Type = LessonType.Lab,
            TimeSlot = new(0),
        });
        // This one won't be in the updates, because Lab is more specific
        ctx.AddExisting(new()
        {
            Day = DayOfWeek.Monday,
            Type = LessonType.Unspecified,
            TimeSlot = new(1),
        });
        var result = ctx.Resolve();

        var c = Assert.Single(result);
        Assert.Equal(expected: LessonEquationCommandType.Update, actual: c.Type);
        TimeSlot expected2 = new(0);
        var actual2 = c.Existing.TimeSlot;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void MissingLessons_AreAdded()
    {
        var ctx = new TestContext();
        ctx.AddScheduled(DefaultLesson);
        ctx.AddScheduled(DefaultLesson with
        {
            TimeSlot = new(1),
        });
        ctx.AddExisting(DefaultLesson);
        var result = ctx.Resolve();
        Assert.Collection(result,
            c =>
            {
                Assert.Equal(expected: LessonEquationCommandType.Create, actual: c.Type);
            });
    }

    [Fact]
    public void LessonsOnDifferentDays_TreatedSeparately()
    {
        var ctx = new TestContext();
        ctx.AddScheduled(new()
        {
            Day = DayOfWeek.Monday,
        });
        ctx.AddScheduled(new()
        {
            Day = DayOfWeek.Tuesday,
        });
        ctx.AddExisting(new()
        {
            Day = DayOfWeek.Monday,
        });
        var result = ctx.Resolve();

        var c = Assert.Single(result);
        Assert.Equal(expected: LessonEquationCommandType.Create, actual: c.Type);
        Assert.Equal(expected: DayOfWeek.Tuesday, actual: c.All.Day);
    }

    [Fact]
    public void MixedCommandsInOneDay()
    {
        var ctx = new TestContext();
        ctx.AddScheduled(new()
        {
            TimeSlot = new(0),
            Type = LessonType.Lab,
        });
        ctx.AddScheduled(new()
        {
            TimeSlot = new(1),
            Type = LessonType.Lab,
        });
        ctx.AddScheduled(new()
        {
            TimeSlot = new(2),
            Type = LessonType.Lab,
        });

        // Equal
        ctx.AddExisting(new()
        {
            TimeSlot = new(0),
            Type = LessonType.Lab,
        });
        // Different type
        ctx.AddExisting(new()
        {
            TimeSlot = new(1),
            Type = LessonType.Curs,
        });
        // Different time
        ctx.AddExisting(new()
        {
            TimeSlot = new(3),
            Type = LessonType.Lab,
        });
        // Extra one
        ctx.AddExisting(new()
        {
            TimeSlot = new(4),
            Type = LessonType.Lab,
        });

        var result = ctx.Resolve();
        Assert.Collection(result,
            c =>
            {
                Assert.Equal(expected: LessonEquationCommandType.Update, actual: c.Type);
                TimeSlot expected2 = new(1);
                var actual2 = c.Existing.TimeSlot;
                Assert.Equal(expected: expected2, actual: actual2);
                Assert.Equal(expected: LessonType.Curs, actual: c.Existing.Type);
            },
            c =>
            {
                Assert.Equal(expected: LessonEquationCommandType.Update, actual: c.Type);
                TimeSlot expected2 = new(3);
                var actual2 = c.Existing.TimeSlot;
                Assert.Equal(expected: expected2, actual: actual2);
                TimeSlot expected3 = new(2);
                var actual3 = c.All.TimeSlot;
                Assert.Equal(expected: expected3, actual: actual3);
            },
            c =>
            {
                Assert.Equal(expected: LessonEquationCommandType.Delete, actual: c.Type);
                TimeSlot expected2 = new(4);
                var actual2 = c.Existing.TimeSlot;
                Assert.Equal(expected: expected2, actual: actual2);
            });
    }

    [Fact(Skip = "works")]
    public void ContextOk()
    {
        {
            void Check(DayOfWeek day)
            {
                var date = TestContext.GetDate(day);
                var actualDay = date.DayOfWeek;
                Assert.Equal(expected: day, actual: actualDay);
            }

            Check(DayOfWeek.Sunday);
            Check(DayOfWeek.Friday);
            Check(DayOfWeek.Tuesday);
        }
        {
            void Check(DayOfWeek day, TimeSlot ts)
            {
                var date = TestContext.GetDate(day);
                var time = TestContext.TimeConfig.Base.GetTimeSlotInterval(ts).Start;
                var dateTime = new DateTime(date, time);

                var (actualDay, actualTs) = TestContext.ReverseEngineerDateTime(dateTime);
                Assert.Equal(expected: day, actual: actualDay);
                Assert.Equal(expected: ts, actual: actualTs);
            }

            var day2 = DayOfWeek.Sunday;
            TimeSlot ts2 = new(0);
            Check(day2, ts2);
            var day3 = DayOfWeek.Friday;
            TimeSlot ts3 = new(1);
            Check(day3, ts3);
            var day4 = DayOfWeek.Tuesday;
            TimeSlot ts4 = new(2);
            Check(day4, ts4);
        }
    }
}

internal record struct LessonConfig()
{
    public DayOfWeek Day = DayOfWeek.Monday;
    public TimeSlot TimeSlot = new(0);
    public LessonType Type = LessonType.Unspecified;
}

file sealed class TestContext
{
    /// <summary>A weekday and time slot recovered from a concrete date and time.</summary>
    internal readonly record struct TimetableSlot(DayOfWeek Day, TimeSlot Slot);

    private const int Year = 2024;
    public static readonly DefaultLessonTimeConfig TimeConfig = LessonTimeConfig.CreateDefault();
    private readonly List<LessonConfig> _lessonConfigs = new();
    private readonly List<RemoteLessonInstance> _existingLessons = new();

    private static DateOnly BaseDate
    {
        get
        {
            var relativeDay = 10;
            var relativeMonth = 1;
            var date = new DateOnly(year: Year,
                day: relativeDay,
                month: relativeMonth);
            return date;
        }
    }

    public void AddScheduled(LessonConfig config)
    {
        _lessonConfigs.Add(config);
    }

    internal static DateOnly GetDayOfWeek(DateOnly d, DayOfWeek day)
    {
        var weekStart = d.AddDays(-(int) d.DayOfWeek);
        var thisDay = weekStart.AddDays((int) day);
        return thisDay;
    }

    internal static DateOnly GetDate(DayOfWeek day)
    {
        var ret = GetDayOfWeek(BaseDate, day);
        return ret;
    }

    // We only do a single week, so this works.
    internal static TimetableSlot ReverseEngineerDateTime(DateTime dateTime)
    {
        var day = dateTime.DayOfWeek;
        var lessonTimeConfig = TimeConfig.Base;
        var startTime = TimeOnly.FromDateTime(dateTime);
        var maybeTimeSlot = lessonTimeConfig.FindTimeSlotByStartTime(startTime);
        Debug.Assert(maybeTimeSlot is not null);
        var timeSlot = maybeTimeSlot.Value;
        return new(day, timeSlot);
    }

    public void AddExisting(LessonConfig config)
    {
        var date = GetDate(config.Day);
        var time = TimeConfig.Base.GetTimeSlotInterval(config.TimeSlot).Start;
        var l = new RemoteLessonInstance
        {
            DateTime = new DateTime(date: date, time: time),
            LessonType = config.Type,
            EditUri = null!,
            ViewUri = null!,
            Attendance = [],
            Topic = "",
        };
        _existingLessons.Add(l);
    }

    internal record struct ResolvedCommand
    {
        public readonly LessonEquationCommandType Type;
        private LessonConfig _existing;
        private LessonConfig _all;

        public ResolvedCommand(LessonEquationCommandType type)
        {
            Type = type;
        }

        public LessonConfig All
        {
            get
            {
                Debug.Assert(Type.HasAll());
                return _all;
            }
            set
            {
                Debug.Assert(Type.HasAll());
                _all = value;
            }
        }

        public LessonConfig Existing
        {
            get
            {
                Debug.Assert(Type.HasExisting());
                return _existing;
            }
            set
            {
                Debug.Assert(Type.HasExisting());
                _existing = value;
            }
        }

        private readonly bool PrintMembers(StringBuilder sb)
        {
            sb.Append(Type);
            if (Type.HasAll())
            {
                sb.Append(", ");
                sb.Append(_all);
            }
            if (Type.HasExisting())
            {
                sb.Append(", ");
                sb.Append(_existing);
            }
            return true;
        }
    }

    public IEnumerable<ResolvedCommand> Resolve()
    {
        var schedule = ScheduleBuilder.Create(builder =>
        {
            builder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(Year),
            });
            var group = builder.Group("I2401(ru)");
            var course = builder.Course("Test course");
            foreach (var configure in _lessonConfigs)
            {
                builder.RegularLesson(lesson =>
                {
                    lesson.Group(group);
                    lesson.Course(course);
                    lesson.DayOfWeek(configure.Day);
                    lesson.TimeSlot(configure.TimeSlot);
                    lesson.Type(configure.Type);
                });
            }
        });
        var existing = _existingLessons;

        var scheduled = new List<LessonInstance>();
        foreach (var lesson in schedule.EnumerateWeeklyLessons())
        {
            var d = lesson.Date;
            // We only do a single week.
            var date = GetDate(d.DayOfWeek);
            var time = TimeConfig.Base.GetTimeSlotInterval(d.TimeSlot).Start;
            scheduled.Add(new()
            {
                DateTime = new DateTime(date, time),
                LessonId = lesson.Id.AsAny(),
                Attendance = [],
                Topic = "",
                RegistryLessonType = lesson.Lesson.Type,
            });
        }

        var derivationAlgorithm = new SameDayDerivation();
        var sameDayDerivation = derivationAlgorithm;
        GetLessonEquationCommandsParams p = new(schedule,
            existing,
            scheduled);
        var result = sameDayDerivation.DeriveCommands(p);
        return result.Select(x =>
        {
            var ret = new ResolvedCommand(x.Type);
            if (x.HasAll)
            {
                var (day, timeSlot) = ReverseEngineerDateTime(x.Local.DateTime);
                var type = schedule.Get(x.Local.LessonId).Lesson.Type;
                ret.All = new()
                {
                    Day = day,
                    TimeSlot = timeSlot,
                    Type = type,
                };
            }
            if (x.HasExisting)
            {
                var (day, timeSlot) = ReverseEngineerDateTime(x.Remote.DateTime);
                var type = x.Remote.LessonType;
                ret.Existing = new()
                {
                    Day = day,
                    TimeSlot = timeSlot,
                    Type = type,
                };
            }
            return ret;
        });
    }
}
