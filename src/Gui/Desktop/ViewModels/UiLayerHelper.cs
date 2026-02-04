using System.Diagnostics;
using Anton.LayeredConfig;
using ScheduleLib.Application.Config;

namespace Desktop.ViewModels;

internal static class UiLayerHelper
{
    public static readonly LayerName UiTeacherLayer = LayerName.Create("User-UI");

    public static bool IsUiLayer(this MutableLayer layer)
    {
        return layer.Name == UiTeacherLayer;
    }

    public static IEnumerable<ApplicationConfigLayerBuilder> GetUiLayers(
        this ApplicationConfigBuilder builder)
    {
        foreach (var x in builder.GetMarkerLayers())
        {
            if (x.Builder.Layer.IsUiLayer())
            {
                yield return x.Builder;
            }
        }
    }

    extension(ApplicationConfigLayerBuilder builder)
    {
        public ApplicationConfigLayerBuilder CreateUiLayer()
        {
            Debug.Assert(!builder.Layer.IsUiLayer());
            var b = builder.AddLayer(UiTeacherLayer);
            b.Builder(TeacherLayerConfig.Key).Enable();
            return b;
        }

        public ApplicationConfigLayerBuilder MaybeCreateUiLayer()
        {
            if (!builder.Layer.IsUiLayer())
            {
                return builder.CreateUiLayer();
            }
            return builder;
        }

        public ApplicationConfigLayerBuilder MaybeCreateUiLayer(TeacherLayerConfig config)
        {
            var b = MaybeCreateUiLayer(builder);
            b.Builder(TeacherLayerConfig.Key).CopyValue(config);
            return b;
        }
    }

    public static void MaybeRemoveLayer(
        MutableLayer layerToRemove,
        ApplicationConfigBuilder root)
    {
        root.RemoveLayers(layer =>
        {
            if (ReferenceEquals(layerToRemove, layer))
            {
                return true;
            }
            return false;
        });
    }

    extension(ConfigSerializationHelper helper)
    {
        public async Task SerializeUiLayers(
            Stream output,
            ApplicationConfigBuilder configRoot)
        {
            var uiLayers = configRoot.GetUiLayers().Select(x => x.Layer);
            await helper.SerializeValues(uiLayers, output, serializeLayerName: false);
            output.SetLength(output.Position);
        }

        public async Task DeserializeUiLayers(
            Stream output,
            ApplicationConfigBuilder configRoot)
        {
            await helper.DeserializeValues(
                output,
                (layerName, configs) =>
                {
                    {
                        if (layerName is { } x && x != UiTeacherLayer)
                        {
                            throw new InvalidOperationException($"Expected a layer with name {UiTeacherLayer}");
                        }
                    }
                    {
                        var marker = (TeacherLayerConfig) configs.Find(x => x.Key == TeacherLayerConfig.Key).Value;
                        var markerLayer = configRoot
                            .GetMarkerLayers()
                            .FirstOrDefault(x => x.Config.Equals(marker))
                            .Builder;

                        if (markerLayer.IsNull)
                        {
                            markerLayer = configRoot.Defaults;
                        }

                        var ret = markerLayer.MaybeCreateUiLayer();
                        return ret;
                    }
                });
        }
    }
}
