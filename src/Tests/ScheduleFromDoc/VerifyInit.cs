using System.Runtime.CompilerServices;
using Argon;
using ScheduleLib;

namespace ScheduleFromDoc.Tests;

internal static class VerifyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.AddExtraSettings(settings =>
        {
            settings.DefaultValueHandling = DefaultValueHandling.Include;
        });
    }
}


