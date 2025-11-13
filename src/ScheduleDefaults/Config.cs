using System.Diagnostics;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.Lesson.Internal;

namespace ScheduleLib.ScheduleDefaults;

public static class Config
{
    public static CourseNameParserConfig CourseNameParser => new(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#", "Python", "Node.js"],
        IgnoredFullWords = ["p/u", "pentru", "Modele"],
        IgnoredShortenedWords = ["Opț"],
        IgnoredProgrammingRelatedWords = ["Programare", "limbaj"],
        MinUsefulWordLength = 3,
    });

    public static CourseNameUnifierConfig CourseNameUnifier =>
        CourseNameUnifierConfig.Create(CourseNameParser,
        [
            (From: "Dezv. apl. server-side cu Node.js", To: "Node.js"),
        ]);

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
    }

    // TODO: read from image??
    public static SemesterIntervalProvider SemesterIntervalProvider()
    {
        var s = new SemesterIntervalBuilder();
        s.Scope(x =>
        {
            x.AttendanceMode(AttendanceMode.Zi);
            x.QualificationType(QualificationType.Licenta);

            {
                x.Year(2025);
                x.Semester(Semester.Sem1);
                x.LessonsStart(month: 9, day: 1);
                x.LessonsEnd(month: 12, day: 14);
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
                x.LessonsEnd(month: 5, day: 10);
                for (int i = 1; i <= 2; i++)
                {
                    var r = x.Range();
                    r.Grade(new(i));
                }

                x.Range(r =>
                {
                    r.Grade(new(3));
                    r.LessonsStart(month: 2, day: 23);
                    r.LessonsEnd(month: 4, day: 11);
                });
            }
        });
        return s.Build();
    }

    public static StudyWeek[] StudyWeeks
    {
        get
        {
            static StudyWeek Week(int month, int day, bool isOddWeek) =>
                new(monday: new(2025, month, day), isOddWeek: isOddWeek);
            StudyWeek[] studyWeeks =
            [
                Week(month: 9,  day: 1,  isOddWeek: false),
                Week(month: 9,  day: 8,  isOddWeek: true),
                Week(month: 9,  day: 15, isOddWeek: false),
                Week(month: 9,  day: 22, isOddWeek: true),
                Week(month: 9,  day: 29, isOddWeek: false),
                Week(month: 10, day: 6,  isOddWeek: true),
                Week(month: 10, day: 13, isOddWeek: false),
                Week(month: 10, day: 20, isOddWeek: true),
                Week(month: 10, day: 27, isOddWeek: false),
                Week(month: 11, day: 3,  isOddWeek: true),
                Week(month: 11, day: 10, isOddWeek: false),
                Week(month: 11, day: 17, isOddWeek: true),
                Week(month: 11, day: 24, isOddWeek: false),
                Week(month: 12, day: 1,  isOddWeek: true),
                Week(month: 12, day: 8,  isOddWeek: false),
                Week(month: 12, day: 15, isOddWeek: true),
                Week(month: 12, day: 22, isOddWeek: false),
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
            ];
            return holidayPeriods;
        }
    }
}
