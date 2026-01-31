using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ScheduleLib.Dates;

public sealed class StudyYearOptions
{
    public required int StudyYear { get; set; } = -1;
    public required Semester Semester { get; set; } = Semester.Invalid;
}

public sealed class StudyYearOptionsValidator : IValidateOptions<StudyYearOptions>
{
    public ValidateOptionsResult Validate(string? name, StudyYearOptions options)
    {
        _ = name;

        if (options.StudyYear == -1)
        {
            return ValidateOptionsResult.Fail("StudyYear not initialized");
        }
        if (options.Semester == Semester.Invalid)
        {
            return ValidateOptionsResult.Fail("Semester not initialized");
        }
        return ValidateOptionsResult.Success;
    }
}

public static class StudyYearHelper
{
    public static OptionsBuilder<StudyYearOptions> AddStudyYear(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<StudyYearOptions>, StudyYearOptionsValidator>();
        var ret = services.AddOptions<StudyYearOptions>();
        ret.ValidateOnStart();
        return ret;
    }
}


