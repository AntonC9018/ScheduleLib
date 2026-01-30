using Anton.LayeredConfig;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core.Topics;

namespace ScheduleLib.Application.Core.Config.Impl.Impl;

public partial class Extensions
{
    extension (ConfigBuilder<LessonTopicsConfig> builder)
    {
        public void Manifest(Action<ManifestSourceBuilder>? configure = null)
        {
            // This should also have the path.
            var x = new ManifestSourceBuilder();
            configure?.Invoke(x);

            var sources = builder.Value().Sources;
            var source = x.Definition;
            sources.Add(source);
        }

        public void FallbackProvider<T>(LessonType lessonType)
            where T : ILessonNameProvider
        {
            var sources = builder.Value().FallbackProviders;
            var x = sources.Find(x => x.LessonType == lessonType);
            if (x == null)
            {
                x = new LessonNameProviderConfig
                {
                    LessonType = lessonType,
                };
                sources.Add(x);
            }
            x.Provider = ActivatorUtilities.GetServiceOrCreateInstance<T>(builder.SingletonServiceProvider);
        }
    }


}
