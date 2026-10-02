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
            x.Specialization("Realitate virtuală și augmentată", Specialization.DJ);
            x.Specialization("Design audio și efecte vizuale", Specialization.DJ);
            x.Specialization("Fotogrametrie și scanare 3D", Specialization.DJ);
            x.Specialization("Server-side", Specialization.DezvoltareaAplicatiilor);
            x.Specialization("Dezvoltarea aplicațiilor mobile", Specialization.DezvoltareaAplicatiilor);
            x.Specialization("Securitatea aplicațiilor enterprise", Specialization.DezvoltareaAplicatiilor);
            x.Specialization("Securitatea aplicațiilor web și mobile", Specialization.DezvoltareaAplicatiilor);
        });
        builder.Scope(x =>
        {
            x.StudyYear = new(2026);
            x.Grade = new(2);
            x.Faculty = new("IA");
            x.AttendanceMode = AttendanceMode.Zi;
            x.Qualification = QualificationType.Licenta;
            x.Specialization("Grafică și animație 2D", Specialization.GA2D);
            x.Specialization("Designul UI/UX", Specialization.UI);
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

    /// <summary>
    /// The initial specialization registry. Uses the canonical extension
    /// accessors on Specialization (only visible in this config layer).
    /// </summary>
    public static SpecializationRegistry SpecializationRegistry { get; } = CreateSpecializationRegistry();

    private static SpecializationRegistry CreateSpecializationRegistry()
    {
        var b = new SpecializationRegistryBuilder();
        b.Set([Specialization.AlgoritmicaGrafurilor, Specialization.Logica])
            .ApplyTo(x =>
            {
                x.Grade = new(1);
                x.Faculty = new("I");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([Specialization.AlgoritmicaGrafurilor, Specialization.Logica])
            .ApplyTo(x =>
            {
                x.Grade = new(1);
                x.Faculty = new("IA");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([Specialization.AlgoritmicaGrafurilor, Specialization.Logica])
            .ApplyTo(x =>
            {
                x.Grade = new(1);
                x.Faculty = new("IA");
                x.AttendanceMode = AttendanceMode.Dual;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([Specialization.Spring])
            .ApplyTo(x =>
            {
                x.Grade = new(2);
                x.Faculty = new("I");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([
                Specialization.CV,
                Specialization.DJ,
                Specialization.GA2D,
                Specialization.GA3D,
                Specialization.React,
                Specialization.Spring,
                Specialization.SSI,
                Specialization.UI,
            ])
            .ApplyTo(x =>
            {
                x.Grade = new(2);
                x.Faculty = new("IA");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([Specialization.DJ, Specialization.DezvoltareaAplicatiilor])
            .ApplyTo(x =>
            {
                x.Grade = new(3);
                x.Faculty = new("IA");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        return b.Build();
    }

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
        subgroup.Add(new("AG"), Specialization.AlgoritmicaGrafurilor);
        subgroup.Add(new("GR"), Specialization.GA2D);
        // "Node" is a grade-2 subgroup label aliasing the UI specialization
        // (see docs/subgroup-model-update-spec.md and docs/domain-model.md).
        // It is unrelated to the grade-3 "Node.js" course ("Dezv. apl.
        // server-side cu Node.js"), whose server-side track maps to
        // Specialization.DezvoltareaAplicatiilor via the ImplicitSplitConfig
        // "Server-side" entry above.
        subgroup.Add(new("Node"), Specialization.UI);

        for (int i = 1; i <= 10; i++)
        {
            var from = $"{(char) ('a' + i - 1)}";
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

    /// <summary>
    /// Teaching weeks of the 2026-2027 academic year, verified against the official USM
    /// "Calendar academic Licenta 2026-2027" PDF (Licenta zi): semestrul I 01.09.2026-13.12.2026
    /// (15 saptamani), semestrul II 01.02.2027-01.05.2027 (13 saptamani). Each entry names its
    /// Monday explicitly instead of generating dates in a loop. Parity follows the continuous
    /// academic-year numbering (fall week 1 is even, so spring week 1 after the 15-week fall
    /// term is odd); the PDF fixes only the week counts and dates, not the parity.
    /// </summary>
    public static StudyWeek[] StudyWeeks =>
    [
        // Semestrul I: 15 weeks starting with the Monday containing the first teaching day.
        new(monday: new(2026, 8, 31), isOddWeek: false),
        new(monday: new(2026, 9, 7), isOddWeek: true),
        new(monday: new(2026, 9, 14), isOddWeek: false),
        new(monday: new(2026, 9, 21), isOddWeek: true),
        new(monday: new(2026, 9, 28), isOddWeek: false),
        new(monday: new(2026, 10, 5), isOddWeek: true),
        new(monday: new(2026, 10, 12), isOddWeek: false),
        new(monday: new(2026, 10, 19), isOddWeek: true),
        new(monday: new(2026, 10, 26), isOddWeek: false),
        new(monday: new(2026, 11, 2), isOddWeek: true),
        new(monday: new(2026, 11, 9), isOddWeek: false),
        new(monday: new(2026, 11, 16), isOddWeek: true),
        new(monday: new(2026, 11, 23), isOddWeek: false),
        new(monday: new(2026, 11, 30), isOddWeek: true),
        new(monday: new(2026, 12, 7), isOddWeek: false),
        // Semestrul II: 13 weeks, numbering continues from the fall term.
        new(monday: new(2027, 2, 1), isOddWeek: true),
        new(monday: new(2027, 2, 8), isOddWeek: false),
        new(monday: new(2027, 2, 15), isOddWeek: true),
        new(monday: new(2027, 2, 22), isOddWeek: false),
        new(monday: new(2027, 3, 1), isOddWeek: true),
        new(monday: new(2027, 3, 8), isOddWeek: false),
        new(monday: new(2027, 3, 15), isOddWeek: true),
        new(monday: new(2027, 3, 22), isOddWeek: false),
        new(monday: new(2027, 3, 29), isOddWeek: true),
        new(monday: new(2027, 4, 5), isOddWeek: false),
        new(monday: new(2027, 4, 12), isOddWeek: true),
        new(monday: new(2027, 4, 19), isOddWeek: false),
        new(monday: new(2027, 4, 26), isOddWeek: true),
    ];

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

