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
    /// <summary>The user-secrets key under which the registry credentials are stored.</summary>
    public const string RegistryCredentialsConfigKey = "Curmanschii Anton:Registry";

    public static CourseNameParserConfig CourseNameParser
    {
        get
        {
            return new(new()
            {
                ProgrammingLanguages = ["Java", "C++", "C#", "Python", "Node.js", "PHP"],
                IgnoredFullWords = ["p/u", "pentru", "Modele", "jocuri"],
                IgnoredShortenedWords = ["Opț"],
                IgnoredProgrammingRelatedWords = ["Programare", "limbaj"],
                MinUsefulWordLength = 3,
            });
        }
    }

    public static ReadOnlySpan<CourseNameMapping> CourseNameUnificationConfig
    {
        get
        {
            return new CourseNameMapping[]
            {
                new(From: "Dezv. apl. server-side cu Node.js", To: "Node.js"),
                new(From: "HTML", To: "HTML și CSS"),
                new(From: "Modele design soft", To: "Design Soft"),
                new(From: "Python pentru aplicații", To: "Python"),
                new(From: "Dezvoltare WEB avansată cu PHP", To: "PHP"),
                new(From: "Dezvoltare WEB avansată", To: "PHP"),
                new(From: "Dezvoltare WEB PHP", To: "PHP"),
                new(From: "DA S-S", To: "Server-side"),
                new(From: "Dezvoltarea de aplicații server-side", To: "Server-side"),
                new(From: "Cloud", To: "Cloud Computing"),
                new(From: "RA U&Vuforia", To: "U&Vuforia"),
                // "UI/UX" is mis-tokenized into "U X" by one of the docs.
                new(From: "Designul UI/U X", To: "Designul UI/UX"),
            };
        }
    }

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
            var implicitSplitScopeBuilder = x;
            string course = "Disciplină umanistică opțională: Antreprenoriat inovativ";
            Alternative alternative = new("Antreprenoriat inovativ");
            implicitSplitScopeBuilder.Alternative(course, alternative);
            var implicitSplitScopeBuilder2 = x;
            string course2 = "Antreprenoriat inovativ";
            Alternative alternative2 = new("Antreprenoriat inovativ");
            implicitSplitScopeBuilder2.Alternative(course2, alternative2);
            var implicitSplitScopeBuilder3 = x;
            string course3 = "Disciplină umanistică opțională: Psihologie";
            Alternative alternative3 = new("Psihologie");
            implicitSplitScopeBuilder3.Alternative(course3, alternative3);
            var implicitSplitScopeBuilder4 = x;
            string course4 = "Psihologie";
            Alternative alternative4 = new("Psihologie");
            implicitSplitScopeBuilder4.Alternative(course4, alternative4);
            var implicitSplitScopeBuilder5 = x;
            string course5 = "Opț. ped.";
            Alternative alternative5 = new("Opț. ped.");
            implicitSplitScopeBuilder5.Alternative(course5, alternative5);
            var implicitSplitScopeBuilder6 = x;
            string course6 = "Cult. comun.";
            Alternative alternative6 = new("Cult. comun.");
            implicitSplitScopeBuilder6.Alternative(course6, alternative6);
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
            new LessonOverlapAllowlistEntry
            {
                CourseA = "Opț. ped.",
                CourseB = "Dezv. WEB.",
                GroupName = "I2501",
                Day = DayOfWeek.Tuesday,
                TimeSlot = new(4),
                Reason =
                    "Known website PDF source conflict at 15:00; retained without changing either lesson. See the 2026-10-11 capture.",
            },
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
                Reason =
                    "An-I doc language block: the every-week straina lesson meets the even-week Educatia fizica lesson.",
            },
        ],
    };

    public static ReadOnlySet<string> GroupLabelsThatAreMaster => ["IASD", "MIA"];

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
                x.AttendanceMode = AttendanceMode.DualZi;
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
        // The 2025 first-semester source calls this group DU2501; its later
        // schedules identify it as DU-IA2501. DU alone does not identify a program.
        remap.GroupNameRemappings.Add(key: "DU2501", value: "DU-IA2501");
        var teach = remap.TeacherLastNameRemappings;
        teach.Add(a: "Curmanschi", b: "Curmanschii");
        teach.Add(a: "Curmanscii", b: "Curmanschii");
        teach.Add(a: "Curmansci", b: "Curmanschii");
        teach.Add(a: "Vișnevschi", b: "Vișnevschii");
        teach.Add(a: "Băț", b: "Beț");
        teach.Add(a: "Spincean", b: "Sprîncean");
        teach.Add(a: "Anghelov", b: "Anghelova");
        teach.Add(a: "Iațîșina", b: "Iațâșina");
        teach.Add(a: "Jelihovskii", b: "Jelihovschii");
        teach.Add(a: "Jelihovschi", b: "Jelihovschii");
        teach.Add(a: "Vișnevshi", b: "Vișnevschii");

        remap.TeacherFullNameRemappings.Add((ref x) =>
        {
            bool EqualFirstPart(
                ref TeacherBuilderModel.NameModel x,
                string first,
                string last)
            {
                if (!(x.FirstName.Longer()[0] is { } l))
                {
                    return false;
                }

                if (!new Word(l).Span.IsEitherShortForOther(new Word(first).Span.Shortened))
                {
                    return false;
                }

                if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(x: x.LastName[0], y: last))
                {
                    return true;
                }

                return false;
            }

            if (EqualFirstPart(
                    ref x,
                    first: "Maria",
                    last: "Cristei"))
            {
                x = default;
                x.FirstName[0].Full = "Maria";
                x.FirstName[0].Short = "M";
                x.LastName[0] = "Marin";
                return true;
            }
            if (EqualFirstPart(
                    ref x,
                    first: "Gabriel",
                    last: "Stănescu"))
            {
                x = default;
                x.FirstName[0].Full = "Gabriel";
                x.FirstName[0].Short = "G";
                x.FirstName[1].Full = "Cătălin";
                x.FirstName[1].Short = "C";
                x.LastName[0] = "Stănescu";
                return true;
            }
            if (EqualFirstPart(
                    ref x,
                    first: "Eva",
                    last: "Arseni")
                || EqualFirstPart(
                    ref x,
                    first: "Mădălina",
                    last: "Arseni"))
            {
                x = default;
                x.FirstName[0].Full = "Eva";
                x.FirstName[1].Short = "E";
                x.FirstName[1].Full = "Mădălina";
                x.FirstName[1].Short = "M";
                x.LastName[0] = "Arseni";
                return true;
            }
            if (EqualFirstPart(
                    ref x,
                    first: "Anatol",
                    last: "Gladei"))
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
        subgroup.ForAlias("AG")
            .To(Specialization.AlgoritmicaGrafurilor);
        subgroup.ForAlias("GR")
            .To(Specialization.GA2D);
        subgroup.ForAlias("SAWM")
            .To(Specialization.DezvoltareaAplicatiilor);
        subgroup.ForAlias("DAS-S")
            .To(Specialization.DezvoltareaAplicatiilor);
        subgroup.ForAlias("UI/UX")
            .To(Specialization.UI);
        subgroup.ForAlias("RVA")
            .To(Specialization.DJ);
        // "Node" is a grade-2 subgroup label aliasing the UI specialization
        // (see docs/subgroup-model-update-spec.md and docs/domain-model.md).
        // It is unrelated to the grade-3 "Node.js" course ("Dezv. apl.
        // server-side cu Node.js"), whose server-side track maps to
        // Specialization.DezvoltareaAplicatiilor via the ImplicitSplitConfig
        // "Server-side" entry above.
        subgroup.ForAlias("Node")
            .To(Specialization.UI);

        for (int i = 1; i <= 10; i++)
        {
            int int32 = 'a' + i;
            int int322 = int32 - 1;
            var from = $"{(char) int322}";
            var to = NumberHelper.ToRoman(i);
            subgroup.Add(key: from, value: to);
        }
    }

    // Source: https://usm.md/?page_id=731
    public static CurrentYearSemesterIntervalProvider SemesterIntervalProvider()
    {
        // Confirmed DU teaching dates use the frecvență zi calendar. Register
        // both identities explicitly; DualFr remains unconfigured.
        var s = new CurrentYearSemesterIntervalBuilder();
        foreach (var mode in new[]
                 {
                     AttendanceMode.Zi,
                     AttendanceMode.DualZi,
                 })
        {
            s.Scope(x =>
            {
                x.AttendanceMode(mode);
                x.QualificationType(QualificationType.Licenta);

                {
                    x.Year(2026);
                    x.Semester(Semester.Sem1);
                    x.LessonsStart(month: 9, day: 1);
                    x.LessonsEndInclusive(month: 12, day: 13);
                    for (int i = 1; i <= 3; i++)
                    {
                        var r = x.Range();
                        var yearDateRangeBuilder = r;
                        Grade grade = new(i);
                        yearDateRangeBuilder.Grade(grade);
                    }
                }
                {
                    x.Year(2027);
                    x.Semester(Semester.Sem2);

                    x.LessonsStart(month: 2, day: 1);
                    x.Range(r =>
                    {
                        r.LessonsEndInclusive(month: 5, day: 1);
                        var yearDateRangeBuilder = r;
                        Grade grade = new(2);
                        yearDateRangeBuilder.Grade(grade);
                    });
                    x.Range(r =>
                    {
                        r.LessonsEndInclusive(month: 5, day: 1);
                        var yearDateRangeBuilder = r;
                        Grade grade = new(1);
                        yearDateRangeBuilder.Grade(grade);
                    });

                    x.Range(r =>
                    {
                        var yearDateRangeBuilder = r;
                        Grade grade = new(3);
                        yearDateRangeBuilder.Grade(grade);
                        r.LessonsStart(month: 2, day: 22);
                        r.LessonsEndInclusive(month: 4, day: 11);
                    });
                }
            });
        }

        s.Scope(x =>
        {
            x.AttendanceMode(AttendanceMode.FrecventaRedusa);
            x.QualificationType(QualificationType.Licenta);

            // Reduced-attendance calendars have several short teaching blocks and do not fit
            // this single-range model. Keep the academic-year envelope until the model supports them.
            DateOnly CalculateStart()
            {
                var start = new DateOnly(
                    year: 2026,
                    month: 9,
                    day: 1);
                return start;
            }

            x.LessonsStart(CalculateStart());
            var yearDateRangeBuilderScope = x;
            var end = new DateOnly(
                year: 2027,
                month: 8,
                day: 31);
            yearDateRangeBuilderScope.LessonsEndInclusive(end);

            foreach (var sem in new EnumMembers<Semester>())
            {
                x.Semester(sem);
                for (int i = 1; i <= 3; i++)
                {
                    var r = x.Range();
                    var yearDateRangeBuilder = r;
                    Grade grade = new(i);
                    yearDateRangeBuilder.Grade(grade);
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
    public static StudyWeek[] StudyWeeks
    {
        get
        {
            DateOnly CalculateMonday()
            {
                DateOnly monday2 = new(
                    year: 2026,
                    month: 8,
                    day: 31);
                return monday2;
            }

            DateOnly CalculateMonday2()
            {
                DateOnly monday3 = new(
                    year: 2026,
                    month: 9,
                    day: 7);
                return monday3;
            }

            DateOnly CalculateMonday3()
            {
                DateOnly monday4 = new(
                    year: 2026,
                    month: 9,
                    day: 14);
                return monday4;
            }

            DateOnly CalculateMonday4()
            {
                DateOnly monday5 = new(
                    year: 2026,
                    month: 9,
                    day: 21);
                return monday5;
            }

            DateOnly CalculateMonday5()
            {
                DateOnly monday6 = new(
                    year: 2026,
                    month: 9,
                    day: 28);
                return monday6;
            }

            DateOnly CalculateMonday6()
            {
                DateOnly monday7 = new(
                    year: 2026,
                    month: 10,
                    day: 5);
                return monday7;
            }

            DateOnly CalculateMonday7()
            {
                DateOnly monday8 = new(
                    year: 2026,
                    month: 10,
                    day: 12);
                return monday8;
            }

            DateOnly CalculateMonday8()
            {
                DateOnly monday9 = new(
                    year: 2026,
                    month: 10,
                    day: 19);
                return monday9;
            }

            DateOnly CalculateMonday9()
            {
                DateOnly monday10 = new(
                    year: 2026,
                    month: 10,
                    day: 26);
                return monday10;
            }

            DateOnly CalculateMonday10()
            {
                DateOnly monday11 = new(
                    year: 2026,
                    month: 11,
                    day: 2);
                return monday11;
            }

            DateOnly CalculateMonday11()
            {
                DateOnly monday12 = new(
                    year: 2026,
                    month: 11,
                    day: 9);
                return monday12;
            }

            DateOnly CalculateMonday12()
            {
                DateOnly monday13 = new(
                    year: 2026,
                    month: 11,
                    day: 16);
                return monday13;
            }

            DateOnly CalculateMonday13()
            {
                DateOnly monday14 = new(
                    year: 2026,
                    month: 11,
                    day: 23);
                return monday14;
            }

            DateOnly CalculateMonday14()
            {
                DateOnly monday15 = new(
                    year: 2026,
                    month: 11,
                    day: 30);
                return monday15;
            }

            DateOnly CalculateMonday15()
            {
                DateOnly monday16 = new(
                    year: 2026,
                    month: 12,
                    day: 7);
                return monday16;
            }

            DateOnly CalculateMonday16()
            {
                DateOnly monday17 = new(
                    year: 2027,
                    month: 2,
                    day: 1);
                return monday17;
            }

            DateOnly CalculateMonday17()
            {
                DateOnly monday18 = new(
                    year: 2027,
                    month: 2,
                    day: 8);
                return monday18;
            }

            DateOnly CalculateMonday18()
            {
                DateOnly monday19 = new(
                    year: 2027,
                    month: 2,
                    day: 15);
                return monday19;
            }

            DateOnly CalculateMonday19()
            {
                DateOnly monday20 = new(
                    year: 2027,
                    month: 2,
                    day: 22);
                return monday20;
            }

            DateOnly CalculateMonday20()
            {
                DateOnly monday21 = new(
                    year: 2027,
                    month: 3,
                    day: 1);
                return monday21;
            }

            DateOnly CalculateMonday21()
            {
                DateOnly monday22 = new(
                    year: 2027,
                    month: 3,
                    day: 8);
                return monday22;
            }

            DateOnly CalculateMonday22()
            {
                DateOnly monday23 = new(
                    year: 2027,
                    month: 3,
                    day: 15);
                return monday23;
            }

            DateOnly CalculateMonday23()
            {
                DateOnly monday24 = new(
                    year: 2027,
                    month: 3,
                    day: 22);
                return monday24;
            }

            DateOnly CalculateMonday24()
            {
                DateOnly monday25 = new(
                    year: 2027,
                    month: 3,
                    day: 29);
                return monday25;
            }

            DateOnly CalculateMonday25()
            {
                DateOnly monday26 = new(
                    year: 2027,
                    month: 4,
                    day: 5);
                return monday26;
            }

            DateOnly CalculateMonday26()
            {
                DateOnly monday27 = new(
                    year: 2027,
                    month: 4,
                    day: 12);
                return monday27;
            }

            DateOnly CalculateMonday27()
            {
                DateOnly monday28 = new(
                    year: 2027,
                    month: 4,
                    day: 19);
                return monday28;
            }

            DateOnly CalculateMonday28()
            {
                DateOnly monday29 = new(
                    year: 2027,
                    month: 4,
                    day: 26);
                return monday29;
            }

            return
            [
                // Semestrul I: 15 weeks starting with the Monday containing the first teaching day.
                new(monday: CalculateMonday(), isOddWeek: false),
                new(monday: CalculateMonday2(), isOddWeek: true),
                new(monday: CalculateMonday3(), isOddWeek: false),
                new(monday: CalculateMonday4(), isOddWeek: true),
                new(monday: CalculateMonday5(), isOddWeek: false),
                new(monday: CalculateMonday6(), isOddWeek: true),
                new(monday: CalculateMonday7(), isOddWeek: false),
                new(monday: CalculateMonday8(), isOddWeek: true),
                new(monday: CalculateMonday9(), isOddWeek: false),
                new(monday: CalculateMonday10(), isOddWeek: true),
                new(monday: CalculateMonday11(), isOddWeek: false),
                new(monday: CalculateMonday12(), isOddWeek: true),
                new(monday: CalculateMonday13(), isOddWeek: false),
                new(monday: CalculateMonday14(), isOddWeek: true),
                new(monday: CalculateMonday15(), isOddWeek: false),
                // Semestrul II: 13 weeks, numbering continues from the fall term.
                new(monday: CalculateMonday16(), isOddWeek: true),
                new(monday: CalculateMonday17(), isOddWeek: false),
                new(monday: CalculateMonday18(), isOddWeek: true),
                new(monday: CalculateMonday19(), isOddWeek: false),
                new(monday: CalculateMonday20(), isOddWeek: true),
                new(monday: CalculateMonday21(), isOddWeek: false),
                new(monday: CalculateMonday22(), isOddWeek: true),
                new(monday: CalculateMonday23(), isOddWeek: false),
                new(monday: CalculateMonday24(), isOddWeek: true),
                new(monday: CalculateMonday25(), isOddWeek: false),
                new(monday: CalculateMonday26(), isOddWeek: true),
                new(monday: CalculateMonday27(), isOddWeek: false),
                new(monday: CalculateMonday28(), isOddWeek: true),
            ];
        }
    }

    public static HolidayPeriod[] HolidayPeriods
    {
        get
        {
            HolidayPeriod[] holidayPeriods;

            DateOnly CalculateSingleDay()
            {
                DateOnly singleDay = new(
                    year: 2026,
                    month: 10,
                    day: 14);
                return singleDay;
            }

            DateOnly CalculateStart()
            {
                DateOnly start2 = new(
                    year: 2027,
                    month: 1,
                    day: 1);
                return start2;
            }

            DateOnly CalculateEndExclusive()
            {
                DateOnly endExclusive2 = new(
                    year: 2027,
                    month: 1,
                    day: 25);
                return endExclusive2;
            }

            DateOnly CalculateSingleDay2()
            {
                DateOnly singleDay2 = new(
                    year: 2027,
                    month: 5,
                    day: 1);
                return singleDay2;
            }

            DateOnly CalculateStart2()
            {
                DateOnly start3 = new(
                    year: 2027,
                    month: 5,
                    day: 2);
                return start3;
            }

            DateOnly CalculateEndExclusive2()
            {
                DateOnly endExclusive3 = new(
                    year: 2027,
                    month: 5,
                    day: 11);
                return endExclusive3;
            }

            holidayPeriods =
            [
                new(CalculateSingleDay()),
                new(start: CalculateStart(), endExclusive: CalculateEndExclusive()),
                new(CalculateSingleDay2()),
                new(start: CalculateStart2(), endExclusive: CalculateEndExclusive2()),
            ];
            return holidayPeriods;
        }
    }
}
