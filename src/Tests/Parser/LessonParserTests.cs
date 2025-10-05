using Argon;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Tests;

public sealed class LessonParserTests
{
    private void AssertEqualName(string expected, TeacherName actual)
    {
        Assert.True(CheckEqualName(expected, actual));
    }

    [Fact]
    public async Task LexerTest()
    {
        var strings = new List<string>
        {
            "15:00 Opț.psihol. (curs,imp),",
            "Psihologie (sem,par)",
            "V.Miron  433/3",
        };
        var lexer = LessonParsingHelper.CreateLexer(strings.GetEnumerator());
        List<Token> result = new();
        while (!lexer.IsEmpty())
        {
            result.Add(lexer.Peek(1));
            lexer.Move();
        }
        Assert.DoesNotContain(result, x => x.Type == TokenType.Invalid);

        var verifyModels = result.Select(x => new
        {
            Type = lexer.TokenTypeLabels.Get(x.Type),
            Value = x.Value.ToString(),
            x.Span.Row,
            ColStart = x.Span.ColStart.Index,
            ColEnd = x.Span.ColEnd.Index,
        });
        await Verify(verifyModels)
            .UseStrictJson()
            .AddExtraSettings(x => x.DefaultValueHandling = DefaultValueHandling.Include);
    }

    private ParsedLesson[] ParseLessons(string[] lines)
    {
        return LessonParsingHelper.ParseLessons(new()
        {
            StringBuilder = new(),
            Lines = lines,
        }).ToArray();
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
            Assert.Null(lesson.SubGroup.Value);
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
                Assert.Equal("CV", lesson1.SubGroup.Value);
                AssertEqualName("M.Croitor", Assert.Single(lesson1.TeacherNames));
                Assert.Equal("326/4", lesson1.RoomName.Span);
                Assert.Equal(LessonType.Lab, lesson1.LessonType);
                Assert.Equal(Parity.OddWeek, lesson1.Parity);
                Assert.Equal("Containerizare și virtualizare", lesson1.LessonName.Span);
            },
            lesson2 =>
            {
                Assert.Equal(Time(15), lesson2.StartTime);
                Assert.Equal("WR", lesson2.SubGroup.Value);
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
                Assert.Equal("WR1", lesson1.SubGroup.Value);
                AssertEqualName("A.Donu", Assert.Single(lesson1.TeacherNames));
                Assert.Equal("143/4", lesson1.RoomName.Span);
            },
            lesson2 =>
            {
                CheckCommon(lesson2);
                Assert.Equal("WR2", lesson2.SubGroup.Value);
                AssertEqualName("Cr.Crudu", Assert.Single(lesson2.TeacherNames));
                Assert.Equal("145a/4", lesson2.RoomName.Span);
            });
    }

    [Fact]
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
                    lesson.SubGroup.Value.AsSpan(),
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }
            return true;

        }

        Assert.Equal(4, lessons.Length);
        Assert.Contains(lessons, x => Check(x, "LessonA", "A", "TeacherA"));
        Assert.Contains(lessons, x => Check(x, "LessonB", "A", "TeacherA"));
        Assert.Contains(lessons, x => Check(x, "LessonA", "B", "TeacherB"));
        Assert.Contains(lessons, x => Check(x, "LessonB", "B", "TeacherB"));
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
                Assert.Equal("A", lesson1.SubGroup.Value);
                AssertEqualName("TeacherA", Assert.Single(lesson1.TeacherNames));
            },
            lesson2 =>
            {
                Lesson(lesson2);
                Assert.Equal("B", lesson2.SubGroup.Value);
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
                Assert.Equal("A", lesson1.SubGroup.Value);
                Assert.Equal(Parity.EvenWeek, lesson1.Parity);
                AssertEqualName("TeacherA", Assert.Single(lesson1.TeacherNames));
            },
            lesson2 =>
            {
                Assert.Equal("Lesson", lesson2.LessonName.Span);
                Assert.Equal("B", lesson2.SubGroup.Value);
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
                Assert.Equal("A", lesson1.SubGroup.Value);
                Assert.Equal(Parity.EvenWeek, lesson1.Parity);
                Assert.Empty(lesson1.TeacherNames);
            },
            lesson2 =>
            {
                Assert.Equal("Lesson", lesson2.LessonName.Span);
                Assert.Equal("B", lesson2.SubGroup.Value);
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
                Assert.Equal("A", lesson1.SubGroup.Value);
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
                Assert.Equal("I", lesson1.SubGroup.Value);
                Assert.Equal(Parity.OddWeek, lesson1.Parity);
            },
            lesson2 =>
            {
                Common(lesson2);
                Assert.Equal("II", lesson2.SubGroup.Value);
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
            "Teacher, 123Room",
        ]);

        var lesson1 = Assert.Single(lessons);
        Assert.Equal("Lesson", lesson1.LessonName.Span);
        AssertEqualName("Teacher", Assert.Single(lesson1.TeacherNames));
        Assert.Equal("123Room", lesson1.RoomName.Span);
    }

    [Fact]
    public void NoMultipleRoomName()
    {
        Assert.Throws<RoomAlreadySpecifiedException>(() =>
        {
            var lessons = ParseLessons([
                "Lesson",
                "Teacher, 123Room, 124Room",
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
            "15:00 Lesson Two 123Room",
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
                Assert.Equal("123Room", lesson2.RoomName.Span);

                var time = TimeOnly.FromTimeSpan(TimeSpan.FromHours(15));
                Assert.Equal(time, lesson2.StartTime);
            });
    }
}
