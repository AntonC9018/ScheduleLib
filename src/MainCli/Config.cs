using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.CourseName;

namespace MainCli;

public static class Config
{
    public static CourseNameParserConfig CourseNameParser => new(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#", "Python"],
        IgnoredFullWords = ["p/u", "pentru", "Modele"],
        IgnoredShortenedWords = ["Opț"],
        IgnoredProgrammingRelatedWords = ["Programare", "limbaj"],
        MinUsefulWordLength = 3,
    });

    public static void ConfigureRemappings(Remappings remap)
    {
        var teach = remap.TeacherLastNameRemappings;
        teach.Add("Curmanschi", "Curmanschii");
        teach.Add("Vișnevschi", "Vișnevschii");
        teach.Add("Băț", "Beț");
        teach.Add("Spincean", "Sprîncean");
        teach.Add("Anghelov", "Anghelova");
    }

    // TODO: read from image??
    internal static SemesterIntervalProvider SemesterIntervalProvider()
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

    internal static StudyWeek[] StudyWeeks
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

    internal static HolidayPeriod[] HolidayPeriods
    {
        get
        {
            HolidayPeriod[] holidayPeriods;
            // TODO: Get this from "calendar academic"
            holidayPeriods = [
                new(new(2025, month: 10, day: 16)),
                new(new(2026, month: 1, day: 1), new(2026, month: 1, day: 26)),
                new(new(2026, month: 4, day: 12), new(2026, month: 4, day: 21)),
            ];
            return holidayPeriods;
        }
    }
}
