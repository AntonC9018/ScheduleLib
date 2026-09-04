using System.Diagnostics;
using System.Text;
using Argon;
using ScheduleLib.Builders;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.Lesson.Internal;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.ParserTests;

public sealed class LessonParserTests
{
    private void AssertEqualName(string expected, TeacherName actual)
    {
        Assert.True(CheckEqualName(expected, actual));
    }

    private IEnumerator<ReadOnlyMemory<char>> TokenInput(params string[] values)
    {
        foreach (var x in values)
        {
            yield return x.AsMemory();
        }
    }

    [Fact]
    public async Task LexerTest()
    {
        var strings = TokenInput(
            "15:00 Opț.psihol. (curs,imp),",
            "Psihologie (sem,par)",
            "V.Miron  433/3");
        var lexer = LessonParsingHelper.CreateLexer();
        lexer.Reset(strings);
        List<Token> result = new();
        while (!lexer.IsEmpty)
        {
            result.Add(lexer.Current);
            lexer.Move();
        }
        Assert.DoesNotContain(result, x => x.Type == TokenType.Invalid);

        var verifyModels = result.Select(x => new
        {
            Type = LessonTokenReader.Instance.Labels.Get(x.Type),
            Value = x.Value.ToString(),
            x.Span.Row,
            ColStart = x.Span.ColStart.Index,
            ColEnd = x.Span.ColEnd.Index,
        });
        await Verify(verifyModels)
            .UseStrictJson()
            .AddExtraSettings(x => x.DefaultValueHandling = DefaultValueHandling.Include);
    }

    [Theory]
    [InlineData(new[]{"START A AB 12 STOP /"}, " A AB 12 ")]
    [InlineData(new[]{"START A", "B", "C STOP 123"}, " A\nB\nC ")]
    public void LexerConcatTest(string[] input, string output)
    {
        var str = TokenInput(input);
        var lexer = LessonParsingHelper.CreateLexer();
        lexer.Reset(str);

        var startLexer = lexer.Scope();
        while (!startLexer.ConsumeExactWord("START"))
        {
            startLexer.Move();
            Assert.False(startLexer.IsEmpty);
        }

        var endLexer = startLexer;
        while (true)
        {
            var t = endLexer.Current;
            if (t.Value.Span.SequenceEqual("STOP"))
            {
                break;
            }
            endLexer.Move();
            Assert.False(endLexer.IsEmpty);
        }

        var part = startLexer.Until(endLexer.Position);
        var sb = new StringBuilder();
        var ret = part.Concat(sb).Span;
        Assert.Equal(output, ret);
    }

    private ParsedLesson[] ParseLessons(
        string[] lines,
        ProcessSpaces? spaces = null)
    {
        var lexer = LessonParsingHelper.CreateLexer();
        using var enumerator = lines.Select(x => x.AsMemory()).GetEnumerator();
        lexer.Reset(enumerator);
        var parameters = new ParseLessonsParams
        {
            StringBuilder = new(),
            Lexer = lexer,
        };
        if (spaces != null)
        {
            parameters.ProcessSpacesCourseName = spaces;
        }
        return LessonParsingHelper.ParseLessons(parameters).ToArray();
    }

