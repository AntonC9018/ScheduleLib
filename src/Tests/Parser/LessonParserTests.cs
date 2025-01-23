using ScheduleLib;
using ScheduleLib.Parsing.Lesson;

namespace App.Tests;

public sealed class LessonParserTests
{
    [Fact]
    public void LessonListEachWithModifiers()
    {
        var lessons = LessonParsingHelper.ParseLessons(new()
        {
            Lines = [
                "15:00 Opț.psihol. (curs,imp),",
                "Psihologie (sem,par)",
                "V.Miron  433/3",
            ],
        });

        var time = TimeOnly.FromTimeSpan(TimeSpan.FromHours(15));

        void CheckCommon(in ParsedLesson lesson)
        {
            Assert.Equal(time, lesson.StartTime);
            Assert.True(lesson.GroupName.IsEmpty);
            var teacherName = Assert.Single(lesson.TeacherNames);
            Assert.Equal("V.Miron".AsMemory(), teacherName);
            Assert.Equal("433/3".AsMemory(), lesson.RoomName);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                CheckCommon(lesson1);
                Assert.Equal(Parity.OddWeek, lesson1.Parity);
                Assert.Equal(LessonType.Curs, lesson1.LessonType);
                Assert.Equal("Opț. psihol.".AsMemory(), lesson1.LessonName);
            },
            lesson2 =>
            {
                CheckCommon(lesson2);
                Assert.Equal(Parity.EvenWeek, lesson2.Parity);
                Assert.Equal(LessonType.Seminar, lesson2.LessonType);
                Assert.Equal("Psihologie".AsMemory(), lesson2.LessonName);
            });
    }

    [Fact]
    public void ParenthesesInLessonName()
    {
        var lessons = LessonParsingHelper.ParseLessons(new()
        {
            Lines = [
                "Matematica discretă (Logica)  (curs)",
                "I.Cucu  404/4",
            ],
        });

        Assert.Collection(lessons,
            lesson =>
            {
                var teacherName = Assert.Single(lesson.TeacherNames);
                Assert.Equal("I.Cucu".AsMemory(), teacherName);
                Assert.Equal("404/4".AsMemory(), lesson.RoomName);
                Assert.Equal(LessonType.Curs, lesson.LessonType);
                Assert.Equal("Matematica discretă (Logica)".AsMemory(), lesson.LessonName);
            });
    }

    [Fact]
    public void RoomNameMayBeUnderscores()
    {
        var lessons = LessonParsingHelper.ParseLessons(new()
        {
            Lines = [
                "Option.didact. (curs)",
                "A.Dabija  ____",
            ],
        });

        Assert.Collection(lessons,
            lesson =>
            {
                var teacherName = Assert.Single(lesson.TeacherNames);
                Assert.Equal("A.Dabija".AsMemory(), teacherName);
                Assert.Equal("____".AsMemory(), lesson.RoomName);
                Assert.Equal(LessonType.Curs, lesson.LessonType);
                Assert.Equal("Option.didact.".AsMemory(), lesson.LessonName);
            });
    }

    [Fact]
    public void TimeSlotThatLooksLikeGroupIsParseProperly()
    {
        var lessons = LessonParsingHelper.ParseLessons(new()
        {
            Lines = [
                "8:00 Containerizare și virtualizare (lab,imp)",
                "CV: M.Croitor  326/4",
                "15:00 Dezvoltare de aplicații WEB cu React (curs)",
                "WR: A.Donu  213a/4",
            ],
        });

        TimeOnly Time(int hours)
        {
            var span =  TimeSpan.FromHours(hours);
            return TimeOnly.FromTimeSpan(span);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                Assert.Equal(Time(8), lesson1.StartTime);
                Assert.Equal("CV".AsMemory(), lesson1.GroupName);
                Assert.Equal("M.Croitor".AsMemory(), Assert.Single(lesson1.TeacherNames));
                Assert.Equal("326/4".AsMemory(), lesson1.RoomName);
                Assert.Equal(LessonType.Lab, lesson1.LessonType);
                Assert.Equal(Parity.OddWeek, lesson1.Parity);
                Assert.Equal("Containerizare și virtualizare".AsMemory(), lesson1.LessonName);
            },
            lesson2 =>
            {
                Assert.Equal(Time(8), lesson2.StartTime);
                Assert.Equal("WR".AsMemory(), lesson2.GroupName);
                Assert.Equal("A.Donu".AsMemory(), Assert.Single(lesson2.TeacherNames));
                Assert.Equal("213a/4".AsMemory(), lesson2.RoomName);
                Assert.Equal(LessonType.Curs, lesson2.LessonType);
                Assert.Equal(Parity.EveryWeek, lesson2.Parity);
                Assert.Equal("Dezvoltare de aplicații WEB cu React".AsMemory(), lesson2.LessonName);
            });
    }

    [Fact]
    public void SubGroupList()
    {
        var lessons = LessonParsingHelper.ParseLessons(new()
        {
            Lines = [
                "Dezvoltare de aplicații WEB cu React (lab)",
                "WR1: A.Donu  143/4,  WR2: Cr.Crudu  145a/4",
            ],
        });

        void CheckCommon(ParsedLesson lesson)
        {
            Assert.Equal(LessonType.Lab, lesson.LessonType);
            Assert.Equal("Dezvoltare de aplicații WEB cu React".AsMemory(), lesson.LessonName);
        }

        Assert.Collection(lessons,
            lesson1 =>
            {
                CheckCommon(lesson1);
                Assert.Equal("WR1".AsMemory(), lesson1.GroupName);
                Assert.Equal("A.Donu".AsMemory(), Assert.Single(lesson1.TeacherNames));
                Assert.Equal("143/4".AsMemory(), lesson1.RoomName);
            },
            lesson2 =>
            {
                CheckCommon(lesson2);
                Assert.Equal("WR2".AsMemory(), lesson2.GroupName);
                Assert.Equal("Cr.Crudu".AsMemory(), Assert.Single(lesson2.TeacherNames));
                Assert.Equal("145a/4".AsMemory(), lesson2.RoomName);
            });
    }
}
