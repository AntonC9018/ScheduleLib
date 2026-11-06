using ScheduleLib;
using ScheduleLib.Dates;

namespace ScheduleLib.Application.Core;

public sealed class ScheduleSourcesNotFoundException(
    StudyYear? studyYear,
    Semester semester,
    string dataDirectory)
    : DirectoryNotFoundException(
        $"No schedule sources for coded study year {studyYear} and semester {semester} under {dataDirectory}.")
{
    public StudyYear? StudyYear { get; } = studyYear;
    public Semester Semester { get; } = semester;
    public string DataDirectory { get; } = dataDirectory;
}
