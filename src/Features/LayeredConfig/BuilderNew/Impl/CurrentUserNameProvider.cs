using System.Diagnostics;
using Anton.LayeredData.Retrieval;
using AutoConstructor.Attributes;

namespace ScheduleLib.Application.Config;

[AutoConstructor]
public sealed partial class CurrentUserNameProvider
{
    private readonly DataProvider _configProvider;

    public string Get()
    {
        var teacher = _configProvider.Get(TeacherLayerConfig.Key);
        Debug.Assert(teacher != null);
        // Could cache this
        return teacher.TeacherName.ToString();
    }
}
