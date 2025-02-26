using System.Diagnostics;
using System.Text;
using QuestPDF.Fluent;

namespace ScheduleLib.Generation;

public sealed class PdfLessonTextDisplayHandler
{
    public struct Services
    {
        public required SubGroupNumberDisplayHandler SubGroupNumberDisplay;
        public required ParityDisplayHandler ParityDisplay;
        public required LessonTypeDisplayHandler LessonTypeDisplay;
    }
    public struct Config()
    {
        public bool PrintsTeacherName = true;
        public bool PreferLongerTeacherName = false;
    }

    private readonly Services _services;
    private readonly Config _config;

    public PdfLessonTextDisplayHandler(Services services, Config config)
    {
        _services = services;
        _config = config;
    }

    public struct Params
    {
        public required TextDescriptor TextDescriptor;
        public required Schedule Schedule;
        public required LessonTimeConfig LessonTimeConfig;
        public required RegularLesson Lesson;
        public required uint ColumnWidth;

        /// <summary>
        /// Comes in clean.
        /// Comes out dirty.
        /// </summary>
        public required StringBuilder StringBuilder;

        public StringBuilder CleanStringBuilder
        {
            get
            {
                Debug.Assert(StringBuilder.Length == 0);
                return StringBuilder;
            }
        }
    }

    public void Handle(Params p)
    {
        _ = p.LessonTimeConfig;

        string CourseName()
        {
            // It's impossible to measure text in this library. Yikes.
            var course = p.Schedule.Get(p.Lesson.Lesson.Course);
            if (p.ColumnWidth == 1)
            {
                return course.Names[^1];
            }

            return course.Names[0];
        }

        var sb = p.CleanStringBuilder;
        {
            var subGroupNumber = _services.SubGroupNumberDisplay.Get(p.Lesson.Lesson.SubGroup);
            if (subGroupNumber is { } s1)
            {
                sb.Append(s1);
                sb.Append(": ");
            }
        }
        {
            var str = sb.ToStringAndClear();
            var span = p.TextDescriptor.Span(str);
            span.Bold();
        }
        {
            var courseName = CourseName();
            sb.Append(courseName);
        }
        {
            var lessonType = _services.LessonTypeDisplay.Get(p.Lesson.Lesson.Type);
            var parity = _services.ParityDisplay.Get(p.Lesson.Date.Parity);
            bool appendAny = lessonType != null || parity != null;
            if (appendAny)
            {
                sb.Append(" (");

                bool written = false;
                void Write(string? str)
                {
                    if (str is not { } notNullS)
                    {
                        return;
                    }
                    if (written)
                    {
                        sb.Append(", ");
                    }
                    else
                    {
                        written = true;
                    }

                    sb.Append(notNullS);
                }

                Write(lessonType);
                Write(parity);
                sb.Append(")");
            }
        }
        {
            var str = sb.ToStringAndClear();
            p.TextDescriptor.Line(str);
        }
        {
            bool added = false;

            if (_config.PrintsTeacherName)
            {
                foreach (var t in p.Lesson.Lesson.Teachers)
                {
                    if (added)
                    {
                        sb.Append(',');
                    }

                    var teacher = p.Schedule.Get(t);
                    LessonTextDisplayHelper.AppendTeacherName(new()
                    {
                        Output = sb,
                        Teacher = teacher,
                        LastNameFirst = false,
                        InsertSpaceAfterShortName = false,
                        PreferLonger = _config.PreferLongerTeacherName,
                    });

                    added = true;
                }
            }

            var r = p.Lesson.Lesson.Room;
            if (r.IsValid)
            {
                if (added)
                {
                    sb.Append("  ");
                }
                var room = p.Schedule.Get(r);
                sb.Append(room);
            }
        }
        {
            var str = sb.ToStringAndClear();
            p.TextDescriptor.Span(str);
        }
    }
}

public static class LessonTextDisplayHelper
{
    public struct TeacherNameParams()
    {
        public required StringBuilder Output;
        public required Teacher Teacher;
        public required bool LastNameFirst;
        public bool PreferLonger = true;
        public bool InsertSpaceAfterShortName = true;
    }

    private enum WhichFirstName
    {
        None,
        Full,
        Short,
    }

    public static void AppendTeacherName(TeacherNameParams p)
    {
        var shouldAddSpaceNext = false;
        if (p.LastNameFirst)
        {
            if (AppendLastName())
            {
                shouldAddSpaceNext = true;
            }
            AppendFirstName();
        }
        else
        {
            var res = AppendFirstName();
            if (res == WhichFirstName.Full
                || res == WhichFirstName.Short && p.InsertSpaceAfterShortName)
            {
                shouldAddSpaceNext = true;
            }

            AppendLastName();
        }

        bool AppendLastName()
        {
            AppendSpaceMaybe();
            p.Output.Append(p.Teacher.PersonName.LastName);
            return true;
        }
        WhichFirstName AppendFirstName()
        {
            if (p.PreferLonger)
            {
                if (AppendLonger())
                {
                    return WhichFirstName.Full;
                }
                if (AppendShorter())
                {
                    return WhichFirstName.Short;
                }
                return WhichFirstName.None;
            }
            {
                if (AppendShorter())
                {
                    return WhichFirstName.Short;
                }
                if (AppendLonger())
                {
                    return WhichFirstName.Full;
                }
                return WhichFirstName.None;
            }

            bool AppendLonger()
            {
                if (p.Teacher.PersonName.FirstName is { } firstName)
                {
                    AppendSpaceMaybe();
                    p.Output.Append(firstName);
                    return true;
                }
                return false;
            }
            bool AppendShorter()
            {
                var shortName = p.Teacher.PersonName.ShortFirstName;
                if (shortName != Word.Empty)
                {
                    AppendSpaceMaybe();
                    p.Output.Append(shortName.Span.Value);
                    return true;
                }
                return false;
            }
        }

        void AppendSpaceMaybe()
        {
            if (shouldAddSpaceNext)
            {
                p.Output.Append(' ');
            }
        }
    }

    public static void AppendGroupNameWithLanguage(StringBuilder b, Group g)
    {
        b.Append($"{g.Name}({g.Language.GetName()})");
    }
}
