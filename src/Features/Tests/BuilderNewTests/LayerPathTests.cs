using Anton.LayeredData.TreeEnumeration.Infrastructure;

namespace Anton.LayeredData.Tests;

public sealed class LayerPathTests
{
    [Fact]
    public void Test()
    {
        // var services = new ServiceCollection();
        // var sp = services.BuildServiceProvider();
        var tree = new TreeBuilder(null!);
        var a = tree.Defaults.AddLayer(new("A"));
        var b = tree.Defaults.AddLayer(new("B"));
        var c = a.AddLayer(new("C"));
        var d = c.AddLayer(new("D"));
        var e = b.AddLayer(new("E"));
        var paths = tree.BaseNode
            .Dfs()
            .AddLayerPath()
            .Process()
            .Where(x => x.Value.Node.IsLeaf())
            .Select(x => x.ContextCollection.Get(LayerPathContext.Key).Path());

        Assert.Collection(paths,
            p1 =>
            {
                Assert.Collection(p1.Path,
                    n1 => Assert.Same(tree.BaseNode, n1),
                    n2 => Assert.Same(a.Node, n2),
                    n3 => Assert.Same(c.Node, n3),
                    n4 => Assert.Same(d.Node, n4));
            },
            p2 =>
            {
                Assert.Collection(p2.Path,
                    n1 => Assert.Same(tree.BaseNode, n1),
                    n2 => Assert.Same(b.Node, n2),
                    n3 => Assert.Same(e.Node, n3));
            });
    }
}
