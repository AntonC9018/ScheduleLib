using System.Diagnostics;
using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;

namespace ScheduleLib.Application.Core.Config.Impl.Impl;

[AutoConstructor]
public sealed partial class CurrentUserNameProvider
{
    private readonly ConfigProvider _configProvider;

    public string Get()
    {
        var teacher = _configProvider.Get(TeacherLayerConfig.Key);
        Debug.Assert(teacher != null);
        // Could cache this
        return teacher.TeacherName.ToString();
    }
}
