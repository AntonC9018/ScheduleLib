using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core.Topics;

namespace ScheduleLib.Application.Config;

public partial class Extensions
{
    extension (NodeDataBuilder<LessonTopicsConfig> builder)
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

        public void FallbackProvider(
            LessonType lessonType,
            ILessonNameProviderFactory provider)
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
            x.Provider = provider;
        }

        public void FallbackProvider(
            LessonType lessonType,
            ILessonNameProvider provider)
        {
            var p = new NonLocalizedLessonNameProvider(provider);
            builder.FallbackProvider(lessonType, p);
        }

        public void FallbackProvider<T>(
            LessonType lessonType)

            where T : ILessonNameProviderBase
        {
            if (typeof(T).IsAssignableTo(typeof(ILessonNameProvider)))
            {
                var ret = (ILessonNameProvider) ActivatorUtilities.GetServiceOrCreateInstance<T>(builder.SingletonServiceProvider);
                builder.FallbackProvider(lessonType, ret);
            }
            else if (typeof(T).IsAssignableTo(typeof(ILessonNameProviderFactory)))
            {
                var ret = (ILessonNameProviderFactory) ActivatorUtilities.GetServiceOrCreateInstance<T>(builder.SingletonServiceProvider);
                builder.FallbackProvider(lessonType, ret);
            }
            else
            {
                throw new NotImplementedException($"Don't inherit {nameof(ILessonNameProviderBase)} directly!");
            }
        }
    }
}