    [Fact]
    public void LessonListEachWithModifiers()
    {
        var lessons = ParseLessons([
            "15:00 Opț.psihol. (curs,imp),",
            "Psihologie (sem,par)",
            "V.Miron  433/3",
        ]);

        var time = TimeOnly.FromTimeSpan(TimeSpan.FromHours(15));

        void CheckCommon(in ParsedLesson lesson)
        {
            Assert.Equal(time, lesson.StartTime);
            Assert.True(lesson.PartitionHint.IsEmpty);
            var teacherName = Assert.Single(lesson.TeacherNames);
            AssertEqualName("V.Miron", teacherName);
            Assert.Equal("433/3", lesson.RoomName.Span);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                CheckCommon(lesson1);
                Assert.Equal(Parity.OddWeek, lesson1.Parity);
                Assert.Equal(LessonType.Curs, lesson1.LessonType);
                Assert.Equal("Opț. psihol.", lesson1.LessonName.Span);
            },
            lesson2 =>
            {
                CheckCommon(lesson2);
                Assert.Equal(Parity.EvenWeek, lesson2.Parity);
                Assert.Equal(LessonType.Seminar, lesson2.LessonType);
                Assert.Equal("Psihologie", lesson2.LessonName.Span);
            });
    }

    [Fact]
    public void ParenthesesInLessonName()
    {
        var lessons = ParseLessons([
            "Matematica discretă (Logica)  (curs)",
            "I.Cucu  404/4",
        ]);

        Assert.Collection(lessons,
            lesson =>
            {
                var teacherName = Assert.Single(lesson.TeacherNames);
                AssertEqualName("I.Cucu", teacherName);
                Assert.Equal("404/4", lesson.RoomName.Span);
                Assert.Equal(LessonType.Curs, lesson.LessonType);
                Assert.Equal("Matematica discretă (Logica)", lesson.LessonName.Span);
            });
    }

    [Fact]
    public void RoomNameMayBeUnderscores()
    {
        var lessons = ParseLessons([
            "Option.didact. (curs)",
            "A.Dabija  ____",
        ]);

        Assert.Collection(lessons,
            lesson =>
            {
                var teacherName = Assert.Single(lesson.TeacherNames);
                AssertEqualName("A.Dabija", teacherName);
                Assert.Equal("____", lesson.RoomName.Span);
                Assert.Equal(LessonType.Curs, lesson.LessonType);
                Assert.Equal("Option. didact.", lesson.LessonName.Span);
            });
    }

    [Fact]
    public void ColonAllowedInCourseName()
    {
        var lesson = Assert.Single(ParseLessons([
            "Disciplină umanistică opțională: Antreprenoriat inovativ (curs), I. Dobrovolschi 528/3",
        ]));

        Assert.Equal("Disciplină umanistică opțională: Antreprenoriat inovativ", lesson.LessonName.Span);
        Assert.Equal(LessonType.Curs, lesson.LessonType);
        AssertEqualName("I. Dobrovolschi", Assert.Single(lesson.TeacherNames));
        Assert.Equal("528/3", lesson.RoomName.Span);
    }

    [Fact]
    public void CourseWithRoomButNoTeacherDoesNotReadLastTeacher()
    {
        var lesson = Assert.Single(ParseLessons([
            "Disciplină umanistică opțională: Psihologie (curs), 113/4",
        ]));

        Assert.Equal("Disciplină umanistică opțională: Psihologie", lesson.LessonName.Span);
        Assert.Equal(LessonType.Curs, lesson.LessonType);
        Assert.Empty(lesson.TeacherNames);
        Assert.Equal("113/4", lesson.RoomName.Span);
    }

    [Fact]
    public void TimeSlotThatLooksLikeGroupIsParseProperly()
    {
        var lessons = ParseLessons([
            "8:00 Containerizare și virtualizare (lab,imp)",
            "CV: M.Croitor  326/4",
            "15:00 Dezvoltare de aplicații WEB cu React (curs)",
            "WR: A.Donu  213a/4",
        ]);

        TimeOnly Time(int hours)
        {
            var span = TimeSpan.FromHours(hours);
            return TimeOnly.FromTimeSpan(span);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Assert.Equal(Time(8), lesson1.StartTime);
                Assert.Equal("CV", lesson1.PartitionHint.Span);
                AssertEqualName("M.Croitor", Assert.Single(lesson1.TeacherNames));
                Assert.Equal("326/4", lesson1.RoomName.Span);
                Assert.Equal(LessonType.Lab, lesson1.LessonType);
                Assert.Equal(Parity.OddWeek, lesson1.Parity);
                Assert.Equal("Containerizare și virtualizare", lesson1.LessonName.Span);
            },
            lesson2 =>
            {
                Assert.Equal(Time(15), lesson2.StartTime);
                Assert.Equal("WR", lesson2.PartitionHint.Span);
                AssertEqualName("A.Donu", Assert.Single(lesson2.TeacherNames));
                Assert.Equal("213a/4", lesson2.RoomName.Span);
                Assert.Equal(LessonType.Curs, lesson2.LessonType);
                Assert.Equal(Parity.EveryWeek, lesson2.Parity);
                Assert.Equal("Dezvoltare de aplicații WEB cu React", lesson2.LessonName.Span);
            });
    }

    [Fact]
    public void SubGroupList()
    {
        var lessons = ParseLessons([
            "Dezvoltare de aplicații WEB cu React (lab)",
            "WR1: A.Donu  143/4,  WR2: Cr.Crudu  145a/4",
        ]);

        void CheckCommon(ParsedLesson lesson)
        {
            Assert.Equal(LessonType.Lab, lesson.LessonType);
            Assert.Equal("Dezvoltare de aplicații WEB cu React", lesson.LessonName.Span);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                CheckCommon(lesson1);
                Assert.Equal("WR1", lesson1.PartitionHint.Span);
                AssertEqualName("A.Donu", Assert.Single(lesson1.TeacherNames));
                Assert.Equal("143/4", lesson1.RoomName.Span);
            },
            lesson2 =>
            {
                CheckCommon(lesson2);
                Assert.Equal("WR2", lesson2.PartitionHint.Span);
                AssertEqualName("Cr.Crudu", Assert.Single(lesson2.TeacherNames));
                Assert.Equal("145a/4", lesson2.RoomName.Span);
            });
    }

    [Fact]
    public void SubGroupListWithoutWhitespaceAfterColon()
    {
        var lessons = ParseLessons([
            "HTML(lab)",
            "I:B.Vișnevschi  218/4a",
            "II:V.Vișnevschi  219/4a",
        ]);

        Assert.Collection(lessons,
            first =>
            {
                Assert.Equal("HTML", first.LessonName.Span);
                Assert.Equal(LessonType.Lab, first.LessonType);
                Assert.Equal("I", first.PartitionHint.Span);
                AssertEqualName("B. Vișnevschi", Assert.Single(first.TeacherNames));
                Assert.Equal("218/4a", first.RoomName.Span);
            },
            second =>
            {
                Assert.Equal("HTML", second.LessonName.Span);
                Assert.Equal(LessonType.Lab, second.LessonType);
                Assert.Equal("II", second.PartitionHint.Span);
                AssertEqualName("V. Vișnevschi", Assert.Single(second.TeacherNames));
                Assert.Equal("219/4a", second.RoomName.Span);
            });
    }

    [Fact()]
    public void NoLessonModifiers_MultipleDefaultModifiers()
    {
        var lessons = ParseLessons([
            "LessonA, LessonB",
            "A: TeacherA",
            "B: TeacherB",
        ]);

        bool Check(in ParsedLesson lesson, string lessonName, string groupName, string teacherName)
        {
            var actualTeacherName = Assert.Single(lesson.TeacherNames);
            if (!CheckEqualName(teacherName, actualTeacherName))
            {
                return false;
            }
            if (!lessonName.AsSpan().Equals(
                    lesson.LessonName.Span,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
            if (!groupName.AsSpan().Equals(
                    lesson.PartitionHint.Span,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
            return true;

        }

        Assert.Contains(lessons, x => Check(x, "LessonA", "A", "TeacherA"));
        Assert.Contains(lessons, x => Check(x, "LessonB", "A", "TeacherA"));
        Assert.Contains(lessons, x => Check(x, "LessonA", "B", "TeacherB"));
        Assert.Contains(lessons, x => Check(x, "LessonB", "B", "TeacherB"));
        Assert.Equal(4, lessons.Length);
    }

    [Fact]
    public void AllLessonModifiers_MultipleDefaultModifiers()
    {
        var lessons = ParseLessons([
            "Lesson (par,curs)",
            "A: TeacherA",
            "B: TeacherB",
        ]);

        void Lesson(in ParsedLesson lesson)
        {
            Assert.Equal("Lesson", lesson.LessonName.Span);
            Assert.Equal(Parity.EvenWeek, lesson.Parity);
            Assert.Equal(LessonType.Curs, lesson.LessonType);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Lesson(lesson1);
                Assert.Equal("A", lesson1.PartitionHint.Span);
                AssertEqualName("TeacherA", Assert.Single(lesson1.TeacherNames));
            },
            lesson2 =>
            {
                Lesson(lesson2);
                Assert.Equal("B", lesson2.PartitionHint.Span);
                AssertEqualName("TeacherB", Assert.Single(lesson2.TeacherNames));
            });
    }

    [Fact]
    public void PerGroupLessonModifiers_MultipleDefaultModifiers()
    {
        var lessons = ParseLessons([
            "Lesson (A-par,B-impar)",
            "A: TeacherA",
            "B: TeacherB",
        ]);

        Assert.Collection(lessons,
            lesson1 =>
            {
                Assert.Equal("Lesson", lesson1.LessonName.Span);
                Assert.Equal("A", lesson1.PartitionHint.Span);
                Assert.Equal(Parity.EvenWeek, lesson1.Parity);
                AssertEqualName("TeacherA", Assert.Single(lesson1.TeacherNames));
            },
            lesson2 =>
            {
                Assert.Equal("Lesson", lesson2.LessonName.Span);
                Assert.Equal("B", lesson2.PartitionHint.Span);
                Assert.Equal(Parity.OddWeek, lesson2.Parity);
                AssertEqualName("TeacherB", Assert.Single(lesson2.TeacherNames));
            });
    }

    [Fact]
    public void AdditionalSubGroupInModifiers_SingleOtherGroup()
    {
        var lessons = ParseLessons([
            "Lesson (A-par)",
            "B: TeacherA",
        ]);

        Assert.Collection(lessons,
            lesson1 =>
            {
                Assert.Equal("Lesson", lesson1.LessonName.Span);
                Assert.Equal("A", lesson1.PartitionHint.Span);
                Assert.Equal(Parity.EvenWeek, lesson1.Parity);
                Assert.Empty(lesson1.TeacherNames);
            },
            lesson2 =>
            {
                Assert.Equal("Lesson", lesson2.LessonName.Span);
                Assert.Equal("B", lesson2.PartitionHint.Span);
                Assert.Equal(Parity.EveryWeek, lesson2.Parity);
                AssertEqualName("TeacherA", Assert.Single(lesson2.TeacherNames));
            });
    }

    [Fact]
    public void LessonSubGroupModifiers_DefaultNoSubGroup()
    {
        var lessons = ParseLessons([
            "Lesson (A-par)",
            "TeacherA",
        ]);

        void Common(in ParsedLesson lesson)
        {
            Assert.Equal("Lesson", lesson.LessonName.Span);
            AssertEqualName("TeacherA", Assert.Single(lesson.TeacherNames));
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Common(lesson1);
                Assert.Equal("A", lesson1.PartitionHint.Span);
                Assert.Equal(Parity.EvenWeek, lesson1.Parity);
            });
    }

    [Fact]
    public void LessonTypeInSubGroupModifierKey_Works()
    {
        var lessons = ParseLessons([
            "LessonA (curs-par,sem-imp)",
            "TeacherA",
        ]);

        void Common(in ParsedLesson lesson)
        {
            Assert.Equal("LessonA", lesson.LessonName.Span);
            AssertEqualName("TeacherA", Assert.Single(lesson.TeacherNames));
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Common(lesson1);
                Assert.Equal(Parity.EvenWeek, lesson1.Parity);
                Assert.Equal(LessonType.Curs, lesson1.LessonType);
            },
            lesson2 =>
            {
                Common(lesson2);
                Assert.Equal(Parity.OddWeek, lesson2.Parity);
                Assert.Equal(LessonType.Seminar, lesson2.LessonType);
            });
    }

    [Fact]
    public void BothGeneralAndSubGroupModifiers()
    {
        var lessons = ParseLessons([
            "MTA 3D (lab, I-imp, II-par)",
            "A.Schiopu  251/4",
        ]);

        void Common(in ParsedLesson lesson)
        {
            Assert.Equal("MTA 3D", lesson.LessonName.Span);
            Assert.Equal(LessonType.Lab, lesson.LessonType);
            AssertEqualName("A.Schiopu", Assert.Single(lesson.TeacherNames));
            Assert.Equal("251/4", lesson.RoomName.Span);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Common(lesson1);
                Assert.Equal("I", lesson1.PartitionHint.Span);
                Assert.Equal(Parity.OddWeek, lesson1.Parity);
            },
            lesson2 =>
            {
                Common(lesson2);
                Assert.Equal("II", lesson2.PartitionHint.Span);
                Assert.Equal(Parity.EvenWeek, lesson2.Parity);
            });
    }

    [Fact]
    public void NoTeacherNoRoom()
    {
        var lessons = ParseLessons([
            "Lesson",
        ]);

        Assert.Collection(lessons,
            lesson =>
            {
                Assert.Equal("Lesson", lesson.LessonName.Span);
            });
    }

    [Fact]
    public void NoTeacherWithModifierTest()
    {
        var lessons = ParseLessons([
            "Educa.fizică (imp)",
        ]);

        var lesson = Assert.Single(lessons);
        Assert.Equal("Educa. fizică", lesson.LessonName.Span);
        Assert.Equal(Parity.OddWeek, lesson.Parity);
    }

    [Fact]
    public void TeacherAll()
    {
        var lessons = ParseLessons([
            "Lesson",
            "Teacher",
        ]);

        Assert.Collection(lessons,
            lesson =>
            {
                Assert.Equal("Lesson", lesson.LessonName.Span);
                var teacher = Assert.Single(lesson.TeacherNames);
                AssertEqualName("Teacher", teacher);
            });
    }

    private bool CheckEqualName(string expected, TeacherName actual)
    {
        var expectedName = TeacherNameHelper.ParseName(expected);
        if (!ShortNameEqual())
        {
            return false;
        }
        if (!LastNameEqual())
        {
            return false;
        }
        return true;

        bool ShortNameEqual()
        {
            return expectedName.FirstName.EachEquals(actual.FirstName, (e, a) =>
            {
                if (e.IsNull)
                {
                    if (!a.IsEmpty)
                    {
                        return false;
                    }
                    return true;
                }

                var e1 = (e.Full ?? e.Short)!.AsSpan();
                var a1 = new WordSpan(a.Span).Value;
                if (!e1.Equals(a1, StringComparison.Ordinal))
                {
                    return false;
                }
                return true;
            });
        }

        bool LastNameEqual()
        {
            if (actual.LastName.All(x => x.IsEmpty))
            {
                return expectedName.LastName == default;
            }

            return expectedName.LastName.Parts.EachEquals(actual.LastName, (a, b) =>
            {
                var a1 = a.AsSpan();
                var b1 = b.Span;
                return a1.Equals(b1, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public void MediacorRoom()
    {
        var lessons = ParseLessons([
            "Lesson",
            "Mediacor, etajul II",
        ]);

        var lesson = Assert.Single(lessons);
        Assert.Equal("Lesson", lesson.LessonName.Span);
        Assert.Equal("Mediacor, etajul II", lesson.RoomName.Span);
    }

    [Fact]
    public void MediacorRoomWithTeacher()
    {
        var lessons = ParseLessons([
            "Lesson",
            "Teacher  Mediacor, etajul I",
        ]);

        var lesson = Assert.Single(lessons);
        Assert.Equal("Lesson", lesson.LessonName.Span);
        Assert.Equal("Teacher", Assert.Single(lesson.TeacherNames).LastName[0].Span);
        Assert.Equal("Mediacor, etajul I", lesson.RoomName.Span);
    }

    [Fact]
    public void LessonWithCommasInName()
    {
        var lessons = ParseLessons([
            "Lesson",
            "Teacher",
            "Other,Lesson",
            "Teacher",
        ]);

        Assert.Collection(lessons,
            lesson1 =>
            {
                Assert.Equal("Lesson", lesson1.LessonName.Span);
                AssertEqualName("Teacher", Assert.Single(lesson1.TeacherNames));
            },
            lesson2 =>
            {
                Assert.Equal("Other, Lesson", lesson2.LessonName.Span);
                AssertEqualName("Teacher", Assert.Single(lesson2.TeacherNames));
            });
    }

    [Fact]
    public void RoomNameAfterTeacherNameWithComma()
    {
        var lessons = ParseLessons([
            "Lesson",
            "Teacher, 123R",
        ]);

        var lesson1 = Assert.Single(lessons);
        Assert.Equal("Lesson", lesson1.LessonName.Span);
        AssertEqualName("Teacher", Assert.Single(lesson1.TeacherNames));
        Assert.Equal("123R", lesson1.RoomName.Span);
    }

    [Fact]
    public void NoMultipleRoomName()
    {
        Assert.Throws<RoomAlreadySpecifiedException>(() =>
        {
            var lessons = ParseLessons([
                "Lesson",
                "Teacher, 123R, 124R",
            ]);
            _ = lessons;
        });
    }

    [Fact]
    public void StarInFrontOfLessonIsIgnored()
    {
        var lessons = ParseLessons([
            "*Lesson",
        ]);

        var lesson = Assert.Single(lessons);
        Assert.Equal("Lesson", lesson.LessonName.Span);
    }

    [Fact]
    public void TeacherNameCommaRoomNameSupported_EvenWithTerribleFormatting()
    {
        // Managementul proiectelor (sem)
        // Iu.Drăgălina ,  213a/4
        var lessons = ParseLessons([
            "Managementul proiectelor (sem)",
            "Iu.Drăgălina ,  213a/4",
        ]);

        var lesson = Assert.Single(lessons);
        AssertEqualName("Iu.Drăgălina", Assert.Single(lesson.TeacherNames));
        Assert.Equal("213a/4", lesson.RoomName.Span);
    }

    [Fact]
    public void DoubleTeacherFirstName()
    {
        var lessons = ParseLessons([
            "Montajul și imaginea video (lab, par)",
            "G.-M. Lastname",
        ]);

        var lesson = Assert.Single(lessons);
        AssertEqualName("G.-M. Lastname", Assert.Single(lesson.TeacherNames));
    }

    [Fact]
    public void TimeAfterNoRoom_NoRoomSpecifiedForLesson()
    {
        var lessons = ParseLessons([
            "Lesson One",
            "15:00 Lesson Two 123R",
        ]);

        Assert.Collection(lessons,
            lesson1 =>
            {
                Assert.Equal("Lesson One", lesson1.LessonName.Span);
                Assert.True(lesson1.RoomName.IsEmpty);
            },
            lesson2 =>
            {
                Assert.Equal("Lesson Two", lesson2.LessonName.Span);
                Assert.Equal("123R", lesson2.RoomName.Span);

                var time = TimeOnly.FromTimeSpan(TimeSpan.FromHours(15));
                Assert.Equal(time, lesson2.StartTime);
            });
    }

    [Fact]
    public void LessonName_DontInsertSpaceInBetweenWords()
    {
        var lessons = ParseLessons([
            "Lesson One",
        ], spaces: c =>
        {
            if (c.Lexer.Current.Value.Span.SequenceEqual("Lesson"))
            {
                c.Lexer.Move();

                Assert.True(c.Lexer.TryConsume(TokenType.Whitespace));
                Assert.True(c.Lexer.TryConsume(LessonTokenType.Word)); // One
                return c.DontInsertAll();
            }
            return c.DefaultAll();
        });

        var l = Assert.Single(lessons);
        Assert.Equal("LessonOne", l.LessonName.Span);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LessonName_InclusiveDontInsert(bool inclusive)
    {
        var lessons = ParseLessons([
            "Lesson One Two",
        ], spaces: c =>
        {
            if (c.Lexer.Current.Value.Span.SequenceEqual("Lesson"))
            {
                c.Lexer.Move();

                Assert.True(c.Lexer.TryConsume(TokenType.Whitespace));
                Assert.True(c.Lexer.TryConsume(LessonTokenType.Word)); // One
                return c.DontInsertAll(inclusive: inclusive);
            }
            return c.DefaultAll();
        });

        var l = Assert.Single(lessons);

        string correct;
        if (inclusive)
        {
            correct = "LessonOneTwo";
        }
        else
        {
            correct = "LessonOne Two";
        }
        Assert.Equal(correct, l.LessonName.Span);
    }

    [Theory]
    [InlineData("Node.JS")]
    // [InlineData("Node . JS")]
    [InlineData("Node. JS")]
    public void LessonName_NodeJsFromConfig(string nodejs)
    {
        var lessons = ParseLessons([
            $"Lesson {nodejs}",
        ], spaces: Config.WhiteSpaceActionCourseName);

        var l = Assert.Single(lessons);
        Assert.Equal("Lesson Node.JS", l.LessonName.Span);
    }

    [Fact]
    public void LessonName_NodeJsFromConfig_FullExample()
    {
        var lessons = ParseLessons([
            "9:45 Dezv. apl. server-side cu Node.js (opț)",
            "N.Nartea  145/4",
        ], spaces: Config.WhiteSpaceActionCourseName);

        var l = Assert.Single(lessons);
        Assert.Equal("Dezv. apl. server-side cu Node.js", l.LessonName.Span);
        var time = TimeOnly.FromTimeSpan(TimeSpan.FromHours(9).Add(TimeSpan.FromMinutes(45)));
        Assert.Equal(time, l.StartTime);
        Assert.Equal("opț", l.GroupName.Span);
        Assert.Equal(LessonType.Unspecified, l.LessonType);
        AssertEqualName("N.Nartea", Assert.Single(l.TeacherNames));
        Assert.Equal("145/4", l.RoomName.Span);
    }

    [Fact]
    public void SpecialCase_SubgroupName()
    {
        var lessons = ParseLessons([
            "S21 NameOne (lab), T.Teacher, 145/4",
            "S1 NameTwo (lab), T.Teacher, 143/4",
        ]);

        Assert.Collection(lessons,
            l1 =>
            {
                Assert.Equal("NameOne", l1.LessonName.Span);
                Assert.Equal("S21", l1.PartitionHint.Span);
            },
            l2 =>
            {
                Assert.Equal("NameTwo", l2.LessonName.Span);
                Assert.Equal("S1", l2.PartitionHint.Span);
            });
    }

    [Fact]
    public void NoSubGroup_CommasAfterModifiers()
    {
        var lessons = ParseLessons([
            "Fund. Progr. (prel),",
            "M. Pavel, 404/4",
        ]);

        Assert.Collection(lessons,
            l1 =>
            {
                Assert.Equal("Fund. Progr.", l1.LessonName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(LessonType.Prelegere, l1.LessonType);
                AssertEqualName("M.Pavel", Assert.Single(l1.TeacherNames));
                Assert.Equal("404/4", l1.RoomName.Span);
            });
    }

    [Fact]
    public void TestBreakingInFr()
    {
        var lessons = ParseLessons([
            "Elab. aplic. graf. (lab),\n M.Marin, 145a/4",
        ], Config.WhiteSpaceActionCourseName);

        Assert.Collection(lessons,
            l1 =>
            {
                Assert.Equal("Elab. aplic. graf.", l1.LessonName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(LessonType.Lab, l1.LessonType);
                AssertEqualName("M.Marin", Assert.Single(l1.TeacherNames));
                Assert.Equal("145a/4", l1.RoomName.Span);
            });
    }

    [Fact]
    public void TestSubGroupSeparatedNames()
    {
        var lessons = ParseLessons([
            "Matematica  discretă  (Algoritmica Grafurilor, curs, imp)",
            "A.Niculiță   213a/4",
        ], Config.WhiteSpaceActionCourseName);

        Assert.Collection(lessons,
            l1 =>
            {
                Assert.Equal("Matematica discretă", l1.LessonName.Span);
                Assert.Equal("Algoritmica Grafurilor", l1.GroupName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(LessonType.Curs, l1.LessonType);
                Assert.Equal(Parity.OddWeek, l1.Parity);
                AssertEqualName("A.Niculiță", Assert.Single(l1.TeacherNames));
                Assert.Equal("213a/4", l1.RoomName.Span);
            });
    }

    [Fact]
    public void TestSubGroupWithMultipleWords()
    {
        var lessons = ParseLessons([
            "Matematica  discretă  (Algoritmica Grafurilor, curs, imp)",
            "A.Niculiță   213a/4",
        ], Config.WhiteSpaceActionCourseName);

        Assert.Collection(lessons,
            l1 =>
            {
                Assert.Equal("Matematica discretă", l1.LessonName.Span);
                Assert.Equal("Algoritmica Grafurilor", l1.GroupName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(LessonType.Curs, l1.LessonType);
                Assert.Equal(Parity.OddWeek, l1.Parity);
                AssertEqualName("A.Niculiță", Assert.Single(l1.TeacherNames));
                Assert.Equal("213a/4", l1.RoomName.Span);
            });
    }

    [Fact(Skip = "Not applicable currenlty, figure out later.")]
    public void CommaSeparatingLessonNames_FailsTeacherRecursion()
    {
        var lessons = ParseLessons([
            "test (curs), Spring: Spring (lab)",
        ]);

        Assert.Collection(lessons,
            l1 =>
            {
                Assert.Equal("test", l1.LessonName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(LessonType.Curs, l1.LessonType);
                Assert.Equal(Parity.EveryWeek, l1.Parity);
            },
            l2 =>
            {
                Assert.Equal("Spring", l2.LessonName.Span);
                Assert.Equal("Spring", l2.PartitionHint.Span);
                Assert.Equal(LessonType.Lab, l2.LessonType);
                Assert.Equal(Parity.EveryWeek, l2.Parity);
            });
    }

    [Fact]
    public void TestSubGroupOnSameLineAsCourseName()
    {
        var lessons = ParseLessons([
            "8:00 Dezvoltare de aplicații enterprise (curs), Spring:  M.Dodi  222/4",
            "9:45  Dezvoltare de aplicații enterprise (lab),  Spring:  M.Dodi  326/4",
        ]);

        Assert.Collection(lessons,
            l1 =>
            {
                Assert.Equal("Dezvoltare de aplicații enterprise", l1.LessonName.Span);
                Assert.Equal("Spring", l1.PartitionHint.Span);
                Assert.Equal(LessonType.Curs, l1.LessonType);
                Assert.Equal(new TimeOnly(hour: 8, minute: 00), l1.StartTime);
                Assert.Equal(Parity.EveryWeek, l1.Parity);
                AssertEqualName("M.Dodi", Assert.Single(l1.TeacherNames));
                Assert.Equal("222/4", l1.RoomName.Span);
            },
            l2 =>
            {
                Assert.Equal("Dezvoltare de aplicații enterprise", l2.LessonName.Span);
                Assert.Equal("Spring", l2.PartitionHint.Span);
                Assert.Equal(LessonType.Lab, l2.LessonType);
                Assert.Equal(new TimeOnly(hour: 9, minute: 45), l2.StartTime);
                Assert.Equal(Parity.EveryWeek, l2.Parity);
                AssertEqualName("M.Dodi", Assert.Single(l2.TeacherNames));
                Assert.Equal("326/4", l2.RoomName.Span);
            });
    }

    [Fact]
    public void EmptyStringTest()
    {
        var lessons = ParseLessons([
            "",
        ]);

        Assert.Empty(lessons);
    }

    [Fact(Skip = "Implement this later, need to bring the domain into the parser as an abstraction.")]
    public void BreaksFR()
    {
        var str = """
        I. Javascript (lab),
        N. Nartea, 145/4
         II: POO (lab),
        Gh. Latul, 216a/4a
        """;

        var lessons = ParseLessons([str], Config.WhiteSpaceActionCourseName);
        Assert.Collection(lessons,
            l1 =>
            {
                Assert.Equal("I", l1.PartitionHint.Span);
                Assert.Equal("Javascript", l1.LessonName.Span);
                Assert.Equal(LessonType.Lab, l1.LessonType);
                AssertEqualName("N. Nartea", Assert.Single(l1.TeacherNames));
                Assert.Equal("145/4", l1.RoomName.Span);
            },
            l2 =>
            {
                Assert.Equal("II", l2.PartitionHint.Span);
                Assert.Equal("POO", l2.LessonName.Span);
                Assert.Equal(LessonType.Lab, l2.LessonType);
                AssertEqualName("Gh. Latul", Assert.Single(l2.TeacherNames));
                Assert.Equal("216a/4a", l2.RoomName.Span);
            });
    }

    [Fact]
    public void DoubleNameTest()
    {
        var str =
            """
            Design soft. (prel),
            G-C. Stănescu, 404/4
            """;

        var lessons = ParseLessons([str], Config.WhiteSpaceActionCourseName);
        var lesson = Assert.Single(lessons);
        Assert.Equal("Design soft.", lesson.LessonName.Span);
        Assert.Equal(LessonType.Prelegere, lesson.LessonType);
        AssertEqualName("G.-C. Stănescu", Assert.Single(lesson.TeacherNames));
        Assert.Equal("404/4", lesson.RoomName.Span);
    }

    [Fact]
    public void GroupNameAsModifier()
    {
        var lessons = ParseLessons([
            "Test (lab,imp,IA2303)",
        ]);
        var lesson = Assert.Single(lessons);
        Assert.Equal("Test", lesson.LessonName.Span);
        Assert.Equal(LessonType.Lab, lesson.LessonType);
        Assert.Equal(Parity.OddWeek, lesson.Parity);
        Assert.Equal("IA2303", lesson.GroupName.Span);

    }

    [Theory]
    [InlineData("UI-1")]
    [InlineData("UI-2")]
    public void DashedLegacySubGroupsParseAsRawLabels(string subGroup)
    {
        var lessons = ParseLessons([
            $"8:00 Limba engleza ({subGroup})",
            "T. Teacher 214/4",
        ]);

        var lesson = Assert.Single(lessons);
        Assert.Equal(subGroup, lesson.PartitionHint.Span.ToString());
    }

    [Fact]
    public void ExplicitNonBeginnersIsRejected()
    {
        Assert.ThrowsAny<Exception>(() => ParseLessons([
            "8:00 Limba engleza (nuîncepători)",
            "T. Teacher 214/4",
        ]));
    }

    [Fact]
    public void ShortenedGroupNameAsModifier()
    {
        var lessons = ParseLessons([
            "8:00  L. str. (încep.)",
            "G.Ciudin   222/4",
            "15:00 Limba straina",
            "O.Bașirov   419/4",
        ]);

        Assert.Collection(lessons,
            first =>
            {
                Assert.Equal(new TimeOnly(8, 0), first.StartTime);
                Assert.Equal("încep.", first.GroupName.Span);
                Assert.True(SpecialSubGroups.TryFromNamePrefix(first.GroupName.Span, out var subGroup));
                Assert.Equal(SpecialSubGroups.Beginners, subGroup);
                AssertEqualName("G. Ciudin", Assert.Single(first.TeacherNames));
                Assert.Equal("222/4", first.RoomName.Span);
            },
            second =>
            {
                Assert.Equal(new TimeOnly(15, 0), second.StartTime);
                AssertEqualName("O. Bașirov", Assert.Single(second.TeacherNames));
                Assert.Equal("419/4", second.RoomName.Span);
            });
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("r")]
    [InlineData("r.")]
    [InlineData("a")]
    [InlineData("a.")]
    public void SpecialSubGroupPrefixRequiresAtLeastTwoCharacters(string value)
    {
        Assert.False(SpecialSubGroups.TryFromNamePrefix(value, out _));
    }

    [Fact]
    public void SpecialSubGroupPrefixAcceptsTwoCharactersAndLongerAbbreviations()
    {
        Assert.True(SpecialSubGroups.TryFromNamePrefix("ro", out var ro));
        Assert.Equal(SpecialSubGroups.Ro, ro);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("ru", out var ru));
        Assert.Equal(SpecialSubGroups.Ru, ru);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("AG", out var ag));
        Assert.Equal(new SubGroup(Specialization.AG.Value!), ag);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("ui", out var ui));
        Assert.Equal(new SubGroup(Specialization.UI.Value!), ui);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("în", out var beginners));
        Assert.Equal(SpecialSubGroups.Beginners, beginners);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("încep.", out beginners));
        Assert.Equal(SpecialSubGroups.Beginners, beginners);
    }

    [Fact]
    public void SpecialSubGroupPrefixPrefersExactLegacyMatchOverLongerSpecialization()
    {
        Assert.True(SpecialSubGroups.TryFromNamePrefix("GA", out var ga));
        Assert.Equal(new SubGroup("GA"), ga);
    }

    [Fact]
    public void DotInTimeAllowed()
    {
        var lessons = ParseLessons(["17.30 Sisteme operare (exam)", "M. Butnaru, 218/4a"]);

        var lesson = Assert.Single(lessons);
        Assert.Equal("Sisteme operare", lesson.LessonName.Span);
        Assert.Equal(new TimeOnly(hour: 17, minute: 30), lesson.StartTime);
        Assert.Equal(LessonType.Exam, lesson.LessonType);
        Assert.Equal("218/4a", lesson.RoomName.Span);
        AssertEqualName("M. Butnaru", Assert.Single(lesson.TeacherNames));
    }

    [Fact(Skip = "Not implemented, this is hard to implement, maybe do later, maybe delete all this code")]
    public void LessonTypeAsModifierKeyAllowed()
    {
        var lessons = ParseLessons(
        [
            "Matem.discr.(Logica)",
            "I.Cucu (curs) 239/4,",
            "N.Cuciuc (lab) 239/4",
        ]);

        void Common(in ParsedLesson l)
        {
            Assert.Equal("Matem. discr.", l.LessonName.Span);
            Assert.Equal("Logica", l.GroupName.Span);
            Assert.Equal("239/4", l.RoomName.Span);
        }
        Assert.Collection(lessons,
            l1 =>
            {
                Common(l1);
                Assert.Equal(LessonType.Curs, l1.LessonType);
                AssertEqualName("I. Cucu", Assert.Single(l1.TeacherNames));
            },
            l2 =>
            {
                Common(l2);
                Assert.Equal(LessonType.Curs, l2.LessonType);
                AssertEqualName("N. Cuciuc", Assert.Single(l2.TeacherNames));
            });
    }

    [Fact]
    public void sb1_ShortModifierForm_WithModifierValue_Passes()
    {
        var lessons = ParseLessons(["Etica și dreptul în Inteligența Artificială (lab, sb1 par, sb 2 imp), A. Poiată, 145/4"]);

        void Common(in ParsedLesson l)
        {
            Assert.Equal("Etica și dreptul în Inteligența Artificială", l.LessonName.Span);
            Assert.Equal(LessonType.Lab, l.LessonType);
            Assert.Equal("145/4", l.RoomName.Span);
            AssertEqualName("A. Poiată", Assert.Single(l.TeacherNames));
        }

        Assert.Collection(lessons,
            l1 =>
            {
                Common(l1);
                Assert.Equal("I", l1.PartitionHint.Span);
                Assert.Equal(Parity.EvenWeek, l1.Parity);
            },
            l2 =>
            {
                Common(l2);
                Assert.Equal("II", l2.PartitionHint.Span);
                Assert.Equal(Parity.OddWeek, l2.Parity);
            });
    }

    [Fact]
    public void sb1_ShortModifierForm_WithoutModifierValue_ForSettingSubGroup()
    {
        var lessons = ParseLessons(["Proiect practic de știința datelor (lab, sb.1), V. Ursachi, 219/4a"]);

        var l1 = Assert.Single(lessons);
        Assert.Equal("Proiect practic de știința datelor", l1.LessonName.Span);
        AssertEqualName("V. Ursachi", Assert.Single(l1.TeacherNames));
        Assert.Equal(LessonType.Lab, l1.LessonType);
        Assert.Equal("I", l1.PartitionHint.Span);
        Assert.Equal("219/4a", l1.RoomName.Span);
    }
}
