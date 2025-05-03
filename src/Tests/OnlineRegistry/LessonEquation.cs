using System.Diagnostics;
using System.Text;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;
using DateOnly = System.DateOnly;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class LessonEquationTests
{
    private LessonConfig DefaultLesson => new()
    {
        Day = DayOfWeek.Monday,
        Type = LessonType.Lab,
        TimeSlot = new(0),
    };

    [Fact]
    public void SingleExactMatch_ProducesNoCommands()
    {
        var ctx = new Context();
        ctx.AddScheduled(DefaultLesson);
        ctx.AddExisting(DefaultLesson);
        var result = ctx.Resolve();
        Assert.Empty(result);
    }

    [Fact]
    public void LessonTypeDifferent_ProducesUpdate()
    {
        var ctx = new Context();
        ctx.AddScheduled(DefaultLesson with
        {
            Type = LessonType.Curs,
        });
        ctx.AddExisting(DefaultLesson);
        var result = ctx.Resolve();
        Assert.Collection(result,
            c =>
            {
                Assert.Equal(LessonEquationCommandType.Update, c.Type);
            });
    }

    [Fact]
    public void TimeDifferent_ProducesUpdate()
    {
        var ctx = new Context();
        ctx.AddScheduled(DefaultLesson with
        {
            TimeSlot = new(1),
        });
        ctx.AddExisting(DefaultLesson);
        var result = ctx.Resolve();
        Assert.Collection(result,
            c =>
            {
                Assert.Equal(LessonEquationCommandType.Update, c.Type);
            });
    }

    [Fact]
    public void ExtraExisting_ProducesDelete()
    {
        var ctx = new Context();
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
                Assert.Equal(LessonEquationCommandType.Delete, c.Type);
                Assert.Equal(new(1), c.Existing.TimeSlot);
            });
    }

    [Fact]
    public void LessonTypeNotSet_IgnoredInChecks()
    {
        var ctx = new Context();
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
        Assert.Equal(LessonEquationCommandType.Update, c.Type);
        Assert.Equal(new(0), c.Existing.TimeSlot);
    }

    [Fact]
    public void MissingLessons_AreAdded()
    {
        var ctx = new Context();
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
                Assert.Equal(LessonEquationCommandType.Create, c.Type);
            });
    }

    [Fact]
    public void LessonsOnDifferentDays_TreatedSeparately()
    {
        var ctx = new Context();
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
        Assert.Equal(LessonEquationCommandType.Create, c.Type);
        Assert.Equal(DayOfWeek.Tuesday, c.All.Day);
    }

    [Fact]
    public void MixedCommandsInOneDay()
    {
        var ctx = new Context();
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
                Assert.Equal(LessonEquationCommandType.Update, c.Type);
                Assert.Equal(new(1), c.Existing.TimeSlot);
                Assert.Equal(LessonType.Curs, c.Existing.Type);
            },
            c =>
            {
                Assert.Equal(LessonEquationCommandType.Update, c.Type);
                Assert.Equal(new(3), c.Existing.TimeSlot);
                Assert.Equal(new(2), c.All.TimeSlot);
            },
            c =>
            {
                Assert.Equal(LessonEquationCommandType.Delete, c.Type);
                Assert.Equal(new(4), c.Existing.TimeSlot);
            });
    }

    [Fact(Skip = "works")]
    public void ContextOk()
    {
        {
            void Check(DayOfWeek day)
            {
                var date = Context.GetDate(day);
                var actualDay = date.DayOfWeek;
                Assert.Equal(day, actualDay);
            }

            Check(DayOfWeek.Sunday);
            Check(DayOfWeek.Friday);
            Check(DayOfWeek.Tuesday);
        }
        {
            void Check(DayOfWeek day, TimeSlot ts)
            {
                var date = Context.GetDate(day);
                var time = Context.TimeConfig.Base.GetTimeSlotInterval(ts).Start;
                var dateTime = new DateTime(date, time);

                var (actualDay, actualTs) = Context.ReverseEngineerDateTime(dateTime);
                Assert.Equal(day, actualDay);
                Assert.Equal(ts, actualTs);
            }

            Check(DayOfWeek.Sunday, new(0));
            Check(DayOfWeek.Friday, new(1));
            Check(DayOfWeek.Tuesday, new(2));
        }
    }
}

