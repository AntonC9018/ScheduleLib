using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.Application.Config;

public sealed class MoodleConfig : INodeData<MoodleConfig>, ICredentialsHolder
{
    public static NodeDataKey<MoodleConfig> Key { get; } = NodeDataKey.Registry.Register<MoodleConfig>();
    public CredentialsSource? Credentials { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.AddConfigProvider(Key);
        services.RegisterBasicOperationsAndMergers<MoodleConfig>();
    }
}
