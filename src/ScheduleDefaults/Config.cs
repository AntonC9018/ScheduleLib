using System.Collections.ObjectModel;
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
        (From: "Dezvoltare WEB avansată cu PHP", To: "PHP"),
        (From: "Dezvoltare WEB avansată", To: "PHP"),
        (From: "Dezvoltare WEB PHP", To: "PHP"),
        (From: "DA S-S", To: "Server-side"),
        (From: "Dezvoltarea de aplicații server-side", To: "Server-side"),
        // "UI/UX" is mis-tokenized into "U X" by one of the docs.
        (From: "Designul UI/U X", To: "Designul UI/UX"),
    };

    public static CourseNameUnifierConfig CourseNameUnifier =>
        CourseNameUnifierConfig.Create(CourseNameParser, CourseNameUnificationConfig);

    /// <summary>
    /// The courses whose lessons belong to a specialization even though the imported docs
    /// stopped carrying the labels. Scopes carry the study year, so rebuilding an older
    /// semester applies only that year's entries.
    /// </summary>
    public static ImplicitSplitConfig ImplicitSplitConfig { get; } = CreateImplicitSplitConfig();

    private static ImplicitSplitConfig CreateImplicitSplitConfig()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.StudyYear = new(2026);
            x.Grade = new(3);
            x.Faculty = new("IA");
            x.AttendanceMode = AttendanceMode.Zi;
            x.Qualification = QualificationType.Licenta;
            x.Specialization("Realitate virtuală și augmentată", Specializations.DJ);
            x.Specialization("Design audio și efecte vizuale", Specializations.DJ);
            x.Specialization("Fotogrametrie și scanare 3D", Specializations.DJ);
            x.Specialization("Server-side", Specializations.DezvoltareaAplicatiilor);
            x.Specialization("Dezvoltarea aplicațiilor mobile", Specializations.DezvoltareaAplicatiilor);
            x.Specialization("Securitatea aplicațiilor enterprise", Specializations.DezvoltareaAplicatiilor);
            x.Specialization("Securitatea aplicațiilor web și mobile", Specializations.DezvoltareaAplicatiilor);
        });
        builder.Scope(x =>
        {
            x.StudyYear = new(2026);
            x.Grade = new(2);
            x.Faculty = new("IA");
            x.AttendanceMode = AttendanceMode.Zi;
            x.Qualification = QualificationType.Licenta;
            x.Specialization("Grafică și animație 2D", Specializations.GA2D);
            x.Specialization("Designul UI/UX", Specializations.UI);
        });
        // Elective stacks students choose between. One scope covers every
        // faculty and attendance mode of the year because the joint elective
        // lessons span I, M, IA and DJ groups, including Dual ones. Both
        // cache variants of the same human course (the long "Disciplină
        // umanistică opțională: ..." form and the short form) point at the
        // same alternative.
        builder.Scope(x =>
        {
            x.StudyYear = new(2026);
            x.Grade = new(2);
            x.Qualification = QualificationType.Licenta;
            x.Alternative("Disciplină umanistică opțională: Antreprenoriat inovativ", new("Antreprenoriat inovativ"));
            x.Alternative("Antreprenoriat inovativ", new("Antreprenoriat inovativ"));
            x.Alternative("Disciplină umanistică opțională: Psihologie", new("Psihologie"));
            x.Alternative("Psihologie", new("Psihologie"));
            x.Alternative("Opț. ped.", new("Opț. ped."));
            x.Alternative("Cult. comun.", new("Cult. comun."));
        });
        return builder.Build();
    }

    /// <summary>
    /// Tolerated lesson overlaps. The elective entries are a permanent safety net
    /// for alternative lessons whose groups fall outside every implicit-split
    /// scope; the data-bug and language-block entries are temporary and are
    /// removed as the source docs get fixed.
    /// </summary>
    public static LessonOverlapValidationConfig OverlapValidationConfig { get; } = new()
    {
        StudyYear = new(2026),
        Allowlist =
        [
            // Elective courses students choose between: parallel alternatives sharing
            // slots by design. Separated by the grade-2 Alternative assignments
            // above; the entries stay as a safety net for lessons whose groups
            // fall outside every scope.
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Disciplină umanistică opțională: Antreprenoriat inovativ",
                CourseB = "Disciplină umanistică opțională: Psihologie",
                Reason = "Elective alternatives (Antreprenoriat vs Psihologie), separated by Alternative assignments.",
            },
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Opț. ped.",
                CourseB = "Disciplină umanistică opțională: Antreprenoriat inovativ",
                Reason = "Elective alternatives, separated by Alternative assignments.",
            },
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Opț. ped.",
                CourseB = "Disciplină umanistică opțională: Psihologie",
                Reason = "Elective alternatives, separated by Alternative assignments.",
            },
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Opț. ped.",
                CourseB = "Antreprenoriat inovativ",
                GroupName = "I2502",
                Reason = "Elective alternatives, separated by Alternative assignments.",
            },
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Opț. ped.",
                CourseB = "Psihologie",
                GroupName = "I2502",
                Reason = "Elective alternatives, separated by Alternative assignments.",
            },
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Cult. comun.",
                CourseB = "Psihologie",
                Reason = "DJ-group elective alternatives, separated by Alternative assignments.",
            },
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Baze de date",
                CourseB = "Tehnologii de programare",
                GroupName = "I2502",
                Day = DayOfWeek.Friday,
                Reason = "Data bug: two regular I2502 courses share the Friday slot in the An-II doc.",
            },
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Limba rom.",
                CourseB = "Limba straina",
                Reason = "An-I doc language block: the odd-week Limba rom. lesson meets the every-week straina lesson.",
            },
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Limba straina",
                CourseB = "Educația fizică",
                Reason = "An-I doc language block: the every-week straina lesson meets the even-week Educatia fizica lesson.",
            },
        ],
    };

    public static ReadOnlySet<string> GroupLabelsThatAreMaster => ["IASD"];

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
        teach.Add("Curmanscii", "Curmanschii");
        teach.Add("Curmansci", "Curmanschii");
        teach.Add("Vișnevschi", "Vișnevschii");
        teach.Add("Băț", "Beț");
        teach.Add("Spincean", "Sprîncean");
        teach.Add("Anghelov", "Anghelova");
        teach.Add("Iațîșina", "Iațâșina");
        teach.Add("Jelihovskii", "Jelihovschii");
        teach.Add("Jelihovschi", "Jelihovschii");
        teach.Add("Vișnevshi", "Vișnevschii");

        remap.TeacherFullNameRemappings.Add((ref x) =>
        {
            bool EqualFirstPart(ref TeacherBuilderModel.NameModel x, string first, string last)
            {
                return (x.FirstName.Longer()[0] is { } l
                    && new Word(l).Span.IsEitherShortForOther(new Word(first).Span.Shortened)
                    && IgnoreDiacriticsAndCaseComparer.Instance.Equals(x.LastName[0], last));
            }

            if (EqualFirstPart(ref x, "Maria", "Cristei"))
            {
                x = default;
                x.FirstName[0].Full = "Maria";
                x.FirstName[0].Short = "M";
                x.LastName[0] = "Marin";
                return true;
            }
            if (EqualFirstPart(ref x, "Gabriel", "Stănescu"))
            {
                x = default;
                x.FirstName[0].Full = "Gabriel";
                x.FirstName[0].Short = "G";
                x.FirstName[1].Full = "Cătălin";
                x.FirstName[1].Short = "C";
                x.LastName[0] = "Stănescu";
                return true;
            }
            if (EqualFirstPart(ref x, "Eva", "Arseni")
                || EqualFirstPart(ref x, "Mădălina", "Arseni"))
            {
                x = default;
                x.FirstName[0].Full = "Eva";
                x.FirstName[1].Short = "E";
                x.FirstName[1].Full = "Mădălina";
                x.FirstName[1].Short = "M";
                x.LastName[0] = "Arseni";
                return true;
            }
            if (EqualFirstPart(ref x, "Anatol", "Gladei"))
            {
                x = default;
                x.FirstName[0].Full = "Anatolie";
                x.FirstName[0].Short = "A";
                x.LastName[0] = "Gladei";
                return true;
            }
            return false;
        });

        var subgroup = remap.SubGroupNameRemappings;
        subgroup.Add(new("AG"), Specializations.AlgoritmicaGrafurilor.Value!);
        subgroup.Add(new("GR"), Specializations.GA2D.Value!);
        subgroup.Add(new("Node"), Specializations.UI.Value!);

        for (int i = 1; i <= 10; i++)
        {
            var from = $"{(char)('a' + i - 1)}";
            var to = NumberHelper.ToRoman(i);
            subgroup.Add(from, to);
        }
    }

    // Source: https://usm.md/?page_id=731
    public static CurrentYearSemesterIntervalProvider SemesterIntervalProvider()
    {
        var s = new CurrentYearSemesterIntervalBuilder();
        s.Scope(x =>
        {
            x.AttendanceMode(AttendanceMode.Zi);
            x.QualificationType(QualificationType.Licenta);

            {
                x.Year(2026);
                x.Semester(Semester.Sem1);
                x.LessonsStart(month: 9, day: 1);
                x.LessonsEndInclusive(month: 12, day: 13);
                for (int i = 1; i <= 3; i++)
                {
                    var r = x.Range();
                    r.Grade(new(i));
                }
            }
            {
                x.Year(2027);
                x.Semester(Semester.Sem2);

                x.LessonsStart(month: 2, day: 1);
                x.Range(r =>
                {
                    r.LessonsEndInclusive(month: 5, day: 1);
                    r.Grade(new(2));
                });
                x.Range(r =>
                {
                    r.LessonsEndInclusive(month: 5, day: 1);
                    r.Grade(new(1));
                });

                x.Range(r =>
                {
                    r.Grade(new(3));
                    r.LessonsStart(month: 2, day: 22);
                    r.LessonsEndInclusive(month: 4, day: 11);
                });
            }
        });

        s.Scope(x =>
        {
            x.AttendanceMode(AttendanceMode.FrecventaRedusa);
            x.QualificationType(QualificationType.Licenta);

            // Reduced-attendance calendars have several short teaching blocks and do not fit
            // this single-range model. Keep the academic-year envelope until the model supports them.
            x.LessonsStart(new DateOnly(year: 2026, month: 9, day: 1));
            x.LessonsEndInclusive(new DateOnly(year: 2027, month: 8, day: 31));

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
            static IEnumerable<StudyWeek> Weeks(DateOnly firstMonday, int count, bool firstIsOdd) =>
                Enumerable.Range(0, count)
                    .Select(i => new StudyWeek(
                        monday: firstMonday.AddDays(i * 7),
                        isOddWeek: i % 2 == 0 ? firstIsOdd : !firstIsOdd));

            return
            [
                .. Weeks(new(2026, 8, 31), count: 15, firstIsOdd: false),
                .. Weeks(new(2027, 2, 1), count: 13, firstIsOdd: true),
            ];
        }
    }

    public static HolidayPeriod[] HolidayPeriods
    {
        get
        {
            HolidayPeriod[] holidayPeriods;
            holidayPeriods = [
                new(new(2026, month: 10, day: 14)),
                new(new(2027, month: 1, day: 1), new(2027, month: 1, day: 25)),
                new(new(2027, month: 5, day: 1)),
                new(new(2027, month: 5, day: 2), new(2027, month: 5, day: 11)),
            ];
            return holidayPeriods;
        }
    }
}