internal record struct LessonConfig()
{
    public DayOfWeek Day = DayOfWeek.Monday;
    public TimeSlot TimeSlot = new(0);
    public LessonType Type = LessonType.Unspecified;
}

file sealed class Context
{
    private const int Year = 2024;
    public static readonly DefaultLessonTimeConfig TimeConfig = LessonTimeConfig.CreateDefault();
    private readonly List<LessonConfig> _lessonConfigs = new();
    private readonly List<LessonInstanceLink> _existingLessons = new();
    private static DateOnly BaseDate
    {
        get
        {
            var relativeDay = 10;
            var relativeMonth = 1;
            var date = new DateOnly(year: Year, day: relativeDay, month: relativeMonth);
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
    internal static (DayOfWeek, TimeSlot) ReverseEngineerDateTime(DateTime dateTime)
    {
        var day = dateTime.DayOfWeek;
        var maybeTimeSlot = TimeConfig.Base.FindTimeSlotByStartTime(TimeOnly.FromDateTime(dateTime));
        Debug.Assert(maybeTimeSlot is not null);
        var timeSlot = maybeTimeSlot.Value;
        return (day, timeSlot);
    }

    public void AddExisting(LessonConfig config)
    {
        var date = GetDate(config.Day);
        var time = TimeConfig.Base.GetTimeSlotInterval(config.TimeSlot).Start;
        var l = new LessonInstanceLink
        {
            DateTime = new DateTime(date: date, time: time),
            LessonType = config.Type,
            EditUri = null!,
            ViewUri = null!,
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
                CurrentStudyYear = Year,
            });
            var group = builder.Group("I2401(ru)");
            var course = builder.Course("Test course");
            var scope = builder.Scope(s =>
            {
                s.Group(group);
                s.Course(course);
            });
            foreach (var configure in _lessonConfigs)
            {
                scope.RegularLesson(lesson =>
                {
                    lesson.DayOfWeek(configure.Day);
                    lesson.TimeSlot(configure.TimeSlot);
                    lesson.Type(configure.Type);
                });
            }
        });
        var existing = _existingLessons;

        var scheduled = new List<LessonInstance>();
        for (int lessonId = 0; lessonId < schedule.RegularLessons.Length; lessonId++)
        {
            var lesson = schedule.RegularLessons[lessonId];
            var d = lesson.Date;
            // We only do a single week.
            var date = GetDate(d.DayOfWeek);
            var time = TimeConfig.Base.GetTimeSlotInterval(d.TimeSlot).Start;
            scheduled.Add(new()
            {
                DateTime = new DateTime(date, time),
                LessonId = new(lessonId),
            });
        }

        var lists = new MatchingLists();
        var result = MissingLessonDetection.GetLessonEquationCommands(new()
        {
            Lists = lists,
            Schedule = schedule,
            AllLessons = scheduled,
            ExistingLessons = existing,
        });
        return result.Select(x =>
        {
            var ret = new ResolvedCommand(x.Type);
            if (x.HasAll)
            {
                var (day, timeSlot) = ReverseEngineerDateTime(x.All.DateTime);
                var type = schedule.Get(x.All.LessonId).Lesson.Type;
                ret.All = new()
                {
                    Day = day,
                    TimeSlot = timeSlot,
                    Type = type,
                };
            }
            if (x.HasExisting)
            {
                var (day, timeSlot) = ReverseEngineerDateTime(x.Existing.DateTime);
                var type = x.Existing.LessonType;
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
