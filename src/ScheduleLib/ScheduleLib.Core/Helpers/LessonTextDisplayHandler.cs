using System.Diagnostics;
using System.Text;
using AutoConstructor.Attributes;

namespace ScheduleLib.Generation;

public interface IRichText
{
    void Span(string str, bool isBold = false);
    void Line(string str);
}

public sealed partial class LessonTextDisplayHandler
{
    [AutoConstructor]
    public sealed partial class Services
    {
        public readonly SubGroupNumberDisplayHandler SubGroupNumberDisplay;
        public readonly ParityDisplayHandler ParityDisplay;
        public readonly LessonTypeDisplayHandler LessonTypeDisplay;
    }
    public struct Config()
    {
        public bool PrintsTeacherName = true;
        public bool PreferLongerTeacherName = false;
        public bool PrintsGroupNames = false;
        public bool PrintsSubGroup = true;
    }

    private readonly Services _services;
    private readonly Config _config;

    public LessonTextDisplayHandler(Services services, Config config)
    {
        _services = services;
        _config = config;
    }

    public struct Params
    {
        public required IRichText TextDescriptor;
        public required Schedule Schedule;
        public required LessonTimeConfig LessonTimeConfig;
        public required AnyLessonAccessor Lesson;
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
            var course = p.Schedule.Get(p.Lesson.Lesson.Course);
            return course.Names[0];
        }

        var sb = p.CleanStringBuilder;
        if (_config.PrintsSubGroup)
        {
            // Alternative first, then specialization, then the subgroup: "A1, GA2D, I: ".
            var lesson = p.Lesson.Lesson;
            string? PartitionDimension(int index) => index switch
            {
                0 => lesson.Alternative.Value,
                1 => lesson.Specialization.Value,
                2 => _services.SubGroupNumberDisplay.Get(lesson.SubGroup),
                _ => null,
            };
            int prefixStart = sb.Length;
            var list = new ListStringBuilder(sb, ", ");
            for (int i = 0; i < 3; i++)
            {
                if (PartitionDimension(i) is { } dimension)
                {
                    list.Append(dimension);
                }
            }
            if (sb.Length > prefixStart)
            {
                sb.Append(": ");
            }
        }
        {
            var str = sb.ToStringAndClear();
            p.TextDescriptor.Span(str, isBold: true);
        }
        {
            var courseName = CourseName();
            sb.Append(courseName);
        }
        {
            var lessonType = _services.LessonTypeDisplay.Get(p.Lesson.Lesson.Type);
            string? parity = null;
            string? date = null;
            if (p.Lesson.Weekly is { } weekly)
            {
                parity = _services.ParityDisplay.Get(weekly.Date.Parity);
            }
            else
            {
                var oneTime = p.Lesson.OneTime!.Value;
                date = oneTime.Date.Date.ToString("dd.MM.yy");
            }
            bool appendGroups = _config.PrintsGroupNames && p.Lesson.Lesson.Groups.Count > 0;
            bool appendAny = lessonType != null || parity != null || appendGroups || date != null;
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
                Write(date);

                if (_config.PrintsGroupNames)
                {
                    var groupIds = p.Lesson.Lesson.Groups;
                    _ = groupIds;
                    foreach (var groupId in groupIds)
                    {
                        var group = p.Schedule.Get(groupId);
                        Write(group.Name);
                    }
                }

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
                    NameDisplayHelper.Append(new()
                    {
                        Output = sb,
                        Name = teacher.PersonName,
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
    public static void AppendGroupNameWithLanguage(StringBuilder b, Group g)
    {
        b.Append($"{g.Name}({g.Language.GetName()})");
    }
}
