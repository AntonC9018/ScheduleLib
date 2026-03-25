using System.Diagnostics;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.Lesson.Internal;

namespace ScheduleLib.ScheduleDefaults;

public static class Config
{
    public static CourseNameParserConfig CourseNameParser => new(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#", "Python", "Node.js", "PHP"],
        IgnoredFullWords = ["p/u", "pentru", "Modele", "jocuri"],
        IgnoredShortenedWords = ["Opț"],
        IgnoredProgrammingRelatedWords = ["Programare", "limbaj"],
        MinUsefulWordLength = 3,
    });

    public static ReadOnlySpan<(string From, string To)> CourseNameUnificationConfig => new[]
    {
        (From: "Dezv. apl. server-side cu Node.js", To: "Node.js"),
        (From: "HTML", To: "HTML și CSS"),
        (From: "Modele design soft", To: "Design Soft"),
        (From: "Python pentru aplicații", To: "Python"),
        (From: "PHP", To: "Dezvoltare WEB avansată cu PHP"),
        (From: "Dezvoltare WEB avansată", To: "Dezvoltare WEB avansată cu PHP"),
        (From: "Dezvoltare WEB PHP", To: "Dezvoltare WEB avansată cu PHP"),
    };

    public static CourseNameUnifierConfig CourseNameUnifier =>
        CourseNameUnifierConfig.Create(CourseNameParser, CourseNameUnificationConfig);

    public static WhiteSpaceResult WhiteSpaceActionCourseName(WhiteSpaceContext c)
    {
        Debug.Assert(!c.Lexer.IsEmpty);
        {
            var t = c.Lexer.Current;
            if (!t.IsAnyWord())
            {
                return c.DefaultAll();
            }

            var span = t.Value.Span;
            if (!span.StartsWith("Node"))
            {
                return c.DefaultAll();
            }
            c.Lexer.Move();
            if (c.Lexer.IsEmpty)
            {
                return c.DefaultAll();
            }
        }

        c.Lexer.TryConsume(TokenType.Whitespace);
        if (c.Lexer.IsEmpty)
        {
            return c.DefaultAll();
        }

        {
            var t = c.Lexer.Current;
            if (t.Type == LessonTokenType.Word
                && t.Value.Span.Equals("JS", StringComparison.OrdinalIgnoreCase))
            {
                return c.DontInsertAll(inclusive: false);
            }
        }

        return c.DefaultAll();
    }

    public static void ConfigureRemappings(Remappings remap)
    {
        var teach = remap.TeacherLastNameRemappings;
        teach.Add("Curmanschi", "Curmanschii");
        teach.Add("Vișnevschi", "Vișnevschii");
        teach.Add("Băț", "Beț");
        teach.Add("Spincean", "Sprîncean");
        teach.Add("Anghelov", "Anghelova");
        teach.Add("Iațîșina", "Iațâșina");

        var subgroup = remap.SubGroupNameRemappings;
        subgroup.Add(new("GR"), new("GA2D"));
        subgroup.Add(new("Node"), new("UI"));

        for (int i = 1; i <= 10; i++)
        {
            var from = $"{(char)('a' + i - 1)}";
            var to = NumberHelper.ToRoman(i);
            subgroup.Add(from, to);
        }
    }

    // TODO: read from image??
    public static CurrentYearSemesterIntervalProvider SemesterIntervalProvider()
    {
        var s = new CurrentYearSemesterIntervalBuilder();
        s.Scope(x =>
        {
            x.AttendanceMode(AttendanceMode.Zi);
            x.QualificationType(QualificationType.Licenta);

            {
                x.Year(2025);
                x.Semester(Semester.Sem1);
                x.LessonsStart(month: 9, day: 1);
                x.LessonsEndInclusive(month: 12, day: 14);
                for (int i = 1; i <= 3; i++)
                {
                    var r = x.Range();
                    r.Grade(new(i));
                }
            }
            {
                x.Year(2026);
                x.Semester(Semester.Sem2);

                x.LessonsStart(month: 2, day: 2);
                x.LessonsEndInclusive(month: 5, day: 10);
                for (int i = 1; i <= 2; i++)
                {
                    var r = x.Range();
                    r.Grade(new(i));
                }

                x.Range(r =>
                {
                    r.Grade(new(3));
                    r.LessonsStart(month: 2, day: 23);
                    r.LessonsEndInclusive(month: 4, day: 11);
                });
            }
        });

        s.Scope(x =>
        {
            x.AttendanceMode(AttendanceMode.FrecventaRedusa);
            x.QualificationType(QualificationType.Licenta);

            // TODO: The semester data on the site is a lie. It doesn't follow this model at all.
            x.LessonsStart(new DateOnly(year: 2025, month: 9, day: 1));
            x.LessonsEndInclusive(new DateOnly(year: 2026, month: 8, day: 31));

            foreach (var sem in new EnumMembers<Semester>())
            {
                x.Semester(sem);
                for (int i = 1; i <= 3; i++)
                {
                    var r = x.Range();
                    r.Grade(new(i));
                }
            }
        });
        return s.Build();
    }

    public static StudyWeek[] StudyWeeks
    {
        get
        {
            static StudyWeek Week(int month, int day, bool isOddWeek) =>
                new(monday: new(2026, month, day), isOddWeek: isOddWeek);

            StudyWeek[] studyWeeks =
            [
                Week(month: 2, day: 2,  isOddWeek: false),
                Week(month: 2, day: 9,  isOddWeek: true),
                Week(month: 2, day: 16, isOddWeek: false),
                Week(month: 2, day: 23, isOddWeek: true),
                Week(month: 3, day: 2,  isOddWeek: false),
                Week(month: 3, day: 9,  isOddWeek: true),
                Week(month: 3, day: 16, isOddWeek: false),
                Week(month: 3, day: 23, isOddWeek: true),
                Week(month: 3, day: 30, isOddWeek: false),
                Week(month: 4, day: 6,  isOddWeek: true),
                Week(month: 4, day: 13, isOddWeek: false),
                Week(month: 4, day: 20, isOddWeek: true),
                Week(month: 4, day: 27, isOddWeek: false),
                Week(month: 5, day: 4,  isOddWeek: true),
                Week(month: 5, day: 11, isOddWeek: false),
                Week(month: 5, day: 18, isOddWeek: true),
            ];

            return studyWeeks;
        }
    }

    public static HolidayPeriod[] HolidayPeriods
    {
        get
        {
            HolidayPeriod[] holidayPeriods;
            // TODO: Get this from "calendar academic"
            holidayPeriods = [
                new(new(2025, month: 10, day: 14)),
                new(new(2026, month: 1, day: 1), new(2026, month: 1, day: 26)),
                new(new(2026, month: 4, day: 12), new(2026, month: 4, day: 21)),
                new(new(2026, month: 5, day: 1)),
                new(new(2026, month: 5, day: 9)),
            ];
            return holidayPeriods;
        }
    }
}

