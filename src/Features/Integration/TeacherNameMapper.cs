using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Builders;
using ScheduleLib.Core.Services;
using ScheduleLib.Parsing;
using ScheduleLib.Theses.Parsing;

namespace ScheduleLib.Application.Config;

// don't know where to put this.
public sealed class TeacherNameMapper(ScheduleBuilder b) : INameRemapper
{
    public static void Register(IServiceCollection services)
    {
        services.AddKeyedSingleton<INameRemapper, TeacherNameMapper>(NameMappingKeys.Teacher);
    }

    public Name RemapName(Name name)
    {
        var nameModel = name.ToNameModel();
        // There should be an api for this that works with regular names.
        _ = b.RemapTeacherName(ref nameModel);
        var ret = nameModel.AsNameFields();
        return new(ret);
    }
}

