using Anton.LayeredConfig;
using MainCli.Topics;
using ScheduleLib;

namespace MainCli.BuilderNew.Impl;

public partial class Extensions
{
    extension (ConfigBuilder<LessonTopicsConfig> builder)
    {
        public void Manifest(Action<ManifestSourceBuilder>? configure = null)
        {
            // This should also have the path.
            var x = new ManifestSourceBuilder();
            configure?.Invoke(x);

            var sources = builder.Enable().Value.Sources;
            var source = new ManifestLessonTopicSourceDefinition();
            sources.Add(source);
        }

        public void FallbackProvider(LessonType lessonType, ILessonNameProvider provider)
        {
            var sources = builder.Enable().Value.FallbackProviders;
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
    }


}
