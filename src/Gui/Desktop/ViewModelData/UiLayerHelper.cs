using System.Diagnostics;
using Anton.LayeredData;
using ScheduleLib.Application.Config;

namespace Desktop.ViewModelData;

public static class UiLayerHelper
{
    public static readonly Layer UiTeacherLayer = Layer.Create("User-UI");

    public static bool IsOnEditableLayer(this MutableNode node)
    {
        return node.Layer == UiTeacherLayer;
    }

    public static IEnumerable<NodeBuilder> GetUiLayers(
        this TreeBuilder builder)
    {
        foreach (var x in builder.GetMarkerNodes())
        {
            if (x.Builder.Node.IsOnEditableLayer())
            {
                yield return x.Builder;
            }
        }
    }

    extension(NodeBuilder builder)
    {
        public NodeBuilder CreateUiLayer()
        {
            Debug.Assert(!builder.Node.IsOnEditableLayer());
            var b = builder.AddLayer(UiTeacherLayer);
            b.Builder(TeacherLayerConfig.Key).Enable();
            return b;
        }

        public NodeBuilder MaybeCreateUiLayer()
        {
            if (!builder.Node.IsOnEditableLayer())
            {
                return builder.CreateUiLayer();
            }
            return builder;
        }

        public NodeBuilder MaybeCreateUiLayer(TeacherLayerConfig config)
        {
            var b = MaybeCreateUiLayer(builder);
            b.Builder(TeacherLayerConfig.Key).CopyValue(config);
            return b;
        }
    }

    public static void MaybeRemoveLayer(
        MutableNode nodeToRemove,
        TreeBuilder root)
    {
        root.RemoveNodes(layer =>
        {
            if (ReferenceEquals(nodeToRemove, layer))
            {
                return true;
            }
            return false;
        });
    }

    extension(TreeSerializer helper)
    {
        public async Task SerializeUiLayers(
            Stream output,
            TreeBuilder configRoot)
        {
            var uiLayers = configRoot.GetUiLayers().Select(x => x.Node);
            await helper.SerializeValues(uiLayers, output, serializeLayerName: false)
                .ConfigureAwait(false);
            output.SetLength(output.Position);
        }

        public async Task DeserializeUiLayers(
            Stream output,
            TreeBuilder configRoot)
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
                            .GetMarkerNodes()
                            .FirstOrDefault(x => x.Config.Equals(marker))
                            .Builder;

                        if (markerLayer.IsNull)
                        {
                            markerLayer = configRoot.Defaults;
                        }

                        var ret = markerLayer.MaybeCreateUiLayer();
                        return ret;
                    }
                }).ConfigureAwait(false);
        }
    }
}
