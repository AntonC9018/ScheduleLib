using Anton.LayeredData.TreeEnumeration;

namespace Anton.LayeredData.Tests;

public sealed class LayerPathTests
{
    [Fact]
    public void Test()
    {
        // var services = new ServiceCollection();
        // var sp = services.BuildServiceProvider();
        var tree = new TreeBuilder(null!);

        Layer CalculateLayer()
        {
            Layer layer = new("A");
            return layer;
        }

        var a = tree.Defaults.AddLayer(CalculateLayer());

        Layer CalculateLayer2()
        {
            Layer layer2 = new("B");
            return layer2;
        }

        var b = tree.Defaults.AddLayer(CalculateLayer2());

        Layer CalculateLayer3()
        {
            Layer layer3 = new("C");
            return layer3;
        }

        var c = a.AddLayer(CalculateLayer3());

        Layer CalculateLayer4()
        {
            Layer layer4 = new("D");
            return layer4;
        }

        var d = c.AddLayer(CalculateLayer4());

        Layer CalculateLayer5()
        {
            Layer layer5 = new("E");
            return layer5;
        }

        var e = b.AddLayer(CalculateLayer5());
        var paths = tree.BaseNode
            .Dfs()
            .AddLayerPath()
            .Process()
            .Where(x => x.Value.Node.IsLeaf())
            .Select(x => x.ContextCollection.Get(LayerPathContext.Key)
                .Path());

        Assert.Collection(
            paths,
            p1 =>
            {
                Assert.Collection(
                    p1.Path,
                    n1 => Assert.Same(expected: tree.BaseNode, actual: n1),
                    n2 => Assert.Same(expected: a.Node, actual: n2),
                    n3 => Assert.Same(expected: c.Node, actual: n3),
                    n4 => Assert.Same(expected: d.Node, actual: n4));
            },
            p2 =>
            {
                Assert.Collection(
                    p2.Path,
                    n1 => Assert.Same(expected: tree.BaseNode, actual: n1),
                    n2 => Assert.Same(expected: b.Node, actual: n2),
                    n3 => Assert.Same(expected: e.Node, actual: n3));
            });
    }
}
