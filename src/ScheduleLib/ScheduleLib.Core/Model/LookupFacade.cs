using System.Collections.Immutable;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib;

/// <summary>Finds schedule objects by the names used outside the schedule. Never changes the lookup.</summary>
public sealed class LookupFacade(
    Schedule schedule,
    ScheduleLookup lookup,
    Remappings remappings,
    GroupParseContext groupParseContext)
{
    private static readonly CourseNameParseOptions StrictCourseNames = new()
    {
        IgnorePunctuation = false,
    };

    public CourseNameIndex Courses => lookup.Courses;

    public CourseId? Course(ReadOnlyMemory<char> name, CourseNameParseOptions? parseOptions = null)
    {
        return lookup.Courses.Find(name, parseOptions ?? StrictCourseNames);
    }

    public IEnumerable<TeacherId> Teachers(LastName lastName)
    {
        return TeacherIds(lastName)
            .Select(id => new TeacherId(id));
    }

    public TeacherId? Teacher(LastName lastName)
    {
        var ids = TeacherIds(lastName);
        if (ids.IsEmpty)
        {
            return null;
        }
        return new(ids[0]);
    }

    public TeacherId? Teacher(TeacherBuilderModel.NameModel name)
    {
        var firstNameParts = name.FirstName.Map(x =>
        {
            if (x.Longer is not { } part)
            {
                return Word.Empty;
            }
            return new Word(part);
        });
        return BestTeacherMatch(name.LastName, firstNameParts);
    }

    public TeacherId? Teacher(string firstName, string lastName)
    {
        var firstNameParts = ParseTeacherFirstName(firstName);
        return BestTeacherMatch(lastName, firstNameParts);
    }

    /// <summary>Applies the teacher name remappings, as imported names are resolved.</summary>
    public TeacherNameRemapStatus RemapTeacherName(ref TeacherBuilderModel.NameModel name)
    {
        return remappings.RemapTeacherName(ref name);
    }

    public GroupId? Group(string fullName)
    {
        var group = groupParseContext.Parse(fullName, remappings);
        if (!lookup.Groups.TryGetValue(group.Name, out var id))
        {
            return null;
        }
        return id;
    }

    public ImmutableArray<AnyLessonId> LessonsOfCourse(CourseId courseId)
    {
        return lookup.LessonsOf(courseId);
    }

    private ImmutableArray<int> TeacherIds(LastName lastName)
    {
        return lookup.TeachersByLastName.GetValueOrDefault(lastName.Parts, defaultValue: []);
    }

    private TeacherId? BestTeacherMatch(LastName lastName, NameParts<Word> firstNameParts)
    {
        var ids = TeacherIds(lastName);
        if (ids.IsEmpty)
        {
            return null;
        }

        int i = TeacherLookupHelper.FindIndexOfBestMatch(schedule.Teachers, ids.AsSpan(), firstNameParts);
        if (i == -1)
        {
            return null;
        }
        return new(ids[i]);
    }

    private static NameParts<Word> ParseTeacherFirstName(string firstName)
    {
        var firstNameParts = default(NameParts<Word>);
        var firstNameSpan = firstName.AsSpan();
        var splitName = firstNameSpan.Split(NameConstants.DoubleNameSeparator);
        var firstNameE = firstNameParts.AsRef()
            .GetEnumerator();

        foreach (var partRange in splitName)
        {
            var span = firstNameSpan[partRange];
            if (span.Length == 0)
            {
                continue;
            }
            span = span.Trim();
            if (span.Length == 0)
            {
                throw new ArgumentException(
                    message: "Don't use double dashes in the names, only use single dashes",
                    paramName: nameof(firstName));
            }

            bool nextNamePartOk = firstNameE.MoveNext();
            if (!nextNamePartOk)
            {
                throw new ArgumentException(
                    message: "Too many name parts",
                    paramName: nameof(firstName));
            }

            var part = span.ToString();
            firstNameE.Current = new(part);
        }

        while (firstNameE.MoveNext())
        {
            firstNameE.Current = Word.Empty;
        }

        return firstNameParts;
    }
}
