using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Theses.Parsing;

namespace ScheduleLib.Application.Config;

// don't know where to put this.
public sealed class TeacherNameMapper(ScheduleBuilder b) : INameRemapper
{
    public static void Register(IServiceCollection services)
    {
        services.AddKeyedSingleton<INameRemapper, TeacherNameMapper>(ThesisListParser.TeacherNameRemapperKey);
    }

    public Name RemapName(Name name)
    {
        var newLastName = b.RemapTeacherName(new(name.LastName));
        return name with
        {
            LastName = newLastName,
        };
    }
}
