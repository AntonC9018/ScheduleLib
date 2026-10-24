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
        Assert.Equal(expected: output, actual: ret);
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
            Assert.Equal(expected: time, actual: lesson.StartTime);
            Assert.True(lesson.PartitionHint.IsEmpty);
            var teacherName = Assert.Single(lesson.TeacherNames);
            AssertEqualName("V.Miron", teacherName);
            Assert.Equal(expected: "433/3", actual: lesson.RoomName.Span);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                CheckCommon(lesson1);
                Assert.Equal(expected: Parity.OddWeek, actual: lesson1.Parity);
                Assert.Equal(expected: LessonType.Curs, actual: lesson1.LessonType);
                Assert.Equal(expected: "Opț. psihol.", actual: lesson1.LessonName.Span);
            },
            lesson2 =>
            {
                CheckCommon(lesson2);
                Assert.Equal(expected: Parity.EvenWeek, actual: lesson2.Parity);
                Assert.Equal(expected: LessonType.Seminar, actual: lesson2.LessonType);
                Assert.Equal(expected: "Psihologie", actual: lesson2.LessonName.Span);
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
                Assert.Equal(expected: "404/4", actual: lesson.RoomName.Span);
                Assert.Equal(expected: LessonType.Curs, actual: lesson.LessonType);
                Assert.Equal(expected: "Matematica discretă (Logica)", actual: lesson.LessonName.Span);
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
                Assert.Equal(expected: "____", actual: lesson.RoomName.Span);
                Assert.Equal(expected: LessonType.Curs, actual: lesson.LessonType);
                Assert.Equal(expected: "Option. didact.", actual: lesson.LessonName.Span);
            });
    }

    [Fact]
    public void ColonAllowedInCourseName()
    {
        var lesson = Assert.Single(ParseLessons([
            "Disciplină umanistică opțională: Antreprenoriat inovativ (curs), I. Dobrovolschi 528/3",
        ]));

        Assert.Equal(expected: "Disciplină umanistică opțională: Antreprenoriat inovativ", actual: lesson.LessonName.Span);
        Assert.Equal(expected: LessonType.Curs, actual: lesson.LessonType);
        AssertEqualName("I. Dobrovolschi", Assert.Single(lesson.TeacherNames));
        Assert.Equal(expected: "528/3", actual: lesson.RoomName.Span);
    }

    [Fact]
    public void CourseWithRoomButNoTeacherDoesNotReadLastTeacher()
    {
        var lesson = Assert.Single(ParseLessons([
            "Disciplină umanistică opțională: Psihologie (curs), 113/4",
        ]));

        Assert.Equal(expected: "Disciplină umanistică opțională: Psihologie", actual: lesson.LessonName.Span);
        Assert.Equal(expected: LessonType.Curs, actual: lesson.LessonType);
        Assert.Empty(lesson.TeacherNames);
        Assert.Equal(expected: "113/4", actual: lesson.RoomName.Span);
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
                Assert.Equal(expected: Time(8), actual: lesson1.StartTime);
                Assert.Equal(expected: "CV", actual: lesson1.PartitionHint.Span);
                AssertEqualName("M.Croitor", Assert.Single(lesson1.TeacherNames));
                Assert.Equal(expected: "326/4", actual: lesson1.RoomName.Span);
                Assert.Equal(expected: LessonType.Lab, actual: lesson1.LessonType);
                Assert.Equal(expected: Parity.OddWeek, actual: lesson1.Parity);
                Assert.Equal(expected: "Containerizare și virtualizare", actual: lesson1.LessonName.Span);
            },
            lesson2 =>
            {
                Assert.Equal(expected: Time(15), actual: lesson2.StartTime);
                Assert.Equal(expected: "WR", actual: lesson2.PartitionHint.Span);
                AssertEqualName("A.Donu", Assert.Single(lesson2.TeacherNames));
                Assert.Equal(expected: "213a/4", actual: lesson2.RoomName.Span);
                Assert.Equal(expected: LessonType.Curs, actual: lesson2.LessonType);
                Assert.Equal(expected: Parity.EveryWeek, actual: lesson2.Parity);
                Assert.Equal(expected: "Dezvoltare de aplicații WEB cu React", actual: lesson2.LessonName.Span);
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
            Assert.Equal(expected: LessonType.Lab, actual: lesson.LessonType);
            Assert.Equal(expected: "Dezvoltare de aplicații WEB cu React", actual: lesson.LessonName.Span);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                CheckCommon(lesson1);
                Assert.Equal(expected: "WR1", actual: lesson1.PartitionHint.Span);
                AssertEqualName("A.Donu", Assert.Single(lesson1.TeacherNames));
                Assert.Equal(expected: "143/4", actual: lesson1.RoomName.Span);
            },
            lesson2 =>
            {
                CheckCommon(lesson2);
                Assert.Equal(expected: "WR2", actual: lesson2.PartitionHint.Span);
                AssertEqualName("Cr.Crudu", Assert.Single(lesson2.TeacherNames));
                Assert.Equal(expected: "145a/4", actual: lesson2.RoomName.Span);
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
                Assert.Equal(expected: "HTML", actual: first.LessonName.Span);
                Assert.Equal(expected: LessonType.Lab, actual: first.LessonType);
                Assert.Equal(expected: "I", actual: first.PartitionHint.Span);
                AssertEqualName("B. Vișnevschi", Assert.Single(first.TeacherNames));
                Assert.Equal(expected: "218/4a", actual: first.RoomName.Span);
            },
            second =>
            {
                Assert.Equal(expected: "HTML", actual: second.LessonName.Span);
                Assert.Equal(expected: LessonType.Lab, actual: second.LessonType);
                Assert.Equal(expected: "II", actual: second.PartitionHint.Span);
                AssertEqualName("V. Vișnevschi", Assert.Single(second.TeacherNames));
                Assert.Equal(expected: "219/4a", actual: second.RoomName.Span);
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

        Assert.Contains(lessons, x => Check(x, lessonName: "LessonA", groupName: "A", teacherName: "TeacherA"));
        Assert.Contains(lessons, x => Check(x, lessonName: "LessonB", groupName: "A", teacherName: "TeacherA"));
        Assert.Contains(lessons, x => Check(x, lessonName: "LessonA", groupName: "B", teacherName: "TeacherB"));
        Assert.Contains(lessons, x => Check(x, lessonName: "LessonB", groupName: "B", teacherName: "TeacherB"));
        Assert.Equal(expected: 4, actual: lessons.Length);
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
            Assert.Equal(expected: "Lesson", actual: lesson.LessonName.Span);
            Assert.Equal(expected: Parity.EvenWeek, actual: lesson.Parity);
            Assert.Equal(expected: LessonType.Curs, actual: lesson.LessonType);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Lesson(lesson1);
                Assert.Equal(expected: "A", actual: lesson1.PartitionHint.Span);
                AssertEqualName("TeacherA", Assert.Single(lesson1.TeacherNames));
            },
            lesson2 =>
            {
                Lesson(lesson2);
                Assert.Equal(expected: "B", actual: lesson2.PartitionHint.Span);
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
                Assert.Equal(expected: "Lesson", actual: lesson1.LessonName.Span);
                Assert.Equal(expected: "A", actual: lesson1.PartitionHint.Span);
                Assert.Equal(expected: Parity.EvenWeek, actual: lesson1.Parity);
                AssertEqualName("TeacherA", Assert.Single(lesson1.TeacherNames));
            },
            lesson2 =>
            {
                Assert.Equal(expected: "Lesson", actual: lesson2.LessonName.Span);
                Assert.Equal(expected: "B", actual: lesson2.PartitionHint.Span);
                Assert.Equal(expected: Parity.OddWeek, actual: lesson2.Parity);
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
                Assert.Equal(expected: "Lesson", actual: lesson1.LessonName.Span);
                Assert.Equal(expected: "A", actual: lesson1.PartitionHint.Span);
                Assert.Equal(expected: Parity.EvenWeek, actual: lesson1.Parity);
                Assert.Empty(lesson1.TeacherNames);
            },
            lesson2 =>
            {
                Assert.Equal(expected: "Lesson", actual: lesson2.LessonName.Span);
                Assert.Equal(expected: "B", actual: lesson2.PartitionHint.Span);
                Assert.Equal(expected: Parity.EveryWeek, actual: lesson2.Parity);
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
            Assert.Equal(expected: "Lesson", actual: lesson.LessonName.Span);
            AssertEqualName("TeacherA", Assert.Single(lesson.TeacherNames));
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Common(lesson1);
                Assert.Equal(expected: "A", actual: lesson1.PartitionHint.Span);
                Assert.Equal(expected: Parity.EvenWeek, actual: lesson1.Parity);
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
            Assert.Equal(expected: "LessonA", actual: lesson.LessonName.Span);
            AssertEqualName("TeacherA", Assert.Single(lesson.TeacherNames));
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Common(lesson1);
                Assert.Equal(expected: Parity.EvenWeek, actual: lesson1.Parity);
                Assert.Equal(expected: LessonType.Curs, actual: lesson1.LessonType);
            },
            lesson2 =>
            {
                Common(lesson2);
                Assert.Equal(expected: Parity.OddWeek, actual: lesson2.Parity);
                Assert.Equal(expected: LessonType.Seminar, actual: lesson2.LessonType);
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
            Assert.Equal(expected: "MTA 3D", actual: lesson.LessonName.Span);
            Assert.Equal(expected: LessonType.Lab, actual: lesson.LessonType);
            AssertEqualName("A.Schiopu", Assert.Single(lesson.TeacherNames));
            Assert.Equal(expected: "251/4", actual: lesson.RoomName.Span);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Common(lesson1);
                Assert.Equal(expected: "I", actual: lesson1.PartitionHint.Span);
                Assert.Equal(expected: Parity.OddWeek, actual: lesson1.Parity);
            },
            lesson2 =>
            {
                Common(lesson2);
                Assert.Equal(expected: "II", actual: lesson2.PartitionHint.Span);
                Assert.Equal(expected: Parity.EvenWeek, actual: lesson2.Parity);
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
                Assert.Equal(expected: "Lesson", actual: lesson.LessonName.Span);
            });
    }

    [Fact]
    public void NoTeacherWithModifierTest()
    {
        var lessons = ParseLessons([
            "Educa.fizică (imp)",
        ]);

        var lesson = Assert.Single(lessons);
        Assert.Equal(expected: "Educa. fizică", actual: lesson.LessonName.Span);
        Assert.Equal(expected: Parity.OddWeek, actual: lesson.Parity);
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
                Assert.Equal(expected: "Lesson", actual: lesson.LessonName.Span);
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
        Assert.Equal(expected: "Lesson", actual: lesson.LessonName.Span);
        Assert.Equal(expected: "Mediacor, etajul II", actual: lesson.RoomName.Span);
    }

    [Fact]
    public void MediacorRoomWithTeacher()
    {
        var lessons = ParseLessons([
            "Lesson",
            "Teacher  Mediacor, etajul I",
        ]);

        var lesson = Assert.Single(lessons);
        Assert.Equal(expected: "Lesson", actual: lesson.LessonName.Span);
        Assert.Equal(expected: "Teacher", actual: Assert.Single(lesson.TeacherNames).LastName[0].Span);
        Assert.Equal(expected: "Mediacor, etajul I", actual: lesson.RoomName.Span);
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
                Assert.Equal(expected: "Lesson", actual: lesson1.LessonName.Span);
                AssertEqualName("Teacher", Assert.Single(lesson1.TeacherNames));
            },
            lesson2 =>
            {
                Assert.Equal(expected: "Other, Lesson", actual: lesson2.LessonName.Span);
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
        Assert.Equal(expected: "Lesson", actual: lesson1.LessonName.Span);
        AssertEqualName("Teacher", Assert.Single(lesson1.TeacherNames));
        Assert.Equal(expected: "123R", actual: lesson1.RoomName.Span);
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
        Assert.Equal(expected: "Lesson", actual: lesson.LessonName.Span);
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
        Assert.Equal(expected: "213a/4", actual: lesson.RoomName.Span);
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
                Assert.Equal(expected: "Lesson One", actual: lesson1.LessonName.Span);
                Assert.True(lesson1.RoomName.IsEmpty);
            },
            lesson2 =>
            {
                Assert.Equal(expected: "Lesson Two", actual: lesson2.LessonName.Span);
                Assert.Equal(expected: "123R", actual: lesson2.RoomName.Span);

                var time = TimeOnly.FromTimeSpan(TimeSpan.FromHours(15));
                Assert.Equal(expected: time, actual: lesson2.StartTime);
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
        Assert.Equal(expected: "LessonOne", actual: l.LessonName.Span);
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
        Assert.Equal(expected: correct, actual: l.LessonName.Span);
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
        Assert.Equal(expected: "Lesson Node.JS", actual: l.LessonName.Span);
    }

    [Fact]
    public void LessonName_NodeJsFromConfig_FullExample()
    {
        var lessons = ParseLessons([
            "9:45 Dezv. apl. server-side cu Node.js (opț)",
            "N.Nartea  145/4",
        ], spaces: Config.WhiteSpaceActionCourseName);

        var l = Assert.Single(lessons);
        Assert.Equal(expected: "Dezv. apl. server-side cu Node.js", actual: l.LessonName.Span);
        var time = TimeOnly.FromTimeSpan(TimeSpan.FromHours(9).Add(TimeSpan.FromMinutes(45)));
        Assert.Equal(expected: time, actual: l.StartTime);
        Assert.Equal(expected: "opț", actual: l.GroupName.Span);
        Assert.Equal(expected: LessonType.Unspecified, actual: l.LessonType);
        AssertEqualName("N.Nartea", Assert.Single(l.TeacherNames));
        Assert.Equal(expected: "145/4", actual: l.RoomName.Span);
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
                Assert.Equal(expected: "NameOne", actual: l1.LessonName.Span);
                Assert.Equal(expected: "S21", actual: l1.PartitionHint.Span);
            },
            l2 =>
            {
                Assert.Equal(expected: "NameTwo", actual: l2.LessonName.Span);
                Assert.Equal(expected: "S1", actual: l2.PartitionHint.Span);
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
                Assert.Equal(expected: "Fund. Progr.", actual: l1.LessonName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(expected: LessonType.Prelegere, actual: l1.LessonType);
                AssertEqualName("M.Pavel", Assert.Single(l1.TeacherNames));
                Assert.Equal(expected: "404/4", actual: l1.RoomName.Span);
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
                Assert.Equal(expected: "Elab. aplic. graf.", actual: l1.LessonName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(expected: LessonType.Lab, actual: l1.LessonType);
                AssertEqualName("M.Marin", Assert.Single(l1.TeacherNames));
                Assert.Equal(expected: "145a/4", actual: l1.RoomName.Span);
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
                Assert.Equal(expected: "Matematica discretă", actual: l1.LessonName.Span);
                Assert.Equal(expected: "Algoritmica Grafurilor", actual: l1.GroupName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(expected: LessonType.Curs, actual: l1.LessonType);
                Assert.Equal(expected: Parity.OddWeek, actual: l1.Parity);
                AssertEqualName("A.Niculiță", Assert.Single(l1.TeacherNames));
                Assert.Equal(expected: "213a/4", actual: l1.RoomName.Span);
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
                Assert.Equal(expected: "Matematica discretă", actual: l1.LessonName.Span);
                Assert.Equal(expected: "Algoritmica Grafurilor", actual: l1.GroupName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(expected: LessonType.Curs, actual: l1.LessonType);
                Assert.Equal(expected: Parity.OddWeek, actual: l1.Parity);
                AssertEqualName("A.Niculiță", Assert.Single(l1.TeacherNames));
                Assert.Equal(expected: "213a/4", actual: l1.RoomName.Span);
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
                Assert.Equal(expected: "test", actual: l1.LessonName.Span);
                Assert.True(l1.PartitionHint.IsEmpty);
                Assert.Equal(expected: LessonType.Curs, actual: l1.LessonType);
                Assert.Equal(expected: Parity.EveryWeek, actual: l1.Parity);
            },
            l2 =>
            {
                Assert.Equal(expected: "Spring", actual: l2.LessonName.Span);
                Assert.Equal(expected: "Spring", actual: l2.PartitionHint.Span);
                Assert.Equal(expected: LessonType.Lab, actual: l2.LessonType);
                Assert.Equal(expected: Parity.EveryWeek, actual: l2.Parity);
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
                Assert.Equal(expected: "Dezvoltare de aplicații enterprise", actual: l1.LessonName.Span);
                Assert.Equal(expected: "Spring", actual: l1.PartitionHint.Span);
                Assert.Equal(expected: LessonType.Curs, actual: l1.LessonType);
                Assert.Equal(expected: new TimeOnly(hour: 8, minute: 00), actual: l1.StartTime);
                Assert.Equal(expected: Parity.EveryWeek, actual: l1.Parity);
                AssertEqualName("M.Dodi", Assert.Single(l1.TeacherNames));
                Assert.Equal(expected: "222/4", actual: l1.RoomName.Span);
            },
            l2 =>
            {
                Assert.Equal(expected: "Dezvoltare de aplicații enterprise", actual: l2.LessonName.Span);
                Assert.Equal(expected: "Spring", actual: l2.PartitionHint.Span);
                Assert.Equal(expected: LessonType.Lab, actual: l2.LessonType);
                Assert.Equal(expected: new TimeOnly(hour: 9, minute: 45), actual: l2.StartTime);
                Assert.Equal(expected: Parity.EveryWeek, actual: l2.Parity);
                AssertEqualName("M.Dodi", Assert.Single(l2.TeacherNames));
                Assert.Equal(expected: "326/4", actual: l2.RoomName.Span);
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
                Assert.Equal(expected: "I", actual: l1.PartitionHint.Span);
                Assert.Equal(expected: "Javascript", actual: l1.LessonName.Span);
                Assert.Equal(expected: LessonType.Lab, actual: l1.LessonType);
                AssertEqualName("N. Nartea", Assert.Single(l1.TeacherNames));
                Assert.Equal(expected: "145/4", actual: l1.RoomName.Span);
            },
            l2 =>
            {
                Assert.Equal(expected: "II", actual: l2.PartitionHint.Span);
                Assert.Equal(expected: "POO", actual: l2.LessonName.Span);
                Assert.Equal(expected: LessonType.Lab, actual: l2.LessonType);
                AssertEqualName("Gh. Latul", Assert.Single(l2.TeacherNames));
                Assert.Equal(expected: "216a/4a", actual: l2.RoomName.Span);
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
        Assert.Equal(expected: "Design soft.", actual: lesson.LessonName.Span);
        Assert.Equal(expected: LessonType.Prelegere, actual: lesson.LessonType);
        AssertEqualName("G.-C. Stănescu", Assert.Single(lesson.TeacherNames));
        Assert.Equal(expected: "404/4", actual: lesson.RoomName.Span);
    }

    [Fact]
    public void GroupNameAsModifier()
    {
        var lessons = ParseLessons([
            "Test (lab,imp,IA2303)",
        ]);
        var lesson = Assert.Single(lessons);
        Assert.Equal(expected: "Test", actual: lesson.LessonName.Span);
        Assert.Equal(expected: LessonType.Lab, actual: lesson.LessonType);
        Assert.Equal(expected: Parity.OddWeek, actual: lesson.Parity);
        Assert.Equal(expected: "IA2303", actual: lesson.GroupName.Span);

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
        Assert.Equal(expected: subGroup, actual: lesson.PartitionHint.Span.ToString());
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
                Assert.Equal(expected: new TimeOnly(hour: 8, minute: 0), actual: first.StartTime);
                Assert.Equal(expected: "încep.", actual: first.GroupName.Span);
                Assert.True(SpecialSubGroups.TryFromNamePrefix(first.GroupName.Span, out var subGroup));
                Assert.Equal(expected: SpecialSubGroups.Beginners, actual: subGroup);
                AssertEqualName("G. Ciudin", Assert.Single(first.TeacherNames));
                Assert.Equal(expected: "222/4", actual: first.RoomName.Span);
            },
            second =>
            {
                Assert.Equal(expected: new TimeOnly(hour: 15, minute: 0), actual: second.StartTime);
                AssertEqualName("O. Bașirov", Assert.Single(second.TeacherNames));
                Assert.Equal(expected: "419/4", actual: second.RoomName.Span);
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
        Assert.Equal(expected: SpecialSubGroups.Ro, actual: ro);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("ru", out var ru));
        Assert.Equal(expected: SpecialSubGroups.Ru, actual: ru);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("AG", out var ag));
        Assert.Equal(expected: new SubGroup(Specialization.AG.Value!), actual: ag);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("ui", out var ui));
        Assert.Equal(expected: new SubGroup(Specialization.UI.Value!), actual: ui);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("în", out var beginners));
        Assert.Equal(expected: SpecialSubGroups.Beginners, actual: beginners);

        Assert.True(SpecialSubGroups.TryFromNamePrefix("încep.", out beginners));
        Assert.Equal(expected: SpecialSubGroups.Beginners, actual: beginners);
    }

    [Fact]
    public void SpecialSubGroupPrefixPrefersExactLegacyMatchOverLongerSpecialization()
    {
        Assert.True(SpecialSubGroups.TryFromNamePrefix("GA", out var ga));
        Assert.Equal(expected: new SubGroup("GA"), actual: ga);
    }

    [Fact]
    public void DotInTimeAllowed()
    {
        var lessons = ParseLessons(["17.30 Sisteme operare (exam)", "M. Butnaru, 218/4a"]);

        var lesson = Assert.Single(lessons);
        Assert.Equal(expected: "Sisteme operare", actual: lesson.LessonName.Span);
        Assert.Equal(expected: new TimeOnly(hour: 17, minute: 30), actual: lesson.StartTime);
        Assert.Equal(expected: LessonType.Exam, actual: lesson.LessonType);
        Assert.Equal(expected: "218/4a", actual: lesson.RoomName.Span);
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
            Assert.Equal(expected: "Matem. discr.", actual: l.LessonName.Span);
            Assert.Equal(expected: "Logica", actual: l.GroupName.Span);
            Assert.Equal(expected: "239/4", actual: l.RoomName.Span);
        }
        Assert.Collection(lessons,
            l1 =>
            {
                Common(l1);
                Assert.Equal(expected: LessonType.Curs, actual: l1.LessonType);
                AssertEqualName("I. Cucu", Assert.Single(l1.TeacherNames));
            },
            l2 =>
            {
                Common(l2);
                Assert.Equal(expected: LessonType.Curs, actual: l2.LessonType);
                AssertEqualName("N. Cuciuc", Assert.Single(l2.TeacherNames));
            });
    }

    [Fact]
    public void sb1_ShortModifierForm_WithModifierValue_Passes()
    {
        var lessons = ParseLessons(["Etica și dreptul în Inteligența Artificială (lab, sb1 par, sb 2 imp), A. Poiată, 145/4"]);

        void Common(in ParsedLesson l)
        {
            Assert.Equal(expected: "Etica și dreptul în Inteligența Artificială", actual: l.LessonName.Span);
            Assert.Equal(expected: LessonType.Lab, actual: l.LessonType);
            Assert.Equal(expected: "145/4", actual: l.RoomName.Span);
            AssertEqualName("A. Poiată", Assert.Single(l.TeacherNames));
        }

        Assert.Collection(lessons,
            l1 =>
            {
                Common(l1);
                Assert.Equal(expected: "I", actual: l1.PartitionHint.Span);
                Assert.Equal(expected: Parity.EvenWeek, actual: l1.Parity);
            },
            l2 =>
            {
                Common(l2);
                Assert.Equal(expected: "II", actual: l2.PartitionHint.Span);
                Assert.Equal(expected: Parity.OddWeek, actual: l2.Parity);
            });
    }

    [Fact]
    public void sb1_ShortModifierForm_WithoutModifierValue_ForSettingSubGroup()
    {
        var lessons = ParseLessons(["Proiect practic de știința datelor (lab, sb.1), V. Ursachi, 219/4a"]);

        var l1 = Assert.Single(lessons);
        Assert.Equal(expected: "Proiect practic de știința datelor", actual: l1.LessonName.Span);
        AssertEqualName("V. Ursachi", Assert.Single(l1.TeacherNames));
        Assert.Equal(expected: LessonType.Lab, actual: l1.LessonType);
        Assert.Equal(expected: "I", actual: l1.PartitionHint.Span);
        Assert.Equal(expected: "219/4a", actual: l1.RoomName.Span);
    }
}
